using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI.Phone;
using UnityEngine;
using UnityEngine.UI;

namespace Lithium.Modules.PhoneApp
{
    // Resolves the game's UI font by cloning it from a vanilla Text element (e.g. the contacts panel's
    // labels, which Lithium already extends elsewhere). Falls back to a builtin font if none is found.
    // Cached after the first successful lookup.
    internal static class PhoneAppFont
    {
        private static Font _cached;

        public static Font Resolve()
        {
            if (_cached != null)
                return _cached;

            // Prefer a Text living under the phone's app canvas, so we inherit the exact phone-UI font.
            AppsCanvas canvas = PlayerSingleton<AppsCanvas>.InstanceExists ? PlayerSingleton<AppsCanvas>.Instance : null;
            if (canvas != null && canvas.canvas != null)
                _cached = FirstFont(canvas.canvas.GetComponentsInChildren<Text>(true));

            // Otherwise borrow from any vanilla Text in the scene.
            if (_cached == null)
                _cached = FirstFont(UnityEngine.Object.FindObjectsOfType<Text>(true));

            if (_cached == null)
                _cached = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                          ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            return _cached;
        }

        private static Font FirstFont(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<Text> texts)
        {
            if (texts == null)
                return null;
            foreach (Text t in texts)
                if (t != null && t.font != null)
                    return t.font;
            return null;
        }
    }
}
