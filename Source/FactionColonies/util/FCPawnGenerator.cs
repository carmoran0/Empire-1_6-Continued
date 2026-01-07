using System;
using System.Collections.Generic;
using RimWorld;
using System.Linq;
using System.Reflection.Emit;
using Verse;

namespace FactionColonies.util
{
	
	class FCPawnGenerator
	{
		public Pawn defaultPawn;
		public XenotypeDef xenotype;
		
		// List of pawn kinds known to commonly generate violence-incapable pawns
		private static readonly HashSet<string> problematicPawnKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			// Civilian/non-combat pawns
			"Beggar", "Villager", "Ghoul", "Hunter", "Slave", "WildMan", 
			"SpaceRefugee", "Refugee", "Drifter", "AncientSoldier",
			// Other problematic types
			"Farmer", "Trader", "Minstrel", "Hermit", "Pilgrim", "Monk",
			"Child", "Baby", "Newborn", "StrangerInBlack"
		};
		
		/// <summary>
		/// Check if pawn kind name contains problematic patterns
		/// </summary>
		private static bool HasProblematicPattern(string defName)
		{
			if (string.IsNullOrEmpty(defName)) return true;
			
			string lower = defName.ToLower();
			// Filter out child pawns, tribal variants that often fail, and other problematic patterns
			return lower.Contains("child") || 
			       lower.Contains("baby") || 
			       lower.Contains("newborn") ||
			       lower.Contains("_child") ||
			       lower.Contains("slave") ||
			       lower.Contains("refugee") ||
			       lower.Contains("beggar") ||
			       lower.Contains("hermit") ||
			       lower.Contains("pilgrim");
		}
		
		// List of pawn kinds that are reliable for military use
		private static readonly List<string> preferredMilitaryKinds = new List<string>
		{
			"Colonist", "Mercenary", "Fighter", "Soldier", "Pirate", "Grenadier",
			"SpaceSoldier", "EliteMercenary", "TownGuard", "Janissary"
		};
		
		/// <summary>
		/// Check if a pawn kind is likely to produce violence-capable pawns
		/// </summary>
		public static bool IsViolenceCapablePawnKind(PawnKindDef kindDef)
		{
			if (kindDef == null) return false;
			
			// Check if it's in the problematic list
			if (problematicPawnKinds.Contains(kindDef.defName))
			{
				return false;
			}
			
			// Check for problematic patterns in the name
			if (HasProblematicPattern(kindDef.defName))
			{
				return false;
			}
			
			// Check combat power - very low combat power suggests non-combat pawn
			if (kindDef.combatPower < 30f)
			{
				return false;
			}
			
			return true;
		}
		
		/// <summary>
		/// Get a violence-capable pawn kind, with fallbacks
		/// </summary>
		public static PawnKindDef GetViolenceCapablePawnKind(Faction faction)
		{
			if (faction?.def?.pawnGroupMakers == null)
			{
				return PawnKindDefOf.Colonist;
			}
			
			// Try to find a violence-capable pawn kind from the faction
			var allKinds = faction.def.pawnGroupMakers
				.Where(pgm => pgm.options != null)
				.SelectMany(pgm => pgm.options)
				.Select(opt => opt.kind)
				.Where(k => k != null && IsViolenceCapablePawnKind(k))
				.ToList();
			
			if (allKinds.Any())
			{
				return allKinds.RandomElement();
			}
			
			// Try preferred military kinds from the database
			foreach (var kindName in preferredMilitaryKinds)
			{
				var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(kindName);
				if (kind != null && IsViolenceCapablePawnKind(kind))
				{
					return kind;
				}
			}
			
			// Final fallback
			return PawnKindDefOf.Colonist;
		}

