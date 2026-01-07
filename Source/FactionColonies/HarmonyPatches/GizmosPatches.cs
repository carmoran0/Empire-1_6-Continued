using System.Collections.Generic;
using System.Linq;
using FactionColonies.util;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FactionColonies
{
	/// Cached faction reference to avoid expensive lookups every frame
	internal static class FactionCache
	{
		private static Faction _cachedFaction;
		private static int _cacheFrame = -1;
		
		public static Faction PlayerColonyFaction
		{
			get
			{
				// Cache for one frame to handle hot reloads/game state changes
				int currentFrame = UnityEngine.Time.frameCount;
				if (_cacheFrame != currentFrame || _cachedFaction == null)
				{
					_cachedFaction = FactionColonies.getPlayerColonyFaction();
					_cacheFrame = currentFrame;
				}
				return _cachedFaction;
			}
		}
		
		public static void InvalidateCache()
		{
			_cachedFaction = null;
			_cacheFrame = -1;
		}
	}

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
			
			if (__instance.Faction == playerColonyFaction)
			{
				Pawn_DraftController pawnDraftController = __instance.drafter ?? new Pawn_DraftController(__instance);
				
				Command_Toggle draftColonists = new Command_Toggle
				{
					hotKey = KeyBindingDefOf.Command_ColonistDraft,
					isActive = () => false,
					toggleAction = () =>
					{
						if (pawnDraftController.pawn.Faction == Faction.OfPlayer) return;
						pawnDraftController.pawn.SetFaction(Faction.OfPlayer);
						pawnDraftController.Drafted = true;
					},
					defaultDesc = "CommandToggleDraftDesc".Translate(),
					icon = TexCommand.Draft,
					turnOnSound = SoundDefOf.DraftOn,
					groupKey = 81729172,
					defaultLabel = "CommandDraftLabel".Translate()
				};
				
				if (pawnDraftController.pawn.Downed)
				{
					draftColonists.Disable("IsIncapped".Translate(pawnDraftController.pawn.LabelShort, pawnDraftController.pawn));
				}
				
				draftColonists.tutorTag = "Draft";
				__result = __result.Append(draftColonists);
				return;
			}
			
			if (__instance.Faction == Faction.OfPlayer && __instance.Drafted)
			{
				// Check if pawn is in a supporting caravan (avoid LINQ closure allocations)
				Pawn found = __instance;
				bool isSupporting = false;
				foreach (var caravan in settlementFc.supporting)
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
							action.toggleAction = () => found.SetFaction(FactionColonies.getPlayerColonyFaction());
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

				List<FloatMenuOption> settlementList = Find.World.GetComponent<FactionFC>().settlements.Select(settlement => new FloatMenuOption("floatMenuOptionSendPrisonerToSettlement".Translate(settlement.name, settlement.settlementLevel, settlement.prisonerList.Count()), delegate
				{
					//disappear prisoner
					FactionColonies.sendPrisoner(prisoner, settlement);

					foreach (var bed in Find.Maps.Where(map => map.IsPlayerHome).SelectMany(map => map.listerBuildings.allBuildingsColonist).OfType<Building_Bed>().Where(bed => bed.OwnersForReading.Any(bedPawn => bedPawn == prisoner)))
					{
						bed.ForPrisoners = false;
						bed.ForPrisoners = true;
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
			
			if (!CanSendPrisoner(__instance)) return;

			__result = __result.Append(SendPrisonerAction(__instance));
		}
	}

	[HarmonyPatch(typeof(WorldObject), "GetGizmos")]
	class AddButtonsToNonEmpireObjects
	{
		private static readonly Dictionary<MilitaryJob, (string, string)> MilJobOptionStringsDic = new Dictionary<MilitaryJob, (string, string)> 
		{ 
			{ MilitaryJob.CaptureEnemySettlement, ("CaptureSettlement", "FCCaptureFloatMenuOption") },
			{ MilitaryJob.RaidEnemySettlement, ("RaidSettlement", "FCRaidFloatMenuOption") },
			{ MilitaryJob.EnslaveEnemySettlement, ("EnslavePopulation", "FCEnslaveFloatMenuOption") },
		};

		/// <summary>
		/// Checks if a <paramref name="settlement"/> has a currently usable military squad
		/// </summary>
		/// <param name="settlement"></param>
		/// <returns>true if usable, false otherwise</returns>
		private static bool SettlementHasUsableMilitary(SettlementFC settlement) => settlement.isMilitaryValid() && !settlement.militaryBusy;

		/// <summary>
		/// Takes a <paramref name="job"/> and generates a FloatMenuOptions using the strings in AddButtonsToNonEmpireObjects.MilJobOptionStringsDic
		/// </summary>
		/// <param name="factionFC"></param>
		/// <param name="faction"></param>
		/// <param name="tile"></param>
		/// <param name="job"></param>
		/// <returns>the generated FloatMenuOption</returns>
		private static FloatMenuOption NewOption(FactionFC factionFC, Faction faction, int tile, MilitaryJob job) => new FloatMenuOption((MilJobOptionStringsDic[job].Item1 ?? "FCUnsupportedMilJobError").Translate(), delegate
		{
			List<FloatMenuOption> settlementList = new List<FloatMenuOption>();

			foreach (SettlementFC settlement in factionFC.settlements)
			{
				if (SettlementHasUsableMilitary(settlement))
				{
					//if military is valid to use.

					settlementList.Add(new FloatMenuOption((MilJobOptionStringsDic[job].Item2 ?? "FCUnsupportedMilJobError").Translate(settlement.name, settlement.settlementMilitaryLevel), delegate
					{
						RelationsUtilFC.attackFaction(faction);
						settlement.SendMilitary(tile, Find.World.info.name, job, 60000, faction);
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

				if (!factionFC.hasPolicy(FCPolicyDefOf.isolationist)) list.Add(NewOption(factionFC, faction, tile, MilitaryJob.CaptureEnemySettlement));
				list.Add(NewOption(factionFC, faction, tile, MilitaryJob.RaidEnemySettlement));
				if (factionFC.hasPolicy(FCPolicyDefOf.authoritarian) && faction.def.defName != "VFEI_Insect") list.Add(NewOption(factionFC, faction, tile, MilitaryJob.EnslaveEnemySettlement));

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
			action = delegate { factionFC.sendDiplomaticEnvoy(faction); }
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
			FactionFC factionFC = Find.World.GetComponent<FactionFC>();

			if (factionFC.hasPolicy(FCPolicyDefOf.pacifist))
			{
				__result = __result.AddItem(PeacefulAction(factionFC, faction));
				return;
			}

			__result = __result.AddItem(HostileAction(factionFC, faction, tile));
		}
	}
}
