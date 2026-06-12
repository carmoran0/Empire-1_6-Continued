using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// A class for tracking whether certain other mods are active. Useful for dynamic UI rendering and stuff.
    /// </summary>
    public static class FactionCompat
    {
        private static bool isGiddyUp2Active = false;
        public static bool GiddyUp2Active => isGiddyUp2Active;

        public static void CheckForMods()
        {
            if (LoadedModManager.RunningMods.Any(mod => mod.PackageId.ToLower() == "memegoddess.giddyup"))
            {
                isGiddyUp2Active = true;
            }
            LogUtil.Message($"[FactionCompat] Giddy Up 2 Active: {GiddyUp2Active}");
        }
    }
}
