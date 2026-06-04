using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Quests;

namespace Lithium.Modules.Customers.Architecture
{
    public interface IBonusPaymentHandler
    {
        bool TryCalculateBonus(Customer customer, Contract contract, List<ItemInstance> items, out List<Contract.BonusPayment> boni);
    }
}
