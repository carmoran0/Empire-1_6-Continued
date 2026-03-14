using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using FactionColonies.util;

namespace FactionColonies
{
    //Mil customization class
    public class MilitaryCustomizationUtil : IExposable
    {
        public List<MilUnitFC> units = new List<MilUnitFC>();
        public List<MilSquadFC> squads = new List<MilSquadFC>();

        public List<MercenarySquadFC> mercenarySquads = new List<MercenarySquadFC>();
        public List<MilitaryFireSupport> fireSupport = new List<MilitaryFireSupport>();
        public List<MilitaryFireSupport> fireSupportDefs = new List<MilitaryFireSupport>();
        public MilUnitFC blankUnit;
        public List<Mercenary> deadPawns = new List<Mercenary>();
        public int tickChanged;

        private HashSet<Pawn> mercenaryPawnSet = new HashSet<Pawn>();

        public bool IsMercenaryPawn(Pawn pawn) => mercenaryPawnSet.Contains(pawn);

        public void RebuildMercenaryPawnSet()
        {
            mercenaryPawnSet.Clear();
            foreach (MercenarySquadFC squad in mercenarySquads)
            {
                foreach (Mercenary merc in squad.mercenaries)
                {
                    if (merc?.pawn != null)
                        mercenaryPawnSet.Add(merc.pawn);
                }
                if (squad.animals != null)
                {
                    foreach (Mercenary animal in squad.animals)
                    {
                        if (animal?.pawn != null)
                            mercenaryPawnSet.Add(animal.pawn);
                    }
                }
            }
        }

        public MilitaryCustomizationUtil()
        {
            //set load stuff here
            if (units == null)
            {
                units = new List<MilUnitFC>();
            }

            if (squads == null)
            {
                squads = new List<MilSquadFC>();
            }

            if (blankUnit == null)
            {
                //blankUnit = new MilUnitFC(true);
            }

            if (mercenarySquads == null)
            {
                mercenarySquads = new List<MercenarySquadFC>();
            }

            if (deadPawns == null)
            {
                deadPawns = new List<Mercenary>();
            }

            if (fireSupportDefs == null)
            {
                fireSupportDefs = new List<MilitaryFireSupport>();
            }
        }

