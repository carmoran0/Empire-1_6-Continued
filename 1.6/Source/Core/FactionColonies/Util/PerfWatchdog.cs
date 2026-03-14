using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using UnityEngine;

#pragma warning disable 612, 618 // Thread.Suspend/Resume and StackTrace(Thread) are deprecated but functional in Mono

namespace FactionColonies
{
    /// <summary>
    /// Lightweight performance watchdog for diagnosing freezes.
    /// Gated behind FCSettings.performanceLogging — no-op when disabled.
    ///
    /// Layer 1 (heartbeat thread): a background thread monitors a counter
    ///   incremented each tick. If it stalls for 5+ seconds, dumps recent
    ///   method calls from a ring buffer + attempts stack trace capture.
    /// Layer 2 (stopwatch): logs a warning if any instrumented call
    ///   exceeds 50ms, catching lag spikes.
    /// Layer 3 (ring buffer): every Empire method call is recorded via
    ///   Harmony bulk patching. On freeze, the last 256 calls are dumped.
    /// Layer 4 (call stack): push on method entry, pop on exit — gives
    ///   exact live call stack at freeze time.
    /// </summary>
    public static class PerfWatchdog
    {
        private static readonly Stopwatch sw = new Stopwatch();
        private static string currentMethod;
        private const long WARN_THRESHOLD_MS = 50;
        private const int FREEZE_DETECT_SECONDS = 5;

        // Heartbeat — incremented by main thread, monitored by watchdog thread
        private static volatile int heartbeat;
        private static volatile bool running;
        private static Thread watchdogThread;
        private static Thread mainThread;

        // Ring buffer — records every Empire method call for freeze diagnostics
        private const int RING_SIZE = 256;
        private static readonly string[] ringBuffer = new string[RING_SIZE];
        private static volatile int ringIndex = 0;

        // Call stack — push on entry, pop on exit, gives live stack at freeze time
        private const int MAX_STACK_DEPTH = 64;
        private static readonly string[] callStack = new string[MAX_STACK_DEPTH];
        private static volatile int stackDepth = 0;

        /// <summary>
        /// Starts the watchdog thread if not already running.
        /// Call from WorldComponentTick each tick — the volatile bool check is near-free.
        /// </summary>
        public static void EnsureRunning()
        {
            // Always capture main thread reference (this runs on the main thread)
            if (mainThread == null)
                mainThread = Thread.CurrentThread;

            if (running || !FCSettings.performanceLogging) return;
            running = true;
            watchdogThread = new Thread(WatchdogLoop);
            watchdogThread.IsBackground = true;
            watchdogThread.Start();
        }

        /// <summary>
        /// Increment the heartbeat counter. Called every tick from WorldComponentTick.
        /// </summary>
        public static void Heartbeat()
        {
            if (!FCSettings.performanceLogging) return;
            heartbeat++;
        }

        public static void Enter(string methodName)
        {
            if (!FCSettings.performanceLogging) return;
            currentMethod = methodName;
            sw.Restart();
        }

        public static void Exit()
        {
            if (!FCSettings.performanceLogging) return;
            sw.Stop();
            if (sw.ElapsedMilliseconds >= WARN_THRESHOLD_MS)
            {
                LogUtil.Warning("PERF: " + currentMethod + " took " + sw.ElapsedMilliseconds + "ms");
            }
            currentMethod = null;
        }

        #region Ring Buffer

        /// <summary>
        /// Records a method entry into the ring buffer.
        /// Single-writer (main thread only), so volatile index is sufficient — no lock needed.
        /// </summary>
        public static void RecordEntry(MethodBase method)
        {
            if (!FCSettings.performanceLogging) return;
            int idx = ringIndex;
            ringBuffer[idx % RING_SIZE] = ">> " + method.DeclaringType.Name + "." + method.Name;
            ringIndex = idx + 1;
        }

        /// <summary>
        /// Records a method exit into the ring buffer.
        /// </summary>
        public static void RecordExit(MethodBase method)
        {
            if (!FCSettings.performanceLogging) return;
            int idx = ringIndex;
            ringBuffer[idx % RING_SIZE] = "<< " + method.DeclaringType.Name + "." + method.Name;
            ringIndex = idx + 1;
        }

