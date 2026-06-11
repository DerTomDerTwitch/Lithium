using System;
using System.Globalization;
using Il2CppFishNet;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Variables;

namespace Lithium.Helper
{
    /// <summary>
    /// Minimal host→client replication channel for mod-only state that the game itself doesn't network
    /// (rent lockout/owed, electric power-cut/outstanding). It is built on the game's <c>VariableDatabase</c>
    /// — a <c>NetworkSingleton</c> whose <c>Bool</c>/<c>Number</c> variable values already replicate to every
    /// client (and are re-sent to late-joiners in <c>OnSpawnServer</c>). We reuse those variable types rather
    /// than hand-rolling FishNet RPCs, because IL2CPP cannot serialise mod-defined message structs.
    ///
    /// <para><b>Direction:</b> the host writes (<see cref="SetBool"/>/<see cref="SetNumber"/>), clients only
    /// read (<see cref="GetBool"/>/<see cref="GetNumber"/>). The mod's own gameplay logic on the host never
    /// reads this channel — it keeps using its local save store — so a replication hiccup can never change
    /// host behaviour. On a client the worst case is reading the fallback, i.e. exactly the pre-feature
    /// behaviour (no info). The whole channel is therefore strictly additive and cannot regress the game.</para>
    ///
    /// <para>All names carry the <see cref="Prefix"/>. <c>VariableSyncGuardPatch</c> drops any client-side
    /// replication of those names, so a client creating a variable (whose constructor replicates once) can
    /// never clobber the host-authoritative value.</para>
    /// </summary>
    public static class HostStateSync
    {
        public const string Prefix = "lithium_";

        private static VariableDatabase Db =>
            NetworkSingleton<VariableDatabase>.InstanceExists ? NetworkSingleton<VariableDatabase>.Instance : null;

        /// <summary>True on the authoritative peer (the host). Only the host should publish.</summary>
        public static bool IsHost => InstanceFinder.IsServer;

        // --- Host writes ---------------------------------------------------------------------------------

        public static void SetBool(string key, bool value)
        {
            if (!InstanceFinder.IsServer)
                return;
            try
            {
                VariableDatabase db = Db;
                if (db == null)
                    return;
                string name = Prefix + key;
                EnsureBool(db, name, value);
                db.SetVariableValue(name, value ? "true" : "false", true);
            }
            catch (Exception e)
            {
                Log.Warning($"[Sync] SetBool '{key}' failed: {e.Message}");
            }
        }

        public static void SetNumber(string key, float value)
        {
            if (!InstanceFinder.IsServer)
                return;
            try
            {
                VariableDatabase db = Db;
                if (db == null)
                    return;
                string name = Prefix + key;
                EnsureNumber(db, name, value);
                db.SetVariableValue(name, value.ToString("R", CultureInfo.InvariantCulture), true);
            }
            catch (Exception e)
            {
                Log.Warning($"[Sync] SetNumber '{key}' failed: {e.Message}");
            }
        }

        // --- Client reads (also safe to call on host; host callers normally read their own store instead) -

        public static bool GetBool(string key, bool fallback = false)
        {
            try
            {
                VariableDatabase db = Db;
                if (db == null)
                    return fallback;
                string name = (Prefix + key).ToLower();
                if (!db.VariableDict.ContainsKey(name))
                {
                    // Create it locally so the host's next replication of this name lands in our dict.
                    EnsureBool(db, Prefix + key, fallback);
                    return fallback;
                }
                return db.GetValue<bool>(name);
            }
            catch
            {
                return fallback;
            }
        }

        public static float GetNumber(string key, float fallback = 0f)
        {
            try
            {
                VariableDatabase db = Db;
                if (db == null)
                    return fallback;
                string name = (Prefix + key).ToLower();
                if (!db.VariableDict.ContainsKey(name))
                {
                    EnsureNumber(db, Prefix + key, fallback);
                    return fallback;
                }
                return db.GetValue<float>(name);
            }
            catch
            {
                return fallback;
            }
        }

        // --- Variable creation ---------------------------------------------------------------------------
        // Created identically on every peer (the BaseVariable ctor adds it to VariableDatabase.VariableDict),
        // so a value the host replicates can be applied on each client. On the host the ctor also replicates
        // the initial value; on a client its one replication attempt is dropped by VariableSyncGuardPatch.

        private static void EnsureBool(VariableDatabase db, string name, bool initial)
        {
            if (db.VariableDict.ContainsKey(name.ToLower()))
                return;
            new BoolVariable(name, EVariableReplicationMode.Networked, false, EVariableMode.Global, null, initial);
        }

        private static void EnsureNumber(VariableDatabase db, string name, float initial)
        {
            if (db.VariableDict.ContainsKey(name.ToLower()))
                return;
            new NumberVariable(name, EVariableReplicationMode.Networked, false, EVariableMode.Global, null, initial);
        }
    }
}
