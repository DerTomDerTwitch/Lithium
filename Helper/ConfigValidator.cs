namespace Lithium.Helper
{
    /// <summary>
    /// Small helpers for sanity-checking loaded configuration values. Each clamps a value into a
    /// sensible range and logs a one-line warning (warnings are always shown) describing what was
    /// wrong and what was used instead. The point is that a typo'd config — e.g. a negative plant
    /// growth modifier or a min greater than its max — fails loudly and safely rather than silently
    /// breaking gameplay. Call these from a config's <c>Validate()</c> override.
    /// </summary>
    public static class ConfigValidator
    {
        public static float AtLeast(string config, string field, float value, float min)
        {
            if (value >= min)
                return value;
            Log.Warning($"[Lithium] {config}: '{field}' = {value} is below the minimum of {min}; using {min}.");
            return min;
        }

        public static int AtLeast(string config, string field, int value, int min)
        {
            if (value >= min)
                return value;
            Log.Warning($"[Lithium] {config}: '{field}' = {value} is below the minimum of {min}; using {min}.");
            return min;
        }

        public static float InRange(string config, string field, float value, float min, float max)
        {
            if (value < min)
            {
                Log.Warning($"[Lithium] {config}: '{field}' = {value} is below {min}; using {min}.");
                return min;
            }
            if (value > max)
            {
                Log.Warning($"[Lithium] {config}: '{field}' = {value} is above {max}; using {max}.");
                return max;
            }
            return value;
        }

        public static int InRange(string config, string field, int value, int min, int max)
        {
            if (value < min)
            {
                Log.Warning($"[Lithium] {config}: '{field}' = {value} is below {min}; using {min}.");
                return min;
            }
            if (value > max)
            {
                Log.Warning($"[Lithium] {config}: '{field}' = {value} is above {max}; using {max}.");
                return max;
            }
            return value;
        }

        /// <summary>Ensures <paramref name="min"/> &lt;= <paramref name="max"/>, swapping (and warning) if reversed.</summary>
        public static void EnsureOrdered(string config, string minField, string maxField, ref int min, ref int max)
        {
            if (min <= max)
                return;
            Log.Warning($"[Lithium] {config}: '{minField}' ({min}) is greater than '{maxField}' ({max}); swapping them.");
            (min, max) = (max, min);
        }

        /// <summary>Ensures <paramref name="min"/> &lt;= <paramref name="max"/>, swapping (and warning) if reversed.</summary>
        public static void EnsureOrdered(string config, string minField, string maxField, ref float min, ref float max)
        {
            if (min <= max)
                return;
            Log.Warning($"[Lithium] {config}: '{minField}' ({min}) is greater than '{maxField}' ({max}); swapping them.");
            (min, max) = (max, min);
        }
    }
}
