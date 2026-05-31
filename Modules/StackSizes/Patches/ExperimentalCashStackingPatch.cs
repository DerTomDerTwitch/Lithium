using HarmonyLib;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Items;
using UnityEngine;

namespace Lithium.Modules.StackSizes.Patches
{
    // EXPERIMENTAL, opt-in (Configuration.ExperimentalCashStacking). The $1000 per-stack cash cap is the
    // native constant CashInstance.MAX_BALANCE, which cannot be written (a raw static write crashes).
    // We can't change the constant, but we can raise the clamps in the cash methods that read it, using
    // only instance-level writes (CashInstance.Balance is a writable auto-property; the drag fields are
    // instance fields) so there is no static-field AccessViolation risk.
    //
    // Money safety: the drag amount is always clamped to the SOURCE balance, so you can never drag more
    // than you have (no duplication); the balance bumps only ever RAISE a value the game clamped down,
    // and the combine top-up moves its remainder from the same source the native transfer used, so cash
    // totals stay conserved.
    internal static class CashStackingConfig
    {
        // Independent drag-amount state: tracked here, not read back from draggedCashAmount, so the
        // native per-frame re-clamp to $1000 cannot corrupt it.
        public static float DragAmount;
        public static bool DragActive;

        // Scroll-acceleration state.
        private static float _lastScrollTime;
        private static int _lastStepFrame = -1;

        public static bool TryGet(out float cap)
        {
            cap = 0f;
            ModStackSizes module = Core.Get<ModStackSizes>();
            if (module == null || !module.Configuration.Enabled || !module.Configuration.ExperimentalCashStacking)
                return false;
            cap = module.Configuration.CashMaxBalance;
            return true;
        }

        // The cash Add/Subtract handlers fire twice per scroll tick (duplicate UI managers in the same
        // frame). Only the first call per frame should apply a step; otherwise the duplicate sees dt~0,
        // triggers max acceleration, and a single notch jumps a large amount.
        public static bool BeginScrollTick()
        {
            int frame = Time.frameCount;
            if (frame == _lastStepFrame)
                return false;
            _lastStepFrame = frame;
            return true;
        }

        // "Nice number" step ladder: 1, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, ... (no 2).
        private static readonly int[] Ladder = BuildLadder();

        private static int[] BuildLadder()
        {
            List<int> ladder = new List<int> { 1, 5 };
            for (long b = 10; b <= 100000000; b *= 10)
            {
                ladder.Add((int)(1 * b));
                ladder.Add((int)(2 * b));
                ladder.Add((int)(5 * b));
            }
            return ladder.ToArray();
        }

        // Largest ladder value not exceeding 'value' (never below the smallest ladder entry).
        public static int SnapToLadder(float value)
        {
            int step = Ladder[0];
            for (int i = 0; i < Ladder.Length; i++)
            {
                if (Ladder[i] <= value)
                    step = Ladder[i];
                else
                    break;
            }
            return step;
        }

        // One scroll tick. The step depends only on scroll SPEED, never on a magnitude floor: scrolling
        // slowly always steps by $1 (so every value is reachable at any amount). Spinning the wheel ramps
        // the step toward a maximum that scales with the current amount — so larger stacks accelerate
        // faster — and the result is snapped to a nice ladder value (1/5/10/20/50/100/200/500/1000/...).
        public static int AcceleratedStep(float amount)
        {
            float now = Time.unscaledTime;
            float dt = Mathf.Max(now - _lastScrollTime, 0.0001f);
            _lastScrollTime = now;

            float speed01 = Mathf.Clamp01((0.18f - dt) / 0.16f);   // 0 when slow (>=0.18s/tick) -> step 1
            float curve = speed01 * speed01 * speed01;             // gentler ramp: stays near $1 longer
            float maxStep = Mathf.Max(5f, amount * 0.33f);         // fastest single-tick step, scales with amount
            return SnapToLadder(Mathf.Lerp(1f, maxStep, curve));
        }

        public static float SourceMax(CashInstance source, float cap)
        {
            float bal = source != null ? source.Balance : cap;
            return Mathf.Min(cap, bal);
        }
    }

    // --- Drag-amount picker reimplementation (lets you choose more than $1000) ---

