using FactionColonies.util;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace FactionColonies
{
    [HarmonyPatch(typeof(JobDriver_Goto), "TryExitMap")]
    public class Patch
    {
        static bool Prefix(ref JobDriver_Goto __instance)
        {
            PerfWatchdog.Enter("TryExitMap.Prefix");
            Pawn pawn = __instance.pawn;
            if (!(pawn.Map?.Parent is WorldSettlementFC settlement)) { PerfWatchdog.Exit(); return true; }

            var military = settlement.MilitaryComp;
            if (military == null || !military.isUnderAttack) { PerfWatchdog.Exit(); return true; }

            // Allow supporting caravan pawns (player's own colonists) to exit
            foreach (var cs in military.supporting)
            {
                if (cs.pawns.Contains(pawn)) { PerfWatchdog.Exit(); return true; }
            }

            // Block defenders from exiting
            if (military.defenders.Contains(pawn)) { PerfWatchdog.Exit(); return false; }

            // Fallback: block squad mercenary pawns removed from defenders
            if (pawn.IsMercenary()) { PerfWatchdog.Exit(); return false; }

            // Fallback: block any drafted pawn (generic generated defenders drafted via our gizmo)
            if (pawn.Drafted) { PerfWatchdog.Exit(); return false; }

            PerfWatchdog.Exit();
            return true;
        }
    }
}
