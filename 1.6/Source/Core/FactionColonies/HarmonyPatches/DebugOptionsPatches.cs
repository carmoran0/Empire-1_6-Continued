using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace FactionColonies
{
    [HarmonyPatch(typeof(WorldPawns), "PassToWorld")]
    class MercenaryPassToWorld
    {
        static bool Prefix(Pawn pawn, PawnDiscardDecideMode discardMode = PawnDiscardDecideMode.Decide)
        {
            PerfWatchdog.Enter("PassToWorld.Prefix");
            FactionFC faction = FactionCache.FactionComp;
            bool result = faction?.militaryCustomizationUtil == null || !faction.militaryCustomizationUtil.IsMercenaryPawn(pawn);
            PerfWatchdog.Exit();
            return result;
        }
    }
}