		public static PawnGenerationRequest WorkerOrMilitaryRequest(PawnKindDef pawnKindDef = null, XenotypeDef xenotypeDef = null)
        {
			var kindDef = pawnKindDef;
			
			// Validate the provided pawn kind for violence capability
			if (kindDef != null && !IsViolenceCapablePawnKind(kindDef))
			{
				kindDef = null; // Force fallback - skip problematic pawn kinds silently
			}
			
			if (kindDef == null)
			{
				try
				{
					var tempFaction = FactionColonies.getPlayerColonyFaction();
					kindDef = GetViolenceCapablePawnKind(tempFaction);
				}
				catch (Exception ex)
				{
					Log.Warning($"Empire: Failed to get pawn kind from player faction: {ex.Message}");
				}
				
				// Fallback to default colonist if still null
				if (kindDef == null)
				{
					kindDef = PawnKindDefOf.Colonist;
				}
			}
			
			var factionFC = Find.World.GetComponent<FactionFC>();
			
			// For military pawns, we need to ensure violence capability
			// Check if the xenotype needs security guards (is non-violent)
			XenotypeDef chosenXenotype = xenotypeDef;
			bool needsSecurityGuards = false;
			
			if (chosenXenotype == null)
			{
				// For military, always use Baseliner unless user specifically requested a xenotype
				// This avoids faction xenotype forcing that causes violence-incapable pawns
				chosenXenotype = XenotypeDefOf.Baseliner;
			}
			else
			{
				// Check if the requested xenotype allows violence
				needsSecurityGuards = factionFC?.xenotypeFilter?.XenotypeNeedsSecurityGuards(chosenXenotype) ?? false;
				
				// If the xenotype is non-violent and we need violence, fall back to Baseliner
				if (needsSecurityGuards)
				{
					chosenXenotype = XenotypeDefOf.Baseliner;
					needsSecurityGuards = false;
				}
			}
			
			// Get a safe age value
			float? fixedAge = null;
			try
			{
				fixedAge = kindDef?.GetReasonableMercenaryAge();
			}
			catch (Exception ex)
			{
				Log.Warning($"Empire: Failed to get reasonable age for {kindDef?.defName}: {ex.Message}");
				fixedAge = null; // Let the game decide the age
			}
			
			// IMPORTANT: Use null faction to prevent faction xenotype forcing
			// The pawn's faction will be set after generation
			return new PawnGenerationRequest(
				kind: kindDef,
				faction: null, // NO faction - prevents faction xenotype requirements
				context: PawnGenerationContext.NonPlayer,
				tile: -1,
				forceGenerateNewPawn: false,
				allowDead: false,
				allowDowned: false,
				canGeneratePawnRelations: false, // No relations for factionless pawn
				mustBeCapableOfViolence: true, // Always require violence for military
				colonistRelationChanceFactor: 0,
				forceAddFreeWarmLayerIfNeeded: false,
				allowGay: true,
				allowFood: true,
				allowAddictions: false,
				inhabitant: false,
				certainlyBeenInCryptosleep: false,
				forceRedressWorldPawnIfFormerColonist: false,
				worldPawnFactionDoesntMatter: true, // Allow any world pawn
				biocodeWeaponChance: 0,
				extraPawnForExtraRelationChance: null,
				relationWithExtraPawnChanceFactor: 0,
				validatorPreGear: null,
				validatorPostGear: null,
				forcedTraits: null,
				prohibitedTraits: null,
				forcedXenotype: chosenXenotype,
				fixedBiologicalAge: fixedAge
			);
		}

