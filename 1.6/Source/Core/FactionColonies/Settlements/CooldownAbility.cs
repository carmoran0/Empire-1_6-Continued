using RimWorld;
using System;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Reusable cooldown tracker for policy abilities (Militaristic extra squad,
    /// Pacifist diplomat, Feudal mercenary request, etc.).
    /// Serializes its state via IExposable for save/load.
    /// </summary>
    public class CooldownAbility : IExposable
    {
        public int tickLastUsed = -1;
        public int cooldownTicks;

        /// <summary>Translation key for the "ability ready" letter. Null to skip.</summary>
        [Unsaved] public string readyLetterKey;

        /// <summary>Translation key for the "still on cooldown" message. Null to skip.</summary>
        [Unsaved] public string cooldownMessageKey;

        [Unsaved] private bool wasReady = true;

        public bool IsReady => DebugSettings.godMode || tickLastUsed < 0 || (tickLastUsed + cooldownTicks) <= Find.TickManager.TicksGame;

        public float DaysRemaining => IsReady
            ? 0f
            : (tickLastUsed + cooldownTicks - Find.TickManager.TicksGame) / (float)GenDate.TicksPerDay;

        public void Use()
        {
            tickLastUsed = Find.TickManager.TicksGame;
            wasReady = false;
        }

        /// <summary>
        /// Sets the cooldown duration from a base tick count, scaled by the faction-wide
        /// policyActionCooldownMultiplier stat. Call from a behavior's PostInitialize so changes to
        /// the multiplier are picked up on each policy init/load. Does not affect tickLastUsed (the live state).
        /// </summary>
        public void SetCooldown(int baseTicks)
        {
            double mult = FindFC.FactionComp?.GetStatValue(FCStatDefOf.policyActionCooldownMultiplier) ?? 1;
            cooldownTicks = (int)Math.Round(baseTicks * mult);
        }

        /// <summary>
        /// Call from Tick(). Sends a "ready" letter when the cooldown expires.
        /// </summary>
        public void TickCheckReady()
        {
            if (wasReady || !IsReady) return;
            wasReady = true;
            if (!readyLetterKey.NullOrEmpty())
            {
                Find.LetterStack.ReceiveLetter(
                    readyLetterKey.Translate(),
                    (readyLetterKey + "Desc").Translate(),
                    LetterDefOf.PositiveEvent);
            }
        }

        /// <summary>
        /// Try to use the ability. Returns true if ready (caller should proceed).
        /// Returns false and shows a cooldown message if still on cooldown.
        /// </summary>
        public bool TryUseOrShowCooldown()
        {
            if (IsReady) return true;
            if (!cooldownMessageKey.NullOrEmpty())
            {
                Messages.Message(
                    cooldownMessageKey.Translate(DaysRemaining.ToString("0.#")),
                    MessageTypeDefOf.RejectInput, false);
            }
            return false;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref tickLastUsed, "tickLastUsed", -1);
            Scribe_Values.Look(ref cooldownTicks, "cooldownTicks");
        }
    }
}
