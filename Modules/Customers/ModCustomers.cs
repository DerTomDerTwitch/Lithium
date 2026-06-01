using System.Collections;
using Il2CppInterop.Runtime.Injection;
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
        // When true, a sample that covers none of the customer's desired effects is rejected outright
        // (no quality / drug-affinity / base-acceptance fallback can carry it).
        public bool RequireEffectMatch { get; set; } = true;
        // How many quality tiers ABOVE the customer's standard still add acceptance. Caps the quality
        // bonus so over-delivering (e.g. a Heavenly sample to someone who'd accept Trash) doesn't
        // overboard or rescue a poorly-covered sample. Under-standard quality is always fully penalised.
        public int MaxQualityOverDeliveryLevels { get; set; } = 1;
        // Exponent applied to a positive drug-type affinity (0..1) to get the acceptance multiplier.
        // Below 1 makes acceptance climb quickly for modest affinity (0.5 = square root: 0.25 affinity
        // -> 0.5x instead of 0.25x). Affinity of 0 or below still yields 0% (disliked types rejected).
        public float DrugAffinitySharpness { get; set; } = 0.5f;
    }

    public class DirectSales
    {
        // When true, a direct (in-person) offer is rejected outright unless at least one offered
        // product covers a desired effect — same hard requirement contracts use, no fallback.
        public bool RequireEffectMatch { get; set; } = true;
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

        // When neither the player nor the dealer offers a product matching the customer's desired
        // effects, the customer still buys a substitute — but pays this fraction of the product's
        // DEFAULT market value (not the player's listed price). 0.75 = 25% below default value.
        public float ReducedDealPriceMultiplier { get; set; } = 0.75f;

        // When a dealer makes a (matching) deal, the customer pays the player's set product price
        // (ProductManager listed price) rather than the product's standard market value.
        public bool DealerSellAtListedPrice { get; set; } = true;

        // When the player refuses a contract offer, or it expires unanswered, the customer re-attempts
        // an order the next day instead of waiting for their next scheduled order day.
        public bool RetryNextDayOnRefusal { get; set; } = true;

        // Larger orders grant a longer window to accept the offer (and the customer texts the deadline).
        public AcceptanceWindow AcceptanceWindow { get; set; } = new AcceptanceWindow();

        // Texts sent when the customer settles for a non-matching product at the reduced price.
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

    public class OrderPatterns
    {
        public bool Enabled { get; set; }
    }

    // Larger orders give the player more time to accept the offer: the acceptance window
    // (ContractInfo.ExpiresAfter, in in-game minutes) is extended for orders above BaseQuantity, on top
    // of the game's own default window, and the customer texts the resulting deadline.
    public class AcceptanceWindow
    {
        public bool Enabled { get; set; } = true;

        // Orders at or below this quantity keep the game's default acceptance window (vanilla feel).
        public int BaseQuantity { get; set; } = 10;

        // Extra in-game minutes granted per unit ordered above BaseQuantity, added to the game's default
        // window. e.g. a 50-unit order at BaseQuantity 10 and 60 min/unit gains 40 * 60 = 2400 mins.
        public float MinutesPerExtraUnit { get; set; } = 60f;

        // Hard cap on the total acceptance window, in in-game minutes (1440 = one in-game day).
        public int MaxWindowMinutes { get; set; } = 10080; // 7 in-game days

        // When true, the customer sends a follow-up text stating the response deadline for large orders.
        public bool SendDeadlineMessage { get; set; } = true;

        // ##QUANTITY## = units ordered, ##DEADLINE## = formatted deadline (e.g. "Monday, 12:00 PM").
        public string[] DeadlineTemplates { get; set; } =
        [
            "That's a big one — ##QUANTITY## units. No rush, just let me know by ##DEADLINE##.",
            "##QUANTITY## units is a serious order, so take your time. I'll need an answer by ##DEADLINE## though.",
            "Since I'm after ##QUANTITY## this time, I'll give you until ##DEADLINE## to confirm.",
            "Big order (##QUANTITY## units) — let me know by ##DEADLINE##."
        ];
    }

    public class CoverageNotifications
    {
        public bool Enabled { get; set; }
        // The existing in-game NPC whose Messages conversation is repurposed as the "Lithium" contact.
        public string ContactNpcName { get; set; } = "Manny Oakfield";
        public string ContactDisplayName { get; set; } = "Lithium";
        // When true, each coverage text also lists every customer that is still uncovered.
        public bool ListUncovered { get; set; }
        // When true, closing a dealer's in-person inventory texts which of that dealer's assigned customers
        // and desired effects their current inventory fails to cover.
        public bool NotifyDealerInventoryOnClose { get; set; }
        // When true, a single compact situation overview is texted shortly after each save loads.
        public bool SendStartupOverview { get; set; } = true;
        // When true, the coverage of customers with no assigned dealer (count, percentage and the uncovered
        // names) is texted at startup and again whenever a product is listed/delisted.
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
            // New save/scene: drop the cached coverage baselines so they re-snapshot fresh.
            ProductCoverageNotifier.Reset();
            DealerCoverageNotifier.ResetNoDealer();
            // Drop the previous save's in-memory retries; the new save's file is reloaded lazily on access.
            ContractRetryTracker.Unload();

            if (!Configuration.Enabled)
                return;

            if (Configuration.Coverage.Enabled &&
                (Configuration.Coverage.SendStartupOverview || Configuration.Coverage.NotifyNoDealerCustomers))
                MelonCoroutines.Start(StartupOverviewRoutine());
        }

        // Waits for the save to finish loading the world (messaging + customer roster) before texting the
        // one-shot startup reports, capped so a save that never populates customers won't wait forever.
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

            // Small buffer so listed products / dealer inventories have settled before we snapshot.
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
