namespace Lithium.Helper
{
    public static class RandomExtensions
    {
        public static T PickRandom<T>(this IEnumerable<T> source) =>
            source.OrderBy(_ => UnityEngine.Random.value).FirstOrDefault();
    }
}
