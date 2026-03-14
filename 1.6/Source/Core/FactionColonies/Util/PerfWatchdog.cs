using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

//#pragma warning disable 612, 618 // Thread.Suspend/Resume and StackTrace(Thread) are deprecated but functional in Mono

namespace FactionColonies
{
    /// <summary>
    /// Lightweight performance watchdog for diagnosing freezes.
    /// Gated behind FCSettings.performanceLogging — no-op when disabled.
    ///
    /// Layer 1 (heartbeat thread): a background thread monitors a counter
    ///   incremented each tick. If it stalls for 5+ seconds, captures the
    ///   main thread's full stack trace via Thread.Suspend + StackTrace.
    /// Layer 2 (stopwatch): logs a warning if any instrumented call
    ///   exceeds 50ms, catching lag spikes.
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

        /// <summary>
        /// Attempts to capture the main thread's stack trace using deprecated
        /// Thread.Suspend/Resume + StackTrace(Thread, bool).
        /// These APIs are deprecated in .NET Framework 4.x but still functional in Mono.
        /// Safe here because the main thread is already frozen.
        /// </summary>
        private static string CaptureMainThreadStack()
        {
            if (mainThread == null) return "(main thread ref not captured)";
            try
            {
                mainThread.Suspend();
                try
                {
                    var st = new StackTrace(mainThread, false);
                    return st.ToString();
                }
                finally
                {
                    mainThread.Resume();
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
                    // Capture stack trace twice, 1 second apart.
                    // Two identical traces = infinite loop. Two different = slow operation.
                    string stack1 = CaptureMainThreadStack();
                    Thread.Sleep(1000);
                    string stack2 = CaptureMainThreadStack();
                    // Use Unity's Debug.LogWarning directly — RimWorld's Log.Warning
                    // acquires lock(logLock) that the frozen main thread may hold,
                    // and can trigger Unity UI calls from this background thread.
                    UnityEngine.Debug.LogWarning("[Empire] PERF FREEZE DETECTED: main thread stuck for "
                        + FREEZE_DETECT_SECONDS + "+ seconds in: " + method
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

//#pragma warning restore 612, 618