    [HarmonyPatch(typeof(ItemUIManager), nameof(ItemUIManager.StartDragCash))]
    public class CashDragStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemUIManager __instance)
        {
            if (!CashStackingConfig.TryGet(out float _))
                return;
            CashStackingConfig.DragAmount = Mathf.Max(1f, Mathf.Round(__instance.draggedCashAmount));
            CashStackingConfig.DragActive = true;
        }
    }

    // State captured before EndCashDrag runs, so the post-step top-up can move the remainder.
    public struct CashCombineState
    {
        public bool Valid;
        public CashInstance Target;
        public CashInstance Source;
        public float TargetBefore;
        public float SourceBefore;
        public float Dragged;
    }

    [HarmonyPatch(typeof(ItemUIManager), nameof(ItemUIManager.EndCashDrag))]
    public class CashDragEndPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ItemUIManager __instance, out CashCombineState __state)
        {
            __state = default;

            if (!CashStackingConfig.TryGet(out float _) || !__instance.isDraggingCash)
                return;

            ItemSlotUI hov = __instance.HoveredSlot;
            ItemSlotUI src = __instance.draggedSlot;
            if (hov == null || hov.assignedSlot == null || src == null || src.assignedSlot == null)
                return;

            CashInstance targetCash = hov.assignedSlot.ItemInstance != null ? hov.assignedSlot.ItemInstance.TryCast<CashInstance>() : null;
            CashInstance sourceCash = src.assignedSlot.ItemInstance != null ? src.assignedSlot.ItemInstance.TryCast<CashInstance>() : null;
            if (targetCash == null || sourceCash == null)
                return;
            if (targetCash.Pointer == sourceCash.Pointer)
                return; // dropping onto itself

            __state = new CashCombineState
            {
                Valid = true,
                Target = targetCash,
                Source = sourceCash,
                TargetBefore = targetCash.Balance,
                SourceBefore = sourceCash.Balance,
                Dragged = __instance.draggedCashAmount,
            };
        }

        [HarmonyPostfix]
        public static void Postfix(CashCombineState __state)
        {
            CashStackingConfig.DragActive = false;

            // Native clamps the merge to the game's $1000 (it moves min(dragged, 1000 - target)). When
            // the target is at/near 1000 it moves nothing. Move the remaining amount ourselves, up to the
            // configured cap, taking it from the same source the native used — money stays conserved.
            if (!__state.Valid || !CashStackingConfig.TryGet(out float cap))
                return;

            float room = cap - __state.TargetBefore;
            if (room <= 0f)
                return;

            float desired = Mathf.Min(Mathf.Min(__state.Dragged, room), __state.SourceBefore);
            float nativeMoved = __state.Target.Balance - __state.TargetBefore;
            float extra = desired - nativeMoved;
            if (extra <= 0.001f)
                return;

            __state.Target.ChangeBalance(extra);
            __state.Source.ChangeBalance(-extra);
        }
    }

    [HarmonyPatch(typeof(ItemUIManager), nameof(ItemUIManager.AddCashAmount))]
    public class CashDragAddPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemUIManager __instance, CashInstance __0)
        {
            if (!CashStackingConfig.TryGet(out float cap) || !__instance.isDraggingCash)
                return;

            float max = CashStackingConfig.SourceMax(__0, cap);
            if (CashStackingConfig.BeginScrollTick())
            {
                int step = CashStackingConfig.AcceleratedStep(CashStackingConfig.DragAmount);
                float raw = CashStackingConfig.DragAmount + step;
                // Snap to integers, but allow the exact (possibly fractional) source balance at the
                // ceiling so the whole stack can still be grabbed without orphaning cents.
                CashStackingConfig.DragAmount = raw >= max ? max : Mathf.Round(raw);
            }
            __instance.draggedCashAmount = CashStackingConfig.DragAmount;
        }
    }

    [HarmonyPatch(typeof(ItemUIManager), nameof(ItemUIManager.SubtractCashAmount))]
    public class CashDragSubtractPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemUIManager __instance, CashInstance __0)
        {
            if (!CashStackingConfig.TryGet(out float cap) || !__instance.isDraggingCash)
                return;

            if (CashStackingConfig.BeginScrollTick())
            {
                int step = CashStackingConfig.AcceleratedStep(CashStackingConfig.DragAmount);
                CashStackingConfig.DragAmount = Mathf.Max(1f, Mathf.Round(CashStackingConfig.DragAmount - step));
            }
            __instance.draggedCashAmount = CashStackingConfig.DragAmount;
        }
    }

    [HarmonyPatch(typeof(ItemUIManager), nameof(ItemUIManager.UpdateCashDragAmount))]
    public class CashDragUpdatePatch
    {
        // Re-assert our tracked amount each frame after the native method clamps draggedCashAmount to $1000.
        [HarmonyPostfix]
        public static void Postfix(ItemUIManager __instance, CashInstance __0)
        {
            if (!CashStackingConfig.TryGet(out float cap) || !__instance.isDraggingCash || !CashStackingConfig.DragActive)
                return;

            CashStackingConfig.DragAmount = Mathf.Min(CashStackingConfig.DragAmount, CashStackingConfig.SourceMax(__0, cap));
            __instance.draggedCashAmount = CashStackingConfig.DragAmount;
        }
    }

    // --- Balance clamp raises (lets a slot actually hold the larger amount) ---

    [HarmonyPatch(typeof(CashInstance), nameof(CashInstance.SetBalance))]
    public class CashSetBalancePatch
    {
        [HarmonyPostfix]
        public static void Postfix(CashInstance __instance, float __0)
        {
            if (!CashStackingConfig.TryGet(out float cap))
                return;

            float desired = Mathf.Min(__0, cap);
            if (__instance.Balance < desired)
                __instance.Balance = desired;
        }
    }

    [HarmonyPatch(typeof(CashInstance), nameof(CashInstance.ChangeBalance))]
    public class CashChangeBalancePatch
    {
        [HarmonyPrefix]
        public static void Prefix(CashInstance __instance, out float __state)
        {
            __state = __instance.Balance;
        }

        [HarmonyPostfix]
        public static void Postfix(CashInstance __instance, float __0, float __state)
        {
            if (!CashStackingConfig.TryGet(out float cap))
                return;

            float desired = Mathf.Min(__state + __0, cap);
            if (desired > __instance.Balance)
                __instance.Balance = desired;
        }
    }

    // Allow cash to be dropped into a slot even when the combined balance would exceed $1000, but only
    // for slots that genuinely accept cash (so we never override the "this slot can't hold cash" rule).
    [HarmonyPatch(typeof(ItemUIManager), nameof(ItemUIManager.CanCashBeDraggedIntoSlot))]
    public class CashCanDragIntoSlotPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemSlotUI __0, ref bool __result)
        {
            if (__result)
                return;
            if (!CashStackingConfig.TryGet(out float _))
                return;
            if (__0 == null || __0.assignedSlot == null)
                return;

            ItemSlot slot = __0.assignedSlot;
            CashInstance stored = slot.ItemInstance != null ? slot.ItemInstance.TryCast<CashInstance>() : null;
            // Allow the drop onto a slot that already holds cash (the join case) or an empty cash-capable
            // slot. CanSlotAcceptCash() alone rejects a slot that is already at the $1000 cap.
            if (stored != null || (slot.ItemInstance == null && slot.CanSlotAcceptCash()))
                __result = true;
        }
    }

    // Report a larger remaining capacity for cash so the shift-click/quick-transfer and drag-combine
    // paths (which size the merge by GetCapacityForItem) move up to the configured cap instead of $1000.
    // The actual money movement still flows through the game's own transfer logic, so it stays balanced.
    [HarmonyPatch(typeof(ItemSlot), nameof(ItemSlot.GetCapacityForItem))]
    public class CashSlotCapacityPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemSlot __instance, ItemInstance __0, ref int __result)
        {
            if (!CashStackingConfig.TryGet(out float cap))
                return;

            CashInstance incoming = __0 != null ? __0.TryCast<CashInstance>() : null;
            if (incoming == null)
                return;

            CashInstance stored = __instance.ItemInstance != null ? __instance.ItemInstance.TryCast<CashInstance>() : null;
            // Cash-compatible = the slot already holds cash (even if "full"), or it is empty and allowed
            // to hold cash. We must not require CanSlotAcceptCash() alone, because that returns false for
            // a slot already at the $1000 cap — which is exactly the join-stacks case we need to enable.
            bool cashCompatible = stored != null || (__instance.ItemInstance == null && __instance.CanSlotAcceptCash());
            if (!cashCompatible)
                return;

            float existing = stored != null ? stored.Balance : 0f;
            int boosted = Mathf.Max(0, Mathf.FloorToInt(cap - existing));
            if (boosted > __result)
                __result = boosted;
        }
    }

    // Two cash items are considered "stackable" so dropping one onto another MERGES (combines balances)
    // instead of swapping. Cash has StackLimit 1, so the generic quantity-based check returns false for
    // a non-empty cash slot — which is why dragging a $200 stack onto a $1000 stack just swapped them.
    [HarmonyPatch(typeof(ItemInstance), nameof(ItemInstance.CanStackWith))]
    public class CashCanStackPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemInstance __instance, ItemInstance __0, ref bool __result)
        {
            if (__result)
                return;
            if (!CashStackingConfig.TryGet(out float _))
                return;

            CashInstance a = __instance != null ? __instance.TryCast<CashInstance>() : null;
            CashInstance b = __0 != null ? __0.TryCast<CashInstance>() : null;
            if (a != null && b != null)
                __result = true;
        }
    }
}
