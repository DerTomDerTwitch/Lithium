using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Quests;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;
using MelonLoader;
using UnityEngine;

namespace Lithium.Modules.Customers.Patches
{
    [HarmonyPatch(typeof(Customer), nameof(Customer.ProcessHandoverServerSide))]
    public static class CustomerProcessHandoverPatch
    {
        [HarmonyPrefix]
        public static void Prefix(
            Customer __instance,
            Il2CppSystem.Collections.Generic.List<ItemInstance> items,
            bool handoverByPlayer,
            ref float totalPayment)
        {
            ModCustomers modCustomers = Core.Get<ModCustomers>();
            if (modCustomers == null)
                return;

            if (!InstanceFinder.IsServer)
                return;

            ModCustomersConfiguration config = modCustomers.Configuration;
            if (!config.Enabled || !config.EffectBonus.Enabled)
                return;

            if (!handoverByPlayer && !config.EffectBonus.AffectsDealers)
                return;

            Contract contract = __instance.CurrentContract;
            if (contract == null || items == null)
                return;

            List<ItemInstance> itemInstances = items.ToList();

            float effectBonus = 0f;
            foreach (IBonusPaymentHandler handler in modCustomers.BonusPaymentHandlers)
            {
                if (handler.TryCalculateBonus(__instance, contract, itemInstances, out List<Contract.BonusPayment> bonus))
                {
                    float sum = bonus.Sum(b => b.Amount);
                    if (sum > 0f)
                        effectBonus += sum;
                }
            }

            if (effectBonus <= 0f)
                return;

            totalPayment = Mathf.Clamp(totalPayment + effectBonus, 0f, float.MaxValue);
            Log.Info($"[Lithium] Effect match bonus applied: +{effectBonus} (new payout {totalPayment})");
        }
    }
}
