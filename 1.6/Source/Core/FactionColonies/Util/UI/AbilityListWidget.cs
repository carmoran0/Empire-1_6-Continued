using FactionColonies.util;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /* Shared abilities/psycasts panel for the unit designer's Abilities tab. A psylink-level stepper at
     * the top gates how powerful the unit's psycasts are. Below it the panel adapts to the active ability
     * system:
     *   - VPE (SupportsExplicitSelection): an "Edit Psycasts" button opens VPE's own picker window, and a
     *     read-only list shows the chosen psycasts with cost.
     *   - Base game (Royalty): no picker — a note explains psycasts are granted randomly at this psylink
     *     level when the unit deploys (vanilla behavior). */
    public static class AbilityListWidget
    {
        public struct Options
        {
            public bool canEdit;
            public bool showHeaderButtons;
            public Func<MilUnitFC> getEditTarget;
            public Func<MilUnitFC> getDisplayUnit;
        }

        private const float headerHeight = 25f;
        private const float rowHeight = 28f;
        private const float IconSize = 24f;
        private const float stepperButtonW = 24f;

        public static void Draw(Rect rect, MilUnitFC displayUnit, ref Vector2 scrollPos, Options opts)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            IAbilitySystemProvider active = AbilitySystemRegistry.Active;

            // No ability system available — explain and bail.
            if (active is null)
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "fcAbilitiesNoSystem".Translate());
                Text.Font = fontBefore;
                Text.Anchor = anchorBefore;
                return;
            }

            MilUnitFC target = opts.getEditTarget?.Invoke();

            // --- Psylink stepper row ---
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, headerHeight);
            int maxLevel = active.MaxPsylinkLevel;
            int curLevel = displayUnit?.psylinkLevel ?? 0;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect psyLabelRect = new Rect(headerRect.x, headerRect.y, 120f, headerHeight);
            Widgets.Label(psyLabelRect, "fcPsylinkLevel".Translate() + ": " + curLevel);

            bool editable = opts.canEdit && opts.showHeaderButtons && target != null;
            if (editable)
            {
                Rect minusRect = new Rect(psyLabelRect.xMax, headerRect.y + (headerHeight - stepperButtonW) / 2f, stepperButtonW, stepperButtonW);
                Rect plusRect = new Rect(minusRect.xMax + 2f, minusRect.y, stepperButtonW, stepperButtonW);
                Text.Anchor = TextAnchor.MiddleCenter;
                if (Widgets.ButtonText(minusRect, "-") && curLevel > 0)
                    target.SetPsylinkLevel(curLevel - 1);
                if (Widgets.ButtonText(plusRect, "+") && curLevel < maxLevel)
                    target.SetPsylinkLevel(curLevel + 1);

                // VPE: "Edit Psycasts" button (right-aligned). Base game: none.
                if (active.SupportsExplicitSelection)
                {
                    float btnW = 130f;
                    Rect editBtnRect = new Rect(headerRect.xMax - btnW, headerRect.y, btnW, headerHeight);
                    bool canEditAbilities = curLevel > 0;
                    if (canEditAbilities)
                    {
                        if (Widgets.ButtonText(editBtnRect, "fcEditAbilities".Translate()))
                            active.OpenEditor(target, delegate { target.ChangeTick(); });
                    }
                    else
                    {
                        GUI.color = Color.gray;
                        Widgets.ButtonText(editBtnRect, "fcEditAbilities".Translate(), active: false);
                        GUI.color = Color.white;
                        TooltipHandler.TipRegion(editBtnRect, "fcAbilitiesNeedPsylink".Translate());
                    }
                }
            }

            Rect bodyRect = new Rect(rect.x, headerRect.yMax + 2f, rect.width, rect.height - headerHeight - 4f);

            // --- Base game: explanatory note, no list ---
            if (!active.SupportsExplicitSelection)
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(bodyRect.ContractedBy(4f), "fcAbilitiesRandomNote".Translate());
                Text.Font = fontBefore;
                Text.Anchor = anchorBefore;
                return;
            }

            // --- VPE: point summary + read-only list of chosen psycasts + Clear ---
            int spent, budget;
            if (active.TryGetPointBudget(displayUnit, out spent, out budget))
            {
                Rect summaryRect = new Rect(bodyRect.x, bodyRect.y, bodyRect.width, 18f);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(summaryRect, "fcAbilityPointsSummary".Translate(spent, budget));
                bodyRect.yMin += 20f;
            }

            // Reserve a bottom strip for the Clear button (only when this unit is editable).
            bool canClear = opts.canEdit && opts.showHeaderButtons && target != null;
            if (canClear)
            {
                Rect clearRect = new Rect(bodyRect.x, bodyRect.yMax - 26f, 120f, 24f);
                bodyRect.height -= 30f;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                if (Widgets.ButtonText(clearRect, "fcClearAbilities".Translate()))
                    target.SetAbilitiesForSystem(active.Key, new List<SavedAbility>());
            }

            // Display picks in their stored order — i.e. the order they were chosen — so the list reads
            // the same way trimming removes them (most recent last).
            var items = displayUnit?.abilities;
            int count = items?.Count ?? 0;
            float viewHeight = count * rowHeight;
            Rect scrollViewRect = ScrollUtil.BeginScrollView(bodyRect, ref scrollPos, viewHeight);

            for (int i = 0; i < count; i++)
            {
                SavedAbility item = items[i];
                IAbilitySystemProvider provider = AbilitySystemRegistry.ByKey(item.systemKey);
                AbilityPickEntry entry = null;
                bool resolved = provider is object && provider.TryGetDisplay(item, out entry);
                Rect row = new Rect(scrollViewRect.x, scrollViewRect.y + i * rowHeight, scrollViewRect.width, rowHeight);
                if (i % 2 == 0) Widgets.DrawHighlight(row);

                Rect iconRect = new Rect(row.x + 2f, row.y + 2f, IconSize, IconSize);
                if (resolved && entry.icon != null)
                    GUI.DrawTexture(iconRect, entry.icon);

                Rect costRect = new Rect(row.xMax - 4f - 60f, row.y, 60f, rowHeight);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                double cost = resolved ? entry.cost : 0;
                Widgets.Label(costRect, "$" + cost.ToString("F0"));

                string fallback = item.abilityDef.NullOrEmpty() ? (item.kind ?? "?") : item.abilityDef;
                string label = resolved ? entry.label : (fallback + " (?)");
                Rect labelRect = new Rect(iconRect.xMax + 6f, row.y, costRect.x - iconRect.xMax - 10f, rowHeight);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                string shownLabel = Text.ClampTextWithEllipsis(labelRect, label);
                Widgets.Label(labelRect, shownLabel);
                if (resolved && (shownLabel != label || !string.IsNullOrEmpty(entry.description)))
                    TooltipHandler.TipRegion(labelRect, label + (string.IsNullOrEmpty(entry.description) ? "" : "\n\n" + entry.description));
            }

            ScrollUtil.EndScrollView();

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }
    }
}
