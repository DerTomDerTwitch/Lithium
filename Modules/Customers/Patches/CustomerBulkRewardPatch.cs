using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.UI.Handover;
using Lithium.Modules.Customers.Architecture;
using UnityEngine;

namespace Lithium.Modules.Customers.Patches
{
    /// <summary>
    /// Scales the affection (relationship) and XP rewards of a completed delivery by the
    /// customer's bulk-order multiplier, so a customer that orders rarely-but-large pays out
    /// proportionally more per delivery.
    ///
    /// IMPORTANT — patch target. This *must* patch <c>ProcessHandover</c>, not the
    /// <c>ProcessHandoverServerSide</c> RPC writer, because neither reward is observable around
    /// the writer:
    ///   • The relationship gain (<c>NPC.RelationData.ChangeRelationship</c>) is applied
    ///     synchronously inside <c>ProcessHandover</c> *before* it calls the writer, so a
    ///     prefix/postfix on the writer always measures a zero delta.
    ///   • The base XP award (<c>AddXP(20)</c> / <c>AddXP(10)</c>) lives in the deferred
    ///     server RPC logic (<c>RpcLogic___ProcessHandoverServerSide</c>), and <c>AddXP</c> is
    ///     itself a ServerRpc→ObserversRpc chain that only bumps <c>TotalXP</c> several network
    ///     ticks later — never within the synchronous call window, so a <c>TotalXP</c> delta is
    ///     always zero too.
    /// Inside <c>ProcessHandover</c> the relationship delta *is* measurable; the base XP is a
    /// flat constant, so we award the bonus directly rather than measuring it.
    /// </summary>
    [HarmonyPatch(typeof(Customer), nameof(Customer.ProcessHandover))]
    public static class CustomerBulkRewardPatch
    {
        // Flat base XP the vanilla server logic (RpcLogic___ProcessHandoverServerSide) awards
        // the player per completed handover.
        private const int PlayerHandoverXP = 20;
        private const int PlayerDealerHandoverXP = 10;

        private static bool _armed;
        private static float _effectiveMultiplier;
        private static int _baseXp;
        private static float _relationshipBefore;

        [HarmonyPrefix]
        public static void Prefix(Customer __instance, HandoverScreen.EHandoverOutcome outcome,
            Contract contract, bool handoverByPlayer)
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

            // No InstanceFinder.IsServer gate — ProcessHandover runs exactly once per handover on
            // the *initiating* peer (the client for a client's own player-handover; the server for
            // dealer handovers), so a server gate would silently skip every client-side delivery.
            // Both rewards self-route to the server: AddXP is a [ServerRpc], and
            // ChangeRelationship(network:true) → NPC.SendRelationship is a [ServerRpc] that the
            // server re-broadcasts to all observers — exactly how vanilla applies the base values
            // from this same method. Applied once on the initiator, replicated correctly to all.

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
            _baseXp = ResolveBaseXp(handoverByPlayer, contract);
            // Captured before the original runs ChangeRelationship (line ~1444 in vanilla),
            // so the postfix delta is exactly that handover's base relationship gain.
            _relationshipBefore = __instance.NPC.RelationData.RelationDelta;
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
            if (extra <= 0f)
                return;

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

            if (bulk.ScaleXP && _baseXp > 0 && LevelManager.Instance != null)
            {
                int bonus = Mathf.RoundToInt(_baseXp * extra);
                if (bonus > 0)
                {
                    LevelManager.Instance.AddXP(bonus);
                    if (Log.DebugEnabled)
                        Log.Info($"[Lithium] Bulk XP x{_effectiveMultiplier:F2}: +{bonus} (base {_baseXp}).");
                }
            }
        }

        // Mirrors the vanilla XP grant in RpcLogic___ProcessHandoverServerSide: a player handover
        // grants 20, a player-dealer handover grants 10, any other dealer (cartel) grants the
        // player nothing — so there is no base XP to scale in that case.
        private static int ResolveBaseXp(bool handoverByPlayer, Contract contract)
        {
            if (handoverByPlayer)
                return PlayerHandoverXP;

            if (contract != null && contract.Dealer != null &&
                contract.Dealer.DealerType == EDealerType.PlayerDealer)
                return PlayerDealerHandoverXP;

            return 0;
        }
    }
}
