using System.Collections.Generic;
using System.Linq;
using FactionColonies.util;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
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

			Faction playerColonyFaction = FactionCache.PlayerColonyFaction;
			
			if (__instance.Faction == playerColonyFaction && !__instance.RaceProps.Animal)
			{
				Pawn pawn = __instance;

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
			
			if (__instance.Faction == Faction.OfPlayer && __instance.Drafted && settlementFc.MilitaryComp != null)
			{
				// Check if pawn is in a supporting caravan (avoid LINQ closure allocations)
				Pawn found = __instance;
				bool isSupporting = false;
				foreach (var caravan in settlementFc.MilitaryComp.supporting)
				{
					if (caravan.pawns.Contains(found))
					{
						isSupporting = true;
						break;
					}
				}
				
				if (!isSupporting)
				{
					// Only convert to list when we actually need to modify existing gizmos
					List<Gizmo> output = __result.ToList();
					
					foreach (Gizmo gizmo in output)
					{
						Command_Toggle action = gizmo as Command_Toggle;
						if (action != null && action.hotKey == KeyBindingDefOf.Command_ColonistDraft)
						{
							action.toggleAction = () =>
							{
								found.SetFaction(FactionCache.PlayerColonyFaction);
								// Re-add to defenders list and defense lord after undrafting
								var milComp = settlementFc.MilitaryComp;
								if (milComp != null && milComp.defenders.Any())
								{
									if (!milComp.defenders.Contains(found))
										milComp.defenders.Add(found);

									var defenderLord = milComp.defenders[0].GetLord();
									if (defenderLord != null && !defenderLord.ownedPawns.Contains(found))
									{
										defenderLord.AddPawn(found);
										defenderLord.CurLordToil.UpdateAllDuties();
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

	[HarmonyPatch(typeof(Pawn), "GetGizmos")]
	class PrisonerGizmosPatch
	{
		/// <summary>
		/// Checks if pawn is a valid prisoner that can be sent to settlements.
		/// Optimized to avoid expensive quest gizmo enumeration when possible.
		/// </summary>
		private static bool CanSendPrisoner(Pawn pawn)
		{
			// Fast checks first
			if (pawn.guest == null) return false;
			if (!pawn.guest.IsPrisoner) return false;
			if (!pawn.guest.PrisonerIsSecure) return false;
			
			// Only do expensive quest check if basic checks pass
			return QuestUtility.GetQuestRelatedGizmos(pawn).EnumerableNullOrEmpty();
		}

		/// <param name="prisoner"></param>
		/// <returns>A <c>Command_Action</c> that sends the selected <paramref name="prisoner"/> to an empire settlementFC.</returns>
		/// TODO: Replace this with needing to actually send the prisoner to the settlement via caravan or droppod? At the very least, the transfer shouldn't be instantaneous
		private static Command_Action SendPrisonerAction(Pawn prisoner) => new Command_Action
		{
			defaultLabel = "SendToSettlement".Translate(),
			defaultDesc = "",
			icon = TexLoad.iconMilitary,
			action = delegate
			{
				if (prisoner.Map.dangerWatcher.DangerRating != StoryDanger.None)
				{
					Messages.Message("cantSendWithDangerLevel".Translate(prisoner.Map.dangerWatcher.DangerRating.ToString()), MessageTypeDefOf.RejectInput);
					return;
				}

				List<FloatMenuOption> settlementList = FactionCache.FactionComp.settlements.Select(settlement => new FloatMenuOption("floatMenuOptionSendPrisonerToSettlement".Translate(settlement.Name, settlement.settlementLevel, settlement.prisonerList.Count()), delegate
				{
					//disappear prisoner
					TravelUtil.SendPrisoner(prisoner, settlement);

					foreach (var bed in Find.Maps.Where(map => map.IsPlayerHome).SelectMany(map => map.listerBuildings.allBuildingsColonist).OfType<Building_Bed>().Where(bed => bed.OwnersForReading.Any(bedPawn => bedPawn == prisoner)))
					{
						bed.ForOwnerType = BedOwnerType.Colonist;
						bed.ForOwnerType = BedOwnerType.Prisoner;
					}
				})).ToList();

				Find.WindowStack.Add(new FloatMenu(settlementList));
			}
		};

		public static void Postfix(ref Pawn __instance, ref IEnumerable<Gizmo> __result)
		{
			// Early exit for non-prisoners (most common case) hmmmm
			if (__instance.guest == null || !__instance.guest.IsPrisoner)
			{
				return;
			}

            if (!FactionCache.FactionComp.IsActionAllowed(FCActionType.SendPrisoner)) return;
            if (!CanSendPrisoner(__instance)) return;

			__result = __result.Append(SendPrisonerAction(__instance));
		}
	}

	[HarmonyPatch(typeof(WorldObject), "GetGizmos")]
	class AddButtonsToNonEmpireObjects
	{
		/// <summary>
		/// Checks if a <paramref name="settlement"/> has a currently usable military squad
		/// </summary>
		/// <param name="settlement"></param>
		/// <returns>true if usable, false otherwise</returns>
		private static bool SettlementHasUsableMilitary(WorldSettlementFC settlement) => settlement.MilitaryComp != null && settlement.MilitaryComp.IsMilitaryValid() && !settlement.MilitaryComp.militaryBusy;

		/// <summary>
		/// Takes a <paramref name="job"/> and generates a FloatMenuOption using the job def's label/desc keys.
		/// </summary>
		private static FloatMenuOption NewOption(FactionFC factionFC, Faction faction, int tile, MilitaryJobDef job) => new FloatMenuOption((job.floatMenuLabelKey ?? "FCUnsupportedMilJobError").Translate(), delegate
		{
			List<FloatMenuOption> settlementList = new List<FloatMenuOption>();

			foreach (WorldSettlementFC settlement in factionFC.settlements)
			{
				if (SettlementHasUsableMilitary(settlement))
				{
					settlementList.Add(new FloatMenuOption((job.floatMenuDescKey ?? "FCUnsupportedMilJobError").Translate(settlement.Name, settlement.settlementMilitaryLevel), delegate
					{
						RelationsUtilFC.AttackFaction(faction);
						settlement.MilitaryComp?.SendMilitary(tile, job, 60000, faction);
					}));
				}
			}

			if (settlementList.Count == 0) settlementList.Add(new FloatMenuOption("NoValidMilitaries".Translate(), null));

			Find.WindowStack.Add(new FloatMenu(settlementList));
		});

		/// <param name="factionFC"></param>
		/// <param name="faction"></param>
		/// <param name="tile"></param>
		/// <returns>A <c>Command_Action</c> that creates a <c>FloatMenu</c> displaying hostile actions a player can take against a settlement</returns>
		private static Command_Action HostileAction(FactionFC factionFC, Faction faction, int tile) => new Command_Action
		{
			defaultLabel = "AttackSettlement".Translate(faction.HasName ? faction.Name : "FCUnsupportedSettlementFaction".Translate().ToString()),
			defaultDesc = "",
			icon = TexLoad.iconMilitary,
			action = delegate
			{
				List<FloatMenuOption> list = new List<FloatMenuOption>();

				foreach (MilitaryJobDef job in FactionCache.HostileMilitaryJobs)
				{
					if (!factionFC.IsMilitaryJobAllowed(job)) continue;
					if (job.Handler != null && !job.Handler.IsValidTarget(faction)) continue;
					list.Add(NewOption(factionFC, faction, tile, job));
				}

				if (list.Count == 0) list.Add(new FloatMenuOption("NoValidMilitaries".Translate(), null));
				Find.WindowStack.Add(new FloatMenu(list));
			}
		};

		/// <param name="factionFC"></param>
		/// <param name="faction"></param>
		/// <returns>A <c>Command_Action</c> that sends a diplomatic envoy if used</returns>
		private static Command_Action PeacefulAction(FactionFC factionFC, Faction faction) => new Command_Action
		{
			defaultLabel = "FCIncreaseRelations".Translate(),
			defaultDesc = "",
			icon = TexLoad.iconProsperity,
			action = delegate { factionFC.SendDiplomaticEnvoy(faction); }
		};

		/// <summary>
		/// Checks if a worldObject is not part of the player or their empire faction
		/// </summary>
		/// <param name="worldObject"></param>
		/// <returns>true if the faction linked isn't from the player or their empire faction, false otherwise</returns>
		private static bool HasValidFaction(WorldObject worldObject) => worldObject.Faction != FactionCache.PlayerColonyFaction && worldObject.Faction != Find.FactionManager.OfPlayer;

		/// <summary>
		/// This Postfix adds Gizmos on settlements not owned by the player or their empire
		/// </summary>
		/// <param name="__instance"></param>
		/// <param name="__result"></param>
		public static void Postfix(ref WorldObject __instance, ref IEnumerable<Gizmo> __result)
		{
			if (__instance.def.defName != "Settlement") return;
			if (!HasValidFaction(__instance)) return;
			
			int tile = __instance.Tile;
			Faction faction = __instance.Faction;
			FactionFC factionFC = FactionCache.FactionComp;

			if (factionFC.IsActionAllowed(FCActionType.SendDiplomat))
				__result = __result.AddItem(PeacefulAction(factionFC, faction));

			if (factionFC.IsActionAllowed(FCActionType.DeployMilitary))
				__result = __result.AddItem(HostileAction(factionFC, faction, tile));
		}
	}
}
