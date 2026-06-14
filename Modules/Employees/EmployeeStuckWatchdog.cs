using System;
using System.Collections.Generic;
using Il2CppFishNet;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Behaviour;
using UnityEngine;
using UnityEngine.AI;

// Il2CppScheduleOne.NPCs.Behaviour.Behaviour collides with UnityEngine.Behaviour; alias the game one.
using GameBehaviour = Il2CppScheduleOne.NPCs.Behaviour.Behaviour;

namespace Lithium.Modules.Employees
{
    /// <summary>
    /// Config for the "unstick stuck workers" sub-feature. Gated by its own <see cref="Enabled"/> (independent of the
    /// employee-tuning <c>Enabled</c>), on by default — it's a pure quality-of-life bug fix with no economy impact.
    /// </summary>
    public class EmployeeUnstickConfiguration
    {
        public bool Enabled = true;

        /// <summary>How often (seconds) to scan employees for being stuck. Cheap; there are only a handful of workers.</summary>
        public float ScanIntervalSeconds = 0.5f;

        /// <summary>
        /// How far (metres, horizontal) a worker must travel within <see cref="StuckSeconds"/> to count as "making
        /// progress". A normally-walking worker covers several metres per second, so this only fails to clear when the
        /// worker is genuinely pinned in place (or shuffling on the spot).
        /// </summary>
        public float ProgressEpsilon = 1.0f;

        /// <summary>
        /// Seconds a worker may stay frozen (while it still has a walk destination) before the first, gentle recovery
        /// (re-path). Kept comfortably above any legitimate brief pause (avoidance shuffles, waiting for an auto-door)
        /// so normal behaviour is never disturbed.
        /// </summary>
        public float StuckSeconds = 5.0f;

        /// <summary>
        /// If a worker is still frozen this many seconds after the gentle re-path, escalate to a short teleport to its
        /// destination (the same kind of recovery the game itself uses after repeated pathing failures).
        /// </summary>
        public float WarpAfterSeconds = 4.0f;

        /// <summary>Radius (metres) to search for a valid NavMesh point near the destination when teleporting a wedged worker.</summary>
        public float WarpSampleRadius = 5.0f;
    }

    /// <summary>
    /// Frees employees (botanists, chemists, packagers, cleaners) who get physically wedged while walking to a task —
    /// the classic "my worker is stuck until I talk to it" problem.
    ///
    /// <para><b>Why they get stuck.</b> Each work step (<see cref="WaterPotBehaviour"/>, <see cref="MoveItemBehaviour"/>,
    /// the station behaviours, …) starts a coroutine that calls <c>SetDestination(...)</c> then blocks on
    /// <c>WaitUntil(() =&gt; !Movement.IsMoving)</c>. <c>IsMoving</c> stays <c>true</c> while the NavMeshAgent has a path
    /// with <c>remainingDistance &gt; 0.25</c>. When the worker physically wedges (another worker in a doorway, a
    /// collider, a mutual deadlock) the agent keeps a <i>valid path it can't traverse</i>, so <c>IsMoving</c> never goes
    /// false, the coroutine never returns, the behaviour stays <c>Active</c>, and the employee's <c>UpdateBehaviour</c>
    /// just marks "still working" forever without re-deriving. The game's own teleport recovery only fires on pathing
    /// <i>failures</i> (<c>WalkResult.Failed</c>), which never happen here — hence the lock-up.</para>
    ///
    /// <para><b>Why talking fixes it.</b> Talking activates <see cref="GenericDialogueBehaviour"/>, whose
    /// <c>Activate()</c> calls <c>Movement.Stop()</c> and pauses the work behaviour (killing the hung coroutine); ending
    /// the chat resumes it, which resets the behaviour's sub-state to Idle so it re-paths from scratch.</para>
    ///
    /// <para><b>What this does.</b> Host-only, it detects a worker that is frozen in place while it still has a walk
    /// destination, and reproduces what talking does: pause + resume the active behaviour (a fresh re-path). If that
    /// doesn't take within <see cref="EmployeeUnstickConfiguration.WarpAfterSeconds"/>, it teleports the worker to a
    /// NavMesh point at its destination — mirroring the game's own failure-recovery warp. Detection is gated on
    /// <c>Movement.HasDestination</c>, which talking clears and which is <c>false</c> while a worker performs a
    /// stationary action (watering/sowing/cooking), so conversations and legitimate stand-still work are never touched.</para>
    /// </summary>
    public class EmployeeStuckWatchdog
    {
        /// <summary>Per-employee progress tracking for the current "walk episode".</summary>
        private sealed class Tracker
        {
            public Vector3 Anchor;        // position we measure progress from
            public float AnchorTime;      // Time.time when the anchor last advanced (i.e. last real progress)
            public int BehaviourId;       // instance id of the active behaviour at anchor time (task change resets the episode)
            public bool Nudged;           // whether the gentle re-path has already been tried this episode
        }

        private readonly Dictionary<int, Tracker> _trackers = new();
        private readonly HashSet<int> _currentIds = new();
        private readonly List<int> _pruneList = new();
        private float _nextScan;

        /// <summary>Drop all stale tracking (call on scene/save load).</summary>
        public void Reset()
        {
            _trackers.Clear();
            _nextScan = 0f;
        }

