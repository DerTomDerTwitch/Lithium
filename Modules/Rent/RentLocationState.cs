namespace Lithium.Modules.Rent
{
    /// <summary>
    /// Per-save, per-location runtime state for the rent system. Persisted (keyed by the property's
    /// stable <c>PropertyCode</c>) via <see cref="Lithium.Modules.Customers.Architecture.SaveSlotStore{TValue}"/>
    /// so it survives save/load. Plain POCO — serialized with Newtonsoft.
    /// </summary>
    public class RentLocationState
    {
        /// <summary>Outstanding rent currently owed for this location.</summary>
        public float Owed;

        /// <summary>
        /// <c>ElapsedDays</c> at which the most recent weekly charge was applied (the cadence anchor).
        /// The next charge is due at <c>LastChargedDay + RentIntervalDays</c>. -1 = not yet initialised
        /// (anchored to the current day the first time the location is processed).
        /// </summary>
        public int LastChargedDay = -1;

        /// <summary>
        /// <c>ElapsedDays</c> on which the current outstanding debt first became due, used to time the
        /// warning and the lockout. -1 when nothing is owed.
        /// </summary>
        public int DueSinceDay = -1;

        /// <summary>True once the pre-lockout final warning has been sent for the current overdue spell.</summary>
        public bool WarningSent;

        /// <summary>True while the property is locked to the player for non-payment (rent is frozen).</summary>
        public bool LockedOut;
    }
}
