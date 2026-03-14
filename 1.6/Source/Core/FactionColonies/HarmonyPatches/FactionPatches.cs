using FactionColonies.util;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using Verse;

namespace FactionColonies
{//stops friendly faction from being a group source
    [HarmonyPatch(typeof(IncidentWorker_RaidFriendly), "TryResolveRaidFaction")]
    class RaidFriendlyStopSettlementFaction
    {
        static void Postfix(ref IncidentWorker_RaidFriendly __instance, ref bool __result, IncidentParms parms)
        {
            PerfWatchdog.Enter("RaidFriendlyStop.Postfix");
            if (parms.faction == FactionCache.PlayerColonyFaction)
            {
                parms.faction = null;
                __result = false;
            }
            PerfWatchdog.Exit();
        }
    }

    //Goodwill by distance to settlement
    [HarmonyPatch(typeof(SettlementProximityGoodwillUtility), "AppendProximityGoodwillOffsets")]
    class GoodwillPatch
    {
        static void Postfix(PlanetTile tile, List<Pair<Settlement, int>> outOffsets, bool ignoreIfAlreadyMinGoodwill, bool ignorePermanentlyHostile)
        {
            PerfWatchdog.Enter("GoodwillPatch.Postfix");
            outOffsets.RemoveAll(pair => pair.First.Faction == FactionCache.PlayerColonyFaction);
            PerfWatchdog.Exit();
        }
    }

    //CheckReachNaturalGoodwill()
    [HarmonyPatch(typeof(Faction), "CheckReachNaturalGoodwill")]
    class GoodwillPatchFunctionsGoodwillTendency
    {
        static bool Prefix(ref Faction __instance)
        {
            PerfWatchdog.Enter("GoodwillTendency.Prefix");
            if (__instance == FactionCache.PlayerColonyFaction)
            {
                PerfWatchdog.Exit();
                return false;
            }

            PerfWatchdog.Exit();
            return true;
        }
    }

    //tryAffectGoodwillWith
    [HarmonyPatch(typeof(Faction), "TryAffectGoodwillWith")]
    class GoodwillPatchFunctionsGoodwillAffect
    {
        static bool Prefix(ref Faction __instance, Faction other, int goodwillChange, bool canSendMessage = true,
            bool canSendHostilityLetter = true, HistoryEventDef reason = null, GlobalTargetInfo? lookTarget = null)
        {
            PerfWatchdog.Enter("GoodwillAffect.Prefix");
            if (__instance == FactionCache.PlayerColonyFaction && other == Find.FactionManager.OfPlayer)
            {
                if (reason == HistoryEventDefOf.RequestedTrader ||
                    reason == HistoryEventDefOf.GaveGift ||
                    reason == HistoryEventDefOf.Traded)
                {
                    PerfWatchdog.Exit();
                    return false;
                }

                PerfWatchdog.Exit();
                return true;
            }

            PerfWatchdog.Exit();
            return true;
        }
    }


    //Notify_MemberDied(Pawn member, DamageInfo? dinfo, bool wasWorldPawn, Map map)
    [HarmonyPatch(typeof(Faction), "Notify_MemberDied")]
    class GoodwillPatchFunctionsMemberDied
    {
        static bool Prefix(ref Faction __instance, Pawn member, DamageInfo? dinfo, bool wasWorldPawn, Map map)
        {
            PerfWatchdog.Enter("MemberDied.Prefix");
            if (member.Faction == FactionCache.PlayerColonyFaction && !wasWorldPawn &&
                !PawnGenerator.IsBeingGenerated(member) && map != null &&
                (map.IsPlayerHome || map.Parent is WorldSettlementFC) &&
                !__instance.HostileTo(Faction.OfPlayer))
            {
                FactionFC faction = FactionCache.FactionComp;
                if (!faction.AnyPolicySuppressesMemberDeathPenalty() && dinfo != null)
                {
                    if (dinfo.Value.Category == DamageInfo.SourceCategory.Collapse)
                    {
                        faction.GainUnrestForReason(new Message("DeathOfFactionPawn".Translate(), MessageTypeDefOf.PawnDeath), 5d);
                        faction.GainHappiness(-5d);
                    }
                    else if (dinfo.Value.Instigator?.Faction == Find.FactionManager.OfPlayer)
                    {
                        faction.GainUnrestForReason(new Message("DeathOfFactionPawn".Translate(), MessageTypeDefOf.PawnDeath), 5d);
                        faction.GainHappiness(-5d);
                    }
                }

                //return false to stop from continuing method
                PerfWatchdog.Exit();
                return false;
            }

            PerfWatchdog.Exit();
            return true;
        }
    }

    //Player traded
    [HarmonyPatch(typeof(Faction), "Notify_PlayerTraded")]
    class GoodwillPatchFunctionsPlayerTraded
    {
        static bool Prefix(ref Faction __instance, float marketValueSentByPlayer, Pawn playerNegotiator)
        {
            PerfWatchdog.Enter("PlayerTraded.Prefix");
            if (__instance == FactionCache.PlayerColonyFaction)
            {
                PerfWatchdog.Exit();
                return false;
            }

            PerfWatchdog.Exit();
            return true;
        }
    }

    //Player traded
    [HarmonyPatch(typeof(Faction), "Notify_MemberCaptured")]
    class GoodwillPatchFunctionsCapturedPawn
    {
        static bool Prefix(ref Faction __instance, Pawn member, Faction violator)
        {
            PerfWatchdog.Enter("CapturedPawn.Prefix");
            if (__instance == FactionCache.PlayerColonyFaction && violator == Faction.OfPlayer && !member.IsSlaveOfColony)
            {
                FactionFC faction = FactionCache.FactionComp;
                faction.GainUnrestForReason(new Message("CaptureOfFactionPawn".Translate(), MessageTypeDefOf.NegativeEvent), 15d);
                faction.GainHappiness(-10d);

                PerfWatchdog.Exit();
                return false;
            }

            PerfWatchdog.Exit();
            return true;
        }
    }

    [HarmonyPatch(typeof(Faction), "Notify_MemberTookDamage")]
    class GoodwillPatchFunctionsTookDamage
    {
        static bool Prefix(ref Faction __instance, Pawn member, DamageInfo dinfo)
        {
            PerfWatchdog.Enter("TookDamage.Prefix");
            if (__instance == FactionCache.PlayerColonyFaction)
            {
                PerfWatchdog.Exit();
                return false;
            }

            PerfWatchdog.Exit();
            return true;
        }
    }
} 
