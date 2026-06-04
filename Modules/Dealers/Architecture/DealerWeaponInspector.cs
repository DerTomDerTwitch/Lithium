using System.Collections.Generic;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Core.Items.Framework;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Levelling;

namespace Lithium.Modules.Dealers.Architecture
{
    internal enum WeaponStatus
    {
        None,
        Outdated,
        Adequate
    }

    internal static class DealerWeaponInspector
    {
        private static List<float> _weaponRankLadder;

        public static WeaponStatus Classify(Dealer dealer)
        {
            if (dealer == null)
                return WeaponStatus.None;

            float? heldBest = BestHeldWeaponRank(dealer);
            if (heldBest == null)
                return WeaponStatus.None;

            float bestBuyable = BestBuyableWeaponRank();
            return heldBest.Value + 0.0001f >= bestBuyable ? WeaponStatus.Adequate : WeaponStatus.Outdated;
        }

        public static bool NeedsAttention(Dealer dealer) => Classify(dealer) != WeaponStatus.Adequate;

        private static float? BestHeldWeaponRank(Dealer dealer)
        {
            Il2CppSystem.Collections.Generic.List<ItemSlot> slots = dealer.GetAllSlots();
            if (slots == null)
                return null;

            float? best = null;
            foreach (ItemSlot slot in slots)
            {
                StorableItemDefinition weapon = AsWeapon(slot?.ItemInstance?.Definition);
                if (weapon == null)
                    continue;
                float r = RankFloat(weapon);
                if (best == null || r > best.Value)
                    best = r;
            }
            return best;
        }

        private static float BestBuyableWeaponRank()
        {
            EnsureLadder();
            if (_weaponRankLadder == null || _weaponRankLadder.Count == 0)
                return 0f;

            LevelManager lvl = LevelManager.Instance;
            if (lvl == null)
                return 0f;

            float playerRank = lvl.GetFullRank().ToFloat();
            float best = 0f;
            foreach (float r in _weaponRankLadder)
                if (r <= playerRank && r > best)
                    best = r;
            return best;
        }

        private static void EnsureLadder()
        {
            if (_weaponRankLadder != null)
                return;

            Registry registry = Registry.Instance;
            if (registry == null)
                return;

            List<float> ladder = new();
            Il2CppSystem.Collections.Generic.List<ItemDefinition> all = registry.GetAllItems();
            if (all != null)
            {
                foreach (ItemDefinition def in all)
                {
                    StorableItemDefinition weapon = AsWeapon(def);
                    if (weapon != null)
                        ladder.Add(RankFloat(weapon));
                }
            }

            _weaponRankLadder = ladder;
            Log.Info($"[Dealers] Weapon rank ladder built: {ladder.Count} weapon definition(s).");
        }

        private static StorableItemDefinition AsWeapon(ItemDefinition def)
        {
            if (def == null)
                return null;
            StorableItemDefinition storable = def.TryCast<StorableItemDefinition>();
            if (storable == null)
                return null;
            if (storable.Category != EItemCategory.Equipment)
                return null;
            if (storable.CombatUtility <= 0f)
                return null;
            return storable;
        }

        private static float RankFloat(StorableItemDefinition weapon) =>
            weapon.RequiresLevelToPurchase ? weapon.RequiredRank.ToFloat() : 0f;
    }
}
