using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;

namespace FactionColonies
{
    public static class AutoDefenderRegistry
    {
        private static readonly List<IAutoDefender> _defenders = new List<IAutoDefender>();

        public static void Register(IAutoDefender defender)
        {
            if (!_defenders.Contains(defender)) _defenders.Add(defender);
        }
        public static void Unregister(IAutoDefender defender) => _defenders.Remove(defender);
        public static void ClearAll() => _defenders.Clear();
        public static IReadOnlyList<IAutoDefender> Defenders => _defenders;

        /// <summary>
        /// Finds the strongest available <see cref="IAutoDefender"/> that can defend the given tile
        /// and is stronger than <paramref name="minMilitaryLevel"/>.
        /// </summary>
        public static IAutoDefender FindBestDefender(PlanetTile targetTile, int minMilitaryLevel)
        {
            IAutoDefender best = null;
            foreach (IAutoDefender defender in _defenders)
            {
                try
                {
                    if (!defender.CanAutoDefend) continue;
                    if (defender.MilitaryLevel <= minMilitaryLevel) continue;
                    int distance = Find.WorldGrid.TraversalDistanceBetween(defender.WorldObject.Tile, targetTile);
                    if (distance > defender.Range) continue;
                    if (best == null || defender.MilitaryLevel > best.MilitaryLevel)
                        best = defender;
                }
                catch (Exception e)
                {
                    LogUtil.Error($"IAutoDefender {defender.GetType().Name} threw in FindBestDefender: {e}");
                }
            }
            return best;
        }
        /// <summary>
        /// Finds the <see cref="IAutoDefender"/> wrapping the given <see cref="WorldObject"/>, or null.
        /// Used to notify an external auto-defender of battle completion.
        /// </summary>
        public static IAutoDefender FindByWorldObject(WorldObject obj)
        {
            if (obj == null) return null;
            foreach (IAutoDefender defender in _defenders)
            {
                try
                {
                    if (defender.WorldObject == obj) return defender;
                }
                catch (Exception e)
                {
                    LogUtil.Error($"IAutoDefender {defender.GetType().Name} threw in FindByWorldObject: {e}");
                }
            }
            return null;
        }
    }
}
