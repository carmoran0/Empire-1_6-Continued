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
    ///   Harmony bulk patching. On freeze, the last 128 calls are dumped.
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
        private const int RING_SIZE = 128;
        private static readonly string[] ringBuffer = new string[RING_SIZE];
        private static volatile int ringIndex = 0;

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
        /// Records a method call into the ring buffer. Called by the bulk Harmony prefix.
        /// Single-writer (main thread only), so volatile index is sufficient — no lock needed.
        /// </summary>
        public static void RecordCall(MethodBase method)
        {
            if (!FCSettings.performanceLogging) return;
            int idx = ringIndex;
            ringBuffer[idx % RING_SIZE] = method.DeclaringType.Name + "." + method.Name;
            ringIndex = idx + 1;
        }

        /// <summary>
        /// Generic Harmony prefix applied to all Empire methods via bulk patching.
        /// Harmony injects __originalMethod automatically.
        /// </summary>
        public static void BulkPrefix(MethodBase __originalMethod)
        {
            RecordCall(__originalMethod);
        }

        /// <summary>
        /// Installs a lightweight prefix on every method in the FactionColonies namespace.
        /// Call once after harmony.PatchAll(). Only patches when performanceLogging is enabled.
        /// </summary>
        public static void InstallBulkPatches(Harmony harmony)
        {
            if (!FCSettings.performanceLogging) return;

            var prefix = new HarmonyMethod(typeof(PerfWatchdog).GetMethod("BulkPrefix", BindingFlags.Public | BindingFlags.Static));
            int count = 0;
            int failed = 0;

            foreach (Type type in typeof(PerfWatchdog).Assembly.GetTypes())
            {
                if (type.Namespace == null || !type.Namespace.StartsWith("FactionColonies")) continue;
                // Skip the watchdog itself to avoid recursion
                if (type == typeof(PerfWatchdog)) continue;

                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (method.IsAbstract) continue;
                    if (method.IsSpecialName) continue; // skip property getters/setters, operators
                    if (method.DeclaringType != type) continue; // skip inherited

                    try
                    {
                        harmony.Patch(method, prefix: prefix);
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
            const int ALIVE_INTERVAL = 6; // log "alive" every 6 cycles (30s at 5s sleep)
            while (running)
            {
                int snapshot = heartbeat;
                Thread.Sleep(FREEZE_DETECT_SECONDS * 1000);
                if (!FCSettings.performanceLogging) { running = false; break; }
                if (heartbeat == snapshot)
                {
                    string method = currentMethod ?? "(unknown)";
                    string recentCalls = DumpRingBuffer();
                    // Capture stack trace twice, 1 second apart (best-effort, may fail on Mono).
                    string stack1 = CaptureMainThreadStack();
                    Thread.Sleep(1000);
                    string stack2 = CaptureMainThreadStack();
                    // Use Unity's Debug.LogWarning directly — RimWorld's Log.Warning
                    // acquires lock(logLock) that the frozen main thread may hold,
                    // and can trigger Unity UI calls from this background thread.
                    UnityEngine.Debug.LogWarning("[Empire] PERF FREEZE DETECTED: main thread stuck for "
                        + FREEZE_DETECT_SECONDS + "+ seconds in: " + method
                        + "\n--- Recent Method Calls (ring buffer, oldest first) ---\n" + recentCalls
                        + "\n--- Stack Capture 1 ---\n" + stack1
                        + "\n--- Stack Capture 2 (1s later) ---\n" + stack2);
                }
                else if (++cycle >= ALIVE_INTERVAL)
                {
                    cycle = 0;
                    UnityEngine.Debug.Log("[Empire] PERF watchdog active (tick #" + heartbeat + ")");
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
