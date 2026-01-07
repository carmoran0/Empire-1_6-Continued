using System;
using System.Collections.Generic;
using System.Reflection;
using FactionColonies.util;
using RimWorld;
using Verse;

namespace FactionColonies
{
    class TraitUtilsFC
    {
        public static double returnVariable(string field, FCTraitEffectDef def)
        {
            if (def == null)
            {
                Log.Warning($"FCTraitEffectDef is null for field '{field}'");
                return 0.0;
            }
            
            Type typ = def.GetType();
            FieldInfo fieldInfo = typ.GetField(field);
            
            if (fieldInfo == null)
            {
                Log.Warning($"Field '{field}' not found on FCTraitEffectDef type '{typ.Name}'");
                return 0.0;
            }
            
            return (double) fieldInfo.GetValue(def);
        }

        public static double cycleTraits(string field, List<FCTraitEffectDef> traits, Operation addOrMultiply)
        {
            double tempTrait = (int) addOrMultiply;

            foreach (FCTraitEffectDef trait in traits)
            {
                if (addOrMultiply == Operation.Addition)
                {
                    tempTrait += returnVariable(field, trait);
                }
                else
                {
                    tempTrait *= returnVariable(field, trait);
                }
            }

            return tempTrait;
        }

        public static int returnResearchAmount()
        {
            int research = 0;
            research += Convert.ToInt32(cycleTraits("researchBaseProduction", Find.World.GetComponent<FactionFC>().traits, Operation.Addition));
            foreach (SettlementFC settlement in Find.World.GetComponent<FactionFC>().settlements)
            {
                research += Convert.ToInt32(cycleTraits("researchBaseProduction", settlement.traits, Operation.Addition));
            }
            return research;
        }
    }
}
