using MelonLoader;

namespace Lithium
{
    /// <summary>
    /// Central logging for Lithium. Informational messages (<see cref="Info"/>) are emitted only when
    /// <c>Debug</c> is enabled in Lithium.json; warnings and errors are always shown.
    /// Prefer guarding an expensive-to-build message with <see cref="DebugEnabled"/> before calling Info.
    /// </summary>
    public static class Log
    {
        public static bool DebugEnabled => LithiumConfig.Instance.Debug;

        public static void Info(string message)
        {
            if (LithiumConfig.Instance.Debug)
                MelonLogger.Msg(message);
        }

        public static void Warning(string message) => MelonLogger.Warning(message);

        public static void Error(string message) => MelonLogger.Error(message);
    }
}
