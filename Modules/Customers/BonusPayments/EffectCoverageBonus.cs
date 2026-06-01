using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Quests;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;
using MelonLoader;
using UnityEngine;

namespace Lithium.Modules.Customers.BonusPayments
{
    public class EffectCoverageBonus : IBonusPaymentHandler
    {
        public bool BonusPaymentHandler(Customer customer, Contract contract, List<ItemInstance> items, out List<Contract.BonusPayment> boni)
        {
            boni = [];
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;

            if (!config.EffectBonus.Enabled)
                return false;

            if (!config.EffectBonus.AffectsDealers && contract.Dealer != null)
                return false;

            List<string> desires = customer.CustomerData.PreferredProperties.ToList().Select(p => p.Name.ToLowerInvariant()).ToList();
            if (desires.Count == 0 || items == null || items.Count == 0)
                return false;

            int totalUnits = items.ToList().Sum(i => i?.Quantity ?? 0);
            float totalBonusAmount = 0f;

            foreach (ItemInstance item in items)
            {
                if (item == null)
                    continue;

                ProductDefinition pd = ProductManager.DiscoveredProducts.ToList().SingleOrDefault(p => p.ID.Equals(item.ID));
                if (pd == null)
                    continue;

                int matchCount = pd.Properties.ToList().Select(p => p.Name.ToLowerInvariant()).Intersect(desires).Count();
                if (matchCount <= 0)
                    continue;

                EffectMatchBonus bucket = matchCount switch
                {
                    1 => config.EffectBonus.OneCoveredEffect,
                    2 => config.EffectBonus.TwoCoveredEffects,
                    _ => config.EffectBonus.ThreeCoveredEffects
                };

                int quantity = Mathf.Max(1, item.Quantity);
                int fixedMin = Mathf.Min(bucket.FixedBonusMin, bucket.FixedBonusMax);
                int fixedMax = Mathf.Max(bucket.FixedBonusMin, bucket.FixedBonusMax);
                float pctMin = Mathf.Min(bucket.PercentageBonusMin, bucket.PercentageBonusMax);
                float pctMax = Mathf.Max(bucket.PercentageBonusMin, bucket.PercentageBonusMax);
                int fixedPerUnit = (fixedMax > fixedMin) ? UnityEngine.Random.Range(fixedMin, fixedMax + 1) : fixedMin;
                float pctForThisItem = (pctMax > pctMin) ? UnityEngine.Random.Range(pctMin, pctMax) : pctMin;

                float fixedPart = fixedPerUnit * quantity;
                float deliveryShare = quantity / (float)totalUnits; // in [0,1]
                float percentPart = contract.Payment * (pctForThisItem / 100f) * deliveryShare;

                float itemBonus = fixedPart + percentPart;
                totalBonusAmount += itemBonus;
                Log.Info($"Effect match boni for item {pd.Name}: x{quantity} (matches: {matchCount}) Fixed: {fixedPart} Percent: {percentPart} Total: {itemBonus}");
            }

            if (totalBonusAmount <= 0f)
                return false;

            boni.Add(new("Effect Match Bonus", totalBonusAmount));
            return true;
        }
    }
}
