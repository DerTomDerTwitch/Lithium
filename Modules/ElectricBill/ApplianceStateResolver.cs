using System;
using System.Collections.Generic;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.ObjectScripts;
using Lithium.Helper;
using ChemStation = Il2CppScheduleOne.ObjectScripts.ChemistryStation;
using Oven = Il2CppScheduleOne.ObjectScripts.LabOven;

namespace Lithium.Modules.ElectricBill
{
    // Maps an appliance item ID to the three behaviours the electric bill needs:
    //   IsActive — is it currently drawing its in-use load (light on / machine operating)?
    //   ForceOff — turn it off when power is cut (only meaningful for player-toggled lights).
    //   Restore  — turn it back on when power returns (lights only).
    // Machines are stopped by their per-property freeze patches, not by ForceOff, so their ForceOff /
    // Restore are no-ops. Unknown IDs resolve to "always standby" and are never forced off.
    public static class ApplianceStateResolver
    {
        public sealed class Behaviour
        {
            public Func<BuildableItem, bool> IsActive;
            public Action<BuildableItem> ForceOff;
            public Action<BuildableItem> Restore;
            public bool IsLight;
        }

        private static readonly Action<BuildableItem> NoOp = _ => { };

        private static readonly Dictionary<string, Behaviour> Map = Build();

        private static Dictionary<string, Behaviour> Build()
        {
            Dictionary<string, Behaviour> map = new(StringComparer.OrdinalIgnoreCase);

            // --- Floor lamp: ToggleableItem (grid-placed) ---
            Behaviour toggleable = new()
            {
                IsLight = true,
                IsActive = bi => { ToggleableItem t = bi.TryCast<ToggleableItem>(); return t != null && t.IsOn; },
                ForceOff = bi => { ToggleableItem t = bi.TryCast<ToggleableItem>(); if (t != null) t.SetIsOn(null, false); },
                Restore = bi => { ToggleableItem t = bi.TryCast<ToggleableItem>(); if (t != null) t.SetIsOn(null, true); },
            };
            map["floorlamp"] = toggleable;

            // --- Wall lamps: ToggleableSurfaceItem (wall-placed) ---
            Behaviour surface = new()
            {
                IsLight = true,
                IsActive = bi => { ToggleableSurfaceItem t = bi.TryCast<ToggleableSurfaceItem>(); return t != null && t.IsOn; },
                ForceOff = bi => { ToggleableSurfaceItem t = bi.TryCast<ToggleableSurfaceItem>(); if (t != null) t.SetIsOn(null, false); },
                Restore = bi => { ToggleableSurfaceItem t = bi.TryCast<ToggleableSurfaceItem>(); if (t != null) t.SetIsOn(null, true); },
            };
            map["antiquewalllamp"] = surface;
            map["modernwalllamp"] = surface;

            // --- Grow lights: GrowLight wrapping a ToggleableLight ---
            Behaviour grow = new()
            {
                IsLight = true,
                IsActive = bi => { GrowLight g = bi.TryCast<GrowLight>(); return g != null && g.Light != null && g.Light.isOn; },
                ForceOff = bi => { GrowLight g = bi.TryCast<GrowLight>(); if (g != null) g.SetIsOn(false); },
                Restore = bi => { GrowLight g = bi.TryCast<GrowLight>(); if (g != null) g.SetIsOn(true); },
            };
            map["ledgrowlight"] = grow;
            map["fullspectrumgrowlight"] = grow;
            map["halogengrowlight"] = grow;

            // --- Sprinkler: momentary; stopped by gating its Water() activation while cut ---
            map["bigsprinkler"] = new()
            {
                IsActive = bi => { Sprinkler s = bi.TryCast<Sprinkler>(); return s != null && s.IsSprinkling; },
                ForceOff = NoOp,
                Restore = NoOp,
            };

            // --- Machines: stopped by per-property freeze patches; IsActive = currently operating ---
            map["chemistrystation"] = Machine(bi => { ChemStation c = bi.TryCast<ChemStation>(); return c != null && c.CurrentCookOperation != null; });
            map["laboven"] = Machine(bi => { Oven o = bi.TryCast<Oven>(); return o != null && o.CurrentOperation != null; });

            Behaviour mixing = Machine(bi => { MixingStation m = bi.TryCast<MixingStation>(); return m != null && m.CurrentMixOperation != null; });
            map["mixingstation"] = mixing;
            map["mixingstationmk2"] = mixing;

            map["cauldron"] = Machine(bi => { Cauldron c = bi.TryCast<Cauldron>(); return c != null && c.isCooking; });

            Behaviour packaging = Machine(bi =>
            {
                PackagingStation p = bi.TryCast<PackagingStation>();
                return p != null && (p.NPCUserObject != null || p.PlayerUserObject != null);
            });
            map["packagingstation"] = packaging;
            map["packagingstationmk2"] = packaging;

            map["launderingstation"] = Machine(bi =>
            {
                LaunderingStation l = bi.TryCast<LaunderingStation>();
                return l != null && l.Interface != null && l.Interface.business != null
                    && l.Interface.business.currentLaunderTotal > 0f;
            });

            return map;
        }

        private static Behaviour Machine(Func<BuildableItem, bool> isActive) => new()
        {
            IsActive = isActive,
            ForceOff = NoOp,
            Restore = NoOp,
        };

        public static bool TryGet(string itemId, out Behaviour behaviour)
        {
            if (!string.IsNullOrEmpty(itemId))
                return Map.TryGetValue(itemId, out behaviour);
            behaviour = null;
            return false;
        }

        public static bool IsActive(string itemId, BuildableItem bi)
        {
            try
            {
                return TryGet(itemId, out Behaviour b) && b.IsActive(bi);
            }
            catch (Exception e)
            {
                Log.Warning($"[ElectricBill] IsActive('{itemId}') failed: {e.Message}");
                return false;
            }
        }

        public static bool IsLight(string itemId) => TryGet(itemId, out Behaviour b) && b.IsLight;

        public static void ForceOff(string itemId, BuildableItem bi)
        {
            try
            {
                if (TryGet(itemId, out Behaviour b)) b.ForceOff(bi);
            }
            catch (Exception e)
            {
                Log.Warning($"[ElectricBill] ForceOff('{itemId}') failed: {e.Message}");
            }
        }

        public static void Restore(string itemId, BuildableItem bi)
        {
            try
            {
                if (TryGet(itemId, out Behaviour b)) b.Restore(bi);
            }
            catch (Exception e)
            {
                Log.Warning($"[ElectricBill] Restore('{itemId}') failed: {e.Message}");
            }
        }
    }
}
