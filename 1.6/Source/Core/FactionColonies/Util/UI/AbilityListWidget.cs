using FactionColonies.util;
using System;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /* Shared abilities/psycasts panel for the unit designer's Abilities tab. Reads from displayUnit;
     * routes mutations through opts.getEditTarget. A psylink-level stepper at the top gates which
     * abilities are pickable; the "Add Ability" button either opens the generic FCWindow_AbilityPicker
     * (base game) or hands off to the active provider's own editor (e.g. VPE's psycast window). */
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
        private const float removeButtonSize = 20f;
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

            if (opts.canEdit && opts.showHeaderButtons && target != null)
            {
                Rect minusRect = new Rect(psyLabelRect.xMax, headerRect.y + (headerHeight - stepperButtonW) / 2f, stepperButtonW, stepperButtonW);
                Rect plusRect = new Rect(minusRect.xMax + 2f, minusRect.y, stepperButtonW, stepperButtonW);
                Text.Anchor = TextAnchor.MiddleCenter;
                if (Widgets.ButtonText(minusRect, "-") && curLevel > 0)
                    target.SetPsylinkLevel(curLevel - 1);
                if (Widgets.ButtonText(plusRect, "+") && curLevel < maxLevel)
                    target.SetPsylinkLevel(curLevel + 1);

                // Add button (right-aligned)
                float addW = 120f;
                Rect addBtnRect = new Rect(headerRect.xMax - addW, headerRect.y, addW, headerHeight);
                bool canAdd = curLevel > 0;
                if (canAdd)
                {
                    if (Widgets.ButtonText(addBtnRect, "fcAddAbility".Translate()))
                    {
                        if (active.UsesCustomEditor)
                        {
                            active.OpenEditor(target, delegate { target.ChangeTick(); });
                        }
                        else
                        {
                            Func<MilUnitFC> getDisplay = opts.getDisplayUnit ?? (() => displayUnit);
                            Find.WindowStack.Add(new FCWindow_AbilityPicker(getDisplay, opts.getEditTarget));
                        }
                    }
                }
                else
                {
                    GUI.color = Color.gray;
                    Widgets.ButtonText(addBtnRect, "fcAddAbility".Translate(), active: false);
                    GUI.color = Color.white;
                    TooltipHandler.TipRegion(addBtnRect, "fcAbilitiesNeedPsylink".Translate());
                }
            }

            // --- Chosen abilities list ---
            Rect listOutRect = new Rect(rect.x, headerRect.yMax + 2f, rect.width, rect.height - headerHeight - 4f);

            var items = displayUnit?.abilities;
            int count = items?.Count ?? 0;
            float viewHeight = count * rowHeight;
            Rect scrollViewRect = ScrollUtil.BeginScrollView(listOutRect, ref scrollPos, viewHeight);

            for (int i = 0; i < count; i++)
            {
                SavedAbility item = items[i];
                int index = i;
                Rect row = new Rect(scrollViewRect.x, scrollViewRect.y + i * rowHeight, scrollViewRect.width, rowHeight);
                if (i % 2 == 0) Widgets.DrawHighlight(row);

                IAbilitySystemProvider provider = AbilitySystemRegistry.ByKey(item.systemKey);
                AbilityPickEntry entry = null;
                bool resolved = provider is object && provider.TryGetDisplay(item.abilityDef, out entry);

                // Icon
                Rect iconRect = new Rect(row.x + 2f, row.y + 2f, IconSize, IconSize);
                if (resolved && entry.icon != null)
                    GUI.DrawTexture(iconRect, entry.icon);

                // Remove button
                Rect removeRect = Rect.zero;
                if (opts.canEdit)
                {
                    removeRect = new Rect(row.xMax - removeButtonSize - 2f, row.y + (rowHeight - removeButtonSize) / 2f, removeButtonSize, removeButtonSize);
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    if (Widgets.ButtonText(removeRect, "X") && target != null)
                        target.RemoveAbility(index);
                }

                // Cost
                float costRight = opts.canEdit ? removeRect.x - 4f : row.xMax - 4f;
                Rect costRect = new Rect(costRight - 60f, row.y, 60f, rowHeight);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                double cost = resolved ? entry.cost : 0;
                Widgets.Label(costRect, "$" + cost.ToString("F0"));

                // Label
                string label = resolved ? entry.label : (item.abilityDef + " (?)");
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
