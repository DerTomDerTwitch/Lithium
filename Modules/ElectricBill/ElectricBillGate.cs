using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Property;

namespace Lithium.Modules.ElectricBill
{
    // Shared gate consulted by every freeze patch. Returns true when the given buildable's property is
    // currently powered off (and the module is enabled), meaning the patched method should be skipped.
    // Reads only the in-memory PowerCutCodes set, so it is cheap to call every tick.
    internal static class ElectricBillGate
    {
        public static bool IsCut(BuildableItem station)
        {
            if (station == null)
                return false;

            ModElectricBill module = Core.Get<ModElectricBill>();
            if (module == null || !module.Configuration.Enabled)
                return false;

            Property prop = station.ParentProperty;
            return prop != null && ModElectricBill.IsPowerCut(prop.PropertyCode);
        }

        // Overload for Business-rooted patches (LaunderingStation laundering runs on the Business, which
        // is itself a Property).
        public static bool IsCut(Property property)
        {
            if (property == null)
                return false;

            ModElectricBill module = Core.Get<ModElectricBill>();
            if (module == null || !module.Configuration.Enabled)
                return false;

            return ModElectricBill.IsPowerCut(property.PropertyCode);
        }
    }
}
