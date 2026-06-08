using FactionColonies.util;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Generic ability picker for ability systems that don't bring their own editor (base game). Lists
    /// the active provider's pickable abilities for the unit's current psylink level, skipping ones
    /// already chosen. Stays open so several can be added; the option list rebuilds when the unit's
    /// editVersion changes (each add bumps it). Providers with their own UI (e.g. VPE) bypass this.
    /// </summary>
    public class FCWindow_AbilityPicker : Window
    {
        private readonly Func<MilUnitFC> getDisplayUnit;
        private readonly Func<MilUnitFC> getEditTarget;

        private List<AbilityPickEntry> options = new List<AbilityPickEntry>();
        private int builtForVersion = int.MinValue;
        private MilUnitFC builtForUnit;
        private string searchTerm = "";
        private Vector2 scrollPos;

        private const float RowHeight = 30f;
        private const float SearchBarHeight = 28f;
        private const float margin = 5f;

        public override Vector2 InitialSize => new Vector2(540f, 620f);

        public FCWindow_AbilityPicker(Func<MilUnitFC> getDisplayUnit, Func<MilUnitFC> getEditTarget)
        {
            this.getDisplayUnit = getDisplayUnit;
            this.getEditTarget = getEditTarget;
            forcePause = false;
            draggable = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), "fcPickAbility".Translate());

            RebuildIfStale();

            Rect searchRect = new Rect(0, 40f, inRect.width, SearchBarHeight);
            searchTerm = Widgets.TextField(searchRect, searchTerm);

            Rect listOut = new Rect(0, searchRect.yMax + margin, inRect.width, inRect.height - searchRect.yMax - margin - 40f);
            Widgets.DrawMenuSection(listOut);

            List<AbilityPickEntry> filtered = string.IsNullOrEmpty(searchTerm)
                ? options
                : options.Where(o => o.label.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            float viewHeight = filtered.Count * RowHeight;
            Rect scrollView = ScrollUtil.BeginScrollView(listOut, ref scrollPos, viewHeight);

            for (int i = 0; i < filtered.Count; i++)
            {
                AbilityPickEntry opt = filtered[i];
                Rect row = new Rect(scrollView.x, scrollView.y + i * RowHeight, scrollView.width, RowHeight);
                if (i % 2 == 0) Widgets.DrawHighlight(row);

                Rect iconRect = new Rect(row.x + margin, row.y + 3f, RowHeight - 6f, RowHeight - 6f);
                if (opt.icon != null)
                    GUI.DrawTexture(iconRect, opt.icon);

                Rect costRect = new Rect(row.xMax - margin - 65f, row.y, 60f, RowHeight);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(costRect, "$" + opt.cost.ToString("F0"));

                Rect labelRect = new Rect(iconRect.xMax + margin, row.y, costRect.x - iconRect.xMax - 2 * margin, RowHeight);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, opt.label);
                if (!string.IsNullOrEmpty(opt.description))
                    TooltipHandler.TipRegion(row, opt.label + "\n\n" + opt.description);

                if (Widgets.ButtonInvisible(row))
                {
                    MilUnitFC target = getEditTarget?.Invoke();
                    IAbilitySystemProvider active = AbilitySystemRegistry.Active;
                    if (target != null && active is object)
                    {
                        target.AddAbility(active.Key, opt.defName);
                        builtForVersion = int.MinValue; // force rebuild so the picked one drops out
                    }
                }
            }

            ScrollUtil.EndScrollView();

            Rect closeRect = new Rect(inRect.width - 120f, inRect.height - 35f, 120f, 30f);
            if (Widgets.ButtonText(closeRect, "FCDialogPawnLoadoutClose".Translate()))
                Close();

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        private void RebuildIfStale()
        {
            MilUnitFC unit = getDisplayUnit?.Invoke();
            int version = unit?.editVersion ?? int.MinValue;
            if (unit == builtForUnit && version == builtForVersion) return;
            builtForUnit = unit;
            builtForVersion = version;
            options = BuildOptions(unit);
        }

        private static List<AbilityPickEntry> BuildOptions(MilUnitFC unit)
        {
            List<AbilityPickEntry> result = new List<AbilityPickEntry>();
            IAbilitySystemProvider active = AbilitySystemRegistry.Active;
            if (unit is null || active is null || unit.psylinkLevel <= 0) return result;

            HashSet<string> chosen = new HashSet<string>(
                unit.abilities.Where(a => a.systemKey == active.Key).Select(a => a.abilityDef));

            foreach (AbilityPickEntry entry in active.ListPickable(unit.psylinkLevel))
            {
                if (entry is null || chosen.Contains(entry.defName)) continue;
                result.Add(entry);
            }

            result.Sort((a, b) =>
            {
                int byLevel = a.level.CompareTo(b.level);
                return byLevel != 0 ? byLevel : string.Compare(a.label, b.label, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }
    }
}
