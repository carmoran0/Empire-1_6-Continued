using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Class for centrally managing logging.
    /// - Adds a slug to the beginning of log messages
    /// - Gives a central place for disabling/enabling verbose logging without recompiling
    /// </summary>
    public static class LogUtil
    {
        private const string slug = "[Empire]";

        public static void Message(string message)
        {
            if (FCSettings.PrintDebug)
            {
                Log.Message($"{slug} {message}");
            }
        }
        /// <summary>
        /// Prints a non-warning, non-error message to the log even if the user has disabled Verbose Logging.
        /// </summary>
        /// <param name="message"></param>
        public static void MessageForce(string message)
        {
            Log.Message($"{slug} {message}");
        }
        public static void Warning(string message)
        {
            Log.Warning($"{slug}[WARN] {message}");
        }
        public static void Error(string message)
        {
            Log.Error($"{slug}[ERR] {message}");
        }
        public static void ErrorOnce(string message, int key)
        {
            Log.ErrorOnce($"{slug}[ERRONCE] {message}", key);
        }
    }
}
