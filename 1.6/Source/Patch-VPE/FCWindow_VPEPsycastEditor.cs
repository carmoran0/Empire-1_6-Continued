using System;
using System.Collections.Generic;
using System.Linq;
using FactionColonies.util;
using RimWorld;
using UnityEngine;
using VanillaPsycastsExpanded;
using VanillaPsycastsExpanded.UI;
using VEF.Abilities;
using Verse;
using AbilityDef = VEF.Abilities.AbilityDef;

namespace FactionColonies.VPE
{
    /// <summary>
    /// Routes psycast picking through VPE's own UI. A throwaway "session" pawn is generated from the
    /// unit template and given psylink at the unit's level (so VPE attaches its tracker + points); the
    /// path/ability tree is then drawn and unlocked with VPE's own helpers and validation
    /// (<see cref="PsycastsUIUtility"/>, <c>UnlockPath</c>, <c>GiveAbility</c>, prerequisite checks).
    /// On close the pawn's learned VPE psycasts are snapshotted back into the template and the pawn is
    /// discarded. The session pawn is owned by this window — decoupled from the shared preview pawn.
    /// </summary>
    public class FCWindow_VPEPsycastEditor : Window
    {
        private readonly MilUnitFC unit;
        private readonly Action onClosed;

        private Pawn sessionPawn;
        private Hediff_PsycastAbilities hediff;
        private CompAbilities compAbilities;
        private bool setupFailed;

        private readonly Dictionary<string, List<PsycasterPathDef>> pathsByTab;
        private readonly List<TabRecord> tabs;
        private string curTab;
        private Vector2 pathsScrollPos;
        private float lastPathsHeight;
        private int pathsPerRow = 3;
        private readonly Dictionary<AbilityDef, Vector2> abilityPos = new Dictionary<AbilityDef, Vector2>();
        private bool useAltBackgrounds;

        public override Vector2 InitialSize => new Vector2(1000f, 720f);

        public FCWindow_VPEPsycastEditor(MilUnitFC unit, Action onClosed)
        {
            this.unit = unit;
            this.onClosed = onClosed;
            forcePause = false;
            draggable = true;
            doCloseX = true;
            resizeable = true;
            absorbInputAroundWindow = true;

            pathsByTab = DefDatabase<PsycasterPathDef>.AllDefs
                .GroupBy(d => d.tab)
                .ToDictionary(g => g.Key, g => g.ToList());
            tabs = pathsByTab.Select(kv => new TabRecord(kv.Key, () => curTab = kv.Key, () => curTab == kv.Key)).ToList();
            curTab = pathsByTab.Keys.FirstOrDefault();

            SetupSessionPawn();
        }

