using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FactionColonies
{
    public class FCRoadBuilder : IExposable
    {
        public FCRoadQueue roadQueue;
        public RoadDef roadDef;

        public int daysBetweenTicks = 3;
        public bool roadBuildingEnabled = true;
        public bool wasRoadBuildingDisabled = true;
        bool hasRoadBuildersBoost;
        bool pathsFullyProcessed;

        public FCRoadBuilder()
        {
        }

        //DirtPath (priority: 10) - This is the lowest priority road, likely the basic dirt path
        //DirtRoad (priority: 20) - This is the traditional dirt road
        //StoneRoad (priority: 30)
        //AncientAsphaltRoad (priority: 40)
        //AncientAsphaltHighway (priority: 50)
        // We could define our own and provide a roaddef for this, such as spacer / glitterworld tech level roads...

        public void ExposeData()
        {
            Scribe_Defs.Look(ref roadDef, "roadDef");
            Scribe_Values.Look(ref daysBetweenTicks, "daysBetweenTicks");
            Scribe_Values.Look(ref roadBuildingEnabled, "roadBuildingEnabled");
            Scribe_Values.Look(ref wasRoadBuildingDisabled, "wasRoadBuildingDisabled");
            Scribe_Deep.Look(ref roadQueue, "roadQueue", new object[] { this.roadDef, this.daysBetweenTicks });
        }

        public void FirstTick()
        {
            CheckForTechChanges();
            CreateRoadQueue(false);
            FlagUpdateRoadQueues();

            if (daysBetweenTicks == 0)
            {
                LogUtil.Message("FCRoadBuilder - Resetting daysBetweenTicks");
                int days = hasRoadBuildersBoost ? 1 : 3;
                daysBetweenTicks = days;
                roadQueue.daysBetweenTicks = days;
            }
        }

        public void RoadTick()
        {
            if (roadDef == null)
            {
                wasRoadBuildingDisabled = true;
                return;
            }

            if (!roadBuildingEnabled)
            {
                wasRoadBuildingDisabled = true;
                return;
            }

            if (Find.TickManager.TicksGame % 250 == 0)
            {
                FactionFC faction = FactionCache.FactionComp;

                if (roadQueue == null)
                {
                    LogUtil.Message("RoadTick: No road queue found");
                    return;
                }

                if (!hasRoadBuildersBoost && faction.IsActionAllowed(FCActionType.BuildRoadsToAllies))
                {
                    roadQueue.shouldUpdateSettlementsToProcess = true;
                    roadQueue.daysBetweenTicks = 1;
                    hasRoadBuildersBoost = true;
                    daysBetweenTicks = 1;
                }

                // If road building was disabled, then set the next tick to make a road to the correct time
                if (wasRoadBuildingDisabled)
                {
                    wasRoadBuildingDisabled = false;
                    roadQueue.nextRoadTick = Find.TickManager.TicksGame + GenDate.TicksPerDay * roadQueue.daysBetweenTicks;
                }

                // Ensure settlement lists are populated before processing paths.
                if (roadQueue.shouldUpdateSettlementsToProcess)
                {
                    roadQueue.UpdateSettlementsToProcess();
                    roadQueue.shouldUpdateSettlementsToProcess = false;
                }

                if (!pathsFullyProcessed)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        if (!roadQueue.ProcessOnePath())
                        {
                            pathsFullyProcessed = true;
                            break;
                        }
                    }
                }

                bool segmentBuilt = roadQueue.BuildRoadSegments();
            }
        }

        // Returns whether or not a settlement would be built to.
        public static bool IsValidRoadTarget(Settlement settlement)
        {
            if (!settlement.Tile.Layer.IsRootSurface)
                return false;

            FactionFC fC = FactionCache.FactionComp;

            // If faction exists and is either player or player has roadBuilders trait and the faction is an ally
            if (settlement.Faction != null)
                if (settlement.Faction.IsPlayer || (fC.IsActionAllowed(FCActionType.BuildRoadsToAllies) && settlement.Faction.PlayerRelationKind == FactionRelationKind.Ally))
                    return true;

            foreach (WorldSettlementFC settlementFC in fC.settlements)
            {
                if (settlementFC.Tile == settlement.Tile)
                    return true;
            }

            return false;
        }

        public FCRoadQueue CreateRoadQueue(bool logFailure = true)
        {
            if (roadQueue != null)
            {
                if (logFailure)
                {
                    LogUtil.Message($"Road queue already exists.");
                }

                return roadQueue;
            }
            roadQueue = new FCRoadQueue(roadDef, daysBetweenTicks);
            return roadQueue;
        }

        public void CheckForTechChanges()
        {
            LogUtil.Message("CheckForTechChanges: Starting tech check...");

            FactionFC faction = FactionCache.FactionComp;
            RoadDef def = this.roadDef;
            RoadDef oldDef = def;

            if (DefDatabase<ResearchProjectDef>.GetNamed("FCRoadBuildingHighway", false).IsFinished)
            {
                def = RoadDefOf.AncientAsphaltHighway;
                LogUtil.Message("CheckForTechChanges: Highway research complete, using AncientAsphaltHighway");
            }
            else if (DefDatabase<ResearchProjectDef>.GetNamed("FCRoadBuildingRoad", false).IsFinished)
            {
                def = RoadDefOf.AncientAsphaltRoad;
                LogUtil.Message("CheckForTechChanges: Road research complete, using AncientAsphaltRoad");
            }
            else if (DefDatabase<ResearchProjectDef>.GetNamed("FCRoadBuildingDirt", false).IsFinished)
            {
                // Use DirtPath (priority 10) to match existing world-generated dirt paths
                def = DefDatabase<RoadDef>.GetNamed("DirtPath", false);
                LogUtil.Message($"CheckForTechChanges: Dirt road research complete, using {def?.defName ?? "null"}");
            }
            else
            {
                LogUtil.Message("CheckForTechChanges: No road research completed yet");
            }

            if (this.roadDef != def)
            {
                LogUtil.Message($"CheckForTechChanges: Road type changed from {oldDef?.defName ?? "null"} to {def?.defName ?? "null"}");
                this.roadDef = def;

                roadQueue.RoadDef = def;
                FlagUpdateRoadQueues();
            }
            else
            {
                LogUtil.Message($"CheckForTechChanges: Road type unchanged: {this.roadDef?.defName ?? "null"}");
            }
        }

        public void DrawPaths()
        {
            if (roadQueue is null) return;
            roadQueue.DrawPaths();
        }

        /// <summary>
        /// Flags all road queues to update whenever they are able.
        /// </summary>
        public void FlagUpdateRoadQueues()
        {
            if (roadQueue != null)
            {
                roadQueue.shouldUpdateSettlementsToProcess = true;
                pathsFullyProcessed = false;
            }
        }
    }
}
