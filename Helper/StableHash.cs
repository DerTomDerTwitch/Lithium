namespace Lithium.Helper
{
    public static class StableHash
    {
        // FNV-1a 32-bit. Unlike string.GetHashCode() (per-process randomized in .NET), this is stable
        // across processes and save reloads, so the same name always yields the same Random seed.
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
