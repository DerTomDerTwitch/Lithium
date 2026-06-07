using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.Property;
using UnityEngine;

namespace Lithium.Modules.Banking.Patches
{
    /// <summary>
    /// Charges the configured laundering cut when a business's laundering operation completes.
    ///
    /// The previous target, <c>Business.CompleteOperation</c>, is a <c>protected</c> helper with a single
    /// internal caller (<c>MinsPass</c>), so the IL2CPP build can inline it and the postfix would silently
    /// never run. Completion is therefore detected at the two un-inlinable delegate targets that reach it:
    /// <c>Business.MinPass</c> (per-minute, here) and <c>Business.TimeSkipped</c> (sleep/skip, in
    /// <c>LaunderCutSkipPatch</c>) — both real subscribers to <c>onMinutePass</c> / <c>onTimeSkip</c>.
    /// The prefix snapshots which operations will complete this tick (before <c>MinsPass</c> increments
    /// them); the postfix charges the cut after the original has paid the laundered amount out, preserving
    /// the original behaviour of capping the cut against the post-payout online balance.
    /// </summary>
    [HarmonyPatch(typeof(Business), nameof(Business.MinPass))]
    public class LaunderCutPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Business __instance, bool __runOriginal)
        {
            // ElectricBill's power-cut freeze (Priority.First) returned false: laundering is paused this
            // tick, so nothing completes and there is nothing to snapshot.
            if (!__runOriginal)
                return;

            LaunderCutSettler.Snapshot(__instance, 1);
        }

        [HarmonyPostfix]
        public static void Postfix(Business __instance, bool __runOriginal)
        {
            if (!__runOriginal)
                return;

            LaunderCutSettler.Settle(__instance);
        }
    }

    /// <summary>
    /// Shared completion-detection + cut-charging logic for the per-minute (<see cref="LaunderCutPatch"/>)
    /// and time-skip (<c>LaunderCutSkipPatch</c>) paths.
    /// </summary>
    internal static class LaunderCutSettler
    {
        private static readonly Dictionary<IntPtr, float> Pending = new();

        public static void Snapshot(Business business, int mins)
        {
            ModBankingConfiguration config = Core.Get<ModBanking>().Configuration;
            if (!config.Enabled || business == null)
                return;

            var ops = business.LaunderingOperations;
            if (ops == null)
                return;

            // MinsPass adds `mins` to each operation then completes any that reach completionTime. Predict
            // those here (operations still in the list have minutesSinceStarted < completionTime).
            float completing = 0f;
            for (int i = 0; i < ops.Count; i++)
            {
                LaunderingOperation op = ops[i];
                if (op == null)
                    continue;
                if (op.minutesSinceStarted + mins >= op.completionTime_Minutes)
                    completing += op.amount;
            }

            if (completing > 0f)
                Pending[business.Pointer] = completing;
            else
                Pending.Remove(business.Pointer);
        }

        public static void Settle(Business business)
        {
            if (business == null)
                return;

            IntPtr key = business.Pointer;
            if (!Pending.TryGetValue(key, out float laundered))
                return;
            Pending.Remove(key);

            if (laundered <= 0f)
                return;

            ModBankingConfiguration config = Core.Get<ModBanking>().Configuration;
            if (!config.Enabled)
                return;

            string name = business.PropertyName;

            float cutPercent = 0f;
            if (!string.IsNullOrEmpty(name)
                && config.Laundering.Businesses.TryGetValue(name, out BusinessLaunderingConfiguration businessCfg))
            {
                cutPercent = businessCfg.Cut;
            }

            float cut = laundered * (cutPercent / 100f);
            if (cut > 0f)
            {
                MoneyManager moneyManager = NetworkSingleton<MoneyManager>.Instance;
                if (moneyManager != null)
                {
                    cut = Mathf.Min(cut, Mathf.Max(0f, moneyManager.onlineBalance));
                    if (cut > 0f)
                        moneyManager.CreateOnlineTransaction("Laundering Cut", -cut, 1f, $"{name} laundering cut");
                }
                else
                {
                    cut = 0f;
                }
            }

            ModBanking.RecordLaundering(name, laundered, cut);
        }
    }
}