        /// <summary>
        /// Generic Harmony prefix applied to all Empire methods via bulk patching.
        /// Records entry in ring buffer and pushes to call stack.
        /// </summary>
        public static void BulkPrefix(MethodBase __originalMethod)
        {
            RecordEntry(__originalMethod);
            if (!FCSettings.performanceLogging) return;
            int depth = stackDepth;
            if (depth < MAX_STACK_DEPTH)
            {
                callStack[depth] = __originalMethod.DeclaringType.Name + "." + __originalMethod.Name;
                stackDepth = depth + 1;
            }
        }

        /// <summary>
        /// Generic Harmony postfix applied to all Empire methods via bulk patching.
        /// Records exit in ring buffer and pops from call stack.
        /// </summary>
        public static void BulkPostfix(MethodBase __originalMethod)
        {
            RecordExit(__originalMethod);
            if (!FCSettings.performanceLogging) return;
            int depth = stackDepth;
            if (depth > 0)
            {
                stackDepth = depth - 1;
                callStack[depth - 1] = null;
            }
        }

        /// <summary>
        /// Installs a lightweight prefix + postfix on every method in the FactionColonies namespace.
        /// Call once after harmony.PatchAll(). Only patches when performanceLogging is enabled.
        /// </summary>
        public static void InstallBulkPatches(Harmony harmony)
        {
            if (!FCSettings.performanceLogging) return;

            var prefix = new HarmonyMethod(typeof(PerfWatchdog).GetMethod("BulkPrefix", BindingFlags.Public | BindingFlags.Static));
            var postfix = new HarmonyMethod(typeof(PerfWatchdog).GetMethod("BulkPostfix", BindingFlags.Public | BindingFlags.Static));
            int count = 0;
            int failed = 0;

            foreach (Type type in typeof(PerfWatchdog).Assembly.GetTypes())
            {
                if (type.Namespace == null || !type.Namespace.StartsWith("FactionColonies")) continue;
                // Skip the watchdog itself to avoid recursion
                if (type == typeof(PerfWatchdog)) continue;
                // Skip test infrastructure — generics/throwing stubs cause Harmony IL errors
                string fullName = type.FullName ?? "";
                if (fullName.Contains("TestAssert") || fullName.Contains("TestRunner")
                    || fullName.Contains("Throwing") || fullName.Contains("Tests+")) continue;

                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (method.IsAbstract) continue;
                    if (method.IsSpecialName) continue; // skip property getters/setters, operators
                    if (method.DeclaringType != type) continue; // skip inherited
                    if (method.IsGenericMethodDefinition) continue; // Harmony can't patch open generics

                    try
                    {
                        harmony.Patch(method, prefix: prefix, postfix: postfix);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        UnityEngine.Debug.Log("[Empire] PERF: failed to bulk-patch " + type.Name + "." + method.Name + ": " + ex.Message);
                    }
                }
            }
            UnityEngine.Debug.Log("[Empire] PERF: bulk-patched " + count + " methods for ring buffer recording (" + failed + " failed)");
        }

