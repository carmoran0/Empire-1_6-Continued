using System;
using System.Linq;
using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FactionColonies
{
    public static class MilitaryUtilFC
    {
        public static bool AttackPlayerSettlement(MilitaryForce attackingForce, WorldSettlementFC settlement, Faction enemyFaction)
        {
            if (settlement?.MilitaryComp is null)
            {
                LogUtil.Warning($"AttackPlayerSettlement rejected: {settlement?.Name ?? "null"} has no MilitaryComp. " +
                    $"Attacker {enemyFaction?.Name ?? "null"} dropped.");
                return false;
            }

            FCEvent existingEvent = ReturnMilitaryEventByLocation(settlement.Tile);
            if (settlement.MilitaryComp.isUnderAttack || existingEvent is object || settlement.HasMap)
            {
                LogUtil.Warning($"AttackPlayerSettlement rejected: {settlement.Name} is already under attack " +
                    $"(isUnderAttack={settlement.MilitaryComp.isUnderAttack}, existingEvent={existingEvent is object}). " +
                    $"Attacker {enemyFaction?.Name ?? "null"} dropped.");
                return false;
            }

            FactionFC factionfc = FactionCache.FactionComp;

            FCEvent tmp = FCEventMaker.MakeEvent(FCEventDefOf.settlementBeingAttacked);
            tmp.hasCustomDescription = true;
            tmp.timeTillTrigger = Find.TickManager.TicksGame + GenDate.TicksPerDay;
            tmp.location = settlement.Tile;
            tmp.hasDestination = true;
            tmp.customDescription = "FCSettlementAboutToBeAttacked".Translate(settlement.Name, enemyFaction.Name);
            tmp.militaryForceDefending = MilitaryForce.CreateMilitaryForceFromSettlement(settlement);
            tmp.militaryForceDefendingFaction = FactionCache.PlayerColonyFaction;
            tmp.militaryForceAttacking = attackingForce;
            tmp.militaryForceAttackingFaction = enemyFaction;
            tmp.settlementFCDefending = settlement;

            WorldSettlementFC highest = null;

            foreach (WorldSettlementFC settlementCompare in factionfc.settlements)
            {
                if (settlementCompare == settlement) continue;

                var mc = settlementCompare.MilitaryComp;
                if (mc == null) continue;

                if (mc.autoDefend && !mc.militaryBusy && !mc.isUnderAttack
                    && DefenseValidatorRegistry.CanDefend(settlementCompare, settlement)
                    && (highest == null || settlementCompare.settlementMilitaryLevel > highest.settlementMilitaryLevel))
                {
                    highest = settlementCompare;
                }
            }

            // Also check external auto-defenders (e.g., defensive outposts)
            IAutoDefender bestExternalDefender = AutoDefenderRegistry.FindBestDefender(settlement.Tile, 0);

            if (highest != null && highest.settlementMilitaryLevel > settlement.settlementMilitaryLevel)
            {
                int externalLevel = bestExternalDefender != null ? bestExternalDefender.MilitaryLevel : 0;
                if (highest.settlementMilitaryLevel >= externalLevel)
                {
                    ChangeDefendingMilitaryForce(tmp, highest);
                }
                else if (bestExternalDefender != null)
                {
                    tmp.militaryForceDefending = bestExternalDefender.CreateDefendingForce();
                    tmp.externalDefenderSource = bestExternalDefender.WorldObject;
                    bestExternalDefender.OnDefenseStarted(settlement);
                    tmp.customDescription += "\n\n" + "FCExternalDefenderAutoAssigned".Translate(bestExternalDefender.WorldObject.LabelCap);
                }
            }
            else if (bestExternalDefender != null && bestExternalDefender.MilitaryLevel > settlement.settlementMilitaryLevel)
            {
                tmp.militaryForceDefending = bestExternalDefender.CreateDefendingForce();
                tmp.externalDefenderSource = bestExternalDefender.WorldObject;
                bestExternalDefender.OnDefenseStarted(settlement);
                tmp.customDescription += "\n\n" + "FCExternalDefenderAutoAssigned".Translate(bestExternalDefender.WorldObject.LabelCap);
            }

            settlement.MilitaryComp.defenderForce = tmp.militaryForceDefending;
            settlement.MilitaryComp.attackerForce = tmp.militaryForceAttacking;

            FactionCache.FactionComp.AddEvent(tmp);

            double winChance = SimulateBattleFc.CalculateDefenderWinChance(tmp.militaryForceAttacking, tmp.militaryForceDefending);
            tmp.customDescription += "\n\n" + "FCBattleForecast".Translate(
                tmp.militaryForceAttacking.forceRemaining,
                tmp.militaryForceAttacking.militaryEfficiency.ToString("0.##"),
                tmp.militaryForceDefending.DefensivePower,
                tmp.militaryForceDefending.militaryEfficiency.ToString("0.##"),
                (winChance * 100).ToString("F0"));
            if (FCSettings.battleMode == BattleMode.Hybrid)
                tmp.customDescription += "\n\n" + "FCSettlementAttackHybridHint".Translate();
            settlement.MilitaryComp.isUnderAttack = true;

            Find.LetterStack.ReceiveLetter("FCSettlementInDanger".Translate(), tmp.customDescription,
                LetterDefOf.ThreatBig, new LookTargets(Find.WorldObjects.WorldObjectAt<WorldSettlementFC>(settlement.Tile)));

            return true;
        }

        /// <summary>
        /// Attacks an external <see cref="IRaidTarget"/> registered via <see cref="RaidTargetRegistry"/>.
        /// Creates a <c>settlementBeingAttacked</c> event with the same 24-hour warning as settlement raids.
        /// Auto-defend logic checks both Empire settlements and <see cref="AutoDefenderRegistry"/> entries.
        /// </summary>
        public static void AttackRaidTarget(MilitaryForce attackingForce, IRaidTarget target, Faction enemyFaction)
        {
            FactionFC factionfc = FactionCache.FactionComp;

            FCEvent tmp = FCEventMaker.MakeEvent(FCEventDefOf.settlementBeingAttacked);
            tmp.hasCustomDescription = true;
            tmp.timeTillTrigger = Find.TickManager.TicksGame + GenDate.TicksPerDay;
            tmp.location = target.Tile;
            tmp.hasDestination = true;
            tmp.customDescription = "FCSettlementAboutToBeAttacked".Translate(target.Name, enemyFaction.Name);

            // Create a default defending force from the target's military level
            double defLevel = Math.Max(1, target.MilitaryLevel);
            double defEfficiency = 1.0;
            defEfficiency *= factionfc.GetStatValue(FCStatDefOf.militaryEfficiencyBonusDefending);
            defLevel += factionfc.GetStatValue(FCStatDefOf.militaryLevelBonusDefending);
            tmp.militaryForceDefending = new MilitaryForce(defLevel, defEfficiency, null, FactionCache.PlayerColonyFaction);
            tmp.militaryForceDefendingFaction = FactionCache.PlayerColonyFaction;
            tmp.militaryForceAttacking = attackingForce;
            tmp.militaryForceAttackingFaction = enemyFaction;
            tmp.settlementFCDefending = target.WorldObject;

            // Check Empire settlements for auto-defend
            WorldSettlementFC highestSettlement = null;
            foreach (WorldSettlementFC settlementCompare in factionfc.settlements)
            {
                if (settlementCompare.MilitaryComp != null &&
                    settlementCompare.MilitaryComp.autoDefend && !settlementCompare.MilitaryComp.militaryBusy &&
                    !settlementCompare.MilitaryComp.isUnderAttack &&
                    (highestSettlement == null || settlementCompare.settlementMilitaryLevel > highestSettlement.settlementMilitaryLevel))
                {
                    highestSettlement = settlementCompare;
                }
            }

            // Check external auto-defenders
            IAutoDefender bestExternalDefender = AutoDefenderRegistry.FindBestDefender(target.Tile, 0);

            // Pick the stronger defender (Empire settlement vs external)
            int externalLevel = bestExternalDefender != null ? bestExternalDefender.MilitaryLevel : 0;

            if (highestSettlement != null && highestSettlement.settlementMilitaryLevel >= externalLevel)
            {
                // Empire settlement defends — assign its force and mark it as busy
                tmp.militaryForceDefending = MilitaryForce.CreateMilitaryForceFromSettlement(highestSettlement);
                highestSettlement.MilitaryComp?.SendMilitary(target.Tile, MilitaryJobDefOf.DefendFriendlySettlement, -1, enemyFaction);
            }
            else if (bestExternalDefender != null)
            {
                tmp.militaryForceDefending = bestExternalDefender.CreateDefendingForce();
                tmp.externalDefenderSource = bestExternalDefender.WorldObject;
                bestExternalDefender.OnDefenseStarted(target.WorldObject);
                tmp.customDescription += "\n\n" + "FCExternalDefenderAutoAssigned".Translate(bestExternalDefender.WorldObject.LabelCap);
            }

            target.IsUnderAttack = true;
            factionfc.AddEvent(tmp);

            double winChance = SimulateBattleFc.CalculateDefenderWinChance(tmp.militaryForceAttacking, tmp.militaryForceDefending);
            tmp.customDescription += "\n\n" + "FCBattleForecast".Translate(
                tmp.militaryForceAttacking.forceRemaining,
                tmp.militaryForceAttacking.militaryEfficiency.ToString("0.##"),
                tmp.militaryForceDefending.DefensivePower,
                tmp.militaryForceDefending.militaryEfficiency.ToString("0.##"),
                (winChance * 100).ToString("F0"));

            Find.LetterStack.ReceiveLetter("FCSettlementInDanger".Translate(), tmp.customDescription,
                LetterDefOf.ThreatBig, new LookTargets(target.WorldObject));
        }

        public static void ChangeDefendingMilitaryForce(FCEvent evt, WorldSettlementFC settlementOfMilitaryForce)
        {
            FactionFC factionfc = FactionCache.FactionComp;
            MilitaryForce tmpMilitaryForce = null;
            WorldSettlementFC homeSettlement = factionfc.ReturnSettlementByLocation(evt.location);
            if (evt.militaryForceDefending.homeSettlement != null
                && settlementOfMilitaryForce == evt.militaryForceDefending.homeSettlement)
            {
                Messages.Message("FCMilitaryAlreadyDefendingSettlement".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            WorldSettlementFC target = Find.World.worldObjects.WorldObjectAt<WorldSettlementFC>(evt.location);

            if (evt.militaryForceDefending.homeSettlement != null
                && evt.militaryForceDefending.homeSettlement != factionfc.ReturnSettlementByLocation(evt.location))
            {
                //if the forces defending aren't the forces belonging to the settlement
                evt.militaryForceDefending.homeSettlement.MilitaryComp?.ReturnMilitary(false);
            }
            else if (evt.externalDefenderSource != null)
            {
                IAutoDefender autoDefender = AutoDefenderRegistry.FindByWorldObject(evt.externalDefenderSource);
                autoDefender?.OnDefenseReplaced();
                evt.externalDefenderSource = null;
            }

            if (settlementOfMilitaryForce != homeSettlement)
            {
                tmpMilitaryForce =
                    MilitaryForce.CreateMilitaryForceFromSettlement(
                        factionfc.ReturnSettlementByLocation(evt.location), true);
            }

            factionfc.RemoveMilitaryTarget(evt.location);
            evt.militaryForceDefending =
                MilitaryForce.CreateMilitaryForceFromSettlement(settlementOfMilitaryForce,
                    homeDefendingForce: tmpMilitaryForce);

            if (target.MilitaryComp == null)
            {
                LogUtil.Warning($"ChangeDefendingMilitaryForce: target settlement {target.Name} has no MilitaryComp. Aborting.");
                return;
            }
            target.MilitaryComp.defenderForce = evt.militaryForceDefending;

            if (settlementOfMilitaryForce == homeSettlement)
            {
                //if home settlement is reseting to defense
                Messages.Message("FCDefendingMilitaryReset".Translate(), MessageTypeDefOf.NeutralEvent);
            }
            else
            {
                //if settlement is foreign
                settlementOfMilitaryForce.MilitaryComp?.SendMilitary(evt.settlementFCDefending.Tile, MilitaryJobDefOf.DefendFriendlySettlement, -1, evt.militaryForceAttackingFaction);
                Find.LetterStack.ReceiveLetter("FCMilitaryAction".Translate(), "FCForeignMilitarySwitch"
                    .Translate(settlementOfMilitaryForce.Name,
                        factionfc.ReturnSettlementByLocation(evt.location).Name,
                        evt.militaryForceDefending.militaryLevel), LetterDefOf.NeutralEvent);
            }
        }

        public static void ChangeDefendingToExternalForce(FCEvent evt, IAutoDefender defender)
        {
            FactionFC factionfc = FactionCache.FactionComp;

            if (evt.externalDefenderSource != null && evt.externalDefenderSource == defender.WorldObject)
            {
                Messages.Message("FCMilitaryAlreadyDefendingSettlement".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            // Clean up current defender
            if (evt.militaryForceDefending.homeSettlement != null
                && evt.militaryForceDefending.homeSettlement != factionfc.ReturnSettlementByLocation(evt.location))
            {
                evt.militaryForceDefending.homeSettlement.MilitaryComp?.ReturnMilitary(false);
            }
            else if (evt.externalDefenderSource != null)
            {
                IAutoDefender old = AutoDefenderRegistry.FindByWorldObject(evt.externalDefenderSource);
                old?.OnDefenseReplaced();
            }

            // Assign new external defender
            factionfc.RemoveMilitaryTarget(evt.location);
            evt.militaryForceDefending = defender.CreateDefendingForce();
            evt.externalDefenderSource = defender.WorldObject;
            defender.OnDefenseStarted(evt.settlementFCDefending);

            WorldSettlementFC target = Find.World.worldObjects.WorldObjectAt<WorldSettlementFC>(evt.location);
            if (target?.MilitaryComp != null)
            {
                target.MilitaryComp.defenderForce = evt.militaryForceDefending;
            }

            Messages.Message("FCExternalDefenderAssigned".Translate(defender.WorldObject.LabelCap),
                MessageTypeDefOf.NeutralEvent);
        }

        public static MilitaryForce ReturnDefendingMilitaryForce(FCEvent evt)
        {
            return evt.militaryForceDefending;
        }

        public static FCEvent ReturnMilitaryEventByLocation(PlanetTile location)
        {
            return FactionCache.FactionComp.FindEventByDefAndLocation(FCEventDefOf.settlementBeingAttacked, location);
        }
    }
}