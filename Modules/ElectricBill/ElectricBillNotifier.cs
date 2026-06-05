using System;
using Il2CppScheduleOne.UI;
using Lithium.Helper;

namespace Lithium.Modules.ElectricBill
{
    // Lightweight HUD notification for power-bill events (cut, restore, paid). The actual money movement
    // already produces its own transaction popup via MoneyManager.CreateOnlineTransaction, so this only
    // surfaces the power-state changes. Best-effort: never throws into the caller.
    internal static class ElectricBillNotifier
    {
        public static void Send(string title, string subtitle)
        {
            try
            {
                NotificationsManager mgr = NotificationsManager.Instance;
                if (mgr != null)
                    mgr.SendNotification(title, subtitle, null, 6f, true);
                else
                    Log.Info($"[ElectricBill] {title}: {subtitle}");
            }
            catch (Exception e)
            {
                Log.Warning($"[ElectricBill] Notification failed: {e.Message}");
            }
        }
    }
}
