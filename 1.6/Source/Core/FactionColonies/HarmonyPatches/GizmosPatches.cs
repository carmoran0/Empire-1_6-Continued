using FactionColonies.util;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace FactionColonies
{
    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    class PawnDraftGizmos
    {
        public static void Postfix(ref Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            // Early exit checks BEFORE any allocations - most pawns will exit here
            if (__result == null || __instance?.Faction == null || __instance.Map == null)
            {
                return;
            }

            WorldSettlementFC settlementFc = __instance.Map.Parent as WorldSettlementFC;
            if (settlementFc == null)
            {
                return;
            }

            Faction playerColonyFaction = FindFC.EmpireFaction;

            // Only allow drafting Empire defenders during an active battle. Sub-pawns (companion animals
            // / bonded mechs) aren't individually draftable — they follow their owning merc's faction
            // automatically (see MercSubPawnsFollowFaction) — so exclude them (checked last so the
            // squad scan only runs for Empire pawns mid-battle).
            if (__instance.Faction == playerColonyFaction && settlementFc.MilitaryComp?.isUnderAttack == true
                && FindFC.Military?.FindSubPawnWrapper(__instance) is null)
            {
                Pawn pawn = __instance;
                var milComp = settlementFc.MilitaryComp;

                Command_Toggle draftColonists = new Command_Toggle
                {
                    hotKey = KeyBindingDefOf.Command_ColonistDraft,
                    isActive = () => false,
                    toggleAction = () =>
                    {
                        if (pawn.Faction == Faction.OfPlayer) return;
                        pawn.SetFaction(Faction.OfPlayer);
                        // SetFaction → AddAndRemoveDynamicComponents creates pawn.drafter for OfPlayer pawns
                        if (pawn.drafter != null)
                            pawn.drafter.Drafted = true;
                        // Vanilla switched any bonded mechs to the player faction too, but the bandwidth
                        // recalc can leave them "uncontrolled" — re-assign them to the (now player)
                        // mechanitor's control groups so the player can command them.
                        Mercenary drafted = FindFC.Military?.FindMercByPawn(pawn);
                        if (drafted != null) MercenaryPawnFactory.RebindMechs(drafted);
                        // Track drafted NPC for faction restoration after battle
                        if (milComp != null && !milComp.draftedNPCs.Contains(pawn))
                            milComp.draftedNPCs.Add(pawn);
                    },
                    defaultDesc = "CommandToggleDraftDesc".Translate(),
                    icon = TexCommand.Draft,
                    turnOnSound = SoundDefOf.DraftOn,
                    groupKey = 81729172,
                    defaultLabel = "CommandDraftLabel".Translate()
                };

                if (pawn.Downed)
                {
                    draftColonists.Disable("IsIncapped".Translate(pawn.LabelShort, pawn));
                }

                draftColonists.tutorTag = "Draft";
                __result = __result.Append(draftColonists);
                return;
            }

            // Undraft toggle for drafted Empire NPCs (only during active battle)
            if (__instance.Faction == Faction.OfPlayer && __instance.Drafted
                && settlementFc.MilitaryComp?.isUnderAttack == true
                && settlementFc.MilitaryComp.draftedNPCs.Contains(__instance))
            {
                Pawn found = __instance;
                var milComp = settlementFc.MilitaryComp;

                List<Gizmo> output = __result.ToList();
                foreach (Gizmo gizmo in output)
                {
                    Command_Toggle action = gizmo as Command_Toggle;
                    if (action != null && action.hotKey == KeyBindingDefOf.Command_ColonistDraft)
                    {
                        action.toggleAction = () =>
                        {
                            // SetFaction cascades to sub-pawns via MercSubPawnsFollowFaction, so the
                            // merc's animals/mechs return to the Empire faction too.
                            found.SetFaction(FindFC.EmpireFaction);
                            milComp.draftedNPCs.Remove(found);
                            // Re-add the merc AND its sub-pawns to defenders + the defense lord after
                            // undrafting, so they rejoin the fight. Routes through the BattlefieldContext
                            // so per-op pawn lists stay aligned.
                            Mercenary merc = FindFC.Military?.FindMercByPawn(found);
                            // Reconnect the merc's mechs to its (now Empire) mechanitor control after the
                            // faction swap back, else they sit uncontrolled.
                            if (merc != null) MercenaryPawnFactory.RebindMechs(merc);

                            if (milComp.defenders.Any())
                            {
                                List<Pawn> rejoin = new List<Pawn> { found };
                                if (merc != null)
                                    foreach (Mercenary sub in merc.SubPawns())
                                        if (sub?.pawn != null && sub.pawn.Spawned && !sub.pawn.Dead)
                                            rejoin.Add(sub.pawn);

                                BattlefieldContext bf = FindFC.MilitaryManager?.GetBattlefield(milComp.WorldSettlement.Tile);
                                bf?.RegisterPawnsAsDefenders(rejoin, assignToLord: false);

                                // Find the active Empire defense lord on this map directly. Picking the
                                // first defender's lord was fragile: `found` is itself in `defenders` and its
                                // lord is null right after undrafting, so if it (or any lordless defender)
                                // came first, the rejoin was silently skipped and the pawns — having no lord
                                // — would try to leave the map instead of fighting.
                                Lord defenderLord = found.Map?.lordManager?.lords
                                    .FirstOrDefault(l => l != null && l.faction == FindFC.EmpireFaction
                                                         && l.LordJob is LordJob_DefendColony);
                                if (defenderLord != null)
                                {
                                    foreach (Pawn p in rejoin)
                                        if (!defenderLord.ownedPawns.Contains(p))
                                            defenderLord.AddPawn(p);
                                    defenderLord.CurLordToil.UpdateAllDuties();
                                    // Force each pawn off its current (wander/follow) job so it immediately
                                    // adopts the lord's combat duty instead of idling.
                                    foreach (Pawn p in rejoin)
                                        p.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                                }
                            }
                        };
                        break;
                    }
                }

                __result = output;
            }
        }
    }

}