        private static string DumpRingBuffer()
        {
            var sb = new StringBuilder(4096);
            int idx = ringIndex; // snapshot volatile
            int start = idx >= RING_SIZE ? idx - RING_SIZE : 0;
            for (int i = start; i < idx; i++)
            {
                string entry = ringBuffer[i % RING_SIZE];
                if (entry != null)
                {
                    sb.Append("  ");
                    sb.Append(i - start);
                    sb.Append(": ");
                    sb.Append(entry);
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Scans the ring buffer for >> entries with no matching << exit.
        /// These represent the reconstructed call stack at freeze time.
        /// O(n²) but only runs during freeze diagnostics.
        /// </summary>
        private static string FindUnmatchedEntries()
        {
            var sb = new StringBuilder(512);
            int idx = ringIndex; // snapshot volatile
            int start = idx >= RING_SIZE ? idx - RING_SIZE : 0;

            for (int i = start; i < idx; i++)
            {
                string entry = ringBuffer[i % RING_SIZE];
                if (entry == null || !entry.StartsWith(">> ")) continue;

                string methodName = entry.Substring(3); // strip ">> "
                bool matched = false;
                for (int j = i + 1; j < idx; j++)
                {
                    string other = ringBuffer[j % RING_SIZE];
                    if (other == null) continue;
                    if (other == "<< " + methodName)
                    {
                        matched = true;
                        break;
                    }
                }
                if (!matched)
                {
                    sb.Append("  ");
                    sb.Append(i - start);
                    sb.Append(": ");
                    sb.Append(methodName);
                    sb.Append('\n');
                }
            }
            return sb.Length > 0 ? sb.ToString() : "  (none)\n";
        }

        /// <summary>
        /// Dumps the live call stack (methods that have been entered but not exited).
        /// </summary>
        private static string DumpCallStack()
        {
            var sb = new StringBuilder(512);
            int depth = stackDepth; // snapshot volatile
            if (depth >= MAX_STACK_DEPTH)
            {
                sb.Append("  (stack overflow — likely infinite recursion, showing top " + MAX_STACK_DEPTH + ")\n");
            }
            int count = depth < MAX_STACK_DEPTH ? depth : MAX_STACK_DEPTH;
            for (int i = 0; i < count; i++)
            {
                string entry = callStack[i];
                if (entry != null)
                {
                    sb.Append("  ");
                    sb.Append(i);
                    sb.Append(": ");
                    sb.Append(entry);
                    sb.Append('\n');
                }
            }
            return sb.Length > 0 ? sb.ToString() : "  (empty)\n";
        }

        #endregion

        /// <summary>
        /// Attempts to capture the main thread's stack trace using deprecated
        /// Thread.Suspend/Resume + StackTrace(Thread, bool).
        /// These APIs are deprecated in .NET Framework 4.x but still functional in Mono.
        /// Safe here because the main thread is already frozen.
        /// NOTE: Mono throws NotImplementedException — this is a best-effort fallback.
        /// </summary>
        private static string CaptureMainThreadStack()
        {
            if (mainThread == null) return "(main thread ref not captured)";
            try
            {
                //mainThread.Suspend();
                try
                {
                    var st = new StackTrace(mainThread, false);
                    return st.ToString();
                }
                finally
                {
                    //mainThread.Resume();
                }
            }
            catch (Exception ex)
            {
                return "(stack capture failed: " + ex.GetType().Name + ": " + ex.Message + ")";
            }
        }

        private static void WatchdogLoop()
        {
            int cycle = 0;
            int freezeDumpCount = 0;
            const int ALIVE_INTERVAL = 6; // log "alive" every 6 cycles (30s at 5s sleep)
            while (running)
            {
                // Escalating sleep: 5s (initial), 10s (after first dump), 30s (after second+)
                int sleepMs;
                if (freezeDumpCount == 0)
                    sleepMs = FREEZE_DETECT_SECONDS * 1000;
                else if (freezeDumpCount == 1)
                    sleepMs = 10 * 1000;
                else
                    sleepMs = 30 * 1000;

                int snapshot = heartbeat;
                Thread.Sleep(sleepMs);
                if (!FCSettings.performanceLogging) { running = false; break; }
                if (heartbeat == snapshot)
                {
                    freezeDumpCount++;
                    string method = currentMethod ?? "(unknown)";
                    string liveStack = DumpCallStack();
                    string recentCalls = DumpRingBuffer();
                    string unmatchedEntries = FindUnmatchedEntries();
                    // Capture stack trace twice, 1 second apart (best-effort, may fail on Mono).
                    string stack1 = CaptureMainThreadStack();
                    Thread.Sleep(1000);
                    string stack2 = CaptureMainThreadStack();
                    // Use Unity's Debug.LogWarning directly — RimWorld's Log.Warning
                    // acquires lock(logLock) that the frozen main thread may hold,
                    // and can trigger Unity UI calls from this background thread.
                    UnityEngine.Debug.LogWarning("[Empire] PERF FREEZE DETECTED (dump #" + freezeDumpCount
                        + "): main thread stuck for " + (sleepMs / 1000) + "+ seconds in: " + method
                        + "\n--- Live Call Stack ---\n" + liveStack
                        + "\n--- Recent Method Calls (ring buffer, oldest first) ---\n" + recentCalls
                        + "\n--- Unmatched Entries (reconstructed call stack) ---\n" + unmatchedEntries
                        + "\n--- Stack Capture 1 ---\n" + stack1
                        + "\n--- Stack Capture 2 (1s later) ---\n" + stack2);
                }
                else
                {
                    freezeDumpCount = 0; // heartbeat resumed, reset escalation
                    if (++cycle >= ALIVE_INTERVAL)
                    {
                        cycle = 0;
                        UnityEngine.Debug.Log("[Empire] PERF watchdog active (tick #" + heartbeat + ")");
                    }
                }
            }
        }

        public static void Shutdown()
        {
            running = false;
        }
    }
}

#pragma warning restore 612, 618
