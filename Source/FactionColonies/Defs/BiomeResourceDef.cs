using System.Collections.Generic;
using RimWorld;
using Verse;

namespace FactionColonies
{
    public class BiomeResourceDef : Def, IExposable
    {
        public List<double> BaseProductionAdditive = new List<double>();
        public List<double> BaseProductionMultiplicative = new List<double>();
        public bool canSettle;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref BaseProductionAdditive, "BaseProductionAdditive", LookMode.Value);
            Scribe_Collections.Look(ref BaseProductionMultiplicative, "BaseProductionMultiplicative", LookMode.Value);
            Scribe_Values.Look(ref canSettle, "canSettle");
        }

        public void EnsureResourceLists()
        {
            // Ensure both lists have exactly 11 elements (one for each resource type) We should be able to increase it further in future...
            const int resourceCount = 11;
            
            while (BaseProductionAdditive.Count < resourceCount)
            {
                BaseProductionAdditive.Add(0.0);
            }
            while (BaseProductionAdditive.Count > resourceCount)
            {
                BaseProductionAdditive.RemoveAt(BaseProductionAdditive.Count - 1);
            }
            
            while (BaseProductionMultiplicative.Count < resourceCount)
            {
                BaseProductionMultiplicative.Add(1.0);
            }
            while (BaseProductionMultiplicative.Count > resourceCount)
            {
                BaseProductionMultiplicative.RemoveAt(BaseProductionMultiplicative.Count - 1);
            }
        }
    }


    [DefOf]
    public class BiomeResourceDefOf
    {
        public static BiomeResourceDef defaultBiome;
        static BiomeResourceDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(BiomeResourceDefOf));
        }
    }
}
