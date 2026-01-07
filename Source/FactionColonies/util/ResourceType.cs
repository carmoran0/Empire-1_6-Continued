using System;
using Verse;
using Verse.Sound;

namespace FactionColonies
{
    public enum ResourceType
    {
        Food,
        Weapons,
        Apparel,
        Animals,
        Logging,
        Mining,
        Research,
        Power,
        Medicine,
        // New orbital-specific resources independent from overloads
        Gravtech,
        Chemfuel
    }

    public static class ResourceUtils
    {
        public static ResourceType[] resourceTypes = (ResourceType[]) Enum.GetValues(typeof(ResourceType));
        
        public static ResourceType getTypeFromName(String name)
        {
            int index = Array.FindIndex(Enum.GetNames(typeof(ResourceType)), 
                foundName => foundName.EqualsIgnoreCase(name));
            
            if (index == -1)
            {
                Log.Warning("Unknown resource type " + name);
            }

            return resourceTypes[index];
        }
        
        // Check if orbital platform
        public static bool IsOrbitalPlatform(SettlementFC settlement)
        {
            return settlement?.worldSettlement?.def?.defName == "FCOrbitalPlatform";
        }
        
        // Get the display name for a resource type
        public static string GetResourceDisplayName(ResourceType resourceType, SettlementFC settlement)
        {
            return resourceType.ToString().ToLower();
        }
        
        // Get the display label for a resource type
        public static string GetResourceDisplayLabel(ResourceType resourceType, SettlementFC settlement)
        {
            return resourceType.ToString();
        }
        
        // Get available resource types for a settlement
        public static ResourceType[] GetAvailableResourceTypes(SettlementFC settlement)
        {
            if (IsOrbitalPlatform(settlement))
            {
                return new ResourceType[] {
                    ResourceType.Food,
                    ResourceType.Weapons,
                    ResourceType.Apparel,
                    ResourceType.Animals,
                    ResourceType.Logging,
                    ResourceType.Mining,
                    ResourceType.Research,
                    ResourceType.Power,
                    ResourceType.Medicine,
                    ResourceType.Gravtech,
                    ResourceType.Chemfuel
                };
            }
            
            return new ResourceType[] {
                ResourceType.Food,
                ResourceType.Weapons,
                ResourceType.Apparel,
                ResourceType.Animals,
                ResourceType.Logging,
                ResourceType.Mining,
                ResourceType.Research,
                ResourceType.Power,
                ResourceType.Medicine
            };
        }

        /* Replaces a switch/case on the enum. This is probably more expensive, but will be better able to flag errors */
        /* resourceType is just an enum, so we *should* be able to just straight cast to int... but the original switch/case had a comment about crashes,
         * so this function exists just to be super careful */
        public static int TypeToInt(ResourceType resourceType, SettlementFC settlement)
        {
            int resourceIndex = (int)resourceType;
            if (resourceIndex < 0)
            {
                Logger.DebugLog($"detected less-than-zero ResourceType for settlement {settlement.name}, setting to 0", LogMessageType.Error);
                return 0;
            }
            else if (!IsOrbitalPlatform(settlement) && resourceIndex > 8)
            {
                Logger.DebugLog($"detected invalid ResourceType {resourceIndex} for non-orbital platform settlement {settlement.name}, setting to 0", LogMessageType.Error);
                return 0;
            }
            else if (resourceIndex > 10)
            {
                Logger.DebugLog($"detected invalid ResourceType {resourceIndex} for orbital platform settlement {settlement.name}, setting to 0", LogMessageType.Error);
                return 0;
            }
            return resourceIndex;
        }
    }
}
