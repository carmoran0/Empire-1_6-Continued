using System;
using System.Collections.Generic;
using System.Linq;
using FactionColonies.util;
using RimWorld;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    public class DesignUnitsWindow : MilitaryWindow
    {
        private readonly MilitaryCustomizationUtil util;
        private readonly FactionFC faction;
        private MilUnitFC selectedUnit;

        private Vector2 unitListScrollPos;
        private string unitSearchTerm = "";
        private Vector2 wornItemsScrollPos;
        private bool isSelectedUnitDeployed;
        private string selectedUnitDeployReason = "";

        // Layout sizing constants
        private const float SidebarWidth = 250f;
        private const float GearWidth = 310f;
        private const float RowHeight = 30f;
        private const float SearchBarHeight = 28f;
        private const float IconSize = 24f;
        private const float margin = 5f;
        private const float ButtonHeight = 30f;

        /// <summary>
        /// Describes an apparel equipment slot for the unit designer UI.
        /// </summary>
        private struct ApparelSlotDef
        {
            public Rect rect;
            public ApparelLayerDef layer;
            public BodyPartGroupDef bodyPart; // null = no body part filter
            public string labelKey;

            public bool ThingFitsSlot(ThingDef thing)
            {
                if (!thing.IsApparel) return false;
                if (!thing.apparel.layers.Contains(layer)) return false;
                if (bodyPart != null && !thing.apparel.bodyPartGroups.Contains(bodyPart)) return false;
                if (!thing.apparel.PawnCanWear(Gender.None, DevelopmentalStage.Adult)) return false;
                return CraftUtil.CanCraftItem(thing);
            }

            public bool ApparelInSlot(ThingDef def)
            {
                return MilUnitFC.MatchesSlot(def, layer, bodyPart);
            }
        }

        public DesignUnitsWindow(MilitaryCustomizationUtil util, FactionFC faction)
        {
            this.util = util;
            this.faction = faction;

            selectedText = "Select A Unit";

            util.CheckMilitaryUtilForErrors();
        }

        public override void Select(IExposable selecting)
        {
            MilUnitFC unit = (MilUnitFC)selecting;
            selectedUnit = unit;
            selectedText = unit.name;
        }

        public override void DrawTab(Rect rect)
        {
            isSelectedUnitDeployed = selectedUnit != null
                && IsUnitDeployed(selectedUnit, out selectedUnitDeployReason);

            Widgets.DrawLineHorizontal(rect.x, rect.y + 45, rect.width);

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            float contentTop = rect.y + 45f + margin;
            float rightEdge = rect.xMax - margin;

            // Layout Y metrics
            float belowHighlight = contentTop + 35f + margin;
            float gearTop = belowHighlight + 61f + margin;
            float gearBottom = gearTop + 305f;
            float contentBottom = rect.yMax - margin;

            // Left sidebar: search + unit list + action buttons
            Rect sidebarRect = new Rect(rect.x + margin, contentTop,
                SidebarWidth, contentBottom - contentTop);
            DrawSidebar(sidebarRect);

            // Content area starts after sidebar + gap
            float contentLeft = sidebarRect.xMax + 10f;
            Rect gearRect = new Rect(contentLeft, gearTop, GearWidth, 305f);

            if (selectedUnit != null)
            {
                Rect headerRect = new Rect(contentLeft, contentTop,
                    rightEdge - contentLeft, 85f);
                DrawUnitHeader(headerRect);

                Rect buttonsRect = new Rect(contentLeft + GearWidth + 10f, belowHighlight,
                    rightEdge - contentLeft - GearWidth - 10f, 61f);
                DrawActionButtons(buttonsRect);

                //Widgets.DrawMenuSection(gearRect);
                DrawGearPanel(gearRect);
            }


            Rect wornRect = new Rect(gearRect.xMax + 10f, gearRect.y,
                rightEdge - gearRect.xMax - 10f, gearRect.height);
            DrawWornItemsSidebar(wornRect);

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        // --- Sidebar ---

        private void DrawSidebar(Rect rect)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            // Search bar
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect searchRect = new Rect(rect.x, rect.y, rect.width, SearchBarHeight);
            unitSearchTerm = Widgets.TextField(searchRect, unitSearchTerm);

            // Unit list (fills space between search bar and buttons)
            float buttonsHeight = ButtonHeight * 2 + margin;
            float listHeight = rect.yMax - searchRect.yMax - margin - buttonsHeight - margin;
            Rect listOutRect = new Rect(rect.x, searchRect.yMax + margin, rect.width, listHeight);
            Widgets.DrawMenuSection(listOutRect);

            List<MilUnitFC> filteredUnits = string.IsNullOrEmpty(unitSearchTerm)
                ? util.units
                : util.units.Where(u => u.name.IndexOf(unitSearchTerm, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            float viewHeight = filteredUnits.Count * RowHeight;
            Rect scrollViewRect = new Rect(listOutRect.x, listOutRect.y,
                rect.width - (viewHeight > listHeight ? 16f : 0f),
                Mathf.Max(viewHeight, listHeight));

            Widgets.BeginScrollView(listOutRect, ref unitListScrollPos, scrollViewRect);

            for (int i = 0; i < filteredUnits.Count; i++)
            {
                MilUnitFC unit = filteredUnits[i];
                Rect row = new Rect(scrollViewRect.x, scrollViewRect.y + i * RowHeight, scrollViewRect.width, RowHeight);

                bool isDeployed = IsUnitDeployed(unit, out string deployReason);

                if (unit == selectedUnit)
                    Widgets.DrawHighlightSelected(row);
                else if (i % 2 == 0)
                    Widgets.DrawHighlight(row);

                Color colorBefore = GUI.color;
                if (isDeployed) GUI.color = Color.gray;

                // Weapon icon
                Rect iconRect = new Rect(row.x + 2f, row.y + 3f, IconSize, IconSize);
                if (unit.HasWeapon)
                    Widgets.DefIcon(iconRect, unit.weapons[0].thing, unit.weapons[0].stuff);

                // Name label
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Rect labelRect = new Rect(iconRect.xMax + 4f, row.y, row.xMax - iconRect.xMax - 6f, RowHeight);
                Widgets.Label(labelRect, unit.name);

                if (isDeployed) GUI.color = colorBefore;

                if (Widgets.ButtonInvisible(row))
                {
                    selectedUnit = unit;
                    selectedText = unit.name;
                }
            }

            Widgets.EndScrollView();

            // Action buttons (2x2 grid)
            float btnY = listOutRect.yMax + margin;
            float buttonW = (rect.width - margin) / 2f;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            Rect createBtn = new Rect(rect.x, btnY, buttonW, ButtonHeight);
            Rect importBtn = new Rect(rect.x + buttonW + margin, btnY, buttonW, ButtonHeight);
            Rect deleteBtn = new Rect(rect.x, btnY + ButtonHeight + margin, buttonW, ButtonHeight);
            Rect exportBtn = new Rect(rect.x + buttonW + margin, btnY + ButtonHeight + margin, buttonW, ButtonHeight);

            if (Widgets.ButtonText(createBtn, "FCCreateNewUnit".Translate()))
            {
                MilUnitFC newUnit = new MilUnitFC(false)
                {
                    name = $"New Unit {util.units.Count + 1}"
                };
                selectedText = newUnit.name;
                selectedUnit = newUnit;
                util.units.Add(newUnit);
            }

            if (Widgets.ButtonText(importBtn, "importUnit".Translate()))
            {
                Find.WindowStack.Add(new Dialog_ManageUnitExportsFC(
                    FactionColoniesMilitary.SavedUnits.ToList()));
            }

            if (selectedUnit != null)
            {
                if (Widgets.ButtonText(deleteBtn, "deleteUnitButton".Translate()))
                {
                    MilUnitFC unitToDelete = selectedUnit;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "FCConfirmDeleteUnit".Translate((NamedArgument)unitToDelete.name),
                        delegate
                        {
                            unitToDelete.RemoveUnit();
                            util.CheckMilitaryUtilForErrors();
                            if (selectedUnit == unitToDelete)
                            {
                                selectedUnit = null;
                                selectedText = "selectAUnitButton".Translate();
                            }
                        }));
                }

                if (Widgets.ButtonText(exportBtn, "exportUnitButton".Translate()))
                {
                    FactionColoniesMilitary.SaveUnit(new SavedUnitFC(selectedUnit));
                    Messages.Message("ExportUnit".Translate(), MessageTypeDefOf.TaskCompletion);
                }
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        // --- Unit Header ---

        private void DrawUnitHeader(Rect rect)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            // Highlight banner behind unit name
            Rect highlightBar = new Rect(rect.x, rect.y, rect.width, 35f);
            Widgets.DrawHighlight(highlightBar);

            // Unit name (large label)
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect nameRect = new Rect(rect.x + margin, rect.y, 400f, 30f);
            Widgets.Label(nameRect, selectedUnit.name);

            // Pencil icon to trigger rename
            float nameTextWidth = Text.CalcSize(selectedUnit.name).x;
            Rect pencilRect = new Rect(rect.x + Mathf.Min(nameTextWidth + 8f + margin, rect.width - 22f), rect.y + 4f, 22f, 22f);
            if (!isSelectedUnitDeployed && Widgets.ButtonImage(pencilRect, TexButton.Rename))
            {
                Find.WindowStack.Add(new FCWindow_Rename(selectedUnit.name, "FCRenameUnit", name => selectedUnit.name = name));
            }

            // Race / Xeno info line
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            string raceName = selectedUnit.pawnKind?.race?.label?.CapitalizeFirst() ?? "Unknown";
            string xenoName = selectedUnit.xenotype?.label?.CapitalizeFirst() ?? "None";
            Rect infoRect = new Rect(rect.x, highlightBar.yMax + margin, rect.width, 20f);
            Widgets.Label(infoRect, "Race".Translate() + ": " + raceName + "   ·   " + "Xenotype".Translate() + ": " + xenoName);

            // Equipment cost
            float totalCost = (float)selectedUnit.getTotalCost;
            Rect costRect = new Rect(rect.x, infoRect.yMax + margin, rect.width, 20f);
            Widgets.Label(costRect, "totalEquipmentCostLabel".Translate() + totalCost);

            if (isSelectedUnitDeployed)
            {
                Color colorBefore = GUI.color;
                GUI.color = Color.yellow;
                Rect viewOnlyRect = new Rect(rect.x, costRect.yMax + 2f, rect.width, 23f);
                Widgets.Label(viewOnlyRect, "CantBeModified".Translate(selectedUnit.name, selectedUnitDeployReason));
                GUI.color = colorBefore;
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        // --- Action Buttons ---

        private void DrawActionButtons(Rect rect)
        {
            float btnH = 28f;
            float gap = 5f;
            float btnW = (rect.width - gap) / 2f;
            bool canEdit = !isSelectedUnitDeployed;
            /* If the unit can't be edited, then don't even render the action buttons. */
            if (!canEdit) return;

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            if (Widgets.ButtonText(new Rect(rect.x, rect.y, btnW, btnH), "changeUnitRaceButton".Translate(), true, true))
            {
                Find.WindowStack.Add(new FCWindow_RacePicker(selectedUnit, faction));
            }

            if (Widgets.ButtonText(new Rect(rect.x + btnW + gap, rect.y, btnW, btnH), "changeUnitXenoButton".Translate(), true, true))
            {
                Find.WindowStack.Add(new FCWindow_XenoPicker(selectedUnit));
            }

            float y2 = rect.y + btnH + gap;

            if (Widgets.ButtonText(new Rect(rect.x, y2, btnW, btnH), "rollANewUnitButton".Translate(), true, true))
            {
                selectedUnit.RerollPreviewPawn();
            }

            if (Widgets.ButtonText(new Rect(rect.x + btnW + gap, y2, btnW, btnH), "resetUnitToDefaultButton".Translate(), true, true))
            {
                selectedUnit.ClearAllEquipment();
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        // --- Deployment Check ---

        private bool IsUnitDeployed(MilUnitFC unit, out string reason)
        {
            reason = "";
            FactionFC factionFC = FactionCache.FactionComp;
            List<MilSquadFC> squadsContainingUnit = factionFC?.militaryCustomizationUtil?.squads
                ?.Where(squad => squad?.units != null && squad.units.Contains(unit)).ToList();

            if (squadsContainingUnit == null || squadsContainingUnit.Count == 0) return false;

            List<WorldSettlementFC> settlementsContainingSquad = factionFC?.settlements
                ?.FindAll(settlement => settlement?.MilitaryComp?.militarySquad?.outfit != null &&
                    squadsContainingUnit.Any(squad => settlement.MilitaryComp.militarySquad.outfit == squad));

            if (settlementsContainingSquad == null || settlementsContainingSquad.Count == 0) return false;

            if (settlementsContainingSquad.Any(s => s.MilitaryComp.militarySquad.isDeployed))
            {
                reason = "ReasonDeployed".Translate();
                return true;
            }

            if (settlementsContainingSquad.Any(s => s.MilitaryComp.isUnderAttack
                && settlementsContainingSquad.Contains(s.MilitaryComp.defenderForce.homeSettlement)))
            {
                reason = "ReasonDefending".Translate();
                return true;
            }

            return false;
        }

        // --- Gear Panel ---

        private void DrawGearPanel(Rect gearArea)
        {
            const float iconSize = 120f;
            const float slotSize = 50f;

            // Unit and animal icons (positioned relative to gearArea)
            Rect unitIcon   = new Rect(gearArea.x + 120, gearArea.y + 100f, iconSize, iconSize + 20f);
            //Rect animalIcon = new Rect(gearArea.x + 120, gearArea.y + 195, iconSize, iconSize);

            // Apparel/equipment slots (positioned relative to unitIcon)
            Rect ApparelHead        = new Rect(unitIcon.x + (iconSize - slotSize) / 2f, unitIcon.y - 75, slotSize, slotSize);
            Rect ApparelTorsoSkin   = new Rect(unitIcon.xMax + 20, unitIcon.y - 55, slotSize, slotSize);
            Rect ApparelBelt        = new Rect(unitIcon.xMax + 20, unitIcon.y + 15,  slotSize, slotSize);
            Rect ApparelLegs        = new Rect(unitIcon.xMax + 20, unitIcon.y + 85, slotSize, slotSize);

            Rect AnimalCompanion    = new Rect(unitIcon.x - 60,  unitIcon.y - 55, slotSize, slotSize);
            Rect ApparelTorsoShell  = new Rect(unitIcon.x - 60,  unitIcon.y + 15,  slotSize, slotSize);
            Rect ApparelTorsoMiddle = new Rect(unitIcon.x - 60,  unitIcon.y + 85, slotSize, slotSize);
            Rect EquipmentWeapon    = new Rect(unitIcon.x - 120, unitIcon.y + 15,  slotSize, slotSize);

            ApparelSlotDef[] apparelSlots = new[]
            {
                new ApparelSlotDef { rect = ApparelHead, layer = ApparelLayerDefOf.Overhead, bodyPart = null, labelKey = "fcLabelHead" },
                new ApparelSlotDef { rect = ApparelTorsoShell, layer = ApparelLayerDefOf.Shell, bodyPart = BodyPartGroupDefOf.Torso, labelKey = "fcLabelOver" },
                new ApparelSlotDef { rect = ApparelTorsoMiddle, layer = ApparelLayerDefOf.Middle, bodyPart = BodyPartGroupDefOf.Torso, labelKey = "fcLabelChest" },
                new ApparelSlotDef { rect = ApparelTorsoSkin, layer = ApparelLayerDefOf.OnSkin, bodyPart = BodyPartGroupDefOf.Torso, labelKey = "fcLabelShirt" },
                new ApparelSlotDef { rect = ApparelLegs, layer = ApparelLayerDefOf.OnSkin, bodyPart = BodyPartGroupDefOf.Legs, labelKey = "fcLabelPants" },
                new ApparelSlotDef { rect = ApparelBelt, layer = ApparelLayerDefOf.Belt, bodyPart = null, labelKey = "fcLabelBelt" },
            };

            // --- Always drawn: slot backgrounds and labels ---
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;

            foreach (ApparelSlotDef slot in apparelSlots)
            {
                Widgets.Label(new Rect(new Vector2(slot.rect.x, slot.rect.y - 15), slot.rect.size), slot.labelKey.Translate());
                Widgets.DrawMenuSection(slot.rect);
            }

            Widgets.Label(new Rect(new Vector2(EquipmentWeapon.x, EquipmentWeapon.y - 15), EquipmentWeapon.size), "fcLabelWeapon".Translate());
            Widgets.DrawMenuSection(EquipmentWeapon);
            Widgets.Label(new Rect(new Vector2(AnimalCompanion.x, AnimalCompanion.y - 15), AnimalCompanion.size), "fcLabelAnimal".Translate());
            Widgets.DrawMenuSection(AnimalCompanion);

            // CE ammo slot — only drawn when CE is loaded, a unit is selected, has a weapon, and the weapon has CE ammo options
            Rect AmmoSlot = new Rect(EquipmentWeapon.x, EquipmentWeapon.yMax + 20f, slotSize, slotSize);
            bool showAmmoSlot = CombatExtendedUtil.IsCELoaded
                && selectedUnit != null
                && selectedUnit.HasWeapon
                && CombatExtendedUtil.GetAmmoOptionsForWeapon(selectedUnit.weapons[0].thing).Count > 0;
            if (showAmmoSlot)
            {
                Widgets.Label(new Rect(AmmoSlot.x, AmmoSlot.y - 15f, AmmoSlot.width, 15f), "fcLabelAmmo".Translate());
                Widgets.DrawMenuSection(AmmoSlot);
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;

            // --- Unit-selected content ---
            if (selectedUnit == null) return;

            // Draw Pawn Preview
            Pawn preview = selectedUnit.PreviewPawn;
            if (preview != null)
            {
                UIUtil.DrawPawnPortrait(unitIcon, preview, 1.2f);
            }

            // --- Animal Companion Slot ---
            if (!isSelectedUnitDeployed && Widgets.ButtonInvisible(AnimalCompanion))
            {
                Find.WindowStack.Add(new FCWindow_AnimalPicker(selectedUnit));
            }

            // --- Weapon Slot ---
            bool unitIsNonViolent = FactionCache.XenotypeIsNonViolent(selectedUnit.xenotype);
            if (unitIsNonViolent)
            {
                Widgets.DrawBoxSolid(EquipmentWeapon, new Color(0.2f, 0.2f, 0.2f, 0.5f));
                TooltipHandler.TipRegion(EquipmentWeapon, "FCNonViolentNoWeapons".Translate());
            }
            else if (!isSelectedUnitDeployed && Widgets.ButtonInvisible(EquipmentWeapon))
            {
                List<ThingDef> weaponDefs = DefDatabase<ThingDef>.AllDefs
                    .Where(t => t.IsWeapon && t.BaseMarketValue != 0 && CraftUtil.CanCraftItem(t)
                        && HARUtil.CanRaceUseWeapon(selectedUnit.pawnKind?.race, t))
                    .OrderBy(t => t.label)
                    .ToList();

                SavedThing? currentWeapon = selectedUnit.HasWeapon ? selectedUnit.weapons[0] : (SavedThing?)null;
                Find.WindowStack.Add(new FCWindow_ItemStuffPicker(
                    weaponDefs,
                    onConfirm: (item, stuff) => selectedUnit.SetWeapon(item, stuff),
                    onUnequip: () => selectedUnit.ClearWeapon(),
                    titleKey: "fcPickWeapon",
                    initialItem: currentWeapon?.thing,
                    initialStuff: currentWeapon?.stuff
                ));
            }

            // --- CE Ammo Slot ---
            if (showAmmoSlot)
            {
                // Click handler must come before icon draw so it can consume the event first
                if (!isSelectedUnitDeployed && Widgets.ButtonInvisible(AmmoSlot))
                {
                    var ammoOptions = CombatExtendedUtil.GetAmmoOptionsForWeapon(selectedUnit.weapons[0].thing);
                    var menuOptions = new List<FloatMenuOption>
                    {
                        new FloatMenuOption("fcAmmoAny".Translate(), () => selectedUnit.ClearPreferredAmmo())
                    };
                    foreach (ThingDef ammo in ammoOptions)
                    {
                        ThingDef captured = ammo;
                        menuOptions.Add(new FloatMenuOption(
                            captured.LabelCap,
                            () => selectedUnit.SetPreferredAmmo(captured),
                            captured.uiIcon,
                            Color.white));
                    }
                    Find.WindowStack.Add(new FloatMenu(menuOptions));
                }

                // Display icon or "Any" label
                if (selectedUnit.preferredAmmo != null)
                {
                    GUI.DrawTexture(AmmoSlot, selectedUnit.preferredAmmo.uiIcon);
                }
                else
                {
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(AmmoSlot, "fcAmmoAny".Translate());
                    Text.Font = fontBefore;
                    Text.Anchor = anchorBefore;
                }
            }

            // --- Apparel Slots (unified handler) ---
            if (!isSelectedUnitDeployed)
            {
                foreach (ApparelSlotDef slot in apparelSlots)
                {
                    HandleApparelSlot(slot, selectedUnit);
                }
            }

            // Animal icon
            if (selectedUnit.animal != null)
            {
                Widgets.ButtonImage(AnimalCompanion, selectedUnit.animal.race.uiIcon);
            }

            // Draw equipped icons in slots
            foreach (ApparelSlotDef slot in apparelSlots)
            {
                SavedThing? worn = selectedUnit.apparel
                    .Cast<SavedThing?>()
                    .FirstOrDefault(a => slot.ApparelInSlot(a.Value.thing));
                if (worn.HasValue && worn.Value.thing != null)
                {
                    Widgets.ButtonImage(slot.rect, worn.Value.thing.uiIcon);
                }
            }

            if (selectedUnit.HasWeapon && !FactionCache.XenotypeIsNonViolent(selectedUnit.xenotype))
            {
                Widgets.ButtonImage(EquipmentWeapon, selectedUnit.weapons[0].thing.uiIcon);
            }
        }

        // --- Worn Items Sidebar ---

        private void DrawWornItemsSidebar(Rect rect)
        {
            if (selectedUnit == null) return;

            Widgets.DrawMenuSection(rect);

            List<(string label, ThingDef thing, ThingDef stuff)> wornEntries = new List<(string, ThingDef, ThingDef)>();

            foreach (SavedThing item in selectedUnit.apparel)
            {
                if (item.thing == null) continue;
                string label = item.stuff != null
                    ? item.thing.LabelCap + " (" + item.stuff.LabelCap + ") Cost: " + item.MarketValue
                    : item.thing.LabelCap + " Cost: " + item.MarketValue;
                wornEntries.Add((label, item.thing, item.stuff));
            }

            foreach (SavedThing w in selectedUnit.weapons)
            {
                if (w.thing == null) continue;
                string label = w.stuff != null
                    ? w.thing.LabelCap + " (" + w.stuff.LabelCap + ") Cost: " + w.MarketValue
                    : w.thing.LabelCap + " Cost: " + w.MarketValue;
                wornEntries.Add((label, w.thing, w.stuff));
            }

            if (CombatExtendedUtil.IsCELoaded && selectedUnit.preferredAmmo != null)
            {
                string ammoLabel = "fcLabelAmmo".Translate() + ": " + selectedUnit.preferredAmmo.LabelCap;
                wornEntries.Add((ammoLabel, selectedUnit.preferredAmmo, null));
            }

            float wornViewHeight = wornEntries.Count * 25f;
            Rect scrollViewRect = new Rect(0f, 0f,
                rect.width - (wornViewHeight > rect.height ? 16f : 0f),
                Mathf.Max(wornViewHeight, rect.height));

            Widgets.BeginScrollView(rect, ref wornItemsScrollPos, scrollViewRect);

            for (int i = 0; i < wornEntries.Count; i++)
            {
                var (label, thing, stuff) = wornEntries[i];
                Rect tmp = new Rect(scrollViewRect.x, scrollViewRect.y + i * 25f, scrollViewRect.width, 25f);
                if (Widgets.CustomButtonText(ref tmp, label, Color.white, Color.black, Color.black))
                {
                    Find.WindowStack.Add(new Dialog_InfoCard(thing, stuff));
                }
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// Handles the click interaction for a single apparel slot.
        /// Opens an item+stuff picker window for matching apparel.
        /// </summary>
        private void HandleApparelSlot(ApparelSlotDef slot, MilUnitFC unit)
        {
            if (!Widgets.ButtonInvisible(slot.rect)) return;

            List<ThingDef> apparelDefs = DefDatabase<ThingDef>.AllDefs
                .Where(t => slot.ThingFitsSlot(t)
                    && HARUtil.CanRaceWearApparel(unit.pawnKind?.race, t))
                .OrderBy(t => t.label)
                .ToList();

            SavedThing? currentApparel = unit.apparel
                .Cast<SavedThing?>()
                .FirstOrDefault(a => slot.ApparelInSlot(a.Value.thing));

            Find.WindowStack.Add(new FCWindow_ItemStuffPicker(
                apparelDefs,
                onConfirm: (item, stuff) => unit.SetApparel(item, stuff),
                onUnequip: () => unit.RemoveApparel(slot.layer, slot.bodyPart),
                titleKey: "fcPickApparel",
                initialItem: currentApparel?.thing,
                initialStuff: currentApparel?.stuff
            ));
        }
    }
}
