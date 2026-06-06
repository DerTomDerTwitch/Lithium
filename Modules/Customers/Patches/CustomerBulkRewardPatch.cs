using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.UI.Handover;
using Lithium.Modules.Customers.Architecture;
using UnityEngine;

namespace Lithium.Modules.Customers.Patches
{
    [HarmonyPatch(typeof(Customer), nameof(Customer.ProcessHandoverServerSide))]
    public static class CustomerBulkRewardPatch
    {
        private static bool _armed;
        private static float _effectiveMultiplier;
        private static float _relationshipBefore;
        private static int _totalXpBefore;

        [HarmonyPrefix]
        public static void Prefix(Customer __instance, HandoverScreen.EHandoverOutcome outcome)
        {
            _armed = false;

            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled)
                return;

            BulkRewards bulk = config.Contracts.BulkRewards;
            if (bulk == null || !bulk.Enabled)
                return;

            if (outcome != HandoverScreen.EHandoverOutcome.Finalize)
                return;

            if (!InstanceFinder.IsServer)
                return;

            if (!config.OrderPatterns.Enabled || LevelManager.Instance == null ||
                !config.OrderPatterns.RankMet())
                return;

            if (__instance == null || __instance.CustomerData == null ||
                __instance.NPC == null || __instance.NPC.RelationData == null)
                return;

            float multiplier = OrderPatternProfile.Create(
                __instance.CustomerData.name,
                __instance.CustomerData.MinOrdersPerWeek,
                __instance.CustomerData.MaxOrdersPerWeek).QuantityMultiplier;

            if (multiplier <= 1f)
                return;

            _effectiveMultiplier = Mathf.Min(multiplier, Mathf.Max(1f, bulk.MaxRewardMultiplier));
            _relationshipBefore = __instance.NPC.RelationData.RelationDelta;
            _totalXpBefore = LevelManager.Instance.TotalXP;
            _armed = true;
        }

        [HarmonyPostfix]
        public static void Postfix(Customer __instance)
        {
            if (!_armed)
                return;
            _armed = false;

            BulkRewards bulk = Core.Get<ModCustomers>().Configuration.Contracts.BulkRewards;

            float extra = _effectiveMultiplier - 1f;

            if (bulk.ScaleRelationship && __instance != null &&
                __instance.NPC != null && __instance.NPC.RelationData != null)
            {
                float gain = __instance.NPC.RelationData.RelationDelta - _relationshipBefore;
                if (gain > 0f)
                {
                    float bonus = gain * extra;
                    __instance.NPC.RelationData.ChangeRelationship(bonus, true);
                    if (Log.DebugEnabled)
                        Log.Info($"[Lithium] Bulk affection x{_effectiveMultiplier:F2} for " +
                            $"{__instance.CustomerData.name}: +{bonus:F2} (base {gain:F2}).");
                }
            }

            if (bulk.ScaleXP && LevelManager.Instance != null)
            {
                int xpGain = LevelManager.Instance.TotalXP - _totalXpBefore;
                if (xpGain > 0)
                {
                    int bonus = Mathf.RoundToInt(xpGain * extra);
                    if (bonus > 0)
                    {
                        LevelManager.Instance.AddXP(bonus);
                        if (Log.DebugEnabled)
                            Log.Info($"[Lithium] Bulk XP x{_effectiveMultiplier:F2}: +{bonus} (base {xpGain}).");
                    }
                }
            }
        }
    }
}
