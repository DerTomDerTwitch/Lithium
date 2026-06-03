using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Quests;

namespace Lithium.Modules.Customers.Architecture
{
    public interface IBonusPaymentHandler
    {
        // Computes any bonus payments this handler awards for the given handover. Returns true (with
        // boni populated) when it contributes a bonus, false (and an empty list) otherwise.
        bool TryCalculateBonus(Customer customer, Contract contract, List<ItemInstance> items, out List<Contract.BonusPayment> boni);
    }
}
