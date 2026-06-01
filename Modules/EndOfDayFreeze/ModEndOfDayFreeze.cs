namespace Lithium.Modules.EndOfDayFreeze
{
    public class ModEndOfDayFreezeConfiguration : ModuleConfiguration
    {
        public override string Name => "EndOfDayFreeze";

        public ModEndOfDayFreezeConfiguration()
        {
            // On by default: the whole point of this module is to close the 4 AM time-freeze exploit.
            // Set "Enabled": false in EndOfDayFreeze.json to restore vanilla behaviour.
            Enabled = true;
        }
    }

    /// <summary>
    /// Stops production timers (chemistry stations, drying racks, plant growth and mixing stations
    /// Mk1 + Mk2) while the game clock is frozen at the end of the day (4 AM). Schedule I freezes the
    /// displayed clock at 4 AM but keeps firing OnMinPass, so stations otherwise keep progressing
    /// indefinitely while the player stands AFK — this closes that exploit. See
    /// <see cref="EndOfDayGate"/> and the patches under Patches/.
    /// </summary>
    public class ModEndOfDayFreeze : ModuleBase<ModEndOfDayFreezeConfiguration>
    {
        public override void Apply()
        {
        }
    }
}