        public void CheckMilitaryUtilForErrors()
        {
            PerfWatchdog.Enter("MilUtil.CheckErrors");
            try
            {
                if (blankUnit == null)
                {
                    blankUnit = new MilUnitFC(true);
                }

                if (squads == null) { PerfWatchdog.Exit(); return; }
                
                LogUtil.Message("MilitaryCustomizationUtil: checking for errors on tick " + Find.TickManager.TicksGame);
                foreach (MilSquadFC squad in squads)
                {
                    if (squad?.units == null) continue;
                    
                    bool changed = false;
                    for (int count = 0; count < 30 && count < squad.units.Count; count++)
                    {
                        if (squad.units[count] != null &&
                            (units.Contains(squad.units[count]) || squad.units[count] == blankUnit)) continue;
                        squad.units[count] = blankUnit;
                        changed = true;
                    }

                    if (!changed) continue;
                    foreach (var squadMerc in mercenarySquads.Where(squadMerc =>
                        squadMerc.outfit != null && squadMerc.outfit == squad))
                    {
                        squadMerc.OutfitSquad(squad);
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.Error($"Error in CheckMilitaryUtilForErrors: {ex.Message}");
                PerfWatchdog.Exit();
                return;
            }

            foreach (MercenarySquadFC squad in mercenarySquads)
            {
                if (squad.outfit == null || squads.Contains(squad.outfit) == false)
                {
                    squad.StripSquad();
                    squad.outfit = null;
                }
                else
                {
                    int settlementMilLevel = 0;
                    if (squad.settlement != null)
                        settlementMilLevel = squad.settlement.settlementMilitaryLevel;
                    if (squad.outfit == null || !(squad.outfit.GetEquipmentTotalCost() >
                                                  CalculateSquadBudget(settlementMilLevel)))
                        continue;
                    if (squad.settlement != null)
                    {
                        Messages.Message(
                            "The max allowed equipment cost for the squad assigned to " + squad.settlement.Name +
                            " has been exceeded. Thus, the settlement's squad has been unassigned.",
                            MessageTypeDefOf.RejectInput);
                    }

                    squad.outfit = null;
                    squad.StripSquad();
                }
            }

            if (tickChanged >= GETLatestChange) { PerfWatchdog.Exit(); return; }
            foreach (var merc in mercenarySquads.Where(merc => merc.outfit != null))
            {
                merc.OutfitSquad(merc.outfit);
            }

            RebuildMercenaryPawnSet();
            PerfWatchdog.Exit();
        }

        public int GETLatestChange
        {
            get { return squads.Select(squadFC => squadFC.getLatestChanged).Prepend(0).Max(); }
        }

        public static double CalculateSquadBudget(int militaryLevel)
        {
            return 500 + (600.0 * militaryLevel * militaryLevel);
        }

        public static double CalculateFireSupportBudget(int militaryLevel)
        {
            return 500 + (500.0 * militaryLevel * militaryLevel);
        }

        public MercenarySquadFC ReturnSquadFromUnit(Pawn unit)
        {
            foreach (var squad in mercenarySquads)
            {
                foreach (var merc in squad.mercenaries)
                {
                    if (merc.pawn.Map != null && merc.pawn == unit)
                        return squad;
                }
                if (squad.animals != null)
                {
                    foreach (var animal in squad.animals)
                    {
                        if (animal.pawn.Map != null && animal.pawn == unit)
                            return squad;
                    }
                }
            }

            LogUtil.Message("MercenarySquadFC - ReturnSquadFromUnit - Did not find squad.");
            return null;
        }

        public Mercenary ReturnMercenaryFromUnit(Pawn unit, MercenarySquadFC squad)
        {
            return squad.mercenaries.FirstOrDefault(merc => merc.pawn == unit);
        }

        public IEnumerable<Mercenary> AllMercenaries =>
            mercenarySquads.SelectMany(squad =>
                squad.animals?.Count > 0
                    ? squad.mercenaries.Concat(squad.animals)
                    : squad.mercenaries);

        public IEnumerable<MercenarySquadFC> DeployedSquads =>
            mercenarySquads.Where(squad => squad.isDeployed);

        public IEnumerable<Pawn> AllMercenaryPawns =>
            AllMercenaries.Select(merc => merc.pawn);

        public void ResetSquads()
        {
            squads = new List<MilSquadFC>();
        }

        public void UpdateUnits()
        {
            foreach (MilUnitFC unit in units)
            {
                unit.UpdateEquipmentTotalCost();
            }
        }

        public void AttemptToAssignSquad(WorldSettlementFC settlement, MilSquadFC squad)
        {
            if (settlement.MilitaryComp == null)
            {
                LogUtil.Message($"Attempted to assign a squad to settlement {settlement.Name} with NULL MilitaryComp");
                return;
            }
            if (!SquadAssignmentRegistry.CanAssign(settlement, squad, out string rejectReason))
            {
                Messages.Message(rejectReason, MessageTypeDefOf.RejectInput);
                return;
            }
            if (CalculateSquadBudget(settlement.settlementMilitaryLevel) >=
                squad.GetEquipmentTotalCost())
            {
                if (SquadExists(settlement))
                {
                    settlement.MilitaryComp.militarySquad.OutfitSquad(squad);
                }
                else
                {
                    //create new squad
                    CreateMercenarySquad(settlement);
                    settlement.MilitaryComp.militarySquad.OutfitSquad(squad);
                }

                Messages.Message(squad.name + "'s loadout has been assigned to " + settlement.Name,
                    MessageTypeDefOf.TaskCompletion);
            }
            else
            {
                Messages.Message("FCSquadExceedsMaxCost".Translate(), MessageTypeDefOf.RejectInput);
            }
        }

        public MercenarySquadFC CreateMercenarySquad(WorldSettlementFC settlement, bool isExtra = false)
        {
            if (settlement.MilitaryComp == null)
            {
                LogUtil.Warning($"Attempted to create a mercenary squad for settlement {settlement.Name} with NULL MilitaryComp. Skipping");
                return null;
            }
            MercenarySquadFC squad = new MercenarySquadFC();
            squad.InitiateSquad();
            mercenarySquads.Add(squad);
            if (!isExtra)
                settlement.MilitaryComp.militarySquad = FindSquad(squad);
            squad.settlement = settlement;
            squad.isExtraSquad = isExtra;

            if (settlement.MilitaryComp.militarySquad == null)
            {
                LogUtil.Warning("CreateMercenarySquad fail. Found squad is Null");
            }

            RebuildMercenaryPawnSet();
            return FindSquad(squad);
        }

        public MercenarySquadFC FindSquad(MercenarySquadFC squad)
        {
            return mercenarySquads.FirstOrDefault(mercSquad => squad == mercSquad);
        }

        public bool SquadExists(WorldSettlementFC settlement)
        {
            return settlement.MilitaryComp?.militarySquad != null;
        }

        public void ChangeTick()
        {
            tickChanged = Find.TickManager.TicksGame;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref units, "units", LookMode.Deep);
            Scribe_Collections.Look(ref squads, "squads", LookMode.Deep);
            Scribe_Collections.Look(ref mercenarySquads, "mercenarySquads", LookMode.Deep);
            Scribe_Collections.Look(ref fireSupport, "fireSupport", LookMode.Deep);
            Scribe_Collections.Look(ref fireSupportDefs, "fireSupportDefs", LookMode.Deep);
            Scribe_Collections.Look(ref deadPawns, "deadPawns", LookMode.Deep);

            Scribe_Deep.Look(ref blankUnit, "blankUnit");
            Scribe_Values.Look(ref tickChanged, "tickChanged");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RebuildMercenaryPawnSet();
            }
        }
    }
}