namespace Lithium.Helper
{
    public static class RandomExtensions
    {
        /// <summary>
        /// Returns a uniformly-random element (or default if the source is empty), using
        /// UnityEngine.Random. Replaces the repeated <c>OrderBy(_ => Random.value).FirstOrDefault()</c>
        /// idiom used to pick message templates.
        /// </summary>
        public static T PickRandom<T>(this IEnumerable<T> source) =>
            source.OrderBy(_ => UnityEngine.Random.value).FirstOrDefault();
    }
}
