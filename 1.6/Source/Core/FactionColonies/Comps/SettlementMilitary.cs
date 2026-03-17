using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.AI.Group;
using Verse.Sound;

namespace FactionColonies
{
    public class WorldObjectCompProperties_SettlementMilitary : WorldObjectCompProperties
    {
        public WorldObjectCompProperties_SettlementMilitary()
        {
            compClass = typeof(WorldObjectComp_SettlementMilitary);
        }
        public override IEnumerable<string> ConfigErrors(WorldObjectDef parentDef)
        {
            foreach (string item in base.ConfigErrors(parentDef))
            {
                yield return item;
            }
            if (!typeof(MapParent).IsAssignableFrom(parentDef.worldObjectClass))
            {
                yield return parentDef.defName + " has WorldObjectCompProperties_SettlementMilitary but it's not MapParent.";
            }
        }
    }

    public class WorldObjectComp_SettlementMilitary : WorldObjectComp
    {
        private WorldSettlementFC cachedWorldSettlementParent = null;
        public WorldSettlementFC WorldSettlement
        {
            get
            {
                if (cachedWorldSettlementParent != null)
                {
                    return cachedWorldSettlementParent;
                }
                if (parent is WorldSettlementFC ws)
                {
                    cachedWorldSettlementParent = ws;
                }
                else
                {
                    cachedWorldSettlementParent = null;
                    LogUtil.ErrorOnce($"WorldObjectComp_SettlementMilitary has a non-WorldSettlementFC parent: {parent.Label}", 93512108);
                }
                return cachedWorldSettlementParent;
            }
        }
        public Map Map => WorldSettlement.Map;

        public militaryForce attackerForce;
        public List<Pawn> attackers = new List<Pawn>();
        public militaryForce defenderForce;
        private FCEvent currentBattleEvent;
        public List<Pawn> defenders = new List<Pawn>();
        public List<CaravanSupporting> supporting = new List<CaravanSupporting>();
        //TODO all code referencing isUnderAttack needs to point to this comp
        //     also need to make it so that WorldSettlementFC's without a defense comp don't get targeted
        //     for attacks
        public bool isUnderAttack;
        public bool militaryBusy;
        public int militaryLocation = -1;
        public MilitaryJobDef militaryJob;
        public Faction militaryEnemy;
        public MercenarySquadFC militarySquad;
        public int artilleryTimer = 0;
        public bool autoDefend = false;
        public int settlementMilitaryLevel;

