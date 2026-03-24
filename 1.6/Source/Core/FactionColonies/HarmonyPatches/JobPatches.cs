using FactionColonies.util;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace FactionColonies
{
    [HarmonyPatch(typeof(JobDriver_Goto), "TryExitMap")]
    public class Patch
    {
        static bool Prefix(ref JobDriver_Goto __instance)
        {
            PerfWatchdog.Enter("JobPatches.TryExitMap.Prefix");
            try
            {
                Pawn pawn = __instance.pawn;
                if (!(pawn.Map?.Parent is WorldSettlementFC settlement)) return true;

                var military = settlement.MilitaryComp;
                if (military == null || !military.isUnderAttack) return true;

                // Allow supporting caravan pawns (player's own colonists) to exit
                foreach (var cs in military.supporting)
                {
                    if (cs.pawns.Contains(pawn)) return true;
                }

                // Block defenders from exiting
                if (military.defenders.Contains(pawn)) return false;

                // Fallback: block squad mercenary pawns removed from defenders
                if (pawn.IsMercenary()) return false;

                // Fallback: block any drafted pawn (generic generated defenders drafted via our gizmo)
                if (pawn.Drafted) return false;

                return true;
            }
            finally
            {
                PerfWatchdog.Exit("JobPatches.TryExitMap.Prefix");
            }
        }
    }
}
