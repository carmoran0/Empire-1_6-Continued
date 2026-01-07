using System;
using Verse;

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
    }
}
