using FactionColonies.util;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace FactionColonies
{
    [HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", typeof(PawnGenerationRequest))]
    class PawnGenerationPatches
    {
        public static void Prefix(ref PawnGenerationRequest request)
        {
            PerfWatchdog.Enter("PawnGenerationPatches.Prefix");
            if (!(request.Faction is null) && request.Faction == FactionCache.PlayerColonyFaction && request.KindDef?.IsHumanLikeRace() == true)
            {
                /* Respect xenotypes that have already been forced (e.g. from designed military units)
                 * This has a chance of allowing through forced xenotypes from other sources, which I'm not sure is desirable.
                 * But this should preserve the xenotype that a player chooses when designing military units, and that's more important. */
                if (!(request.ForcedXenotype is null) || !(request.ForcedCustomXenotype is null))
                {
                    PerfWatchdog.Exit();
                    return;
                }

                XenotypeFilter filter = FactionCache.FactionComp?.xenotypeFilter;
                if (filter is null) { PerfWatchdog.Exit(); return; }

                XenotypeDef chosenXenotype = null;
                CustomXenotype chosenCustomXenotype = null;

                filter.GetRandomXenotypeForRequest(request, out chosenXenotype, out chosenCustomXenotype);

                /* Modify the request to force our chosen xenotype */
                if (!(chosenXenotype is null))
                {
                    request.ForcedXenotype = chosenXenotype;
                    //Debug logging
                    LogUtil.Message($"GeneratePawn patch forced xenotype: {chosenXenotype.defName} for pawnKind: {request.KindDef.defName}");
                }
                else if (!(chosenCustomXenotype is null))
                {
                    request.ForcedCustomXenotype = chosenCustomXenotype;
                    //Debug logging
                    LogUtil.Message($"GeneratePawn patch forced custom xenotype: {chosenCustomXenotype.name} for pawnKind: {request.KindDef.defName}");
                }
                else
                {
                    //Debug Logging
                    LogUtil.Warning($"GeneratePawn patch failed to force a xenotype or custom xenotype for pawnKind: {request.KindDef.defName}");
                }
            }
            PerfWatchdog.Exit();
        }
    }
}