        private bool endingBattle = false;
        private bool battleMapInitialized = false;
        private int initialDefenderCount;
        private string pendingDeliveryMessage;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref attackers, "attackers", LookMode.Reference);
            Scribe_Collections.Look(ref defenders, "defenders", LookMode.Reference);
            Scribe_Collections.Look(ref supporting, "supporting", LookMode.Deep);
            Scribe_Deep.Look(ref defenderForce, "defenderForce");
            Scribe_Deep.Look(ref attackerForce, "attackerForce");
            Scribe_Values.Look(ref isUnderAttack, "isUnderAttack");
            Scribe_Values.Look(ref militaryBusy, "militaryBusy");
            Scribe_Values.Look(ref militaryLocation, "militaryLocation");
            Scribe_Defs.Look(ref militaryJob, "militaryJob");
            Scribe_References.Look(ref militaryEnemy, "militaryEnemy");
            Scribe_References.Look(ref militarySquad, "militarySquad");
            Scribe_Values.Look(ref artilleryTimer, "artilleryTimer");
            Scribe_Values.Look(ref autoDefend, "autoDefend");
            Scribe_Values.Look(ref settlementMilitaryLevel, "settlementMilitaryLevel");
            Scribe_Values.Look(ref initialDefenderCount, "initialDefenderCount");
            Scribe_Values.Look(ref battleMapInitialized, "battleMapInitialized");
        }

        public override void Initialize(WorldObjectCompProperties props)
        {
            base.Initialize(props);

            attackers = new List<Pawn>();
            defenders = new List<Pawn>();
            supporting = new List<CaravanSupporting>();
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!isUnderAttack || endingBattle) return;
            if (Find.TickManager.TicksGame % 250 != 0) return;
            if (Map == null) return;

            // Clean stale references (null from failed save/load resolution)
            attackers.RemoveAll(p => p == null || p.Destroyed);
            defenders.RemoveAll(p => p == null || p.Destroyed);

            if (!attackers.Any() || !defenders.Any())
            {
                LogUtil.Warning($"Stuck battle detected at {WorldSettlement.Name}, forcing resolution.");
                endingBattle = true;
                LongEventHandler.QueueLongEvent(EndAttack,
                    "EndingAttack", false, error =>
                    {
                        DelayedErrorWindowRequest.Add("ErrorEndingAttack".Translate(),
                            "ErrorEndingAttackDescription".Translate());
                        LogUtil.Error(error.Message);
                    });
            }
        }

        private static string FoundSettlementString(WorldSettlementFC settlement)
        {
            return settlement.Name + " " + "ShortMilitary".Translate() + " " + settlement.settlementMilitaryLevel +
                   " - " + "FCAvailable".Translate() + ": " + (settlement.MilitaryComp?.IsMilitaryBusySilent() != true).ToString();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            if (isUnderAttack)
            {
                yield return DefendColonyAction();
            }
            if (isUnderAttack && !attackers.Any())
            {
                FCEvent evt = MilitaryUtilFC.ReturnMilitaryEventByLocation(WorldSettlement.Tile);
                if (evt != null)
                {
                    yield return ChangeDefenderAction(evt);
                }
                else
                {
                    LogUtil.Warning($"Settlment {WorldSettlement.Name} is under attack, but found no valid associated event");
                }
            }
        }

        private Command DefendColonyAction()
        {
            Command_Action defendColony = new Command_Action
            {
                defaultLabel = "DefendColony".Translate(),
                defaultDesc = "DefendColonyDesc".Translate(),
                icon = TexLoad.iconMilitary,
                action = delegate
                {
                    StartDefence(MilitaryUtilFC.ReturnMilitaryEventByLocation(WorldSettlement.Tile), () => { });
                }
            };
            /* If auto-battle is enabled, then disable the button. We leave it visible, though, so that the player knows that this is an option if
             * they change their settings. (Once manual fighting becomes an option the player can use, at least) */
            AcceptanceReport canUse = CanDoManualFight();
            if (!canUse.Accepted)
            {
                defendColony.Disable(canUse.Reason);
            }

            return defendColony;
        }

        private Command ChangeDefenderAction(FCEvent evt)
        {
            Command_Action changeDefender = new Command_Action
            {
                defaultLabel = "DefendSettlement".Translate(),
                defaultDesc = "",
                icon = TexLoad.iconCustomize,
                action = delegate
                {
                    if (evt.militaryForceDefending == null || evt.militaryForceDefending.homeSettlement == null)
                    {
                        LogUtil.Warning($"ChangeDefenderAction: militaryForceDefending or its homeSettlement is null for event at {evt.location}");
                        ChangeDefendingForceAction(evt);
                        return;
                    }

                    var list = new List<FloatMenuOption>()
                    {
                        new FloatMenuOption("SettlementDefendingInformation".Translate(evt.militaryForceDefending.homeSettlement.Name,
                                                                                       evt.militaryForceDefending.DefensivePower),
                                            null, MenuOptionPriority.High),
                        new FloatMenuOption("ChangeDefendingForce".Translate(), () => ChangeDefendingForceAction(evt))
                    };

                    var floatMenu = new FloatMenu(list)
                    {
                        vanishIfMouseDistant = true
                    };
                    Find.WindowStack.Add(floatMenu);
                }
            };

            return changeDefender;
        }

        private void ChangeDefendingForceAction(FCEvent evt)
        {
            var faction = FactionCache.FactionComp;
            var settlementList = new List<FloatMenuOption>
            {
                new FloatMenuOption
                (
                    "ResetToHomeSettlement".Translate(settlementMilitaryLevel),
                    delegate { MilitaryUtilFC.ChangeDefendingMilitaryForce(evt, WorldSettlement); },
                    MenuOptionPriority.High
                )
            };


            settlementList.AddRange
            (
                from foundSettlement in faction.settlements
                where foundSettlement != WorldSettlement
                    && foundSettlement.MilitaryComp?.IsMilitaryValid() == true
                    && DefenseValidatorRegistry.CanDefend(foundSettlement, WorldSettlement)
                select new FloatMenuOption
                (
                    FoundSettlementString(foundSettlement),
                    delegate
                    {
                        if (foundSettlement.MilitaryComp?.IsMilitaryBusy() != true)
                            MilitaryUtilFC.ChangeDefendingMilitaryForce(evt, foundSettlement);
                    }
                )
            );

            // Add external auto-defenders (VOE outposts, etc.)
            foreach (IAutoDefender defender in AutoDefenderRegistry.Defenders)
            {
                if (!defender.CanAutoDefend) continue;
                if (evt.externalDefenderSource != null && evt.externalDefenderSource == defender.WorldObject) continue;
                int distance = Find.WorldGrid.TraversalDistanceBetween(defender.WorldObject.Tile, WorldSettlement.Tile);
                if (distance > defender.Range) continue;

                IAutoDefender d = defender;
                settlementList.Add(new FloatMenuOption(
                    d.WorldObject.LabelCap + " (" + "MilitaryLevel".Translate() + " " + d.MilitaryLevel + ")",
                    delegate { MilitaryUtilFC.ChangeDefendingToExternalForce(evt, d); }
                ));
            }

            if (settlementList.Count == 0)
                settlementList.Add(new FloatMenuOption("NoValidMilitaries".Translate(), null));

            var floatMenu2 = new FloatMenu(settlementList)
            {
                vanishIfMouseDistant = true
            };
            Find.WindowStack.Add(floatMenu2);
        }

        public override IEnumerable<Gizmo> GetCaravanGizmos(Caravan caravan)
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            if (isUnderAttack)
            {
                yield return DefendColonyCaravan(caravan);
            }
        }

        private Command DefendColonyCaravan(Caravan caravan)
        {
            Command_Action defendColonyCaravan = new Command_Action
            {
                defaultLabel = "DefendColony".Translate(),
                defaultDesc = "DefendColonyDesc".Translate(),
                icon = TexLoad.iconMilitary,
                action = () =>
                {
                    StartDefence(MilitaryUtilFC.ReturnMilitaryEventByLocation(WorldSettlement.Tile), () => CaravanDefend(caravan));
                }
            };
            /* If auto-battle is enabled, then disable the button. We leave it visible, though, so that the player knows that this is an option if
             * they change their settings. (Once manual fighting becomes an option the player can use, at least) */
            AcceptanceReport canUse = CanDoManualFight();
            if (!canUse.Accepted)
            {
                defendColonyCaravan.Disable(canUse.Reason);
            }

            return defendColonyCaravan;
        }

        private AcceptanceReport CanDoManualFight()
        {
            if (FCSettings.battleMode == BattleMode.Auto)
            {
                return new AcceptanceReport("autoBattleEnabledNoManualFight".Translate());
            }
            else if (FCSettings.battleMode == BattleMode.Hybrid && !IsPlayerCaravanOnTile())
            {
                return new AcceptanceReport("hybridBattleEnabledNoManualFight".Translate());
            }
            return AcceptanceReport.WasAccepted;
        }

        private bool IsPlayerCaravanOnTile()
        {
            return Find.WorldObjects.Caravans.Any(c =>
                c.Tile == WorldSettlement.Tile &&
                c.Faction == Faction.OfPlayer);
        }

        //TOOD: All following methods were yoinked from WorldSettlementFC. parameters and variables need to be adjusted accordingly
        public void CaravanDefend(Caravan caravan)
        {
            var pawns = caravan.pawns.InnerListForReading.ListFullCopy();
            AddToDefenceFromList(pawns, caravan.Tile);

            if (!caravan.Destroyed) caravan.Destroy();
            var enterCell = FindNearEdgeCell(Map);
            foreach (var pawn in pawns)
            {
                var loc =
                    CellFinder.RandomSpawnCellForPawnNear(enterCell, Map);
                GenSpawn.Spawn(pawn, loc, Map, Rot4.Random);
            }
        }

        public void AddToDefenceFromList(List<Pawn> pawns, int destinationTile)
        {
            if (pawns.NullOrEmpty())
            {
                LogUtil.Error("Tried to add an empty list of pawns to an FCEvent");
                return;
            }

            StartDefence(
                MilitaryUtilFC.ReturnMilitaryEventByLocation(destinationTile), () =>
                {
                    foreach (var pawn in pawns)
                    {
                        if (defenders.Contains(pawn)) continue;
                        if (defenders.Any())
                            defenders[0].GetLord().AddPawn(pawn);
                        else
                            LordMaker.MakeNewLord(FactionCache.PlayerColonyFaction, new LordJob_ColonistsIdle(WorldSettlement), WorldSettlement.Map, pawns);
                    }

                    var caravanSupporting = new CaravanSupporting
                    {
                        pawns = pawns
                    };

                    supporting.Add(caravanSupporting);

                    defenders.AddRange(caravanSupporting.pawns);
                    initialDefenderCount = defenders.Count;
                });
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
        {
            if (isUnderAttack)
                foreach (var option in WorldSettlementDefendAction.GetFloatMenuOptions(caravan, WorldSettlement))
                    yield return option;
        }

        private void DeleteMap(bool won = true)
        {
            var map = Map;
            if (map == null) return;

            var lords = map.lordManager.lords.ListFullCopy();
            foreach (var lord in lords)
            {
                map.lordManager.RemoveLord(lord);
            }

            CameraJumper.TryJump(WorldSettlement.Tile);
            //Prevent player from zooming back into the settlement
            Current.Game.CurrentMap = Find.AnyPlayerHomeMap;

            //Ignore any empty caravans
            var AllDowned = supporting.All(supporting => supporting.pawns.All(pawn => pawn.Downed || pawn.Dead));
            foreach (var caravanSupporting in supporting.Where(supporting => supporting.pawns.Any(pawn => pawn.Spawned && !pawn.Downed && !pawn.Dead)).ToList())
            {
                CaravanFormingUtility.FormAndCreateCaravan(caravanSupporting.pawns.Where(pawn => pawn.Spawned), Faction.OfPlayer, WorldSettlement.Tile, WorldSettlement.Tile, -1);
            }

            if (AllDowned)
            {
                var pawns = new HashSet<Thing>();
                foreach (var caravanSupporting in supporting)
                    foreach (var pawn in caravanSupporting.pawns)
                        if (!pawn.Dead)
                        {
                            if (pawn.Spawned) pawn.DeSpawn();
                            pawns.Add(pawn);
                        }

                foreach (Pawn pawn in pawns)
                    if (!pawn.Dead)
                    {
                        var num2 = 0;
                        while (pawn.health.HasHediffsNeedingTend())
                        {
                            num2++;
                            if (num2 > 10000)
                            {
                                LogUtil.Error("WorldSettlementFC.deleteMap: Too many iterations.");
                                break;
                            }

                            TendUtility.DoTend(null, pawn, null);
                        }
                    }

                string eventText = won
                    ? DeliveryEvent.ShuttleEventInjuredString
                    : DeliveryEvent.ShuttleEventInjuredLostString;
                int travelTicks = TravelUtil.ReturnTicksToArrive(WorldSettlement.Tile, Find.AnyPlayerHomeMap.Tile);
                if (!won) travelTicks += GenDate.TicksPerDay;

                var eventParams = new FCEvent
                {
                    location = Find.AnyPlayerHomeMap.Tile,
                    source = WorldSettlement.Tile,
                    goods = pawns.ToList(),
                    customDescription = eventText,
                    timeTillTrigger = Find.TickManager.TicksGame + travelTicks
                };

                if (pawns.Any())
                {
                    DeliveryEvent.CreateDeliveryEvent(eventParams);
                    string travelDays = ((float)travelTicks / GenDate.TicksPerDay).ToString("0.#");
                    pendingDeliveryMessage = "InjuredCaravanMembersReturning".Translate(pawns.Count, travelDays);
                }
            }

            if (map.mapPawns?.AllPawnsSpawned != null)
            {
                //Despawn removes them from AllPawnsSpawned, so we copy it
                foreach (var pawn in map.mapPawns.AllPawnsSpawned.ToList())
                {
                    pawn.DeSpawn();
                }
            }

            Current.Game.DeinitAndRemoveMap(map, false);

            // Safety net: reclaim any defender pawns that escaped into caravans
            var empireDefenders = new HashSet<Pawn>(defenders);
            foreach (var cs in supporting)
                foreach (var p in cs.pawns)
                    empireDefenders.Remove(p);

            if (empireDefenders.Count > 0)
            {
                foreach (var caravan in Find.WorldObjects.Caravans.ToList())
                {
                    foreach (var pawn in caravan.PawnsListForReading.ToList())
                    {
                        if (empireDefenders.Contains(pawn) && pawn.Faction != Faction.OfPlayer)
                        {
                            caravan.RemovePawn(pawn);
                            if (!pawn.Destroyed) pawn.Destroy();
                        }
                    }
                    if (!caravan.Destroyed && !caravan.PawnsListForReading.Any())
                    {
                        caravan.Destroy();
                    }
                }
            }
        }

        public void StartDefence(FCEvent evt, Action after)
        {
            currentBattleEvent = evt;
            bool shouldAutoResolve = false;
            if (FCSettings.battleMode == BattleMode.Auto)
            {
                shouldAutoResolve = true;
            }
            else if (FCSettings.battleMode == BattleMode.Hybrid)
            {
                shouldAutoResolve = !battleMapInitialized && !IsPlayerCaravanOnTile();
            }

            if (shouldAutoResolve)
            {
                initialDefenderCount = (int)evt.militaryForceDefending.forceRemaining;
                BattleResult battleResult = SimulateBattleFc.FightBattle(evt.militaryForceAttacking, evt.militaryForceDefending);
                EndBattle(battleResult.DefenderVictory, (int)evt.militaryForceDefending.forceRemaining, battleResult);
                return;
            }

            if (defenderForce == null)
            {
                LogUtil.Warning($"StartDefence: defenderForce is null for {WorldSettlement.Name}, settlement loses by default.");
                EndBattle(false, 0, null);
                return;
            }

            LongEventHandler.QueueLongEvent(() =>
            {
                if (Map == null)
                    MapGenerator.GenerateMap(new IntVec3(70 + WorldSettlement.settlementLevel * 10, 1, 70 + WorldSettlement.settlementLevel * 10),
                                             WorldSettlement, WorldSettlement.MapGeneratorDef, WorldSettlement.ExtraGenStepDefs);

                ZoomIntoTile(evt);
                after.Invoke();
            },
                "GeneratingMap", false, GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap);
        }

        private void ZoomIntoTile(FCEvent evt)
        {
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            if (!battleMapInitialized)
            {
                if (evt == null)
                {
                    LogUtil.Warning("Aborting defense, null FCEvent! Resetting battle state.");
                    EndBattle(false, 0, null);
                    return;
                }

                var force = MilitaryUtilFC.ReturnDefendingMilitaryForce(evt);
                if (force == null)
                {
                    LogUtil.Warning($"Aborting defense for {WorldSettlement?.Name}, null defending force. Resetting battle state.");
                    EndBattle(false, 0, null);
                    return;
                }

                battleMapInitialized = true;
                evt.timeTillTrigger = Find.TickManager.TicksGame;

                if (force.homeSettlement?.MilitaryComp != null)
                    force.homeSettlement.MilitaryComp.militaryBusy = true;

                Map.fogGrid.ClearAllFog();

                GenerateFriendlies(force);
                RecruitMapInhabitants();
                Find.TickManager.Notify_GeneratedPotentiallyHostileMap();

                string enemyName = attackerForce?.homeFaction?.Name ?? "Unknown";
                GlobalTargetInfo jumpTarget = defenders.Any()
                    ? new GlobalTargetInfo(defenders[0])
                    : new GlobalTargetInfo(new IntVec3(Map.Size.x / 2, 0, Map.Size.z / 2), Map);
                Find.LetterStack.ReceiveLetter(
                    "ManualBattleStarted".Translate(WorldSettlement.Name),
                    "ManualBattleStartedDesc".Translate(WorldSettlement.Name, enemyName),
                    LetterDefOf.ThreatBig,
                    new LookTargets(jumpTarget));
            }
        }

        public static IntVec3 FindNearEdgeCell(Map map)
        {
            bool BaseValidator(IntVec3 x)
            {
                return x.Standable(map) && !x.Fogged(map);
            }

            var hostFaction = map.ParentFaction;
            if (CellFinder.TryFindRandomEdgeCellWith(x =>
            {
                if (!BaseValidator(x))
                    return false;
                if (hostFaction != null && map.reachability.CanReachFactionBase(x, hostFaction))
                    return true;
                return hostFaction == null && map.reachability.CanReachBiggestMapEdgeDistrict(x);
            }, map, CellFinder.EdgeRoadChance_Neutral, out var result))
                return CellFinder.RandomClosewalkCellNear(result, map, 5);
            if (CellFinder.TryFindRandomEdgeCellWith(BaseValidator, map, CellFinder.EdgeRoadChance_Neutral, out result))
                return CellFinder.RandomClosewalkCellNear(result, map, 5);
            LogUtil.Warning("Could not find any valid edge cell.");
            return CellFinder.RandomCell(map);
        }

        private void GenerateFriendlies(militaryForce force)
        {
            var points = Math.Max((float)(force.forceRemaining * 100), 50f);
            List<Pawn> friendlies = null;
            var riders = new Dictionary<Pawn, Pawn>();

            // Try external defender pawns (VOE outposts, etc.)
            if (force.homeSettlement == null && currentBattleEvent?.externalDefenderSource != null)
            {
                IAutoDefender extDefender = AutoDefenderRegistry.FindByWorldObject(
                    currentBattleEvent.externalDefenderSource);
                friendlies = extDefender?.GetDefendingPawns();
            }

            if (friendlies != null && friendlies.Count > 0)
            {
                // External defender provided real pawns — skip squad/random generation
            }
            else
            {
                var homeComp = force.homeSettlement?.MilitaryComp;
                bool squadAvailable = homeComp?.militarySquad != null
                    && homeComp.militarySquad.outfit != null
                    && homeComp.militarySquad.mercenaries.Any()
                    && (homeComp.militaryJob == null
                        || homeComp.militaryJob == MilitaryJobDefOf.Undefined
                        || homeComp.militaryJob == MilitaryJobDefOf.DefendFriendlySettlement);
                if (squadAvailable)
                {
                    var squad = force.homeSettlement.MilitaryComp.militarySquad;
                    squad.CheckInitialization();

                    squad.OutfitSquad(squad.outfit);
                    squad.UpdateSquadStats(force.homeSettlement.settlementMilitaryLevel);
                    squad.ResetNeeds();

                    friendlies = squad.AllEquippedMercenaryPawns.ToList();

                    foreach (var animal in squad.animals) riders.Add(animal.handler.pawn, animal.pawn);
                }
                else
                {
                    var parms = new IncidentParms
                    {
                        target = Map,
                        faction = FactionCache.PlayerColonyFaction,
                        generateFightersOnly = true,
                        raidStrategy = RaidStrategyDefOf.ImmediateAttackFriendly
                    };
                    parms.points = IncidentWorker_Raid.AdjustedRaidPoints(points,
                        PawnsArrivalModeDefOf.EdgeWalkIn, parms.raidStrategy,
                        parms.faction, PawnGroupKindDefOf.Combat,
                        parms.target // new required parameter
                    );
                    friendlies = PawnGroupMakerUtility.GeneratePawns(
                        IncidentParmsUtility.GetDefaultPawnGroupMakerParms(
                            PawnGroupKindDefOf.Combat, parms, true)).ToList();
                    if (!friendlies.Any()) LogUtil.Error("Got no pawns spawning raid from parms " + parms);

                    // Add guard animals for non-violent factions
                    XenotypeFilter xenoFilter = FactionCache.FactionComp?.xenotypeFilter;
                    if (xenoFilter != null && xenoFilter.OnlyNonViolentXenos)
                    {
                        List<PawnKindDef> guardAnimals = xenoFilter.GuardAnimals;
                        if (guardAnimals != null && guardAnimals.Any())
                        {
                            int humanCount = friendlies.Count(p => p.RaceProps.Humanlike);
                            int animalCount = Math.Max(1, humanCount / 2);
                            for (int i = 0; i < animalCount; i++)
                            {
                                PawnKindDef animalKind = guardAnimals.RandomElement();
                                Pawn animal = PawnGenerator.GeneratePawn(FCPawnGenerator.AnimalRequest(animalKind));
                                if (animal != null) friendlies.Add(animal);
                            }
                        }
                    }
                }
            } // end else (no external defender pawns)

            void tryFindLoc(out IntVec3 loc, Pawn friendly)
            {
                var min = (70 + WorldSettlement.settlementLevel * 10) / 2 - 5 - 5 * WorldSettlement.settlementLevel;
                var size = 10 + WorldSettlement.settlementLevel * 10;
                CellFinder.TryFindRandomCellInsideWith(new CellRect(min, min, size, size),
                    testing => testing.Standable(Map) && Map.reachability.CanReachMapEdge(testing,
                        TraverseParms.For(TraverseMode.PassDoors)), out loc);
                if (loc.x == -1000)
                {
                    LogUtil.Message("Failed with " + friendly + ", " + loc);
                    CellFinder.TryFindRandomCellNear(new IntVec3(min + 10 + WorldSettlement.settlementLevel, 1,
                            min + 10 + WorldSettlement.settlementLevel), Map, 75,
                        testing => testing.Standable(Map), out loc);
                }
            }

            foreach (var friendly in friendlies)
            {
                if (friendly.IsWildMan()) continue;

                friendly.ApplyIdeologyRitualWounds();

                IntVec3 loc;
                if (friendly.AnimalOrWildMan())
                {
                    if (riders.Count > 0)
                    {
                        var pair = riders.FirstOrDefault(p => p.Value.thingIDNumber == friendly.thingIDNumber);
                        if (pair.Key == null)
                        {
                            // No rider pair — guard animal without a handler, spawn at regular location
                            tryFindLoc(out loc, friendly);
                        }
                        else
                        {
                            var owner = pair.Key;
                            CellFinder.TryFindRandomCellInsideWith(new CellRect((int)owner.DrawPos.x - 5,
                                    (int)owner.DrawPos.z - 5, 10, 10),
                                testing => testing.Standable(Map) && Map.reachability.CanReachMapEdge(testing,
                                    TraverseParms.For(TraverseMode.PassDoors)), out loc);
                        }
                    }
                    else
                    {
                        // No riders at all — guard animals from auto-generated defenders
                        tryFindLoc(out loc, friendly);
                    }
                }
                else
                {
                    tryFindLoc(out loc, friendly);
                }

                GenSpawn.Spawn(friendly, loc, Map, new Rot4());
                friendly.drafter = new Pawn_DraftController(friendly);

                Map.mapPawns.RegisterPawn(friendly);
            }

            LordMaker.MakeNewLord(FactionCache.PlayerColonyFaction, new LordJob_DefendColony(WorldSettlement, riders), Map, friendlies);

            defenders = friendlies;
            initialDefenderCount = defenders.Count;
        }

        private void RecruitMapInhabitants()
        {
            if (Map == null || !defenders.Any()) return;

            Lord defenseLord = defenders[0].GetLord();
            if (defenseLord == null) return;

            Faction empireFaction = FactionCache.PlayerColonyFaction;
            var inhabitants = new List<Pawn>();

            foreach (Pawn pawn in Map.mapPawns.AllPawnsSpawned)
            {
                if (defenders.Contains(pawn)) continue;
                if (!pawn.RaceProps.Humanlike) continue;
                if (pawn.Downed || pawn.Dead) continue;
                if (pawn.Faction != empireFaction) continue;
                if (pawn.IsPrisonerOfColony) continue;
                inhabitants.Add(pawn);
            }

            int targetCount = (int)WorldSettlement.workers;

            // Remove excess civilians
            while (inhabitants.Count > targetCount)
            {
                Pawn excess = inhabitants[inhabitants.Count - 1];
                inhabitants.RemoveAt(inhabitants.Count - 1);
                if (excess.Spawned) excess.Destroy();
            }

            // Spawn additional civilians if needed
            while (inhabitants.Count < targetCount)
            {
                Pawn civilian = PawnGenerator.GeneratePawn(FCPawnGenerator.CivilianRequest());
                IntVec3 loc;
                if (!CellFinder.TryFindRandomCellNear(Map.Center, Map, 15, c => c.Standable(Map), out loc))
                    loc = Map.Center;
                GenSpawn.Spawn(civilian, loc, Map);
                inhabitants.Add(civilian);
            }

            // Strip weapons from most civilians so they are visually distinct from guards (~12% keep weapons)
            for (int i = 0; i < inhabitants.Count; i++)
            {
                if (i % 8 != 0)
                    inhabitants[i].equipment.DestroyAllEquipment();
            }

            foreach (Pawn inhabitant in inhabitants)
            {
                Lord existingLord = inhabitant.GetLord();
                if (existingLord != null)
                    existingLord.Notify_PawnLost(inhabitant, PawnLostCondition.LeftVoluntarily);

                defenseLord.AddPawn(inhabitant);
                defenders.Add(inhabitant);
            }

            if (inhabitants.Count > 0)
            {
                initialDefenderCount = defenders.Count;
                LogUtil.Message($"Added {inhabitants.Count} settlement inhabitants to defenders at {WorldSettlement.Name}");
            }
        }

        public void EndBattle(bool won, int remaining, BattleResult battleResult = null)
        {
            var faction = FactionCache.FactionComp;

            LogUtil.Message("WorldSettlementFC.EndBattle: Handling combat resolution...");
            try
            {
                if (won)
                {
                    WinBattle(faction);
                }
                else
                {
                    LoseBattle(faction);
                }
                LogUtil.Message("WorldSettlementFC.EndBattle: Handling foreign defenders...");
                CooldownMilitary(remaining, won);
            }
            catch (Exception e)
            {
                LogUtil.Error($"Encountered an error while trying to resolve combat in Empire{Environment.NewLine}{e}");
            }
            isUnderAttack = false;
            battleMapInitialized = false;
            LifecycleRegistry.InvokeOnBattleResolved(WorldSettlement, MilitaryJobDefOf.DefendFriendlySettlement, won, battleResult);
        }

        private void CooldownMilitary(int remaining, bool won)
        {
            if (defenderForce?.homeSettlement == WorldSettlement)
            {
                var homeComp = defenderForce.homeSettlement.MilitaryComp;
                // If squad was busy elsewhere (raid, capture, etc.), don't interfere — generated pawns were used
                if (homeComp != null && homeComp.militaryJob != null
                    && homeComp.militaryJob != MilitaryJobDefOf.Undefined
                    && homeComp.militaryJob != MilitaryJobDefOf.DefendFriendlySettlement)
                {
                    return;
                }

                int battleDeaths = Math.Max(0, initialDefenderCount - remaining);
                if (won && remaining >= initialDefenderCount)
                {
                    Find.LetterStack.ReceiveLetter("OverwhelmingVictory".Translate(), "OverwhelmingVictoryDesc".Translate(), LetterDefOf.PositiveEvent);
                    homeComp?.ReturnMilitary(true);
                }
                else
                {
                    homeComp?.CooldownMilitaryFinal(battleDeaths);
                }
            }
            else if (defenderForce == null)
            {
                LogUtil.Message("Defending force not set-- if the attack came from another mod, this is fine.");
            }
            else if (defenderForce.homeSettlement != null)
            {
                // if not the home settlement defending (foreign Empire settlement)
                int battleDeaths = Math.Max(0, initialDefenderCount - remaining);
                if (won && remaining >= initialDefenderCount)
                {
                    Find.LetterStack.ReceiveLetter("OverwhelmingVictory".Translate(), "OverwhelmingVictoryDesc".Translate(), LetterDefOf.PositiveEvent);
                    defenderForce.homeSettlement.MilitaryComp?.ReturnMilitary(true);
                }
                else
                {
                    defenderForce.homeSettlement.MilitaryComp?.CooldownMilitaryFinal(battleDeaths);
                }
            }
            else
            {
                // External auto-defender (no homeSettlement) — notify via stored event reference
                if (currentBattleEvent?.externalDefenderSource != null)
                {
                    IAutoDefender extDefender = AutoDefenderRegistry.FindByWorldObject(currentBattleEvent.externalDefenderSource);
                    extDefender?.OnDefenseComplete(won, null);
                }
            }
        }

        private void LoseBattle(FactionFC faction)
        {
            faction.threatAdaptation.Notify_BattleLost();

            var happinessLostMultiplier = WorldSettlement.GetStatValue(FCStatDefOf.happinessLostMultiplier);
            var loyaltyLostMultiplier = WorldSettlement.GetStatValue(FCStatDefOf.loyaltyLostMultiplier);

            var (prosperityLoss, happinessLoss, loyaltyLoss) = SettlementFormulas.CalculateBattleLossPenalties(happinessLostMultiplier, loyaltyLostMultiplier);
            prosperityLoss *= faction.GetStatValue(FCStatDefOf.battleProsperityLossMultiplier);
            happinessLoss *= faction.GetStatValue(FCStatDefOf.battleHappinessLossMultiplier);
            loyaltyLoss *= faction.GetStatValue(FCStatDefOf.battleLoyaltyLossMultiplier);
            var canDestroyBuildings = !faction.AnyPolicyPreventsBuildingDestruction();

            WorldSettlement.prosperity -= prosperityLoss;
            WorldSettlement.happiness -= happinessLoss;
            WorldSettlement.loyalty -= loyaltyLoss;

            string str = "DefenseFailureFull".Translate(WorldSettlement.Name);

            // Penalty summary
            str += "\n\n" + "DefenseFailurePenaltiesHeader".Translate();

            int displayProsperity = (int)Math.Round(prosperityLoss);
            int displayHappiness = (int)Math.Round(happinessLoss);
            int displayLoyalty = (int)Math.Round(loyaltyLoss);

            if (displayProsperity > 0)
            {
                str += "\n  - " + "DefenseFailureProsperityLoss".Translate(displayProsperity);
            }
            if (displayHappiness > 0)
            {
                str += "\n  - " + "DefenseFailureHappinessLoss".Translate(displayHappiness);
            }
            if (displayLoyalty > 0)
            {
                str += "\n  - " + "DefenseFailureLoyaltyLoss".Translate(displayLoyalty);
            }

            if (canDestroyBuildings && WorldSettlement?.BuildingsComp != null)
            {
                // Collect candidate slots for demolition
                List<int> candidates = new List<int>();
                for (var k = 0; k < 4; k++)
                {
                    var deconstructRoll = new IntRange(0, 10).RandomInRange;
                    var deconstructChance = 7;
                    if (deconstructRoll < deconstructChance ||
                        !WorldSettlement.BuildingsComp.BuildingSlotIsBuilding(k))
                    {
                        continue;
                    }
                    candidates.Add(k);
                }

                // Sort so buildings that depend on other buildings are demolished first
                candidates.Sort((a, b) =>
                {
                    BuildingFCDef defA = WorldSettlement.BuildingsComp.GetBuildingInSlot(a);
                    BuildingFCDef defB = WorldSettlement.BuildingsComp.GetBuildingInSlot(b);
                    bool aRequiresB = defA.requiredBuildings != null && defA.requiredBuildings.Contains(defB);
                    bool bRequiresA = defB.requiredBuildings != null && defB.requiredBuildings.Contains(defA);
                    if (aRequiresB) return -1; // a depends on b, demolish a first
                    if (bRequiresA) return 1;  // b depends on a, demolish b first
                    // Buildings with any requirements go before those without
                    int aReqCount = defA.requiredBuildings?.Count ?? 0;
                    int bReqCount = defB.requiredBuildings?.Count ?? 0;
                    return bReqCount.CompareTo(aReqCount);
                });

                foreach (int k in candidates)
                {
                    str += "\n  - " + "BuildingDestroyedInRaid".Translate(WorldSettlement.BuildingsComp.BuildingLabel(k));
                    WorldSettlement.DeconstructBuilding(k);
                }
            }

            if (!canDestroyBuildings)
            {
                str += "\n  - " + "DefenseFailureBuildingsProtected".Translate();
            }

            // level remover checker
            if (WorldSettlement.settlementLevel > 1 && canDestroyBuildings)
            {
                var num = new IntRange(0, 10).RandomInRange;
                if (num >= 7)
                {
                    str += "\n  - " + "SettlementDeleveledRaid".Translate();
                    WorldSettlement.DelevelSettlement();
                }
            }

            if (!string.IsNullOrEmpty(pendingDeliveryMessage))
            {
                str += "\n\n" + pendingDeliveryMessage;
            }
            Find.LetterStack.ReceiveLetter("DefenseFailure".Translate(), str, LetterDefOf.Death,
                new LookTargets(WorldSettlement));
        }

        private void WinBattle(FactionFC faction)
        {
            faction.AddExperienceToFactionLevel(5f);
            faction.threatAdaptation.Notify_BattleWon();
            string text = "DefenseSuccessfulFull".Translate(WorldSettlement.Name);
            if (!string.IsNullOrEmpty(pendingDeliveryMessage))
            {
                text += "\n\n" + pendingDeliveryMessage;
            }
            Find.LetterStack.ReceiveLetter("DefenseSuccessful".Translate(),
                text,
                LetterDefOf.PositiveEvent, new LookTargets(WorldSettlement));
        }

        public void EndAttack()
        {
            bool won = defenders.Any();
            int remaining = defenders.Count;

            // Return external defender pawns before map cleanup destroys them
            if (currentBattleEvent?.externalDefenderSource != null)
            {
                IAutoDefender extDefender = AutoDefenderRegistry.FindByWorldObject(
                    currentBattleEvent.externalDefenderSource);
                if (extDefender != null)
                {
                    List<Pawn> survivingPawns = new List<Pawn>();
                    foreach (Pawn pawn in defenders)
                    {
                        if (pawn != null && !pawn.Dead && !pawn.Destroyed)
                        {
                            if (pawn.Spawned) pawn.DeSpawn();
                            survivingPawns.Add(pawn);
                        }
                    }
                    extDefender.ReturnDefendingPawns(survivingPawns);
                    defenders.Clear();
                }
            }

            DeleteMap(won);
            EndBattle(won, remaining);

            supporting.Clear();
            defenders.Clear();
            defenderForce = null;
            attackers.Clear();
            attackerForce = null;
            endingBattle = false;
            pendingDeliveryMessage = null;
            currentBattleEvent = null;
        }

        public void RemoveAttacker(Pawn downed)
        {
            attackers.Remove(downed);
            if (attackers.Any() || endingBattle) return;

            endingBattle = true;
            LongEventHandler.QueueLongEvent(EndAttack,
                "EndingAttack", false, error =>
                {
                    DelayedErrorWindowRequest.Add("ErrorEndingAttack".Translate(),
                        "ErrorEndingAttackDescription".Translate());
                    LogUtil.Error(error.Message);
                });
        }

        public void RemoveDefender(Pawn defender)
        {
            defenders.Remove(defender);
            if (defenders.Any() || endingBattle) return;

            endingBattle = true;
            LongEventHandler.QueueLongEvent(EndAttack,
                "EndingAttack", false, error =>
                {
                    DelayedErrorWindowRequest.Add("ErrorEndingAttack".Translate(),
                        "ErrorEndingAttackDescription".Translate());
                    LogUtil.Error(error.Message);
                });
        }

        public override void PostCaravanFormed(Caravan caravan)
        {
            var foundCaravan = new List<CaravanSupporting>();
            foreach (var found in caravan.pawns)
            {
                var lord = found.GetLord();
                if (lord != null)
                {
                    lord.Notify_PawnLost(found, PawnLostCondition.LeftVoluntarily);
                }

                // Also remove directly from defenders (lord notification may not fire for despawned pawns)
                defenders.Remove(found);

                foreach (var caravanSupporting in
                    supporting.Where(caravanSupporting => caravanSupporting.pawns.Contains(found)))
                {
                    foundCaravan.Add(caravanSupporting);
                    caravanSupporting.pawns.Remove(found);
                    break;
                }
            }

            foreach (var caravanSupporting in foundCaravan.Where(caravanSupporting =>
                    caravanSupporting.pawns.Find(pawn => !pawn.Downed &&
                                                         !pawn.Dead && !pawn.AnimalOrWildMan()) == null))
            {
                //Prevent removing while creating end battle caravans
                if (isUnderAttack)
                    supporting.Remove(caravanSupporting);
            }
            /*It appears vanilla handles this automatically
                foreach (Pawn animal in caravanSupporting.supporting.FindAll(pawn => pawn.AnimalOrWildMan()))
                {
                    animal.holdingOwner = null;
                    animal.DeSpawn();
                    Find.WorldPawns.PassToWorld(animal);
                    caravan.pawns.TryAdd(animal);
                }*/

            //Appears to not happen sometimes, no clue why
            foreach (var pawn in caravan.pawns) Map.reservationManager.ReleaseAllClaimedBy(pawn);

            base.PostCaravanFormed(caravan);
        }

        public void SendMilitary(PlanetTile location, MilitaryJobDef job, int timeToFinish, Faction enemy)
        {
            if (IsMilitaryBusy() || IsTargetOccupied(location)) return;

            militaryBusy = true;
            militaryJob = job;
            militaryLocation = location;

            if (enemy != null) militaryEnemy = enemy;
            if (job.occupiesTarget) FactionCache.FactionComp.militaryTargets.Add(location);

            job.Handler?.OnDeployed(this, location, timeToFinish, enemy);

            LifecycleRegistry.InvokeOnSquadDeployed(WorldSettlement, job);
        }

        public Settlement ReturnMilitaryTarget()
        {
            return militaryLocation == -1 ? null : Find.WorldObjects.SettlementAt(militaryLocation);
        }

        public void ProcessMilitaryEvent()
        {
            FactionFC faction = FactionCache.FactionComp;
            if (faction.militaryTargets.Contains(militaryLocation))
            {
                faction.militaryTargets.Remove(militaryLocation);
            }

            BattleResult result = null;
            MilitaryJobDef resolvedJob = militaryJob;

            if (militaryJob.Handler != null)
            {
                result = militaryJob.Handler.OnResolved(this);
            }

            bool victory = result != null && result.AttackerVictory;
            LifecycleRegistry.InvokeOnBattleResolved(WorldSettlement, resolvedJob, victory, result);
            CooldownMilitaryFinal();
        }

        public void ReturnMilitary(bool alert)
        {
            militaryBusy = false;
            militaryJob = MilitaryJobDefOf.Undefined;
            militaryLocation = -1;
            militaryEnemy = null;

            LifecycleRegistry.InvokeOnSquadRecalled(WorldSettlement);

            if (alert)
            {
                Find.LetterStack.ReceiveLetter("Military Cooldown", "FCMilitaryCooldown".Translate(WorldSettlement.Name),
                    LetterDefOf.PositiveEvent);
            }
        }

        public void CooldownMilitaryFinal(int battleDeaths = 0)
        {
            FactionFC faction = FactionCache.FactionComp;

            int cooldown = GenDate.TicksPerDay * 3;
            cooldown += (int)faction.GetStatValue(FCStatDefOf.militaryCooldownOffset);
            if (militaryJob != null && militaryJob.cooldownStatDef != null)
                cooldown += (int)faction.GetStatValue(militaryJob.cooldownStatDef);

            // Dead pawn cooldown: use battleDeaths for defense, squad.dead for deployment
            int deaths = battleDeaths;
            if (deaths == 0 && militaryJob != null && militaryJob.deadPawnCooldown
                && FCSettings.deadPawnsIncreaseMilitaryCooldown)
            {
                deaths = militarySquad != null ? militarySquad.dead : 0;
            }
            if (deaths > 0 && FCSettings.deadPawnsIncreaseMilitaryCooldown)
            {
                int deadMultiplier = 10000 + (int)faction.GetStatValue(FCStatDefOf.deadPawnCooldownOffset);
                cooldown += deaths * deadMultiplier;
            }
            cooldown = Math.Max(cooldown, 0);

            militaryJob = MilitaryJobDefOf.Cooldown;
            militaryBusy = true;
            militaryLocation = WorldSettlement.Tile;
            militaryEnemy = null;

            FCEvent tmp = FCEventMaker.MakeEvent(FCEventDefOf.cooldownMilitary);
            tmp.hasCustomDescription = true;
            tmp.timeTillTrigger = Find.TickManager.TicksGame + cooldown;
            tmp.location = WorldSettlement.Tile;
            tmp.customDescription = "MilitaryForcesReorganizing".Translate(WorldSettlement.Name); // + 
            FactionCache.FactionComp.AddEvent(tmp);
        }

        public bool IsMilitaryBusy(bool silent = false)
        {
            if (militaryBusy && !silent)
            {
                Messages.Message("militaryAlreadyAssigned".Translate(), MessageTypeDefOf.RejectInput);
            }

            return militaryBusy;
        }

        public bool IsMilitarySquadValid()
        {
            if (militarySquad != null)
            {
                militarySquad.CheckInitialization();
                if (militarySquad.outfit != null)
                {
                    if (militarySquad.EquippedMercenaries.Any())
                    {
                        return true;
                    }

                    Messages.Message("FCNoSquadEquipped".Translate(),
                        MessageTypeDefOf.RejectInput);
                    return false;
                }

                Messages.Message("FCNoSquadLoadoutAssigned".Translate(),
                    MessageTypeDefOf.RejectInput);
                return false;
            }

            Messages.Message("FCNoSquadAssigned".Translate(), MessageTypeDefOf.RejectInput);
            return false;
        }

        public bool IsMilitarySquadValidSilent()
        {
            return !(militarySquad is null);
        }

        public bool IsMilitaryBusySilent()
        {
            return militaryBusy;
        }

        public bool IsMilitaryValid()
        {
            return settlementMilitaryLevel > 0;
        }

        public bool IsTargetOccupied(int location)
        {
            if (FactionCache.FactionComp.militaryTargets.Contains(location))
            {
                Messages.Message("targetAlreadyBeingAttacked".Translate(), MessageTypeDefOf.RejectInput);
                return true;
            }

            return false;
        }
    }
}
