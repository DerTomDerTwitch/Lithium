using System.Collections.Generic;

namespace Lithium.Modules.ElectricBill
{
    // Per-save, per-property runtime state for the electric bill. Plain POCO serialized with
    // Newtonsoft.Json, persisted via SaveSlotStore keyed by the property's stable PropertyCode.
    // Mirrors the shape of Rent's RentLocationState.
    public class ElectricBillState
    {
        // Running meter since the last weekly bill, in watt-minutes (watts summed once per in-game
        // minute). Accrues nothing while PoweredOff. Converted to kWh at billing time.
        public float AccruedWattMinutes;

        // Same meter broken down per appliance id (plus the synthetic built-in-lights id), so the phone
        // app can show the actual cost accrued by each source this billing period. Sums to
        // AccruedWattMinutes; reset together at billing.
        public Dictionary<string, float> AccruedByAppliance = new();

        // ElapsedDays of the last weekly rollover. -1 = not yet initialised (anchored on first sight).
        public int LastBilledDay = -1;

        // Money still owed after an auto-deduct that the online balance couldn't cover. 0 when paid.
        public float OutstandingBill;

        // True while the property is powered off for non-payment: appliances are forced off / frozen
        // and metering is suspended. Cleared when the outstanding bill is auto-paid.
        public bool PoweredOff;

        // Captured at the moment of cutoff: each light appliance's GUID -> whether it was on. Used to
        // restore the player's exact on/off layout when power returns. Empty while powered on.
        public Dictionary<string, bool> LightStatesAtCutoff = new();
    }
}
