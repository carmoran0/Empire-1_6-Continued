using FactionColonies.util;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Multi-layer freeze diagnostic system. Gated behind FCSettings.PerformanceLogging.
    /// When disabled, all public methods are instant no-ops (single volatile bool check).
    /// </summary>
    public static class PerfWatchdog
    {
        // ══════════════════════════════════════════════
        //  GATE
        // ══════════════════════════════════════════════

        private static volatile bool _enabled = false;
        public static bool Enabled => _enabled;

        public static void SetEnabled(bool enabled)
        {
            if (enabled == _enabled) return;
            _enabled = enabled;

            if (enabled)
            {
                _mainThread = Thread.CurrentThread;
                _mainThreadCallStack = CallStack;

                if (_perfHarmony == null)
                {
                    ApplyBulkPatches();
                    RunStartupAudit();
                }

                StartHeartbeatThread();
                LogUtil.MessageForce("PerfWatchdog: ENABLED - freeze monitoring active");
            }
            else
            {
                LogUtil.MessageForce("PerfWatchdog: DISABLED");
            }
        }

        // ══════════════════════════════════════════════
        //  LAYER 1: HEARTBEAT THREAD
        // ══════════════════════════════════════════════

        private static volatile int _heartbeat = 0;
        private static Thread _heartbeatThread;
        private static Thread _mainThread;
        private const int HeartbeatIntervalMs = 5000;

        public static void Pulse()
        {
            if (!_enabled) return;
            Interlocked.Increment(ref _heartbeat);
        }

        private static void StartHeartbeatThread()
        {
            if (_heartbeatThread != null && _heartbeatThread.IsAlive) return;

            _heartbeatThread = new Thread(HeartbeatLoop);
            _heartbeatThread.IsBackground = true;
            _heartbeatThread.Name = "Empire.PerfWatchdog";
            _heartbeatThread.Start();
        }

        private static void HeartbeatLoop()
        {
            int lastHeartbeat = _heartbeat;
            int missedBeats = 0;
            int aliveLogCounter = 0;

            while (_enabled)
            {
                Thread.Sleep(HeartbeatIntervalMs);
                if (!_enabled) break;

                int current = _heartbeat;
                if (current == lastHeartbeat)
                {
                    missedBeats++;
                    if (missedBeats >= 2)
                    {
                        DumpFreezeReport(missedBeats);
                        missedBeats = 0;
                    }
                }
                else
                {
                    missedBeats = 0;
                    aliveLogCounter++;
                    if (aliveLogCounter >= 6) // every 30s
                    {
                        LogUtil.MessageForce("PerfWatchdog: main thread alive");
                        aliveLogCounter = 0;
                    }
                }
                lastHeartbeat = current;
            }
        }

        // ══════════════════════════════════════════════
        //  LAYER 2: STOPWATCH TIMING
        // ══════════════════════════════════════════════

        private const double SlowThresholdMs = 50.0;

        public static long EnterTimed(string name)
        {
            if (!_enabled) return 0;
            Enter(name);
            return Stopwatch.GetTimestamp();
        }

        public static void ExitTimed(string name, long startTimestamp)
        {
            if (!_enabled) return;
            long elapsed = Stopwatch.GetTimestamp() - startTimestamp;
            double ms = (double)elapsed / Stopwatch.Frequency * 1000.0;
            Exit(name);

            if (ms >= SlowThresholdMs)
            {
                LogUtil.Warning("PerfWatchdog: " + name + " took " + ms.ToString("F1") + "ms");
            }
        }

        // ══════════════════════════════════════════════
        //  LAYER 3: RING BUFFER
        // ══════════════════════════════════════════════

        private struct RingEntry
        {
            public string methodName;
            public long timestampTicks;
            public bool isEnter;
        }

        private const int RingSize = 1024;
        private static readonly RingEntry[] _ring = new RingEntry[RingSize];
        private static int _ringIndex = -1;

        public static void Enter(string name)
        {
            if (!_enabled) return;

            int idx = Interlocked.Increment(ref _ringIndex) & (RingSize - 1);
            _ring[idx].methodName = name;
            _ring[idx].timestampTicks = Stopwatch.GetTimestamp();
            _ring[idx].isEnter = true;

            CallStack.Push(name);
        }

        public static void Exit(string name)
        {
            if (!_enabled) return;

            int idx = Interlocked.Increment(ref _ringIndex) & (RingSize - 1);
            _ring[idx].methodName = name;
            _ring[idx].timestampTicks = Stopwatch.GetTimestamp();
            _ring[idx].isEnter = false;

            Stack<string> stack = CallStack;
            if (stack.Count > 0) stack.Pop();
        }

        // ══════════════════════════════════════════════
        //  LAYER 4: LIVE CALL STACK
        // ══════════════════════════════════════════════

        [ThreadStatic]
        private static Stack<string> _callStack;

        private static Stack<string> _mainThreadCallStack;

        private static Stack<string> CallStack
        {
            get
            {
                if (_callStack == null) _callStack = new Stack<string>(32);
                return _callStack;
            }
        }

        // ══════════════════════════════════════════════
        //  LAYER 5: FREEZE DUMP
        // ══════════════════════════════════════════════

        private static int _dumpCount = 0;

        private static void DumpFreezeReport(int missedBeats)
        {
            int count = Interlocked.Increment(ref _dumpCount);
            StringBuilder sb = new StringBuilder(8192);
            sb.AppendLine();
            sb.AppendLine("=== EMPIRE FREEZE REPORT (dump #" + count + ") ===");
            sb.AppendLine("Main thread stuck for " + (missedBeats * HeartbeatIntervalMs / 1000) + "+ seconds");
            sb.AppendLine("Timestamp: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));

            // 1. Live call stack
            sb.AppendLine();
            sb.AppendLine("--- LIVE CALL STACK (main thread) ---");
            DumpCallStack(sb);

            // 2. Ring buffer
            sb.AppendLine();
            sb.AppendLine("--- RECENT METHOD CALLS (ring buffer, newest first) ---");
            DumpRingBuffer(sb);

            // 3. Unmatched enters
            sb.AppendLine();
            sb.AppendLine("--- UNMATCHED ENTERS (possibly stuck) ---");
            DumpUnmatchedEntries(sb);

            // 4. Harmony conflicts
            sb.AppendLine();
            sb.AppendLine("--- HARMONY PATCHES (non-Empire) ON RING BUFFER METHODS ---");
            DumpHarmonyConflicts(sb);

            // 5. Thread info
            sb.AppendLine();
            sb.AppendLine("--- THREAD INFO ---");
            if (_mainThread != null)
            {
                sb.AppendLine("Main thread state: " + _mainThread.ThreadState);
            }

            // 6. Managed stack trace (via a second snapshot 1s later)
            sb.AppendLine();
            sb.AppendLine("--- STACK CAPTURE ---");
            sb.AppendLine("(Note: managed stack trace from watchdog thread -- main thread stack not directly accessible)");

            sb.AppendLine();
            sb.AppendLine("=== END FREEZE REPORT ===");

            Log.Error("[Empire] " + sb.ToString());
        }

        private static void DumpCallStack(StringBuilder sb)
        {
            if (_mainThreadCallStack == null || _mainThreadCallStack.Count == 0)
            {
                sb.AppendLine("  (empty or not captured)");
                return;
            }

            try
            {
                string[] stack = _mainThreadCallStack.ToArray();
                for (int i = 0; i < stack.Length; i++)
                {
                    sb.AppendLine("  " + i + ": " + stack[i]);
                }
            }
            catch
            {
                sb.AppendLine("  (failed to read -- concurrent modification)");
            }
        }

        private static void DumpRingBuffer(StringBuilder sb)
        {
            int currentIdx = _ringIndex;
            if (currentIdx < 0)
            {
                sb.AppendLine("  (no entries recorded)");
                return;
            }

            // Read entries from newest to oldest (up to 128 most recent)
            int count = Math.Min(128, RingSize);
            long prevTimestamp = 0;
            double freqMs = 1000.0 / Stopwatch.Frequency;

            for (int i = 0; i < count; i++)
            {
                int idx = (currentIdx - i) & (RingSize - 1);
                RingEntry entry = _ring[idx];
                if (entry.methodName == null) break;

                string direction = entry.isEnter ? "ENTER" : "EXIT ";
                string delta = "";
                if (prevTimestamp > 0 && entry.timestampTicks > 0)
                {
                    double deltaMs = (prevTimestamp - entry.timestampTicks) * freqMs;
                    delta = " [+" + deltaMs.ToString("F1") + "ms";
                    if (deltaMs >= 100.0) delta += " !!!";
                    delta += "]";
                }

                sb.AppendLine("  " + i.ToString().PadLeft(3) + ": " + direction + " " + entry.methodName + delta);
                prevTimestamp = entry.timestampTicks;
            }
        }

        private static void DumpUnmatchedEntries(StringBuilder sb)
        {
            int currentIdx = _ringIndex;
            if (currentIdx < 0)
            {
                sb.AppendLine("  (no entries)");
                return;
            }

            // Scan ring buffer for enters without matching exits
            Dictionary<string, int> enterCounts = new Dictionary<string, int>();
            int count = Math.Min(256, RingSize);

            for (int i = 0; i < count; i++)
            {
                int idx = (currentIdx - i) & (RingSize - 1);
                RingEntry entry = _ring[idx];
                if (entry.methodName == null) break;

                if (entry.isEnter)
                {
                    if (enterCounts.ContainsKey(entry.methodName))
                        enterCounts[entry.methodName]++;
                    else
                        enterCounts[entry.methodName] = 1;
                }
                else
                {
                    if (enterCounts.ContainsKey(entry.methodName))
                    {
                        enterCounts[entry.methodName]--;
                        if (enterCounts[entry.methodName] <= 0)
                            enterCounts.Remove(entry.methodName);
                    }
                }
            }

            if (enterCounts.Count == 0)
            {
                sb.AppendLine("  (none found)");
                return;
            }

            foreach (var kvp in enterCounts)
            {
                if (kvp.Value > 0)
                    sb.AppendLine("  " + kvp.Key + " (unmatched x" + kvp.Value + ")");
            }
        }

        private static void DumpHarmonyConflicts(StringBuilder sb)
        {
            HashSet<string> recentMethods = new HashSet<string>();
            int currentIdx = _ringIndex;
            if (currentIdx < 0) return;

            for (int i = 0; i < RingSize; i++)
            {
                int idx = (currentIdx - i) & (RingSize - 1);
                if (_ring[idx].methodName != null)
                    recentMethods.Add(_ring[idx].methodName);
            }

            bool found = false;
            foreach (string method in recentMethods)
            {
                MethodBase mb;
                if (!_patchedMethodLookup.TryGetValue(method, out mb)) continue;

                Patches patches = Harmony.GetPatchInfo(mb);
                if (patches == null) continue;

                List<string> conflicts = new List<string>();

                foreach (HarmonyLib.Patch prefix in patches.Prefixes)
                {
                    if (!IsEmpirePatch(prefix.owner))
                        conflicts.Add("PRE [" + prefix.owner + "] " + prefix.PatchMethod.DeclaringType.FullName + "." + prefix.PatchMethod.Name);
                }
                foreach (HarmonyLib.Patch postfix in patches.Postfixes)
                {
                    if (!IsEmpirePatch(postfix.owner))
                        conflicts.Add("POST [" + postfix.owner + "] " + postfix.PatchMethod.DeclaringType.FullName + "." + postfix.PatchMethod.Name);
                }
                foreach (HarmonyLib.Patch transpiler in patches.Transpilers)
                {
                    if (!IsEmpirePatch(transpiler.owner))
                        conflicts.Add("TRANS [" + transpiler.owner + "] " + transpiler.PatchMethod.DeclaringType.FullName + "." + transpiler.PatchMethod.Name);
                }

                if (conflicts.Count > 0)
                {
                    found = true;
                    sb.AppendLine("  " + method + ":");
                    foreach (string c in conflicts)
                        sb.AppendLine("    " + c);
                }
            }

            if (!found)
                sb.AppendLine("  (none found)");
        }

        private static bool IsEmpirePatch(string owner)
        {
            return owner != null && owner.StartsWith("com.Matathias.Empire");
        }

        // ══════════════════════════════════════════════
        //  STARTUP AUDIT
        // ══════════════════════════════════════════════

        private static void RunStartupAudit()
        {
            StringBuilder sb = new StringBuilder(2048);
            sb.AppendLine();
            sb.AppendLine("=== EMPIRE HARMONY AUDIT ===");

            int conflictCount = 0;
            foreach (var kvp in _patchedMethodLookup)
            {
                Patches patches = Harmony.GetPatchInfo(kvp.Value);
                if (patches == null) continue;

                bool hasEmpire = false;
                bool hasOther = false;
                List<string> others = new List<string>();

                IEnumerable<HarmonyLib.Patch> allPatches = patches.Prefixes
                    .Concat(patches.Postfixes)
                    .Concat(patches.Transpilers);

                foreach (HarmonyLib.Patch p in allPatches)
                {
                    if (IsEmpirePatch(p.owner))
                        hasEmpire = true;
                    else
                    {
                        hasOther = true;
                        others.Add(p.owner + " (" + p.PatchMethod.DeclaringType.FullName + "." + p.PatchMethod.Name + ")");
                    }
                }

                if (hasEmpire && hasOther)
                {
                    conflictCount++;
                    sb.AppendLine(kvp.Key + ":");
                    foreach (string other in others)
                        sb.AppendLine("  " + other);
                }
            }

            sb.AppendLine("Total methods with both Empire and non-Empire patches: " + conflictCount);
            sb.AppendLine("=== END AUDIT ===");

            LogUtil.MessageForce(sb.ToString());
        }

        // ══════════════════════════════════════════════
        //  BULK PATCHING
        // ══════════════════════════════════════════════

        private static Dictionary<string, MethodBase> _patchedMethodLookup = new Dictionary<string, MethodBase>();
        private static Harmony _perfHarmony;

        private static void ApplyBulkPatches()
        {
            _perfHarmony = new Harmony("com.Matathias.Empire.PerfWatchdog");

            Assembly empireAssembly = typeof(FactionFC).Assembly;
            int patchedCount = 0;
            int skippedCount = 0;

            foreach (Type type in empireAssembly.GetTypes())
            {
                if (type.Namespace == null || !type.Namespace.StartsWith("FactionColonies")) continue;
                if (type == typeof(PerfWatchdog)) continue;
                if (type.Name.Contains("<") || type.Name.Contains(">")) continue;
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0) continue;

                MethodInfo[] methods;
                try
                {
                    methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                }
                catch
                {
                    continue;
                }

                foreach (MethodInfo method in methods)
                {
                    if (method.IsAbstract || method.IsSpecialName) { skippedCount++; continue; }
                    if (method.Name.StartsWith("<")) { skippedCount++; continue; }
                    if (method.ContainsGenericParameters) { skippedCount++; continue; }

                    try
                    {
                        MethodBody body = method.GetMethodBody();
                        if (body == null) { skippedCount++; continue; }
                        byte[] il = body.GetILAsByteArray();
                        if (il == null || il.Length < 16) { skippedCount++; continue; }
                    }
                    catch { skippedCount++; continue; }

                    string name = type.Name + "." + method.Name;
                    try
                    {
                        _perfHarmony.Patch(method,
                            prefix: new HarmonyMethod(typeof(PerfWatchdog), nameof(BulkPrefix)),
                            postfix: new HarmonyMethod(typeof(PerfWatchdog), nameof(BulkPostfix)));
                        _patchedMethodLookup[name] = method;
                        patchedCount++;
                    }
                    catch
                    {
                        skippedCount++;
                    }
                }
            }

            PatchBaseGameMethods();

            LogUtil.MessageForce("PerfWatchdog: Bulk patched " + patchedCount + " Empire methods (" + skippedCount + " skipped)");
        }

        private static void PatchBaseGameMethods()
        {
            var targets = new List<KeyValuePair<string, MethodInfo>>();

            // Pawn generation
            AddTarget(targets, typeof(PawnGenerator), "GeneratePawn",
                new Type[] { typeof(PawnGenerationRequest) });

            // Stock generation
            AddTarget(targets, typeof(StockGenerator), "GenerateThings", null);

            // Incidents
            AddTarget(targets, typeof(IncidentWorker), "TryExecute", null);

            // Spawning
            AddTarget(targets, typeof(GenSpawn), "Spawn",
                new Type[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool) });
            AddTarget(targets, typeof(ThingMaker), "MakeThing",
                new Type[] { typeof(ThingDef), typeof(ThingDef) });

            // Map generation
            AddTarget(targets, typeof(MapGenerator), "GenerateMap", null);

            // World pawns
            AddTarget(targets, typeof(WorldPawns), "PassToWorld", null);

            // Pawn group generation
            AddTarget(targets, typeof(PawnGroupMakerUtility), "GeneratePawns", null);

            int basePatched = 0;
            foreach (var kvp in targets)
            {
                if (kvp.Value == null) continue;
                try
                {
                    _perfHarmony.Patch(kvp.Value,
                        prefix: new HarmonyMethod(typeof(PerfWatchdog), nameof(BulkPrefix)),
                        postfix: new HarmonyMethod(typeof(PerfWatchdog), nameof(BulkPostfix)));
                    _patchedMethodLookup[kvp.Key] = kvp.Value;
                    basePatched++;
                }
                catch (Exception ex)
                {
                    LogUtil.Warning("PerfWatchdog: Failed to patch " + kvp.Key + ": " + ex.Message);
                }
            }

            LogUtil.MessageForce("PerfWatchdog: Patched " + basePatched + " base game methods");
        }

        private static void AddTarget(List<KeyValuePair<string, MethodInfo>> targets,
            Type type, string methodName, Type[] parameters)
        {
            string name = "BASE:" + type.Name + "." + methodName;
            try
            {
                MethodInfo mi;
                if (parameters != null)
                    mi = AccessTools.Method(type, methodName, parameters);
                else
                    mi = AccessTools.Method(type, methodName);

                if (mi != null)
                    targets.Add(new KeyValuePair<string, MethodInfo>(name, mi));
                else
                    LogUtil.Warning("PerfWatchdog: Could not find " + name);
            }
            catch (Exception ex)
            {
                LogUtil.Warning("PerfWatchdog: Error resolving " + name + ": " + ex.Message);
            }
        }

        // ── Bulk Prefix/Postfix ──

        public static void BulkPrefix(MethodBase __originalMethod, ref string __state)
        {
            if (!_enabled) return;
            string name = __originalMethod.DeclaringType.Name + "." + __originalMethod.Name;
            __state = name;
            Enter(name);
        }

        public static void BulkPostfix(string __state)
        {
            if (!_enabled || __state == null) return;
            Exit(__state);
        }
    }
}
