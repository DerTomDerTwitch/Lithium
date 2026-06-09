using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Quests;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;
using UnityEngine;

namespace Lithium.Modules.Customers.Patches
{
    [HarmonyPatch(typeof(Customer), nameof(Customer.Load), typeof(Il2CppScheduleOne.Persistence.Datas.CustomerData))]
    public class CustomerLoadOfferDeadlinePatch
    {
        private const int GraceMinutes = 180;

        [HarmonyPostfix]
        public static void Postfix(Customer __instance)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled)
                return;

            AcceptanceWindow window = config.Contracts.AcceptanceWindow;
            if (window == null)
                return;

            if (!InstanceFinder.IsServer)
                return;

            ContractInfo contract = __instance.OfferedContractInfo;
            if (contract == null || !contract.Expires || contract.Products == null)
                return;

            string name = __instance.CustomerData?.name;
            if (string.IsNullOrEmpty(name))
                return;

            int now = TimeManager.Instance.GetDateTime().GetMinSum();

            int quantity = ProductHelper.GetTotalQuantity(contract.Products);
            int baseWindow = Customer.OFFER_EXPIRY_TIME_MINS;
            int windowMins = window.Enabled
                ? OfferAcceptanceWindow.Extend(baseWindow, quantity, window)
                : baseWindow;

            int deadline;
            if (OfferDeadlineTracker.TryGet(name, out int existing))
            {
                if (existing > now)
                {
                    deadline = existing;
                }
                else
                {
                    // The tracked deadline lapsed while the game was closed (wake-save race or a
                    // pre-skip-shift save), yet the offer itself was saved alive. Honoring it would
                    // expire the offer with a bare "nvm" on the session's first minute tick. Grant a
                    // short grace — deliberately far below the full window so quit/reload cannot
                    // farm a fresh one.
                    deadline = now + Mathf.Min(windowMins, GraceMinutes);
                    OfferDeadlineTracker.Set(name, deadline);
                    Log.Info($"[Customers] Granted {name} a {deadline - now} min grace window for a " +
                        "save-restored offer whose deadline had lapsed.");
                }
            }
            else
            {
                deadline = now + windowMins;
                OfferDeadlineTracker.Set(name, deadline);
            }

            __instance.OfferedContractTime = TimeManager.Instance.GetDateTime();
        }
    }
}
