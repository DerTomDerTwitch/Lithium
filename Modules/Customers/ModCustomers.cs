using System.Collections;
using Il2CppInterop.Runtime.Injection;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;
using Lithium.Modules.Customers.Behaviours;
using Lithium.Modules.Customers.BonusPayments;
using MelonLoader;
using UnityEngine;

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
        public bool RequireEffectMatch { get; set; } = true;
        public int MaxQualityOverDeliveryLevels { get; set; } = 1;
        public float DrugAffinitySharpness { get; set; } = 0.5f;
    }

    public class DirectSales
    {
        public bool RequireEffectMatch { get; set; } = true;

        public float MinIntervalFractionBeforeOffer { get; set; } = 0.5f;

        public float PriceToleranceMultiplier { get; set; } = 1.0f;

        public float PriceToleranceJitter { get; set; } = 0f;
    }

    public class Contracts
    {
        public bool Enabled { get; set; }
        public int XPRequired { get; set; } = 1400;
        public bool SendNotification { get; set; } = true;
        public bool SendNotificationForDealers { get; set; } = true;
        public int NotificationCooldownInMinutes { get; set; } = 5;

        public int NotificationWindowStartHour { get; set; } = 8;
        public int NotificationWindowEndHour { get; set; } = 22;
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

        public float ReducedDealPriceMultiplier { get; set; } = 0.75f;

        public bool DealerSellAtListedPrice { get; set; } = true;

        public bool SellAtListedPrice { get; set; } = true;

        public bool RetryNextDayOnRefusal { get; set; } = true;

        public AcceptanceWindow AcceptanceWindow { get; set; } = new AcceptanceWindow();

        public ProductSelection ProductSelection { get; set; } = new ProductSelection();

        public BulkRewards BulkRewards { get; set; } = new BulkRewards();

        public string[] ReducedSaleTemplates { get; set; } =
        [
            "Couldn't find anything ##DESIRES##, so I grabbed something else — paying less for it though.",
            "You had nothing ##DESIRES##, so I settled for a substitute at a lower price.",
            "No ##DESIRES##? I'll take something else, but I'm not paying full price for it.",
            "Bought something off you, but since it wasn't ##DESIRES## I knocked the price down."
        ];
        public string[] ReducedDealerTemplates { get; set; } =
        [
            "##DEALER## had nothing ##DESIRES##, so I took a substitute at a lower price.",
            "Since ##DEALER## didn't have ##DESIRES##, I settled for something cheaper.",
            "No ##DESIRES## from ##DEALER## — bought something else, but paid less for it.",
            "Got something from ##DEALER##, but it wasn't ##DESIRES## so I paid a reduced price."
        ];
    }

    public class ProductSelection
    {
        public float CoverageBiasExponent { get; set; } = 3f;

        public bool EnableSecondProduct { get; set; } = true;

        public float SecondProductChance { get; set; } = 0.5f;

        public float SecondProductQuantityShare { get; set; } = 0.25f;
    }

    public class BulkRewards
    {
        public bool Enabled { get; set; } = true;

        public bool ScaleRelationship { get; set; } = true;

        public bool ScaleXP { get; set; } = true;

        public float MaxRewardMultiplier { get; set; } = 7f;
    }

    public class OrderPatternWeights
    {
        public float Weekly { get; set; } = 55f;
        public float TwiceWeekly { get; set; } = 30f;
        public float EveryThreeDays { get; set; } = 15f;
    }

    public class OrderPatterns
    {
        public bool Enabled { get; set; }

        public float BulkOrderSizeFactor { get; set; } = 1.0f;

        public OrderPatternWeights ArchetypeWeights { get; set; } = new OrderPatternWeights();

        public bool AnnounceNextOrder { get; set; } = true;

        public bool ShowPatternInContactPanel { get; set; } = true;

        public string[] NextOrderTemplates { get; set; } =
        [
            "Thanks, good doing business! I'll hit you up again ##DAY##.",
            "Appreciate it — I'll call you back ##DAY##.",
            "Solid as always. Expect to hear from me ##DAY##.",
            "Nice doing business. I'll be in touch again ##DAY##."
        ];
    }

    public class AcceptanceWindow
    {
        public bool Enabled { get; set; } = true;

        public float DurationMultiplier { get; set; } = 1.5f;

        public int BaseQuantity { get; set; } = 10;

        public float MinutesPerExtraUnit { get; set; } = 60f;

        public int MaxWindowMinutes { get; set; } = 8640;

        public bool SendDeadlineMessage { get; set; } = true;

        public string[] OfferDeadlineTemplates { get; set; } =
        [
            "You've got until ##DEADLINE## to let me know.",
            "Let me know by ##DEADLINE##.",
            "I'll need your answer by ##DEADLINE##.",
            "Hit me back by ##DEADLINE## or I'll assume you're not interested."
        ];
    }

    public class CoverageNotifications
    {
        public bool Enabled { get; set; }
        public string ContactNpcName { get; set; } = "Manny Oakfield";
        public string ContactDisplayName { get; set; } = "Lithium";
        public bool ListUncovered { get; set; }
        public bool NotifyDealerInventoryOnClose { get; set; }
        public bool SendStartupOverview { get; set; } = true;
        public bool NotifyNoDealerCustomers { get; set; } = true;
    }

    public class ModCustomersConfiguration : ModuleConfiguration
    {
        public override string Name => "Customers";
        public SampleOffering SampleOffering { get; set; } = new SampleOffering();
        public DirectSales DirectSales { get; set; } = new DirectSales();
        public Contracts Contracts { get; set; } = new Contracts();
        public EffectBonus EffectBonus { get; set; } = new EffectBonus();
        public OrderPatterns OrderPatterns { get; set; } = new OrderPatterns();
        public CoverageNotifications Coverage { get; set; } = new CoverageNotifications();

        public override void Validate()
        {
            Contracts.NotificationWindowStartHour = ConfigValidator.InRange(
                Name, "Contracts.NotificationWindowStartHour", Contracts.NotificationWindowStartHour, 0, 23);
            Contracts.NotificationWindowEndHour = ConfigValidator.InRange(
                Name, "Contracts.NotificationWindowEndHour", Contracts.NotificationWindowEndHour, 1, 24);
            if (Contracts.NotificationWindowStartHour >= Contracts.NotificationWindowEndHour)
            {
                Log.Warning($"[Lithium] {Name}: 'Contracts.NotificationWindowStartHour' " +
                            $"({Contracts.NotificationWindowStartHour}) must be before " +
                            $"'Contracts.NotificationWindowEndHour' ({Contracts.NotificationWindowEndHour}); " +
                            "reverting to 8–22.");
                Contracts.NotificationWindowStartHour = 8;
                Contracts.NotificationWindowEndHour = 22;
            }
        }
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
            ProductCoverageNotifier.Reset();
            DealerCoverageNotifier.ResetNoDealer();
            ContractRetryTracker.Unload();
            OfferDeadlineTracker.Unload();
            OrderPatternProfile.ClearCache();
            Patches.CustomerContractGenerationPatch.ResetState();

            if (!Configuration.Enabled)
                return;

            if (Configuration.Coverage.Enabled &&
                (Configuration.Coverage.SendStartupOverview || Configuration.Coverage.NotifyNoDealerCustomers))
                MelonCoroutines.Start(StartupOverviewRoutine());
        }

        private static IEnumerator StartupOverviewRoutine()
        {
            float waited = 0f;
            while (waited < 30f && !LithiumStartupReport.WorldReady())
            {
                yield return new WaitForSeconds(1f);
                waited += 1f;
            }

            if (!LithiumStartupReport.WorldReady())
                yield break;

            yield return new WaitForSeconds(2f);

            CoverageNotifications coverage = Core.Get<ModCustomers>().Configuration.Coverage;
            if (coverage.SendStartupOverview)
                LithiumStartupReport.Send();
            if (coverage.NotifyNoDealerCustomers)
                DealerCoverageNotifier.ReportNoDealerCustomers();
        }

        public void RegisterBonusPaymentHandler(IBonusPaymentHandler handler)
        {
            if (BonusPaymentHandlers.All(h => h.GetType() != handler.GetType()))
                BonusPaymentHandlers.Add(handler);
        }
    }
}
