using System.Collections;
using Il2CppInterop.Runtime.Injection;
using Il2CppScheduleOne.Levelling;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;
using Lithium.Modules.Customers.Behaviours;
using Lithium.Modules.Customers.BonusPayments;
using MelonLoader;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
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

        /// <summary>
        /// Below this rank, the entire Lithium sample-acceptance override is skipped and the game's
        /// own <c>GetSampleSuccess</c> calculation is used instead. This protects early-game players
        /// who cannot yet craft matching effects (e.g. before owning a Mixing Station). Combined
        /// with <see cref="MinRankTier"/> (e.g. Hoodlum + tier 2 = "Hoodlum II"). Defaults to
        /// Street_Rat I so the gate is met from the very start and the Lithium calculation applies
        /// immediately (unchanged behaviour).
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public ERank MinRank { get; set; } = ERank.Street_Rat;

        /// <summary>
        /// The tier (I–V, i.e. 1–5) within <see cref="MinRank"/> at which the Lithium sample
        /// calculation starts being applied.
        /// </summary>
        public int MinRankTier { get; set; } = 1;

        public int MaxQualityOverDeliveryLevels { get; set; } = 1;
        public float DrugAffinitySharpness { get; set; } = 0.5f;

        /// <summary>
        /// True when the local player's rank is at or above the configured <see cref="MinRank"/> /
        /// <see cref="MinRankTier"/>. Returns true when the level manager is unavailable so the
        /// Lithium calculation is applied by default. Uses <see cref="FullRank.ToFloat"/> for
        /// comparison (matching the codebase convention).
        /// </summary>
        public bool RankMet() => RankHelper.PlayerRankAtLeast(MinRank, MinRankTier);
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

        // When true, a customer's desired effects are a hard requirement: if nothing in the
        // available stock covers them, the order falls back to a reduced-price substitute deal
        // (ReducedDealPriceMultiplier). When false, effect coverage becomes a soft *preference*
        // only (see ProductSelection): the best-available product is still chosen — weighted
        // toward more covered effects and the customer's preferred drug type — but sold at full
        // price with no substitute discount. Decouples bulk/order-pattern sizing from the
        // effect-coverage requirement. DealerRequireEffectMatch is the same toggle for
        // dealer-fulfilled contracts, so the two paths can be governed independently.
        public bool RequireEffectMatch { get; set; } = true;

        public bool DealerRequireEffectMatch { get; set; } = true;

        public float ReducedDealPriceMultiplier { get; set; } = 0.75f;

        public bool DealerSellAtListedPrice { get; set; } = true;

        public bool SellAtListedPrice { get; set; } = true;

        public bool RetryNextDayOnRefusal { get; set; } = true;

        public AcceptanceWindow AcceptanceWindow { get; set; } = new AcceptanceWindow();

        public ProductSelection ProductSelection { get; set; } = new ProductSelection();

        public BulkRewards BulkRewards { get; set; } = new BulkRewards();

        public string[] ReducedSaleTemplates { get; set; } =
        [
            "Couldn't find anything ##DESIRES##, so I grabbed ##QTY##x ##PRODUCT## instead at a lower ##PRICE_EACH## each — ##TOTAL## total.",
            "You had nothing ##DESIRES##, so I settled for ##QTY##x ##PRODUCT## at ##PRICE_EACH## each (##TOTAL## in total).",
            "No ##DESIRES##? I took ##QTY##x ##PRODUCT## instead, but only paid ##PRICE_EACH## each — ##TOTAL## total.",
            "Bought ##QTY##x ##PRODUCT## off you, but since it wasn't ##DESIRES## I knocked it down to ##PRICE_EACH## each (##TOTAL## total)."
        ];
        public string[] ReducedDealerTemplates { get; set; } =
        [
            "##DEALER## had nothing ##DESIRES##, so I took ##QTY##x ##PRODUCT## at a lower ##PRICE_EACH## each — ##TOTAL## total.",
            "Since ##DEALER## didn't have ##DESIRES##, I settled for ##QTY##x ##PRODUCT## at ##PRICE_EACH## each (##TOTAL## in total).",
            "No ##DESIRES## from ##DEALER## — grabbed ##QTY##x ##PRODUCT## instead at ##PRICE_EACH## each, ##TOTAL## total.",
            "Got ##QTY##x ##PRODUCT## from ##DEALER##, but it wasn't ##DESIRES## so I only paid ##PRICE_EACH## each — ##TOTAL## total."
        ];
    }

    public class ProductSelection
    {
        // Selection weight for a candidate product is coverage^CoverageBiasExponent scaled by a
        // drug-type factor^DrugTypeBiasExponent, where coverage = (covered desired effects + 1)
        // and the drug-type factor is the customer's affinity for the product's drug type mapped
        // to [0,1]. Higher CoverageBiasExponent makes effect coverage dominate; higher
        // DrugTypeBiasExponent sharpens the preference for the customer's liked drug type.
        // Set either to 0 to neutralise that dimension.
        public float CoverageBiasExponent { get; set; } = 3f;

        public float DrugTypeBiasExponent { get; set; } = 1f;

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

        /// <summary>
        /// When a sleep / time-skip jumps clean over a customer's order window on one of their order
        /// days, re-offer that missed order the morning the player wakes into. This is a one-time
        /// catch-up that does NOT alter <see cref="OrderPatternProfile"/> / GetOrderDays, so the
        /// recurring cadence stays anchored to its normal days (the order does not permanently shift
        /// forward by a day). Bulk (once-weekly) customers benefit most: without this, a single
        /// slept-through night means they do not order at all that week. Handled by
        /// <c>CustomerMissedOrderCatchupPatch</c>.
        /// </summary>
        public bool CatchUpMissedOrders { get; set; } = true;

        public OrderPatternWeights ArchetypeWeights { get; set; } = new OrderPatternWeights();

        public bool AnnounceNextOrder { get; set; } = true;

        public bool ShowPatternInContactPanel { get; set; } = true;

        /// <summary>
        /// Below this rank, order-pattern reshaping (and its coupled bulk-reward scaling,
        /// next-order texts and contact-panel cadence line) stays off and customers keep their
        /// vanilla ordering days. Independent of <c>Contracts.XPRequired</c> so cadence reshaping
        /// can switch on earlier than the contract system. Combined with <see cref="MinRankTier"/>
        /// (e.g. Street_Rat + tier 3 = "Street Rat III"). Defaults to Street_Rat III.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public ERank MinRank { get; set; } = ERank.Street_Rat;

        /// <summary>
        /// The tier (I–V, i.e. 1–5) within <see cref="MinRank"/> at which order-pattern reshaping
        /// starts being applied.
        /// </summary>
        public int MinRankTier { get; set; } = 3;

        /// <summary>
        /// True when the local player's rank is at or above the configured <see cref="MinRank"/> /
        /// <see cref="MinRankTier"/>. Returns true when the level manager is unavailable so the
        /// reshaping is applied by default. Uses <see cref="FullRank.ToFloat"/> for comparison
        /// (matching the codebase convention).
        /// </summary>
        public bool RankMet() => RankHelper.PlayerRankAtLeast(MinRank, MinRankTier);

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
            SampleOffering.MinRankTier = ConfigValidator.InRange(
                Name, "SampleOffering.MinRankTier", SampleOffering.MinRankTier, 1, 5);

            OrderPatterns.MinRankTier = ConfigValidator.InRange(
                Name, "OrderPatterns.MinRankTier", OrderPatterns.MinRankTier, 1, 5);

            Contracts.ProductSelection.CoverageBiasExponent = ConfigValidator.AtLeast(
                Name, "Contracts.ProductSelection.CoverageBiasExponent", Contracts.ProductSelection.CoverageBiasExponent, 0f);
            Contracts.ProductSelection.DrugTypeBiasExponent = ConfigValidator.AtLeast(
                Name, "Contracts.ProductSelection.DrugTypeBiasExponent", Contracts.ProductSelection.DrugTypeBiasExponent, 0f);

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
            DailyOrderTracker.Unload();
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
