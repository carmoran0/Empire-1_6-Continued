using FactionColonies.util;
using HarmonyLib;
using Verse;

namespace FactionColonies
{
    [HarmonyPatch(typeof(Pawn), "Kill")]
    class MercenaryDied
    {
        static bool Prefix(Pawn __instance)
        {
            if (__instance.IsMercenary())
            {
                if (__instance.Faction != FindFC.EmpireFaction) __instance.SetFaction(FindFC.EmpireFaction);
                var mfc = FindFC.Military;
                if (mfc is null) return true;
                MercenarySquadFC squad = mfc.ReturnSquadFromUnit(__instance);
                if (squad != null)
                {
                    Mercenary merc = mfc.ReturnMercenaryFromUnit(__instance, squad);
                    if (merc != null)
                    {
                        if (squad.settlement != null)
                        {
                            const double basePenalty = 1.0;
                            double offset = FindFC.FactionComp?
                                .GetStatValue(FCStatDefOf.mercenaryDeathHappinessPenalty, squad.settlement) ?? 0;
                            double total = basePenalty + offset;
                            if (total < 0) total = 0;
                            squad.settlement.GainHappiness(-total);
                        }

                        // Fire death event so submods can react. Auto-replacement was removed by
                        // the strict-manual outfit refactor; the merc's slot is left as an empty
                        // placeholder (pawn = null) and the player must explicitly use
                        // "Fill Empty Slots" in the inspection window to refill it.
                        MercenaryDeathEvent deathEvt = new MercenaryDeathEvent(merc, squad, squad.settlement);
                        LifecycleRegistry.InvokeOnMercenaryDeath(deathEvt);

                        // Mark the slot empty — keep the Mercenary entry so its loadout reference
                        // survives for Fill, but null its pawn.
                        merc.pawn = null;
                        FindFC.Military?.RebuildMercenaryPawnSet();
                    }
                    else
                    {
                        // Not a top-level merc — it's a sub-pawn (animal or mech). Leave the wrapper
                        // in place as a "Missing" placeholder (identity preserved) so the player pays
                        // to replace it; just null the pawn. Neither animals nor mechs are free-replaced.
                        NullDeadSubPawn(mfc.FindSubPawnWrapper(__instance));
                    }

                    // Anti-exploit: sweep gear this squad dropped/left behind so it can't be looted.
                    if (FCSettings.antiExploit) squad.Equipment.RemoveDroppedEquipment();
                }
                else
                {
                    // ReturnSquadFromUnit only matches on-map pawns; a sub-pawn can die off-map.
                    // Fall back to a global, map-independent sub-pawn lookup before warning.
                    Mercenary sub = mfc.FindSubPawnWrapper(__instance);
                    if (sub != null) NullDeadSubPawn(sub);
                    else LogUtil.Warning("Mercenary Errored out. Did not find squad.");
                }

                // Anti-exploit: destroy the dying merc's own gear. When disabled, the gear (and the
                // corpse, via MercenaryAnimalDied) is left intact for the player to loot.
                if (FCSettings.antiExploit)
                {
                    __instance.equipment?.DestroyAllEquipment();
                    __instance.apparel?.DestroyAll();
                }
                return true;
            }

            return true;
        }

        /// <summary>Turns a dead sub-pawn wrapper into a "Missing" placeholder: severs a mech's Overseer
        /// bond (so the mechanitor doesn't keep a relation to the unsaved mech) and nulls the pawn.</summary>
        static void NullDeadSubPawn(Mercenary sub)
        {
            if (sub is null) return;
            if (sub.subPawnType == Mercenary.SubPawnType.Mech && sub.handler?.pawn != null && sub.pawn != null)
                MercenaryPawnFactory.UnbondMech(sub.handler.pawn, sub.pawn);
            sub.pawn = null;
            FindFC.Military?.RebuildMercenaryPawnSet();
        }
    }

    [HarmonyPatch(typeof(DeathActionWorker_Simple), "PawnDied")]
    class MercenaryAnimalDied
    {
        static bool Prefix(Corpse corpse)
        {
            if (FindFC.Military?.IsMercenaryPawn(corpse.InnerPawn) == true)
            {
                //corpse.InnerPawn.SetFaction(FactionColonies.getPlayerColonyFaction());
                // Anti-exploit: destroy the merc corpse (taking implants/gear with it). When disabled,
                // let the corpse persist normally so the player can loot/butcher/harvest it.
                if (FCSettings.antiExploit)
                {
                    corpse.Destroy();
                    return false;
                }
            }

            return true;
        }
    }

    // [HarmonyPatch(typeof(JobGiver_AnimalFlee), "TryGiveJob")]
    class TryGiveJobFleeAnimal
    {
        static bool Prefix(Pawn pawn)
        {
            if (FindFC.Military?.IsMercenaryPawn(pawn) == true)
            {
                return false;
            }

            return true;
        }
    }

    // Strip FC_CombatEfficiency hediffs whenever a pawn leaves the map. Catches every exit path
    // (caravan reformation with captured enemies, fleeing off-map, external defender return,
    // map removal via MapDeiniter.DespawnAll) without enumerating them by hand.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
    class StripCombatEfficiencyOnDeSpawn
    {
        static void Prefix(Pawn __instance)
        {
            MilitaryEfficiencyUtil.RemoveCombatEfficiencyHediff(__instance);
        }
    }

}
