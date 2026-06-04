namespace Lithium.Modules.Rent
{
    public class RentLocationState
    {
        public float Owed;

        public int LastChargedDay = -1;

        public int DueSinceDay = -1;

        public bool WarningSent;

        public bool LockedOut;
    }
}
