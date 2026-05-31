using Il2CppInterop.Runtime.Injection;
using Lithium.Modules.Customers.Architecture;
using Lithium.Modules.Customers.Behaviours;
using Lithium.Modules.Customers.BonusPayments;

namespace Lithium.Modules.Customers
{
    public class EffectMatchBonus
    {
        public float PercentageBonusMin { get; set; } = 0f;
        public float PercentageBonusMax { get; set; } = 0f;
        public int FixedBonusMin { get; set; } = 0;
        public int FixedBonusMax { get; set; } = 0;
    }

    public class EffectBonus
    {
        public bool Enabled { get; set; }
        public bool AffectsDealers { get; set; } = true;

        // Bonus scales with how many of the customer's desired effects the product covers: a small
        // tip for one match, a solid bonus for two, a big payout for three+. Fixed is per unit,
        // percentage is a share of the contract payment.
        public EffectMatchBonus OneCoveredEffect { get; set; } =
            new() { FixedBonusMin = 2, FixedBonusMax = 5, PercentageBonusMin = 1f, PercentageBonusMax = 3f };
        public EffectMatchBonus TwoCoveredEffects { get; set; } =
            new() { FixedBonusMin = 5, FixedBonusMax = 10, PercentageBonusMin = 3f, PercentageBonusMax = 7f };
        public EffectMatchBonus ThreeCoveredEffects { get; set; } =
            new() { FixedBonusMin = 10, FixedBonusMax = 20, PercentageBonusMin = 7f, PercentageBonusMax = 15f };
    }

    public class SampleOffering
    {
        public bool Enabled { get; set; }
        public float QualityLevelModifier { get; set; } = 0.2f;
        public bool IncludeDrugPreference { get; set; } = true;
        public float BaseAcceptance { get; set; } = 0.0f;
    }

    public class Contracts
    {
        public bool Enabled { get; set; }
        public int XPRequired { get; set; } = 1400;
        public bool SendNotification { get; set; } = true;
        public bool SendNotificationForDealers { get; set; } = true;
        public int NotificationCooldownInMinutes { get; set; } = 5;
        public string[] MessageTemplates { get; set; } =
        [
            "Hey, I wanted to get fresh stuff, but you don't offer good stuff. I prefer ##DESIRES##",
            "I was looking for something ##DESIRES##, but you don't have any. Please improve your offering.",
            "Yo you still haven't got anything ##DESIRES##? Dang!",
            "##DESIRES## ... come on, can't be that hard to find, right?"
        ];
        public string[] DealerTemplates { get; set; } =
        [
            "Hey, I wanted to get fresh stuff, but ##DEALER## doesn't offer good stuff. I prefer ##DESIRES##",
            "I was looking for something ##DESIRES##, but ##DEALER## doesn't have any. Could you help him out?",
            "Yo ##DEALER## still hasn't got anything ##DESIRES##? Dang!",
            "##DESIRES## ... come on, can't be that hard for ##DEALER## to find, right?"
        ];
    }

    public class ModCustomersConfiguration : ModuleConfiguration
    {
        public override string Name => "Customers";
        public SampleOffering SampleOffering { get; set; } = new SampleOffering();
        public Contracts Contracts { get; set; } = new Contracts();
        public EffectBonus EffectBonus { get; set; } = new EffectBonus();
    }

    public class ModCustomers : ModuleBase<ModCustomersConfiguration>
    {
        internal readonly List<IBonusPaymentHandler> BonusPaymentHandlers = [];

        public ModCustomers()
        {
            ClassInjector.RegisterTypeInIl2Cpp<CustomerNotificationState>();
            RegisterBonusPaymentHandler(new EffectCoverageBonus());
        }

        public override void Apply()
        {
            if (!Configuration.Enabled)
                return;
        }

        public void RegisterBonusPaymentHandler(IBonusPaymentHandler handler)
        {
            if (BonusPaymentHandlers.All(h => h.GetType() != handler.GetType()))
                BonusPaymentHandlers.Add(handler);
        }
    }
}
