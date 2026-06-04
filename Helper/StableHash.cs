namespace Lithium.Helper
{
    public static class StableHash
    {
        public static int Compute(string s)
        {
            unchecked
            {
                const uint offset = 2166136261;
                const uint prime = 16777619;
                uint hash = offset;
                if (s != null)
                {
                    foreach (char c in s)
                    {
                        hash ^= c;
                        hash *= prime;
                    }
                }
                return (int)hash;
            }
        }
    }
}
