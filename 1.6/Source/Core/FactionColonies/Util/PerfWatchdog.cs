using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace FactionColonies
{
    /// <summary>
    /// Lightweight performance watchdog for diagnosing freezes.
    /// Gated behind FCSettings.performanceLogging — no-op when disabled.
    ///
    /// Layer 1 (heartbeat thread): a background thread monitors a counter
    ///   incremented each tick. If it stalls for 5+ seconds, logs which
    ///   Empire method the main thread is stuck in.
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

        /// <summary>
        /// Starts the watchdog thread if not already running.
        /// Call from WorldComponentTick each tick — the volatile bool check is near-free.
        /// </summary>
        public static void EnsureRunning()
        {
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
                    // Use Unity's Debug.LogWarning directly — RimWorld's Log.Warning
                    // acquires lock(logLock) that the frozen main thread may hold,
                    // and can trigger Unity UI calls from this background thread.
                    UnityEngine.Debug.LogWarning("[Empire] PERF FREEZE DETECTED: main thread stuck for "
                        + FREEZE_DETECT_SECONDS + "+ seconds in: " + method);
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
