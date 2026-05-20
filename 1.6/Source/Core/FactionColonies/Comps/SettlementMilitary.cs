using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
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

    public class WorldObjectComp_SettlementMilitary : WorldObjectComp, ISettlementPostLoadInit
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

        public MilitaryForce attackerForce;
        public List<Pawn> attackers = new List<Pawn>();
        public MilitaryForce defenderForce;
        private FCEvent currentBattleEvent;
        public List<Pawn> defenders = new List<Pawn>();
        public List<Pawn> draftedNPCs = new List<Pawn>();
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
        private bool shuttleLandingPending = false;
        private int initialDefenderCount;
        private string pendingDeliveryMessage;

        /* Pod-bound pawns during Skyfaller descent are !Spawned but ParentHolder != null.
         * A pure !Spawned check treats them as lost and triggers false victory. */
        private static bool IsPawnTrulyGone(Pawn p)
        {
            if (p is null) return true;
            if (p.Destroyed) return true;
            if (p.Spawned) return false;
            if (p.ParentHolder is object) return false;
            return true;
        }

        private bool HasPendingPodAttackers()
        {
            List<Pawn> list = attackers;
            if (list is null) return false;
            foreach (Pawn p in list)
            {
                if (p is null || p.Destroyed || p.Dead) continue;
                if (!p.Spawned && p.ParentHolder is object) return true;
            }

            return false;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref attackers, "attackers", LookMode.Reference);
            Scribe_Collections.Look(ref defenders, "defenders", LookMode.Reference);
            Scribe_Collections.Look(ref draftedNPCs, "draftedNPCs", LookMode.Reference);
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

        public override void Initialize(WorldObjectCompProperties props_l)
        {
            base.Initialize(props_l);

            attackers = new List<Pawn>();
            defenders = new List<Pawn>();
            draftedNPCs = new List<Pawn>();
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!isUnderAttack) return;
            if (endingBattle) return;

            if (isUnderAttack && !endingBattle && Find.TickManager.TicksGame % 2500 == 0
                && Map is null && attackers.Count == 0 && defenders.Count == 0)
            {
                // Events are removed from the queue before battle starts (see FCEventMaker.ProcessEvents),
                // so an orphaned flag is only "stuck" if the battle also isn't in progress; i.e. no map loaded
                // and no active combatants.
                FCEvent evt = MilitaryUtilFC.ReturnMilitaryEventByLocation(WorldSettlement.Tile);
                if (evt is null)
                {
                    LogUtil.Warning($"Clearing orphaned isUnderAttack flag on {WorldSettlement.Name} " +
                        $"(no matching settlementBeingAttacked event in queue).");
                    ClearAttackState();
                    return;
                }
            }

            if (Find.TickManager.TicksGame % 250 != 0) return;
            if (Map == null) return;

            // Clean stale references: null (save/load), destroyed, or despawned-alive
            // (e.g. pawn joined an existing caravan without PostCaravanFormed firing).
            // Pod-bound pawns in a descending Skyfaller are !Spawned but ParentHolder != null;
            //   they stay tracked until the pod opens.
            attackers.RemoveAll(IsPawnTrulyGone);
            defenders.RemoveAll(IsPawnTrulyGone);

            // Detect untracked player pawns on the battle map (e.g. shuttle-delivered pawns
            // that spawned via the Unload job after the ArrivePatch fired).
            // Also assigns lordless defenders to the battle lord (e.g. pawns that just
            // unloaded from a shuttle after being registered with assignToLord: false).
            var map = Map;
            Lord battleLord = null;
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.Dead || pawn.Downed) continue;
                if (!defenders.Contains(pawn))
                {
                    LogUtil.Warning($"Registering untracked player pawn {pawn.LabelShort} with defense at {WorldSettlement.Name}");
                    defenders.Add(pawn);
                    initialDefenderCount++;
                }
                if (pawn.GetLord() is null)
                {
                    if (battleLord is null)
                        battleLord = defenders.FirstOrDefault(d => d.GetLord() != null)?.GetLord();
                    if (battleLord != null && !battleLord.ownedPawns.Contains(pawn))
                        battleLord.AddPawn(pawn);
                }
            }

            if (attackers.Count == 0 || defenders.Count == 0)
            {
                LogUtil.Warning($"Stuck battle detected at {WorldSettlement.Name}, forcing resolution.");
                endingBattle = true;
                LongEventHandler.QueueLongEvent(EndAttack,
                    "EndingAttack", false, error =>
                    {
                        DelayedErrorWindowRequest.Add("FCErrorEndingAttack".Translate(),
                            "FCErrorEndingAttackDescription".Translate());
                        LogUtil.Error(error.Message);
                    });
            }
        }

        private static string FoundSettlementString(WorldSettlementFC settlement, string winChanceText = null, bool isCurrentDefender = false)
        {
            string s = settlement.Name + " " + "FCShortMilitary".Translate() + " " + settlement.settlementMilitaryLevel;
            if (!winChanceText.NullOrEmpty())
                s += " - Victory: " + winChanceText + "%";
            if (isCurrentDefender)
                s += " - [" + "FCCurrentDefender".Translate() + "]";
            else
                s += " - " + "FCAvailable".Translate() + ": " + (settlement.MilitaryComp?.IsMilitaryBusySilent() != true).ToString();
            return s;
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
                // No else-branch log here; CompTick's orphan cleanup (line ~126) already logs
                // and repairs the stuck flag. Logging here just spams every frame.
            }
        }

        private Command DefendColonyAction()
        {
            Command_Action defendColony = new Command_Action
            {
                defaultLabel = "FCDefendColony".Translate(),
                defaultDesc = "FCDefendColonyDesc".Translate(),
                icon = TexLoad.iconMilitary,
                action = delegate
                {
                    StartDefence(MilitaryUtilFC.ReturnMilitaryEventByLocation(WorldSettlement.Tile), () => { });
                }
            };
            /* If auto-battle is enabled, then disable the button. We leave it visible, though, so that the player knows that this is an option if
             * they change their settings. */
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
                defaultLabel = "FCDefendSettlement".Translate(),
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

                    double winChance = SimulateBattleFc.CalculateDefenderWinChance(evt.militaryForceAttacking, evt.militaryForceDefending);
                    var list = new List<FloatMenuOption>()
                    {
                        new FloatMenuOption("FCSettlementDefendingInformation".Translate(evt.militaryForceDefending.homeSettlement.Name,
                                                                                       evt.militaryForceDefending.DefensivePower,
                                                                                       (winChance * 100).ToString("F0")),
                                            null, MenuOptionPriority.High),
                        new FloatMenuOption("FCChangeDefendingForce".Translate(), () => ChangeDefendingForceAction(evt))
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
            MilitaryForce attackForce = evt.militaryForceAttacking;
            WorldSettlementFC currentDefender = evt.militaryForceDefending?.homeSettlement;

            // "Reset to Home Settlement" option with win chance
            MilitaryForce homeForce = MilitaryForce.CreateMilitaryForceFromSettlement(WorldSettlement);
            double homeWinChance = SimulateBattleFc.CalculateDefenderWinChance(attackForce, homeForce);
            var settlementList = new List<FloatMenuOption>
            {
                new FloatMenuOption
                (
                    "FCResetToHomeSettlement".Translate(settlementMilitaryLevel, (homeWinChance * 100).ToString("F0")),
                    delegate { MilitaryUtilFC.ChangeDefendingMilitaryForce(evt, WorldSettlement); },
                    MenuOptionPriority.High
                )
            };

            // Other Empire settlements with win chance per option
            foreach (WorldSettlementFC foundSettlement in faction.settlements)
            {
                if (foundSettlement == WorldSettlement) continue;
                if (foundSettlement.MilitaryComp?.IsMilitaryValid() != true) continue;
                if (!DefenseValidatorRegistry.CanDefend(foundSettlement, WorldSettlement)) continue;

                MilitaryForce tmpHome = MilitaryForce.CreateMilitaryForceFromSettlement(WorldSettlement, true);
                MilitaryForce hypothetical = MilitaryForce.CreateMilitaryForceFromSettlement(foundSettlement, homeDefendingForce: tmpHome);
                double wc = SimulateBattleFc.CalculateDefenderWinChance(attackForce, hypothetical);
                string wcText = (wc * 100).ToString("F0");

                WorldSettlementFC s = foundSettlement;
                settlementList.Add(new FloatMenuOption(
                    FoundSettlementString(s, wcText, s == currentDefender),
                    delegate
                    {
                        if (s.MilitaryComp?.IsMilitaryBusy() != true)
                            MilitaryUtilFC.ChangeDefendingMilitaryForce(evt, s);
                    }
                ));
            }

            // Add external auto-defenders (VOE outposts, etc.)
            foreach (IAutoDefender defender in AutoDefenderRegistry.Defenders)
            {
                if (!defender.CanAutoDefend) continue;
                if (evt.externalDefenderSource != null && evt.externalDefenderSource == defender.WorldObject) continue;
                int distance = Find.WorldGrid.TraversalDistanceBetween(defender.WorldObject.Tile, WorldSettlement.Tile);
                if (distance > defender.Range) continue;

                IAutoDefender d = defender;
                MilitaryForce extForce = d.CreateDefendingForce();
                double extWc = SimulateBattleFc.CalculateDefenderWinChance(attackForce, extForce);
                settlementList.Add(new FloatMenuOption(
                    d.WorldObject.LabelCap + " (" + "FCMilitaryLevel".Translate() + " " + d.MilitaryLevel
                        + " - Victory: " + (extWc * 100).ToString("F0") + "%)",
                    delegate { MilitaryUtilFC.ChangeDefendingToExternalForce(evt, d); }
                ));
            }

            if (settlementList.Count == 0)
                settlementList.Add(new FloatMenuOption("FCNoValidMilitaries".Translate(), null));

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
                defaultLabel = "FCDefendColony".Translate(),
                defaultDesc = "FCDefendColonyDesc".Translate(),
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
            if (!WorldSettlement.settlementDef.supportsManualBattle)
            {
                return new AcceptanceReport("FCSettlementTypeNoManualBattle".Translate());
            }
            if (FCSettings.battleMode == BattleMode.Auto)
            {
                return new AcceptanceReport("FCAutoBattleEnabledNoManualFight".Translate());
            }
            if (FCSettings.battleMode == BattleMode.Hybrid && !IsPlayerCaravanOnTile())
            {
                return new AcceptanceReport("FCHybridBattleEnabledNoManualFight".Translate());
            }
            return AcceptanceReport.WasAccepted;
        }

        private bool IsPlayerCaravanOnTile()
        {
            return Find.WorldObjects.Caravans.Any(c =>
                c.Tile == WorldSettlement.Tile &&
                c.Faction == Faction.OfPlayer);
        }

        private bool AnyOtherSettlementMapOpen()
        {
            foreach (WorldSettlementFC settlement in FactionCache.FactionComp?.settlements ?? Enumerable.Empty<WorldSettlementFC>())
            {
                if (settlement == WorldSettlement) continue;
                if (settlement.Map != null) return true;
            }
            return false;
        }

        public void CaravanDefend(Caravan caravan)
        {
            var pawns = caravan.pawns.InnerListForReading.ListFullCopy();

            // Check for shuttle in caravan (Odyssey DLC passenger shuttle)
            var shuttle = caravan.Shuttle;
            if (shuttle != null && Map != null)
            {
                ShuttleCaravanDefend(caravan, pawns, shuttle);
                return;
            }

            // Standard flow — spawn pawns at map edge
            RegisterPawnsAsDefenders(pawns, assignToLord: true);
            if (!caravan.Destroyed) caravan.Destroy();
            SpawnPawnsAtEdge(pawns);
        }

        private void ShuttleCaravanDefend(Caravan caravan, List<Pawn> pawns, Building_PassengerShuttle shuttle)
        {
            // Extract shuttle from pawn inventory before destroying caravan
            Pawn owner = CaravanInventoryUtility.GetOwnerOf(caravan, shuttle);
            owner?.inventory.innerContainer.Remove(shuttle);

            // Register pawns as defenders (for win/loss counting) but don't assign to a lord
            // since they're still inside the shuttle and not spawned on the map yet.
            RegisterPawnsAsDefenders(pawns, assignToLord: false);
            if (!caravan.Destroyed) caravan.Destroy();

            // Build TransportShip with pawns loaded inside
            CompShuttle compShuttle = shuttle.TryGetComp<CompShuttle>();
            TransportShipDef shipDef = compShuttle?.Props?.shipDef ?? TransportShipDefOf.Ship_Shuttle;
            TransportShip transportShip = TransportShipMaker.MakeTransportShip(shipDef, pawns, shuttle);

            // Prevent DeleteMap from removing the map while shuttle is in flight
            shuttleLandingPending = true;

            // Defer targeting to the next CompTick — UI can't render during LongEvents.
            var map = Map;
            var settlement = WorldSettlement;
            var shuttleDef = shuttle.def;
            Rot4 shuttleRotation = shuttleDef.defaultPlacingRot;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                // Force camera to the battle map so the player sees where to land
                Current.Game.CurrentMap = map;
                CameraJumper.TryJump(new IntVec3(map.Size.x / 2, 0, map.Size.z / 2), map);

                var targetParams = new TargetingParameters
                {
                    canTargetLocations = true,
                    canTargetSelf = false,
                    canTargetPawns = false,
                    canTargetFires = false,
                    canTargetBuildings = false,
                    canTargetItems = false
                };

                bool landed = false;
                Find.Targeter.BeginTargeting(targetParams,
                    delegate(LocalTargetInfo target)
                    {
                        landed = true;
                        shuttleLandingPending = false;
                        shuttle.Rotation = shuttleRotation;
                        transportShip.ArriveAt(target.Cell, settlement);
                        transportShip.AddJobs(ShipJobDefOf.Unload, ShipJobDefOf.WaitForever);
                    },
                    delegate(LocalTargetInfo target)
                    {
                        RoyalTitlePermitWorker_CallShuttle.DrawShuttleGhost(target, map, shuttleDef, shuttleRotation);
                    },
                    delegate(LocalTargetInfo target)
                    {
                        return RoyalTitlePermitWorker_CallShuttle.ShuttleCanLandHere(target, map, shuttleDef, shuttleRotation);
                    },
                    null,
                    delegate
                    {
                        if (landed) return;
                        shuttleLandingPending = false;
                        // Player cancelled targeting — auto-land at best spot
                        if (!Find.Maps.Contains(map)) return;
                        IntVec3 fallback = DropCellFinder.GetBestShuttleLandingSpot(map, Faction.OfPlayer);
                        transportShip.ArriveAt(fallback, settlement);
                        transportShip.AddJobs(ShipJobDefOf.Unload, ShipJobDefOf.WaitForever);
                    },
                    null, true, null,
                    delegate(LocalTargetInfo target)
                    {
                        if (!shuttleDef.rotatable) return;
                        if (KeyBindingDefOf.Designator_RotateRight.KeyDownEvent)
                            shuttleRotation = shuttleRotation.Rotated(RotationDirection.Clockwise);
                        if (KeyBindingDefOf.Designator_RotateLeft.KeyDownEvent)
                            shuttleRotation = shuttleRotation.Rotated(RotationDirection.Counterclockwise);
                    });
            });
        }

        private void SpawnPawnsAtEdge(List<Pawn> pawns)
        {
            var enterCell = FindNearEdgeCell(Map);
            foreach (var pawn in pawns)
            {
                var loc = CellFinder.RandomSpawnCellForPawnNear(enterCell, Map);
                GenSpawn.Spawn(pawn, loc, Map, Rot4.Random);
            }
        }

        public void AddToDefenceFromList(List<Pawn> pawns, int destinationTile)
        {
            AddToDefenceFromList(pawns, destinationTile, assignToLord: true);
        }

        /// <summary>
        /// Registers pawns with the defense system (defenders list + battle lord).
        /// When <paramref name="assignToLord"/> is false, pawns are added to defenders
        /// but not to the battle lord. This is needed for entities like Vehicle Framework
        /// vehicles that have their own job systems and conflict with lord duty assignments.
        /// </summary>
        public void AddToDefenceFromList(List<Pawn> pawns, int destinationTile, bool assignToLord)
        {
            if (pawns.NullOrEmpty())
            {
                LogUtil.Error("Tried to add an empty list of pawns to an FCEvent");
                return;
            }

            // If battle is already in progress (map loaded), register directly.
            // Avoids a redundant StartDefence call that would fail to find the event
            // (already consumed) and queue a second LongEvent causing cascade errors.
            if (isUnderAttack && Map != null)
            {
                RegisterPawnsAsDefenders(pawns, assignToLord);
                return;
            }

            StartDefence(
                MilitaryUtilFC.ReturnMilitaryEventByLocation(destinationTile), () =>
                {
                    RegisterPawnsAsDefenders(pawns, assignToLord);
                });
        }

        private void RegisterPawnsAsDefenders(List<Pawn> pawns, bool assignToLord)
        {
            if (assignToLord)
            {
                Lord existingLord = defenders.Any() ? defenders[0].GetLord() : null;
                if (existingLord != null)
                {
                    foreach (var pawn in pawns)
                    {
                        if (!defenders.Contains(pawn) && !existingLord.ownedPawns.Contains(pawn))
                            existingLord.AddPawn(pawn);
                    }
                }
                else if (Map != null)
                {
                    var lordless = new List<Pawn>();
                    foreach (var pawn in pawns)
                    {
                        if (!defenders.Contains(pawn) && pawn.GetLord() is null)
                            lordless.Add(pawn);
                    }
                    if (lordless.Any())
                        LordMaker.MakeNewLord(FactionCache.PlayerColonyFaction,
                            new LordJob_ColonistsIdle(WorldSettlement), Map, lordless);
                }
            }

            foreach (var pawn in pawns)
            {
                if (!defenders.Contains(pawn))
                {
                    defenders.Add(pawn);
                    initialDefenderCount++;
                }
            }
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
            if (map is null) return;

            // Restore faction on Empire defenders the player drafted during battle.
            // After this, any remaining Faction.OfPlayer pawns are real player colonists.
            Faction empireFaction = FactionCache.PlayerColonyFaction;
            foreach (Pawn npc in draftedNPCs)
            {
                if (npc is null || npc.Dead || npc.Destroyed) continue;
                if (npc.Faction == Faction.OfPlayer)
                    npc.SetFaction(empireFaction);
            }
            draftedNPCs.Clear();

            // Check for player units on the map. Use AllPawnsSpawned + faction filter
            // instead of FreeColonistsSpawned so VehiclePawns (and player animals) count.
            // Pawns aboard a VehiclePawn are not directly spawned, but the vehicle itself
            // is a player-faction Pawn; detecting the vehicle is enough to keep the map
            // alive so the player can drive off and reform the caravan.
            List<Pawn> playerPawns = new List<Pawn>();
            bool anyMobile = false;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.Faction != Faction.OfPlayer) continue;
                playerPawns.Add(pawn);
                if (!pawn.Downed) anyMobile = true;
            }

            if (anyMobile || shuttleLandingPending)
            {
                // Mobile player pawns (or shuttles) exist — keep the map alive.
                // ShouldRemoveMapNow checks AnyPawnBlockingMapRemoval and will
                // auto-remove the map once all player pawns/shuttles have left.
                // Notify_MyMapAboutToBeRemoved handles Empire pawn cleanup at that point.

                // Remove battle lords, then re-assign Empire defenders to an idle lord
                // so they hold position instead of wandering to the map edge.
                foreach (var lord in map.lordManager.lords.ListFullCopy())
                    map.lordManager.RemoveLord(lord);

                List<Pawn> empireDefenders = new List<Pawn>();
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                    if (pawn.Faction == empireFaction && !pawn.Dead && !pawn.Downed)
                        empireDefenders.Add(pawn);

                // Stop stale jobs that survived lord cleanup (e.g. Goto with exitMapOnArrival).
                // Lord.Cleanup only interrupts jobs where EndPawnJobOnCleanup returns true;
                // the rest keep executing and can walk pawns off the map.
                foreach (Pawn pawn in empireDefenders)
                    pawn.jobs.StopAll();

                if (empireDefenders.Any())
                    LordMaker.MakeNewLord(empireFaction, new LordJob_ColonistsIdle(WorldSettlement), map, empireDefenders);

                return;
            }

            // Immediate path — remove all lords before cleanup
            foreach (var lord in map.lordManager.lords.ListFullCopy())
                map.lordManager.RemoveLord(lord);

            if (playerPawns.Count > 0)
            {
                // All player pawns are downed — deliver them home via event, then remove map
                foreach (Pawn pawn in playerPawns)
                    if (pawn.Spawned) pawn.DeSpawn();

                foreach (Pawn pawn in playerPawns)
                    if (!pawn.Dead)
                    {
                        int iterations = 0;
                        while (pawn.health.HasHediffsNeedingTend())
                        {
                            iterations++;
                            if (iterations > 10000)
                            {
                                LogUtil.Error("SettlementMilitary.DeleteMap: Too many tend iterations.");
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

                var goods = new List<Thing>(playerPawns.Count);
                foreach (Pawn pawn in playerPawns) goods.Add(pawn);

                var eventParams = new FCEvent
                {
                    location = Find.AnyPlayerHomeMap.Tile,
                    source = WorldSettlement.Tile,
                    goods = goods,
                    customDescription = eventText,
                    timeTillTrigger = Find.TickManager.TicksGame + travelTicks
                };
                DeliveryEvent.CreateDeliveryEvent(eventParams);
                string travelDays = ((float)travelTicks / GenDate.TicksPerDay).ToString("0.#");
                pendingDeliveryMessage = "FCInjuredCaravanMembersReturning".Translate(playerPawns.Count, travelDays);
            }

            // No player pawns (or all downed and delivered) — immediate map removal.
            // Notify_MyMapAboutToBeRemoved handles Empire pawn cleanup.
            CameraJumper.TryJump(WorldSettlement.Tile);
            Current.Game.CurrentMap = Find.AnyPlayerHomeMap;
            Current.Game.DeinitAndRemoveMap(map, false);
        }

        public void StartDefence(FCEvent evt, Action after)
        {
            currentBattleEvent = evt;

            // Consume the event from the faction queue exactly once, regardless of entry point
            // (manual Defend button, caravan defend, or timer-driven ProcessEvents). Prevents
            // ProcessEvents from re-triggering a second defense after this one resolves.
            FactionCache.FactionComp?.RemoveEvent(evt);

            bool shouldAutoResolve = false;
            if (FCSettings.battleMode == BattleMode.Auto || !WorldSettlement.settlementDef.supportsManualBattle)
            {
                shouldAutoResolve = true;
            }
            else if (FCSettings.battleMode == BattleMode.Hybrid)
            {
                shouldAutoResolve = !battleMapInitialized && !IsPlayerCaravanOnTile();
            }

            // Map still loaded from previous battle (player hasn't left yet) — auto-resolve.
            // Also auto-resolve if ANY other Empire settlement has a battle map open,
            // to prevent multiple simultaneous battle maps.
            if (Map != null && !isUnderAttack)
                shouldAutoResolve = true;
            if (!shouldAutoResolve && AnyOtherSettlementMapOpen())
                shouldAutoResolve = true;

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
                SetupAttack(evt);
                after.Invoke();
            },
                "GeneratingMap", false, GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap);
        }

        private void SetupAttack(FCEvent temp)
        {
            // ZoomIntoTile may have aborted via EndBattle (null event / null force); don't
            // spawn attackers into a cleaned-up state.
            if (!isUnderAttack) return;
            // Idempotency guard: if StartDefence runs again on an already-active battle,
            // don't spawn a second wave of attackers.
            if (attackers.Any()) return;

            if (Map is null)
            {
                LogUtil.Error($"SetupAttack: {WorldSettlement.Name} has no map. Resetting battle state.");
                EndBattle(false, 0, null);
                return;
            }

            if (temp.militaryForceAttacking is null || temp.militaryForceAttackingFaction is null)
            {
                LogUtil.Error($"SetupAttack: Missing attacking force or faction for {WorldSettlement.Name}. Resetting battle state.");
                EndBattle(false, 0, null);
                return;
            }

            IncidentParms parms = new IncidentParms
            {
                target = Map,
                faction = temp.militaryForceAttackingFaction,
                generateFightersOnly = true,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                raidNeverFleeIndividual = true
            };
            parms.points = Math.Max(
                IncidentWorker_Raid.AdjustedRaidPoints(
                    (float)temp.militaryForceAttacking.forceRemaining * 175,
                    PawnsArrivalModeDefOf.EdgeWalkIn, parms.raidStrategy,
                    parms.faction, PawnGroupKindDefOf.Combat,
                    parms.target),
                300f);
            parms.raidArrivalMode = ResolveRaidArriveMode(parms) ?? PawnsArrivalModeDefOf.EdgeWalkIn;
            parms.raidArrivalMode.Worker.TryResolveRaidSpawnCenter(parms);

            List<Pawn> newAttackers = PawnGroupMakerUtility.GeneratePawns(
                IncidentParmsUtility.GetDefaultPawnGroupMakerParms(
                    PawnGroupKindDefOf.Combat, parms, true)).ToList();
            if (!newAttackers.Any())
            {
                LogUtil.Error("Got no pawns spawning raid from parms " + parms);
                // Queue cleanup as a separate LongEvent so the map's deferred initialization
                // (MapDrawer.RegenerateEverythingNow) completes before we try to dispose it.
                LongEventHandler.QueueLongEvent(EndAttack, "EndingAttack", false, null);
                return;
            }

            double attackerEfficiency = temp.militaryForceAttacking.militaryEfficiency;
            foreach (Pawn attacker in newAttackers)
            {
                MilitaryEfficiencyUtil.ApplyCombatEfficiencyHediff(attacker, attackerEfficiency);
            }

            parms.raidArrivalMode.Worker.Arrive(newAttackers, parms);

            attackers = newAttackers;
            attackerForce = temp.militaryForceAttacking;
            defenderForce = temp.militaryForceDefending;
            LordMaker.MakeNewLord(
                parms.faction,
                new LordJob_HuntColonists(WorldSettlement, parms.raidArrivalMode != PawnsArrivalModeDefOf.CenterDrop),
                Map, newAttackers);
        }

        private static PawnsArrivalModeDef ResolveRaidArriveMode(IncidentParms parms)
        {
            return parms.raidStrategy.arriveModes
                .Where(mode => mode.Worker.CanUseWith(parms))
                .TryRandomElementByWeight(mode => mode.Worker.GetSelectionWeight(parms), out PawnsArrivalModeDef output)
                ? output
                : PawnsArrivalModeDefOf.EdgeWalkIn;
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

                if (force.homeSettlement?.MilitaryComp != null)
                    force.homeSettlement.MilitaryComp.militaryBusy = true;

                Map.fogGrid.ClearAllFog();

                // Remove pawns spawned by KCSG/VBGE that don't belong to Empire or the player.
                // KCSG's SymbolResolver_Settlement spawns generic faction pawns during map gen
                // that conflict with Empire's lord-based military system.
                Faction empireFaction = FactionCache.PlayerColonyFaction;
                List<Pawn> toRemove = new List<Pawn>();
                foreach (Pawn pawn in Map.mapPawns.AllPawnsSpawned)
                {
                    if (!pawn.RaceProps.Humanlike) continue;
                    if (pawn.Faction == empireFaction) continue;
                    if (pawn.Faction == Faction.OfPlayer) continue;
                    toRemove.Add(pawn);
                }
                foreach (Pawn pawn in toRemove)
                {
                    pawn.Destroy();
                }
                if (toRemove.Count > 0)
                    LogUtil.Message($"Cleaned up {toRemove.Count} unrelated pawns from {WorldSettlement.Name}");

                GenerateFriendlies(force);
                RecruitMapInhabitants();
                Find.TickManager.Notify_GeneratedPotentiallyHostileMap();

                string enemyName = attackerForce?.homeFaction?.Name ?? "Unknown";
                GlobalTargetInfo jumpTarget = defenders.Any()
                    ? new GlobalTargetInfo(defenders[0])
                    : new GlobalTargetInfo(new IntVec3(Map.Size.x / 2, 0, Map.Size.z / 2), Map);
                Find.LetterStack.ReceiveLetter(
                    "FCManualBattleStarted".Translate(WorldSettlement.Name),
                    "FCManualBattleStartedDesc".Translate(WorldSettlement.Name, enemyName),
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

        private void GenerateFriendlies(MilitaryForce force)
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

                    double efficiency = force.militaryEfficiency;
                    foreach (Pawn merc in squad.AllEquippedMercenaryPawns)
                    {
                        MilitaryEfficiencyUtil.ShiftPawnGearQuality(merc, efficiency);
                        MilitaryEfficiencyUtil.ApplyCombatEfficiencyHediff(merc, efficiency);
                    }

                    friendlies = squad.AllEquippedMercenaryPawns.ToList();

                    foreach (var animal in squad.animals)
                    {
                        if (animal.handler?.pawn is object)
                            riders.Add(animal.handler.pawn, animal.pawn);
                    }
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

                    double efficiency = force.militaryEfficiency;
                    foreach (Pawn defender in friendlies)
                    {
                        MilitaryEfficiencyUtil.ApplyCombatEfficiencyHediff(defender, efficiency);
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

            var spawnedFriendlies = new List<Pawn>();
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
                            var isAnimal = friendly.RaceProps.Animal ? "animal" : "human";
                            LogUtil.Error("No rider pair found for " + isAnimal + ": " + friendly.thingIDNumber + ", and riders dictionary is not empty!");
                            continue;
                        }
                        var owner = pair.Key;
                        CellFinder.TryFindRandomCellInsideWith(new CellRect((int)owner.DrawPos.x - 5,
                                (int)owner.DrawPos.z - 5, 10, 10),
                            testing => testing.Standable(Map) && Map.reachability.CanReachMapEdge(testing,
                                TraverseParms.For(TraverseMode.PassDoors)), out loc);
                    }
                    else
                    {
                        LogUtil.Error("Rider Dictionary is empty but animal was still generated?");
                        continue;
                    }
                }
                else
                {
                    tryFindLoc(out loc, friendly);
                }

                try
                {
                    GenSpawn.Spawn(friendly, loc, Map, new Rot4());
                    friendly.drafter = new Pawn_DraftController(friendly);
                    Map.mapPawns.RegisterPawn(friendly);
                    spawnedFriendlies.Add(friendly);
                }
                catch (Exception e)
                {
                    LogUtil.Warning($"Failed to spawn defender {friendly.LabelShort} (likely a mod conflict): {e}");
                }
            }

            LordMaker.MakeNewLord(FactionCache.PlayerColonyFaction, new LordJob_DefendColony(WorldSettlement, riders), Map, spawnedFriendlies);

            defenders = spawnedFriendlies;
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
            int spawnAttempts = 0;
            while (inhabitants.Count < targetCount && spawnAttempts < targetCount * 2)
            {
                spawnAttempts++;
                Pawn civilian = PawnGenerator.GeneratePawn(FCPawnGenerator.CivilianRequest());
                IntVec3 loc;
                if (!CellFinder.TryFindRandomCellNear(Map.Center, Map, 15, c => c.Standable(Map), out loc))
                    loc = Map.Center;
                try
                {
                    GenSpawn.Spawn(civilian, loc, Map);
                    inhabitants.Add(civilian);
                }
                catch (Exception e)
                {
                    LogUtil.Warning($"Failed to spawn civilian (likely a mod conflict): {e}");
                }
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

        private void ClearAttackState()
        {
            // Notify foreign defender so they don't keep a stale
            // militaryJob = DefendFriendlySettlement / militaryLocation pointed here.
            if (defenderForce?.homeSettlement is object
                && defenderForce.homeSettlement != WorldSettlement)
            {
                defenderForce.homeSettlement.MilitaryComp?.ReturnMilitary(false);
            }

            isUnderAttack = false;
            endingBattle = false;
            battleMapInitialized = false;
            shuttleLandingPending = false;
            attackers?.Clear();
            defenders?.Clear();
            draftedNPCs?.Clear();
            defenderForce = null;
            attackerForce = null;
            currentBattleEvent = null;
        }

        public void PostSettlementLoadInit(WorldSettlementFC settlement)
        {
            if (isUnderAttack
                && MilitaryUtilFC.ReturnMilitaryEventByLocation(settlement.Tile) is null
                && !attackers.Any() && !defenders.Any())
            {
                // Save taken mid-battle: event was removed from the queue but combatants are still
                // scribed. Leave the battle state alone (EndBattle will clear naturally on resolve).
                LogUtil.Warning($"Repairing stuck isUnderAttack flag on {settlement.Name} during load " +
                    $"(no matching settlementBeingAttacked event).");
                ClearAttackState();
            }

            // Orphan-DefendFriendlySettlement repair: catches deploys whose target was cleaned up
            // without notifying us (any code path that bypasses ClearAttackState's notify hook).
            if (militaryBusy
                && militaryJob == MilitaryJobDefOf.DefendFriendlySettlement
                && IsStaleDeploy())
            {
                LogUtil.Warning($"Clearing orphaned DefendFriendlySettlement on {settlement.Name} during load.");
                ReturnMilitary(false);
            }
        }

        // Shared by load-time and debug-force paths. Caller has already verified
        // militaryJob == DefendFriendlySettlement.
        public bool IsStaleDeploy()
        {
            // Tile-less deploy: SendMilitary sets job + location together, so this is broken state.
            if (militaryLocation == -1) return true;
            
            // Active warning event for the target; defense is actually in progress.
            if (MilitaryUtilFC.ReturnMilitaryEventByLocation(militaryLocation) is object) return false;
            
            // Target world object is gone (settlement destroyed, outpost despawned, etc.): stale.
            var targetComp = Find.WorldObjects.WorldObjectAt<WorldSettlementFC>(militaryLocation)?.MilitaryComp;
            if (targetComp is null) return true;
            
            // Target exists, no event, not under attack: stale.
            return !targetComp.isUnderAttack;
        }

        private void CooldownMilitary(int remaining, bool won)
        {
            if (defenderForce?.homeSettlement == WorldSettlement && defenderForce?.homeSettlement != null)
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
                    Find.LetterStack.ReceiveLetter("FCOverwhelmingVictory".Translate(), "FCOverwhelmingVictoryDesc".Translate(), LetterDefOf.PositiveEvent);
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
                    Find.LetterStack.ReceiveLetter("FCOverwhelmingVictory".Translate(), "FCOverwhelmingVictoryDesc".Translate(), LetterDefOf.PositiveEvent);
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

            // buildingDestructionChance stat scales the survival threshold:
            // stat=1.0 -> threshold 7 (36% destruction, default)
            // stat<1.0 -> higher threshold (less destruction)
            // stat>1.0 -> lower threshold (more destruction)
            double destructionStat = faction.GetStatValue(FCStatDefOf.buildingDestructionChance);
            int deconstructChance = Math.Max(0, Math.Min(11, (int)Math.Round(11 - 4 * destructionStat)));

            WorldSettlement.prosperity -= prosperityLoss;
            WorldSettlement.happiness -= happinessLoss;
            WorldSettlement.loyalty -= loyaltyLoss;

            string str = "FCDefenseFailureFull".Translate(WorldSettlement.Name);

            // Penalty summary
            str += "\n\n" + "FCDefenseFailurePenaltiesHeader".Translate();

            int displayProsperity = (int)Math.Round(prosperityLoss);
            int displayHappiness = (int)Math.Round(happinessLoss);
            int displayLoyalty = (int)Math.Round(loyaltyLoss);

            if (displayProsperity > 0)
            {
                str += "\n  - " + "FCDefenseFailureProsperityLoss".Translate(displayProsperity);
            }
            if (displayHappiness > 0)
            {
                str += "\n  - " + "FCDefenseFailureHappinessLoss".Translate(displayHappiness);
            }
            if (displayLoyalty > 0)
            {
                str += "\n  - " + "FCDefenseFailureLoyaltyLoss".Translate(displayLoyalty);
            }

            if (canDestroyBuildings && WorldSettlement?.BuildingsComp != null)
            {
                // Collect candidate slots for demolition
                List<int> candidates = new List<int>();
                for (var k = 0; k < 4; k++)
                {
                    var deconstructRoll = new IntRange(0, 10).RandomInRange;
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
                    bool aRequiresB = FactionCache.SatisfiesAnyRequirement(defB, defA.requiredBuildings);
                    bool bRequiresA = FactionCache.SatisfiesAnyRequirement(defA, defB.requiredBuildings);
                    if (aRequiresB) return -1; // a depends on b, demolish a first
                    if (bRequiresA) return 1;  // b depends on a, demolish b first
                    // Buildings with any requirements go before those without
                    int aReqCount = defA.requiredBuildings?.Count ?? 0;
                    int bReqCount = defB.requiredBuildings?.Count ?? 0;
                    return bReqCount.CompareTo(aReqCount);
                });

                foreach (int k in candidates)
                {
                    str += "\n  - " + "FCBuildingDestroyedInRaid".Translate(WorldSettlement.BuildingsComp.BuildingLabel(k));
                    WorldSettlement.DeconstructBuilding(k);
                }
            }

            if (!canDestroyBuildings)
            {
                str += "\n  - " + "FCDefenseFailureBuildingsProtected".Translate();
            }

            // level remover checker — uses same destruction stat scaling
            if (WorldSettlement?.settlementLevel > 1 && canDestroyBuildings)
            {
                var num = new IntRange(0, 10).RandomInRange;
                if (num >= deconstructChance)
                {
                    str += "\n  - " + "FCSettlementDeleveledRaid".Translate();
                    WorldSettlement.DelevelSettlement();
                }
            }

            if (!string.IsNullOrEmpty(pendingDeliveryMessage))
            {
                str += "\n\n" + pendingDeliveryMessage;
            }
            if (Map != null)
            {
                str += "\n\n" + "FCDefenseBattleOverLeaveMap".Translate();
            }
            Find.LetterStack.ReceiveLetter("FCDefenseFailure".Translate(), str, LetterDefOf.Death,
                new LookTargets(WorldSettlement));
        }

        private void WinBattle(FactionFC faction)
        {
            faction.AddExperienceToFactionLevel(5f);
            faction.threatAdaptation.Notify_BattleWon();
            string text = "FCDefenseSuccessfulFull".Translate(WorldSettlement.Name);
            if (!string.IsNullOrEmpty(pendingDeliveryMessage))
            {
                text += "\n\n" + pendingDeliveryMessage;
            }
            if (Map != null)
            {
                text += "\n\n" + "FCDefenseBattleOverLeaveMap".Translate();
            }
            Find.LetterStack.ReceiveLetter("FCDefenseSuccessful".Translate(),
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

            // Strip combat efficiency hediffs from surviving defenders (squad mercs persist between battles)
            foreach (Pawn defender in defenders)
            {
                if (defender != null && !defender.Dead && !defender.Destroyed)
                    MilitaryEfficiencyUtil.RemoveCombatEfficiencyHediff(defender);
            }

            DeleteMap(won);
            EndBattle(won, remaining);

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
            attackers.RemoveAll(p => p == null || p.Destroyed || !p.Spawned);
            if (attackers.Any() || endingBattle || !isUnderAttack) return;

            endingBattle = true;
            LongEventHandler.QueueLongEvent(EndAttack,
                "EndingAttack", false, error =>
                {
                    DelayedErrorWindowRequest.Add("FCErrorEndingAttack".Translate(),
                        "FCErrorEndingAttackDescription".Translate());
                    LogUtil.Error(error.Message);
                });
        }

        public void RemoveDefender(Pawn defender)
        {
            defenders.Remove(defender);
            defenders.RemoveAll(p => p == null || p.Destroyed || !p.Spawned);
            if (defenders.Any() || endingBattle || !isUnderAttack) return;

            endingBattle = true;
            LongEventHandler.QueueLongEvent(EndAttack,
                "EndingAttack", false, error =>
                {
                    DelayedErrorWindowRequest.Add("FCErrorEndingAttack".Translate(),
                        "FCErrorEndingAttackDescription".Translate());
                    LogUtil.Error(error.Message);
                });
        }

        public override void PostCaravanFormed(Caravan caravan)
        {
            foreach (var pawn in caravan.pawns)
            {
                var lord = pawn.GetLord();
                if (lord != null)
                    lord.Notify_PawnLost(pawn, PawnLostCondition.LeftVoluntarily);
                defenders.Remove(pawn);
            }

            if (Map is object)
                foreach (var pawn in caravan.pawns)
                    Map.reservationManager.ReleaseAllClaimedBy(pawn);

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
            if (militaryJob is null || militaryJob == MilitaryJobDefOf.Undefined || militaryJob == MilitaryJobDefOf.Cooldown)
            {
                LogUtil.Warning($"ProcessMilitaryEvent: {WorldSettlement.Name} has no active operation (job={militaryJob?.defName ?? "null"}). Skipping.");
                return;
            }

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
            if (!militaryBusy) return; // Already returned; duplicate cooldown event

            militaryBusy = false;
            militaryJob = MilitaryJobDefOf.Undefined;
            militaryLocation = -1;
            militaryEnemy = null;

            LifecycleRegistry.InvokeOnSquadRecalled(WorldSettlement);

            if (militarySquad != null)
                FactionCache.FactionComp?.militaryCustomizationUtil?.RegisterSquadInjuries(militarySquad);

            if (alert)
            {
                Find.LetterStack.ReceiveLetter("Military Cooldown", "FCMilitaryCooldown".Translate(WorldSettlement.Name),
                    LetterDefOf.PositiveEvent);
            }
        }

        public void CooldownMilitaryFinal(int battleDeaths = 0)
        {
            FactionFC faction = FactionCache.FactionComp;

            // Prevent duplicate cooldown events for the same settlement
            if (faction.HasEventWithDefAndLocation(FCEventDefOf.cooldownMilitary, WorldSettlement.Tile))
            {
                LogUtil.Warning($"CooldownMilitaryFinal: cooldownMilitary event already exists for {WorldSettlement.Name}. Skipping duplicate.");
                return;
            }

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
            if (DebugSettings.godMode) cooldown = 1;

            militaryJob = MilitaryJobDefOf.Cooldown;
            militaryBusy = true;
            militaryLocation = WorldSettlement.Tile;
            militaryEnemy = null;

            FCEvent tmp = FCEventMaker.MakeEvent(FCEventDefOf.cooldownMilitary);
            tmp.hasCustomDescription = true;
            tmp.timeTillTrigger = Find.TickManager.TicksGame + cooldown;
            tmp.location = WorldSettlement.Tile;
            tmp.customDescription = "FCMilitaryForcesReorganizing".Translate(WorldSettlement.Name); // + 
            FactionCache.FactionComp.AddEvent(tmp);
        }

        public bool IsMilitaryBusy(bool silent = false)
        {
            if (militaryBusy && !silent)
            {
                Messages.Message("FCMilitaryAlreadyAssigned".Translate(), MessageTypeDefOf.RejectInput);
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
                Messages.Message("FCTargetAlreadyBeingAttacked".Translate(), MessageTypeDefOf.RejectInput);
                return true;
            }

            return false;
        }
    }
}
