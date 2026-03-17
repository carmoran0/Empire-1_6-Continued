using System;
using System.Collections.Generic;
using System.Linq;
using FactionColonies.util;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FactionColonies
{
	/// <summary>
	/// Injects guard animals into PColony trader caravans when the faction has only non-violent xenotypes.
	/// Vanilla's guards list pipeline (GetOptions → XenotypesAvailableFor) was never designed for animal
	/// PawnKindDefs, so we bypass it and generate guard animals directly in a postfix.
	/// </summary>
	[HarmonyPatch(typeof(PawnGroupKindWorker_Trader))]
	[HarmonyPatch("GenerateGuards")]
	class TraderCaravanPatches
	{
		static void Postfix(PawnGroupMakerParms parms, List<Pawn> outPawns)
		{
			if (parms.faction != FactionCache.PlayerColonyFaction) return;

			XenotypeFilter filter = FactionCache.FactionComp?.xenotypeFilter;
			if (filter == null || !filter.OnlyNonViolentXenos) return;

			List<PawnKindDef> guardAnimals = filter.GuardAnimals;
			if (guardAnimals == null || !guardAnimals.Any()) return;

			int humanGuardCount = outPawns.Count(p => p.RaceProps.Humanlike);
			int animalCount = Math.Max(1, humanGuardCount / 2);

			for (int i = 0; i < animalCount; i++)
			{
				PawnKindDef animalKind = guardAnimals.RandomElement();
				PawnGenerationRequest request = FCPawnGenerator.AnimalRequest(animalKind);
				Pawn animal = PawnGenerator.GeneratePawn(request);
				if (animal != null)
				{
					outPawns.Add(animal);
				}
			}
		}
	}
}
