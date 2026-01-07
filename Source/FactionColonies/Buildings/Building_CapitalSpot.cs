using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    public class Building_CapitalSpot : Building
    {
        private bool isActiveCapitalSpot = false;
        private int lastKnownTile = -1; // Track the last known tile location
        
        public bool IsActiveCapitalSpot
        {
            get => isActiveCapitalSpot;
            set
            {
                if (value && isActiveCapitalSpot != value)
                {
                    // Disable other capital spots when enabling this one
                    DisableOtherCapitalSpots();
                    // Set this location as the empire capital
                    SetAsEmpireCapital();
                }
                isActiveCapitalSpot = value;
            }
        }

        private void DisableOtherCapitalSpots()
        {
            // Find all other capital spots and disable them
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                
                foreach (Building building in map.listerBuildings.allBuildingsColonist)
                {
                    if (building is Building_CapitalSpot otherCapitalSpot && otherCapitalSpot != this)
                    {
                        otherCapitalSpot.isActiveCapitalSpot = false;
                    }
                }
            }
        }

        private void SetAsEmpireCapital()
        {
            FactionFC faction = Find.World.GetComponent<FactionFC>();
            if (faction != null && Map != null)
            {
                int newTile = Map.Parent.Tile;
                faction.capitalLocation = newTile;
                faction.capitalPlanet = Find.World.info.name;
                lastKnownTile = newTile;
                
                // Handle SoS2 ship detection
                if (Map.Parent.def.defName == "ShipOrbiting")
                {
                    faction.SoSShipCapital = true;
                }
                else
                {
                    faction.SoSShipCapital = false;
                }
                
                Log.Message($"Capital Building: Set Empire capital to tile {newTile} (SoS2: {faction.SoSShipCapital})");
            }
            else
            {
                Log.Error($"Capital Building: Failed to set capital - faction={faction != null}, Map={Map != null}");
            }
        }

        // Check if the map tile has changed and update capital location accordingly
        private void UpdateCapitalLocationIfMoved()
        {
            if (isActiveCapitalSpot && Map != null)
            {
                int currentTile = Map.Parent.Tile;
                
                // Initialize lastKnownTile if it's not set (shouldn't happen but just in case)
                if (lastKnownTile == -1)
                {
                    lastKnownTile = currentTile;
                    Log.Message($"Capital Spot Debug: Initialized lastKnownTile to {currentTile}");
                }
                
                Log.Message($"Capital Spot Debug: Active={isActiveCapitalSpot}, CurrentTile={currentTile}, LastKnown={lastKnownTile}");
                
                if (lastKnownTile != currentTile)
                {
                    // The gravship has moved! Update the capital location
                    FactionFC faction = Find.World.GetComponent<FactionFC>();
                    if (faction != null)
                    {
                        int oldCapital = faction.capitalLocation;
                        faction.capitalLocation = currentTile;
                        faction.capitalPlanet = Find.World.info.name;
                        lastKnownTile = currentTile;
                        
                        // Handle SoS2 ship detection
                        if (Map.Parent.def.defName == "ShipOrbiting")
                        {
                            faction.SoSShipCapital = true;
                        }
                        else
                        {
                            faction.SoSShipCapital = false;
                        }
                        
                        Log.Message($"Empire capital location updated from {oldCapital} to {currentTile} (gravship moved)");
                        Log.Message($"Empire SoSShipCapital set to: {faction.SoSShipCapital}");
                        
                        Find.LetterStack.ReceiveLetter(
                            "Empire Relocated", 
                            "Your Empire has moved accordingly after your travels", 
                            LetterDefOf.NeutralEvent
                        );
                    }
                    else
                    {
                        Log.Error("Capital Spot Debug: FactionFC component not found!");
                    }
                }
            }
            else if (isActiveCapitalSpot)
            {
                Log.Message($"Capital Spot Debug: Active but Map is null!");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref isActiveCapitalSpot, "isActiveCapitalSpot", false);
            Scribe_Values.Look(ref lastKnownTile, "lastKnownTile", -1);
            
            // After loading, if this is the active capital spot but lastKnownTile is uninitialized, set it
            if (Scribe.mode == LoadSaveMode.PostLoadInit && isActiveCapitalSpot && lastKnownTile == -1 && Map != null)
            {
                lastKnownTile = Map.Parent.Tile;
            }
        }

        public override void TickRare()
        {
            base.TickRare();
            
            // TickRare runs every 250 ticks automatically, perfect for our needs
            UpdateCapitalLocationIfMoved();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            if (Faction.OfPlayer.IsPlayer)
            {
                yield return new Command_Toggle
                {
                    defaultLabel = "Set Empire Capital",
                    defaultDesc = isActiveCapitalSpot 
                        ? "This is currently your Empire's capital seat. Click to disable." 
                        : "Click to make this the seat of your Empire's capital. This location will be used for travel time calculations and event targeting.",
                    icon = TexLoad.iconCustomize, // Using existing customize icon
                    isActive = () => isActiveCapitalSpot,
                    toggleAction = () =>
                    {
                        bool wasActive = IsActiveCapitalSpot;
                        IsActiveCapitalSpot = !IsActiveCapitalSpot;
                        
                        Log.Message($"Capital Building: Toggle from {wasActive} to {IsActiveCapitalSpot}");
                        
                        if (IsActiveCapitalSpot)
                        {
                            Messages.Message(
                                $"Empire capital established at {Map.Parent.LabelCap}! This location will be used as the center of your empire.",
                                MessageTypeDefOf.PositiveEvent
                            );
                        }
                        else
                        {
                            Messages.Message(
                                "Capital seat disabled. Empire will use fallback capital location if available.",
                                MessageTypeDefOf.NeutralEvent
                            );
                        }
                    }
                };
            }
        }

        public override string GetInspectString()
        {
            string baseString = base.GetInspectString();
            string statusString = isActiveCapitalSpot 
                ? "Active Empire Capital" 
                : "Capital seat (inactive)";
            
            return string.IsNullOrEmpty(baseString) 
                ? statusString 
                : baseString + "\n" + statusString;
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            // If this was the active capital spot and it's being destroyed, clear the capital
            if (isActiveCapitalSpot)
            {
                FactionFC faction = Find.World.GetComponent<FactionFC>();
                if (faction != null)
                {
                    faction.capitalLocation = -1;
                    Messages.Message(
                        "Empire capital has been lost! You should establish a new capital seat.",
                        MessageTypeDefOf.NegativeEvent
                    );
                }
            }
            base.DeSpawn(mode);
        }
    }
}

