using System.Collections.Generic;
using Il2CppScheduleOne.Doors;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.PlayerScripts;
using Lithium.Helper;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace Lithium.Modules.Warehouse
{
    public class ModWarehouseConfiguration : ModuleConfiguration
    {
        public override string Name => "Warehouse";

        /// <summary>
        /// The rank the player must reach before the warehouse (Dark Market) stays open 24/7.
        /// Below this rank the vanilla opening hours apply. Combined with <see cref="RequiredRankTier"/>
        /// (e.g. Hoodlum + tier 2 = "Hoodlum II").
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public ERank RequiredRank = ERank.Hoodlum;

        /// <summary>
        /// The tier (I–V, i.e. 1–5) within <see cref="RequiredRank"/> that must be reached.
        /// </summary>
        public int RequiredRankTier = 1;

        /// <summary>
        /// When true, the warehouse still closes while any player is being pursued by police
        /// (vanilla behaviour). When false, it stays open even during a pursuit once the rank
        /// requirement is met.
        /// </summary>
        public bool CloseDuringPursuit = true;

        /// <summary>
        /// When true, the warehouse's interior door handle can only be operated while the local
        /// player is genuinely standing inside the warehouse. This closes the "skateboard glitch"
        /// where a player clips outside the building yet can still trigger the interior handle to
        /// open the (exit-only, after-hours-locked) door from the wrong side. Legitimate exits from
        /// inside are unaffected. Only takes effect while the door is in its exit-only state; once
        /// the market is fully open the door behaves normally.
        /// </summary>
        public bool PreventInteriorGlitch = true;

        public override void Validate()
        {
            if (RequiredRankTier < 1)
                RequiredRankTier = 1;
            if (RequiredRankTier > 5)
                RequiredRankTier = 5;
        }
    }

    /// <summary>
    /// Keeps the warehouse (the Dark Market black market run by Oscar/Igor) open around the clock
    /// once the player reaches a configured rank, removing its vanilla after-hours-only restriction.
    /// <para>
    /// The market's open state is governed by two time checks: <c>DarkMarket.ShouldBeOpen()</c>
    /// (drives <c>IsOpen</c> — i.e. whether the vendor/deliveries are active) and
    /// <c>DarkMarketAccessZone.GetIsOpen()</c> (drives the door locks). Both are patched (see
    /// Patches/) to bypass the time-of-day check once <see cref="RequirementMet"/> is true, while
    /// preserving the unlock requirement and (optionally) the police-pursuit lockout.
    /// </para>
    /// </summary>
    public class ModWarehouse : ModuleBase<ModWarehouseConfiguration>
    {
        /// <summary>
        /// Interior-side <see cref="DoorSensor"/>s belonging to the Dark Market doors, keyed by the
        /// owning <see cref="DoorController"/>'s instance id. A door's presence here also marks it as
        /// a warehouse door (so non-warehouse doors are left untouched). Resolved lazily once the
        /// Main scene is live (see <see cref="ResolveDarkMarketDoors"/>).
        /// </summary>
        private Dictionary<int, List<DoorSensor>> _interiorSensorsByDoor;

        private bool _darkMarketResolved;

        public override void Apply()
        {
            // Reset so the door/sensor lookup is rebuilt against the freshly loaded save's objects.
            _interiorSensorsByDoor = null;
            _darkMarketResolved = false;
        }

        /// <summary>
        /// Decides whether an interior-side open of <paramref name="door"/> should be blocked because
        /// it is a Dark Market door and the local player is not actually inside its interior vicinity
        /// (the skateboard-glitch case). Returns false (allow) for any non-warehouse door, when the
        /// feature is disabled, or whenever the player genuinely stands in the interior sensor.
        /// </summary>
        public bool ShouldBlockInteriorOpen(DoorController door)
        {
            if (door == null || !Configuration.PreventInteriorGlitch)
                return false;

            ResolveDarkMarketDoors();

            if (_interiorSensorsByDoor == null ||
                !_interiorSensorsByDoor.TryGetValue(door.GetInstanceID(), out List<DoorSensor> sensors))
                return false; // not a warehouse door — leave it alone

            return !LocalPlayerInsideAny(sensors);
        }

        /// <summary>
        /// True when the local player is currently registered in any of the supplied interior door
        /// sensors' contact lists (i.e. physically standing in the warehouse-side vicinity).
        /// </summary>
        private static bool LocalPlayerInsideAny(List<DoorSensor> sensors)
        {
            Player local = Player.Local;
            if (local == null)
                return false;

            int localId = local.GetInstanceID();
            foreach (DoorSensor sensor in sensors)
            {
                if (sensor == null)
                    continue;

                Il2CppSystem.Collections.Generic.List<Player> contacts = sensor.playersInContact;
                if (contacts == null)
                    continue;

                for (int i = 0; i < contacts.Count; i++)
                {
                    Player p = contacts[i];
                    if (p != null && p.GetInstanceID() == localId)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Lazily builds the map of Dark Market doors to their interior-side sensors. Finds the
        /// <see cref="DarkMarketAccessZone"/>, takes its <c>Doors</c> as the authoritative warehouse
        /// door set, then scans every <see cref="DoorSensor"/> for the interior sensors pointing at
        /// those doors. Stays unresolved (and retries on the next call) until the access zone exists,
        /// so it tolerates being called before the map has fully spawned.
        /// </summary>
        private void ResolveDarkMarketDoors()
        {
            if (_darkMarketResolved)
                return;

            DarkMarketAccessZone zone = UnityEngine.Object.FindObjectOfType<DarkMarketAccessZone>();
            if (zone == null)
                return; // map not ready yet — try again next time

            var doorIds = new HashSet<int>();
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<DoorController> doors = zone.Doors;
            if (doors != null)
            {
                for (int i = 0; i < doors.Count; i++)
                {
                    DoorController d = doors[i];
                    if (d != null)
                        doorIds.Add(d.GetInstanceID());
                }
            }

            var map = new Dictionary<int, List<DoorSensor>>();
            foreach (DoorSensor sensor in UnityEngine.Object.FindObjectsOfType<DoorSensor>(true))
            {
                if (sensor == null || sensor.Door == null || sensor.DetectorSide != EDoorSide.Interior)
                    continue;

                int doorId = sensor.Door.GetInstanceID();
                if (!doorIds.Contains(doorId))
                    continue;

                if (!map.TryGetValue(doorId, out List<DoorSensor> list))
                {
                    list = new List<DoorSensor>();
                    map[doorId] = list;
                }

                list.Add(sensor);
            }

            _interiorSensorsByDoor = map;
            _darkMarketResolved = true;
            Log.Info($"[Warehouse] Resolved {map.Count} Dark Market door(s) with interior sensors for glitch guard.");
        }

        /// <summary>
        /// True when the local player's rank is at or above the configured requirement.
        /// Uses <see cref="FullRank.ToFloat"/> for comparison (matching the codebase convention)
        /// to avoid constructing an IL2CPP <see cref="FullRank"/> struct just to compare it.
        /// </summary>
        public bool RequirementMet() =>
            RankHelper.PlayerRankAtLeast(
                Configuration.RequiredRank, Configuration.RequiredRankTier, defaultWhenUnavailable: false);

        /// <summary>
        /// True when any player currently has an active police pursuit. Mirrors the loop in the
        /// game's <c>DarkMarket.ShouldBeOpen()</c>.
        /// </summary>
        public static bool AnyPlayerPursued()
        {
            for (int i = 0; i < Player.PlayerList.Count; i++)
            {
                Player player = Player.PlayerList[i];
                if (player != null && player.CrimeData != null &&
                    player.CrimeData.CurrentPursuitLevel != PlayerCrimeData.EPursuitLevel.None)
                    return true;
            }

            return false;
        }
    }
}
