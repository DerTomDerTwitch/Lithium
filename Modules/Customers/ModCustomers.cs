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

        // Anti-exploit gate (only active for customers reshaped into bulk buyers by order patterns).
        // A customer placing one big weekly/bi-weekly order would otherwise still accept extra in-person
        // offers any day, letting the player sell a week's worth daily and defeat the bulk cadence. So a
        // customer refuses an unscheduled in-person offer until at least this fraction of the wait until
        // their next scheduled order has elapsed — i.e. they've had time to run low. 0.5 = halfway to
        // their next order. 0 disables the gate (vanilla "buy anytime").
        public float MinIntervalFractionBeforeOffer { get; set; } = 0.5f;
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

        // When the player fills a (matching) direct order themselves (no assigned dealer), the customer
        // pays the player's set listed price (ProductManager listed price) rather than the game's standard
        // per-unit market roll. Off restores vanilla pricing for direct sales.
        public bool SellAtListedPrice { get; set; } = true;

        // When the player refuses a contract offer, or it expires unanswered, the customer re-attempts
        // an order the next day instead of waiting for their next scheduled order day.
        public bool RetryNextDayOnRefusal { get; set; } = true;

        // Larger orders grant a longer window to accept the offer (and the customer texts the deadline).
        public AcceptanceWindow AcceptanceWindow { get; set; } = new AcceptanceWindow();

        // How the ordered product(s) are chosen — coverage preference and optional second product.
        public ProductSelection ProductSelection { get; set; } = new ProductSelection();

        // A bulk order replaces several normal orders' worth of volume, so it grants scaled affection
        // and XP on completion to match what those separate orders would have earned.
        public BulkRewards BulkRewards { get; set; } = new BulkRewards();

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

    // Controls which product(s) a customer asks for, weighted toward effect coverage.
    public class ProductSelection
    {
        // Higher = customers much more strongly prefer products covering more of their desired effects.
        // A candidate's pick weight is coverage^Exponent (coverage = how many desired effects it
        // carries). 3 => a 3-effect match is 27x as likely as a 1-effect match. Products covering no
        // desired effect are never picked when the customer has desires (weight 0).
        public float CoverageBiasExponent { get; set; } = 3f;

        // When true, a matching order has a chance to include a second, different product.
        public bool EnableSecondProduct { get; set; } = true;

        // Probability (0..1) that an eligible matching order gains a second product.
        public float SecondProductChance { get; set; } = 0.5f;

        // Fraction (0..1) of the order quantity reallocated to the second product (the first keeps the
        // rest). ~0.25 = a quarter of the units go to the second product.
        public float SecondProductQuantityShare { get; set; } = 0.25f;
    }

    // Scales the affection (customer relationship) and XP earned from a completed bulk order by the order's
    // quantity multiplier — how many normal orders it stands in for (e.g. a weekly order worth ~7 daily
    // ones). The affection/XP the game actually awards for the handover is measured, then topped up so the
    // total is multiplier-times that base. So a 7x-volume order grants ~7x the affection and XP, matching
    // what the separate deliveries it replaced would have earned — no guessed reward constants.
    public class BulkRewards
    {
        public bool Enabled { get; set; } = true;

        // Scale the customer-relationship (affection) gain by the order's quantity multiplier.
        public bool ScaleRelationship { get; set; } = true;

        // Scale the XP gain by the order's quantity multiplier.
        public bool ScaleXP { get; set; } = true;

        // Upper bound on the reward multiplier so an unusually large order can't grant a runaway one-shot
        // bonus. The effective multiplier is min(orderMultiplier, this).
        public float MaxRewardMultiplier { get; set; } = 7f;
    }

    // Relative likelihood of each ordering cadence (any non-negative numbers; they need not sum to 100).
    // No customer ever orders daily. Weekly is the common case; shorter intervals are progressively
    // rarer, so most customers place fewer, larger (bulk) orders.
    public class OrderPatternWeights
    {
        public float Weekly { get; set; } = 55f;          // 1 day / week    — most common
        public float TwiceWeekly { get; set; } = 30f;     // 2 days / week   — uncommon
        public float EveryThreeDays { get; set; } = 15f;  // 2-3 days / week — most rare
    }

    public class OrderPatterns
    {
        public bool Enabled { get; set; }

        // Relative likelihood of each ordering scheme (see OrderPatternWeights).
        public OrderPatternWeights ArchetypeWeights { get; set; } = new OrderPatternWeights();

        // After a customer completes a direct order with the player, they text roughly when they'll
        // next order (their next pattern order day).
        public bool AnnounceNextOrder { get; set; } = true;

        // Show the customer's order pattern (days + cadence) in the phone Contacts customer panel, next
        // to their desires. Only shown when order patterns are actually in effect (Enabled + XP met).
        public bool ShowPatternInContactPanel { get; set; } = true;

        // ##DAY## = when they'll next order, e.g. "tomorrow", "on Wednesday" or "next Monday".
        public string[] NextOrderTemplates { get; set; } =
        [
            "Thanks, good doing business! I'll hit you up again ##DAY##.",
            "Appreciate it — I'll call you back ##DAY##.",
            "Solid as always. Expect to hear from me ##DAY##.",
            "Nice doing business. I'll be in touch again ##DAY##."
        ];
    }

    // Larger orders give the player more time to accept the offer: the acceptance window
    // (ContractInfo.ExpiresAfter, in in-game minutes) is extended for orders above BaseQuantity, on top
    // of the game's own default window, and the customer texts the resulting deadline.
    public class AcceptanceWindow
    {
        public bool Enabled { get; set; } = true;

        // Multiplies the whole acceptance window (base + size bonus) so the player gets more time to
        // answer every offer. 1.5 = +50%; 1.0 = no global extension. Large (weekly bulk) orders comfortably
        // span multiple in-game days, and even small orders aren't cut short by a deadline that lands the
        // same night. Applied in OfferAcceptanceWindow.Extend, then bounded by MaxWindowMinutes.
        public float DurationMultiplier { get; set; } = 1.5f;

        // Orders at or below this quantity keep the game's default acceptance window, scaled by
        // DurationMultiplier.
        public int BaseQuantity { get; set; } = 10;

        // Extra in-game minutes granted per unit ordered above BaseQuantity, added to the game's default
        // window. e.g. a 50-unit order at BaseQuantity 10 and 60 min/unit gains 40 * 60 = 2400 mins.
        public float MinutesPerExtraUnit { get; set; } = 60f;

        // Hard cap on the total acceptance window, in in-game minutes (1440 = one in-game day).
        public int MaxWindowMinutes { get; set; } = 10080; // 7 in-game days

        // When true, the customer sends a follow-up text telling the player how long they have to accept
        // the offer. Sent for EVERY expiring offer (not just extended ones), and works even when the
        // window extension above is disabled.
        public bool SendDeadlineMessage { get; set; } = true;

        // Follow-up deadline texts. ##DEADLINE## = when the offer expires (e.g. "Monday, 12:00 PM");
        // ##QUANTITY## = units ordered (optional).
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
            // Same for offer deadlines: the new save's persisted deadlines are reloaded lazily, so an offer
            // still pending across a save/reload keeps the extended acceptance window it was promised.
            OfferDeadlineTracker.Unload();

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