        private void SetupSessionPawn()
        {
            try
            {
                sessionPawn = FCPawnGenerator.GenerateWithForcedXenotype(FCPawnGenerator.WorkerOrMilitaryRequestForUnit(unit));
                if (sessionPawn is null) { setupFailed = true; return; }

                Faction empire = FindFC.EmpireFaction;
                if (sessionPawn.Faction is null && empire != null)
                    sessionPawn.SetFaction(empire);

                new VPEAbilityProvider().ApplyPsylink(sessionPawn, unit.psylinkLevel);

                hediff = sessionPawn.Psycasts();
                compAbilities = sessionPawn.GetComp<CompAbilities>();
                if (hediff is null || compAbilities is null) { setupFailed = true; return; }

                // Replay already-chosen VPE abilities onto the session pawn, mirroring the UI's
                // spend-then-grant so the remaining point budget reflects prior choices.
                foreach (SavedAbility saved in unit.abilities.Where(a => a.systemKey == VPEAbilityProvider.ProviderKey))
                {
                    AbilityDef def = DefDatabase<AbilityDef>.GetNamedSilentFail(saved.abilityDef);
                    if (def is null || compAbilities.HasAbility(def)) continue;
                    AbilityExtension_Psycast psycast = def.Psycast();
                    if (psycast != null)
                    {
                        if (psycast.path != null && !hediff.unlockedPaths.Contains(psycast.path))
                        {
                            if (hediff.points >= 1) hediff.SpentPoints();
                            hediff.UnlockPath(psycast.path);
                        }
                        if (hediff.points >= 1) hediff.SpentPoints();
                        psycast.UnlockWithPrereqs(compAbilities);
                    }
                    else
                    {
                        compAbilities.GiveAbility(def);
                    }
                }
            }
            catch (Exception ex)
            {
                setupFailed = true;
                LogUtil.Error($"VPE psycast editor: failed to set up session pawn for '{unit?.name}': {ex.Message}");
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            if (setupFailed || sessionPawn is null || hediff is null || compAbilities is null)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(inRect, "fcVPEEditorUnavailable".Translate());
                Text.Anchor = anchorBefore;
                return;
            }

            // VPE's drawing helpers read these statics.
            PsycastsUIUtility.Hediff = hediff;
            PsycastsUIUtility.CompAbilities = compAbilities;

            // --- Left info column ---
            Rect left = inRect.LeftPartPixels(260f);
            var listing = new Listing_Standard();
            listing.Begin(left);
            Text.Font = GameFont.Medium;
            listing.Label("fcVPEEditorTitle".Translate());
            Text.Font = GameFont.Small;
            listing.Gap(6f);
            listing.Label("fcPsylinkLevel".Translate() + ": " + hediff.level);
            listing.Label("VPE.Points".Translate(hediff.points));
            listing.Gap(8f);
            Text.Font = GameFont.Tiny;
            listing.Label("fcVPEEditorHint".Translate());
            listing.Gap(8f);
            listing.CheckboxLabeled("VPE.UseAltBackground".Translate(), ref useAltBackgrounds);
            listing.End();

            // --- Right path panel (tabs + scrolling tree) ---
            Rect right = inRect;
            right.xMin = left.xMax + 10f;

            if (pathsByTab.NullOrEmpty())
            {
                Widgets.DrawMenuSection(right);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(right, "No Paths");
            }
            else
            {
                pathsPerRow = Mathf.Max(1, Mathf.FloorToInt(right.width / 200f));
                Rect tabArea = new Rect(right.x, right.y + 40f, right.width, right.height - 40f);
                TabDrawer.DrawTabs(tabArea, tabs);
                Rect panel = tabArea;
                Widgets.DrawMenuSection(panel);
                Rect viewRect = new Rect(0, 0, panel.width - 20f, lastPathsHeight);
                Widgets.BeginScrollView(panel.ContractedBy(2f), ref pathsScrollPos, viewRect);
                DoPaths(viewRect);
                Widgets.EndScrollView();
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        /* Mirrors VanillaPsycastsExpanded.UI.ITab_Pawn_Psycasts.DoPaths, operating on the session pawn:
         * unlocked paths render their ability tree (clickable via DoAbility); locked paths show an
         * Unlock button gated by VPE's own CanPawnUnlock + point checks. */
        private void DoPaths(Rect inRect)
        {
            Vector2 curPos = inRect.position + Vector2.one * 10f;
            float widthPerPath = (inRect.width - (pathsPerRow + 1) * 10f) / pathsPerRow;
            float maxHeight = 0f;
            int remaining = pathsPerRow;

            foreach (PsycasterPathDef def in pathsByTab[curTab]
                .OrderByDescending(p => hediff.unlockedPaths.Contains(p))
                .ThenBy(p => p.order)
                .ThenBy(p => p.label))
            {
                Texture2D texture = useAltBackgrounds ? def.backgroundImage : def.altBackgroundImage;
                float height = widthPerPath / texture.width * texture.height + 30f;
                Rect rect = new Rect(curPos, new Vector2(widthPerPath, height));
                PsycastsUIUtility.DrawPathBackground(ref rect, def, useAltBackgrounds);

                if (hediff.unlockedPaths.Contains(def))
                {
                    if (def.HasAbilities)
                        PsycastsUIUtility.DoPathAbilities(rect, def, abilityPos, DoAbility);
                }
                else
                {
                    Widgets.DrawRectFast(rect, new Color(0f, 0f, 0f, useAltBackgrounds ? 0.7f : 0.55f));
                    if (hediff.points >= 1)
                    {
                        Rect centerRect = new Rect(rect.center.x - 70f, rect.center.y - 15f, 140f, 30f);
                        if (def.CanPawnUnlock(sessionPawn))
                        {
                            if (Widgets.ButtonText(centerRect, "VPE.Unlock".Translate()))
                            {
                                hediff.SpentPoints();
                                hediff.UnlockPath(def);
                            }
                        }
                        else
                        {
                            GUI.color = Color.grey;
                            string label = "VPE.Locked".Translate().Resolve() + ": " + def.lockedReason;
                            centerRect.width = Mathf.Max(centerRect.width, Text.CalcSize(label).x + 10f);
                            Widgets.ButtonText(centerRect, label, active: false);
                            GUI.color = Color.white;
                        }
                    }

                    TooltipHandler.TipRegion(
                        rect,
                        () => def.tooltip + "\n\n" + "VPE.AbilitiesList".Translate() + "\n" + def.abilities.Select(ab => ab.label).ToLineList("  ", true),
                        def.GetHashCode());
                }

                maxHeight = Mathf.Max(maxHeight, height + 10f);
                curPos.x += widthPerPath + 10f;
                remaining--;
                if (remaining == 0)
                {
                    curPos.x = inRect.x + 10f;
                    curPos.y += maxHeight;
                    remaining = pathsPerRow;
                    maxHeight = 0f;
                }
            }

            lastPathsHeight = curPos.y + maxHeight;
        }

        /* Mirrors ITab_Pawn_Psycasts.DoAbility: an ability is unlockable when its prerequisites are met
         * and a point is available; clicking spends a point and grants it via VPE's own CompAbilities. */
        private void DoAbility(Rect inRect, AbilityDef ability)
        {
            bool unlockable = false;
            bool locked = false;
            if (!compAbilities.HasAbility(ability))
            {
                if (ability.Psycast().PrereqsCompleted(compAbilities) && hediff.points >= 1)
                    unlockable = true;
                else
                    locked = true;
            }

            if (unlockable) Widgets.DrawStrongHighlight(inRect.ExpandedBy(12f));
            PsycastsUIUtility.DrawAbility(inRect, ability);
            if (locked) Widgets.DrawRectFast(inRect, new Color(0f, 0f, 0f, 0.6f));

            TooltipHandler.TipRegion(
                inRect,
                () => $"{ability.LabelCap}\n\n{ability.description}{(unlockable ? "\n\n" + "VPE.ClickToUnlock".Translate().Resolve().ToUpper() : "")}",
                ability.GetHashCode());

            if (unlockable && Widgets.ButtonInvisible(inRect))
            {
                hediff.SpentPoints();
                compAbilities.GiveAbility(ability);
            }
        }

        public override void PreClose()
        {
            base.PreClose();

            try
            {
                if (compAbilities != null && unit != null)
                {
                    List<string> chosen = compAbilities.LearnedAbilities
                        .Where(a => a.def != null && a.def.Psycast() != null)
                        .Select(a => a.def.defName)
                        .ToList();
                    unit.SetAbilitiesForSystem(VPEAbilityProvider.ProviderKey, chosen);
                }
            }
            catch (Exception ex)
            {
                LogUtil.Error($"VPE psycast editor: failed to snapshot abilities for '{unit?.name}': {ex.Message}");
            }
            finally
            {
                PsycastsUIUtility.Hediff = null;
                PsycastsUIUtility.CompAbilities = null;
                DestroySessionPawn();
                onClosed?.Invoke();
            }
        }

        private void DestroySessionPawn()
        {
            if (sessionPawn is null) return;
            try
            {
                sessionPawn.apparel?.DestroyAll();
                sessionPawn.equipment?.DestroyAllEquipment();
                if (!sessionPawn.Destroyed) sessionPawn.Destroy();
            }
            catch (Exception ex)
            {
                LogUtil.Warning($"VPE psycast editor: failed to discard session pawn: {ex.Message}");
            }
            sessionPawn = null;
        }
    }
}
