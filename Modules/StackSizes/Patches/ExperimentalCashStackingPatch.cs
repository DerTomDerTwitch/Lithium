using HarmonyLib;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Items;
using UnityEngine;

namespace Lithium.Modules.StackSizes.Patches
{
    internal static class CashStackingConfig
    {
        public static float DragAmount;
        public static bool DragActive;

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

        public static bool BeginScrollTick()
        {
            int frame = Time.frameCount;
            if (frame == _lastStepFrame)
                return false;
            _lastStepFrame = frame;
            return true;
        }

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

        public static int AcceleratedStep(float amount)
        {
            float now = Time.unscaledTime;
            float dt = Mathf.Max(now - _lastScrollTime, 0.0001f);
            _lastScrollTime = now;

            float speed01 = Mathf.Clamp01((0.18f - dt) / 0.16f);
            float curve = speed01 * speed01 * speed01;
            float maxStep = Mathf.Max(5f, amount * 0.33f);
            return SnapToLadder(Mathf.Lerp(1f, maxStep, curve));
        }

        public static float SourceMax(CashInstance source, float cap)
        {
            float bal = source != null ? source.Balance : cap;
            return Mathf.Min(cap, bal);
        }
    }

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
                return;

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
        [HarmonyPostfix]
        public static void Postfix(ItemUIManager __instance, CashInstance __0)
        {
            if (!CashStackingConfig.TryGet(out float cap) || !__instance.isDraggingCash || !CashStackingConfig.DragActive)
                return;

            CashStackingConfig.DragAmount = Mathf.Min(CashStackingConfig.DragAmount, CashStackingConfig.SourceMax(__0, cap));
            __instance.draggedCashAmount = CashStackingConfig.DragAmount;
        }
    }

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
            if (stored != null || (slot.ItemInstance == null && slot.CanSlotAcceptCash()))
                __result = true;
        }
    }

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
            bool cashCompatible = stored != null || (__instance.ItemInstance == null && __instance.CanSlotAcceptCash());
            if (!cashCompatible)
                return;

            float existing = stored != null ? stored.Balance : 0f;
            int boosted = Mathf.Max(0, Mathf.FloorToInt(cap - existing));
            if (boosted > __result)
                __result = boosted;
        }
    }

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
