using HarmonyLib;
using RimWorld;
using Verse;

namespace FactionColonies
{
    [HarmonyPatch(typeof(ResearchManager), "FinishProject")]
    class ResearchCompleted
    {
        static void Postfix(ResearchProjectDef proj, bool doCompletionDialog = false, Pawn researcher = null)
        {
            PerfWatchdog.Enter("ResearchCompleted.Postfix");
            FactionFC fc = FactionCache.FactionComp;
            fc.DirtyTechLevelCache();
            fc.roadBuilder.CheckForTechChanges();

            foreach(WorldSettlementFC settlement in fc.settlements)
            {
                settlement.PrepareResources(fc.techLevel);
            }

            LifecycleRegistry.InvokeOnResearchCompleted(proj);
            PerfWatchdog.Exit();
        }
    }
}