        public void Tick(EmployeeUnstickConfiguration settings)
        {
            if (settings == null || !settings.Enabled)
                return;

            // Movement, pursuit and warps are all server-authoritative — only the host drives this.
            if (!InstanceFinder.IsServer || !Core.IsInMainScene)
                return;

            float now = Time.time;
            if (now < _nextScan)
                return;
            _nextScan = now + Mathf.Max(0.1f, settings.ScanIntervalSeconds);

            if (!NetworkSingleton<EmployeeManager>.InstanceExists)
                return;
            EmployeeManager manager = NetworkSingleton<EmployeeManager>.Instance;
            Il2CppSystem.Collections.Generic.List<Employee> employees = manager?.AllEmployees;
            if (employees == null)
                return;

            _currentIds.Clear();
            for (int i = 0; i < employees.Count; i++)
            {
                Employee e = employees[i];
                if (e == null)
                    continue;

                int id = e.GetInstanceID();
                _currentIds.Add(id);

                try
                {
                    Evaluate(e, id, now, settings);
                }
                catch (Exception ex)
                {
                    // Never let one bad worker break the whole watchdog.
                    _trackers.Remove(id);
                    if (Log.DebugEnabled)
                        Log.Warning($"[EmployeeUnstick] Evaluate failed for an employee: {ex.Message}");
                }
            }

            PruneDespawned();
        }

        private void Evaluate(Employee e, int id, float now, EmployeeUnstickConfiguration settings)
        {
            NPCMovement movement = e.Movement;
            NPCBehaviour behaviour = e.Behaviour;
            GameBehaviour active = behaviour != null ? behaviour.activeBehaviour : null;

            // Only consider a worker that is actively trying to walk to a task:
            //  - conscious, not in a vehicle, not fired, not idling outside;
            //  - has a live walk destination and isn't deliberately paused;
            //  - has an active behaviour driving that walk.
            // HasDestination is the key gate: talking clears it (dialogue calls Movement.Stop()), and it is false while
            // the worker performs a stationary action — so conversations and legitimate stand-still work are excluded.
            bool eligible =
                e.IsConscious &&
                !e.IsInVehicle &&
                !e.Fired &&
                !e.IsWaitingOutside &&
                movement != null && movement.HasDestination && !movement.IsPaused &&
                active != null && active.Active;

            if (!eligible)
            {
                _trackers.Remove(id);
                return;
            }

            Vector3 pos = e.transform.position;
            int behId = active.GetInstanceID();

            if (!_trackers.TryGetValue(id, out Tracker t))
            {
                _trackers[id] = new Tracker { Anchor = pos, AnchorTime = now, BehaviourId = behId };
                return;
            }

            // Progress (moved away from the anchor) or a task switch → fresh episode, all clear.
            if (behId != t.BehaviourId || HorizontalDistance(pos, t.Anchor) >= settings.ProgressEpsilon)
            {
                t.Anchor = pos;
                t.AnchorTime = now;
                t.BehaviourId = behId;
                t.Nudged = false;
                return;
            }

            float frozenFor = now - t.AnchorTime;

            if (!t.Nudged)
            {
                if (frozenFor >= settings.StuckSeconds)
                {
                    Nudge(e, movement, active);
                    t.Nudged = true;
                    t.AnchorTime = now; // give the re-path time to take effect before escalating
                }
            }
            else if (frozenFor >= settings.WarpAfterSeconds)
            {
                Warp(e, movement, active, settings);
                // Recovery applied — reset the episode from the (now warped) position.
                t.Anchor = e.transform.position;
                t.AnchorTime = now;
                t.BehaviourId = active.GetInstanceID();
                t.Nudged = false;
            }
        }

        /// <summary>
        /// Gentle recovery — exactly what talking does: stop the wedged path, then pause+resume the active behaviour so
        /// its hung walk coroutine is killed and its sub-state resets to Idle, forcing a fresh re-path next tick.
        /// </summary>
        private static void Nudge(Employee e, NPCMovement movement, GameBehaviour active)
        {
            if (Log.DebugEnabled)
                Log.Info($"[EmployeeUnstick] {e.fullName} appears stuck — re-pathing (pause/resume '{active.Name}').");

            movement.Stop();
            active.Pause_Server();
            active.Resume_Server();
        }

        /// <summary>
        /// Escalated recovery for a worker the re-path couldn't free (e.g. a mutual deadlock): teleport it to a NavMesh
        /// point at its destination, then reset the behaviour so it proceeds with the task. This mirrors the game's own
        /// recovery, which warps an NPC to its destination after repeated pathing failures.
        /// </summary>
        private void Warp(Employee e, NPCMovement movement, GameBehaviour active, EmployeeUnstickConfiguration settings)
        {
            Vector3 dest = movement.CurrentDestination;
            Vector3 target = dest;
            if (NavMesh.SamplePosition(dest, out NavMeshHit hit, Mathf.Max(1f, settings.WarpSampleRadius), NavMesh.AllAreas))
                target = hit.position;

            if (Log.DebugEnabled)
                Log.Info($"[EmployeeUnstick] {e.fullName} still stuck after re-path — warping to {target} (near destination {dest}).");

            movement.Warp(target);
            active.Pause_Server();
            active.Resume_Server();
        }

        /// <summary>Forget trackers for employees that have despawned (fired/transferred) so the map can't slowly grow.</summary>
        private void PruneDespawned()
        {
            if (_trackers.Count <= _currentIds.Count)
                return;

            _pruneList.Clear();
            foreach (KeyValuePair<int, Tracker> kv in _trackers)
                if (!_currentIds.Contains(kv.Key))
                    _pruneList.Add(kv.Key);
            foreach (int id in _pruneList)
                _trackers.Remove(id);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
