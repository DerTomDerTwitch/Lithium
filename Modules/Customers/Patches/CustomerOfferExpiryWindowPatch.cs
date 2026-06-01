using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Quests;

namespace Lithium.Modules.Customers.Patches
{
    // Safety net for the deal-acceptance expiry. CustomerOfferDeadlinePatch extends the per-contract
    // ContractInfo.ExpiresAfter, which is the natural source UpdateOfferExpiry counts against. In case
    // the native check instead consults the global Customer.OFFER_EXPIRY_TIME_MINS, mirror the static to
    // this customer's offered-contract window for the duration of the check, then restore it so a large
    // order's extended window can never leak into another customer's default offer.
    [HarmonyPatch(typeof(Customer), nameof(Customer.UpdateOfferExpiry))]
    public class CustomerOfferExpiryWindowPatch
    {
        private static int _savedWindow;
        private static bool _overridden;

        [HarmonyPrefix]
        public static void Prefix(Customer __instance)
        {
            _overridden = false;

            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled)
                return;

            AcceptanceWindow window = config.Contracts.AcceptanceWindow;
            if (window == null || !window.Enabled)
                return;

            ContractInfo contract = __instance.OfferedContractInfo;
            if (contract == null || !contract.Expires)
                return;

            _savedWindow = Customer.OFFER_EXPIRY_TIME_MINS;
            Customer.OFFER_EXPIRY_TIME_MINS = contract.ExpiresAfter;
            _overridden = true;
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!_overridden)
                return;

            Customer.OFFER_EXPIRY_TIME_MINS = _savedWindow;
            _overridden = false;
        }
    }
}