		public static PawnGenerationRequest CivilianRequest(PawnKindDef pawnKindDef = null, XenotypeDef xenotypeDef = null)
		{
			var kindDef = pawnKindDef;
			if (kindDef == null)
			{
				try
				{
					kindDef = FactionColonies.getPlayerColonyFaction()?.RandomPawnKind();
				}
				catch (Exception ex)
				{
					Log.Warning($"Empire: Failed to get pawn kind from player faction: {ex.Message}");
				}
				
				// Fallback to default colonist if still null
				if (kindDef == null)
				{
					kindDef = PawnKindDefOf.Colonist;
				}
			}
			
			var faction = FactionColonies.getPlayerColonyFaction();
			if (faction == null)
			{
				faction = Faction.OfPlayer; // Fallback to player faction
			}
			
			var factionFC = Find.World.GetComponent<FactionFC>();
			
			// If no specific xenotype is requested, select from allowed xenotypes
			if (xenotypeDef == null)
			{
				if (factionFC?.xenotypeFilter != null && factionFC.xenotypeFilter.AllowedXenotypes.Any())
				{
					xenotypeDef = factionFC.xenotypeFilter.AllowedXenotypes.RandomElement();
				}
				else
				{
					xenotypeDef = XenotypeDefOf.Baseliner; // Fallback to default
				}
			}
			
			// Check if the xenotype needs security guards (is non-violent)
			bool needsSecurityGuards = factionFC?.xenotypeFilter?.XenotypeNeedsSecurityGuards(xenotypeDef) ?? false;
			
			// Get a safe age value
			float? fixedAge = null;
			try
			{
				fixedAge = kindDef?.GetReasonableMercenaryAge();
			}
			catch (Exception ex)
			{
				Log.Warning($"Empire: Failed to get reasonable age for {kindDef?.defName}: {ex.Message}");
				fixedAge = null; // Let the game decide the age
			}
			
			return new PawnGenerationRequest(
				kind: kindDef,
				faction: faction,
				context: PawnGenerationContext.NonPlayer,
				tile: -1,
				forceGenerateNewPawn: false,
				allowDead: false,
				allowDowned: false,
				canGeneratePawnRelations: true,
				mustBeCapableOfViolence: !needsSecurityGuards, // Allow non-violent pawns if they have security guards
				colonistRelationChanceFactor: 0,
				forceAddFreeWarmLayerIfNeeded: false,
				allowGay: true,
				allowFood: true,
				allowAddictions: false,
				inhabitant: false,
				certainlyBeenInCryptosleep: false,
				forceRedressWorldPawnIfFormerColonist: false,
				worldPawnFactionDoesntMatter: false,
				biocodeWeaponChance: 0,
				extraPawnForExtraRelationChance: null,
				relationWithExtraPawnChanceFactor: 0,
				validatorPreGear: null,
				validatorPostGear: null,
				forcedTraits: null,
				prohibitedTraits: null,
				forcedXenotype: xenotypeDef,
				fixedBiologicalAge: fixedAge,
				fixedChronologicalAge: fixedAge
			);
		}

		public static PawnGenerationRequest AnimalRequest(PawnKindDef race)
		{
			var faction = FactionColonies.getPlayerColonyFaction();
			
			return new PawnGenerationRequest(
				kind: race,
				faction: faction,
				context: PawnGenerationContext.NonPlayer,
				tile: -1,
				forceGenerateNewPawn: false,
				allowDead: false,
				allowDowned: false,
				canGeneratePawnRelations: true,
				mustBeCapableOfViolence: false,
				colonistRelationChanceFactor: 0,
				forceAddFreeWarmLayerIfNeeded: false,
				allowGay: true,
				allowFood: true,
				allowAddictions: false,
				inhabitant: false,
				certainlyBeenInCryptosleep: false,
				forceRedressWorldPawnIfFormerColonist: false,
				worldPawnFactionDoesntMatter: false,
				biocodeWeaponChance: 0,
				extraPawnForExtraRelationChance: null,
				relationWithExtraPawnChanceFactor: 0,
				validatorPreGear: null,
				validatorPostGear: null,
				forcedTraits: null,
				prohibitedTraits: null,
				fixedBiologicalAge: race.GetReasonableMercenaryAge()
			);
		}

		/// <summary>
		/// Generate a simple delivery pawn that bypasses xenotype filtering issues
		/// </summary>
		public static PawnGenerationRequest SimpleDeliveryRequest()
		{
			var faction = FactionColonies.getPlayerColonyFaction();
			if (faction == null)
			{
				faction = Faction.OfPlayer; // Fallback to player faction
			}
			
			return new PawnGenerationRequest(
				kind: PawnKindDefOf.Colonist,
				faction: faction,
				context: PawnGenerationContext.NonPlayer,
				tile: -1,
				forceGenerateNewPawn: false,
				allowDead: false,
				allowDowned: false,
				canGeneratePawnRelations: true,
				mustBeCapableOfViolence: true, // Always capable of violence for delivery
				colonistRelationChanceFactor: 0,
				forceAddFreeWarmLayerIfNeeded: false,
				allowGay: true,
				allowFood: true,
				allowAddictions: false,
				inhabitant: false,
				certainlyBeenInCryptosleep: false,
				forceRedressWorldPawnIfFormerColonist: false,
				worldPawnFactionDoesntMatter: false,
				biocodeWeaponChance: 0,
				extraPawnForExtraRelationChance: null,
				relationWithExtraPawnChanceFactor: 0,
				validatorPreGear: null,
				validatorPostGear: null,
				forcedTraits: null,
				prohibitedTraits: null,
				forcedXenotype: XenotypeDefOf.Baseliner // Force baseliner to avoid xenotype issues
			);
		}
	}
}

