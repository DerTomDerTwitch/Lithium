using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Quests;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Modules.EffectCombos.BonusPayments
{
    public class EffectComboBonus : IBonusPaymentHandler
    {
        public bool TryCalculateBonus(Customer customer, Contract contract, List<ItemInstance> items, out List<Contract.BonusPayment> boni)
        {
            boni = [];
            ModEffectCombosConfiguration configuration = Core.Get<ModEffectCombos>().Configuration;
            if(!configuration.Enabled)
                return false;

            if (!configuration.AffectsDealers && contract.Dealer != null)
                return false;

            int totalUnits = items.ToList().Sum(i => i?.Quantity ?? 0);
            foreach (ItemInstance item in items)
            {
                if(item == null)
                    continue;

                ProductDefinition pd = ProductManager.DiscoveredProducts.ToList()
                    .SingleOrDefault(p => p.ID.Equals(item.ID));
                if(pd == null)
                    continue;

                string[] effects = pd.Properties.ToList().Select(p => p.Name.ToLowerInvariant()).ToArray();
                float deliveryShare = totalUnits > 0 ? item.Quantity / (float)totalUnits : 0f; // in [0,1]

                // Only the single best-paying combo this item qualifies for is awarded, rather than
                // stacking every matching combo.
                EffectCombo bestCombo = null;
                float bestBonus = 0f;
                foreach (EffectCombo combo in configuration.Combos)
                {
                    if (combo.Effects == null || combo.Effects.Length == 0)
                        continue;
                    if (combo.Effects.Select(s => s.ToLowerInvariant()).Intersect(effects).Count() != combo.Effects.Length)
                        continue;

                    float fixedPart = combo.FixedBonus * item.Quantity;
                    float percentPart = contract.Payment * (combo.PercentageBonus / 100f) * deliveryShare;
                    float itemBonus = fixedPart + percentPart;

                    if (bestCombo == null || itemBonus > bestBonus)
                    {
                        bestCombo = combo;
                        bestBonus = itemBonus;
                    }
                }

                if (bestCombo != null && bestBonus > 0f)
                    boni.Add(new($"\"{bestCombo.Name}\" Combo Bonus", bestBonus));
            }

            return boni.Any();
        }
    }
}
