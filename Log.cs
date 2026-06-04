using MelonLoader;

namespace Lithium
{
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
