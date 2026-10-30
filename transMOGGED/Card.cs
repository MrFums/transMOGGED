using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Transmogged
{
    internal static class Card
    {
        private const string RowName = "transMOGGED.Row";
        private static readonly Color Accent = new Color32(190, 150, 255, 255);

        private static readonly Dictionary<ItemSpecificationPanel, Shown> Open = new Dictionary<ItemSpecificationPanel, Shown>();
        private static bool _dirty;

        private struct Shown
        {
            public Item Item;
            public SimpleTooltip Tooltip;
        }

        public static void Apply(Harmony harmony)
        {
            var show = AccessTools.Method(typeof(ItemSpecificationPanel), nameof(ItemSpecificationPanel.Show))
                       ?? throw new MissingMethodException(nameof(ItemSpecificationPanel), nameof(ItemSpecificationPanel.Show));
            harmony.Patch(show, postfix: new HarmonyMethod(typeof(Card), nameof(Postfix)));
            Looks.Changed += _ => _dirty = true;
        }

        private static void Postfix(ItemSpecificationPanel __instance, ItemContext itemContext, SimpleTooltip secondaryTooltip)
        {
            try
            {
                var shown = new Shown { Item = itemContext?.Item, Tooltip = secondaryTooltip };
                Open[__instance] = shown;
                Build(__instance, shown);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't add the transmog line to the inspect window: {e.Message}");
            }
        }

        public static void MarkDirty() => _dirty = true;

        public static void Refresh()
        {
            if (!_dirty)
                return;
            _dirty = false;
            foreach (var open in Open.ToList())
            {
                if (open.Key == null)
                {
                    Open.Remove(open.Key);
                    continue;
                }
                if (!open.Key.isActiveAndEnabled)
                    continue;
                try
                {
                    Build(open.Key, open.Value);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Couldn't refresh the transmog line: {e.Message}");
                }
            }
        }

        private static void Build(ItemSpecificationPanel panel, Shown shown)
        {
            Transform grid = panel._compactPanel;
            CompactCharacteristicPanel template = panel._compactCharTemplate;
            if (grid == null || template == null)
                return;

            Transform old = grid.Find(RowName);
            if (old != null)
            {
                old.name = RowName + " (old)";
                UnityEngine.Object.Destroy(old.gameObject);
            }

            if (Plugin.Style.Value == MenuStyle.Spotlight)
            {
                SpotlightCard.Build(panel, shown.Item);
                return;
            }
            SpotlightCard.Remove(panel);

            if (!Plugin.Enabled.Value)
                return;
            List<KeyValuePair<Item, ItemTemplate>> looked = Badges.Looked(shown.Item);
            if (looked.Count == 0)
                return;

            CompactCharacteristicPanel row = UnityEngine.Object.Instantiate(template, grid, false);
            row.name = RowName;
            row.gameObject.SetActive(true);
            row.transform.SetAsLastSibling();

            Image icon = row.Icon;
            TextMeshProUGUI name = row.NameText;
            TextMeshProUGUI value = row.ValueText;
            // its script would overwrite the texts
            UnityEngine.Object.Destroy(row);

            if (icon != null)
            {
                Sprite arrows = Art.Arrows;
                if (arrows != null)
                    icon.sprite = arrows;
                icon.color = Accent;
            }
            if (name != null)
                name.text = "TRANSMOG";

            bool single = looked.Count == 1 && looked[0].Key == shown.Item;
            if (value != null)
            {
                value.text = single ? Rules.ShortName(looked[0].Value) : $"{looked.Count} {(looked.Count == 1 ? "PART" : "PARTS")}";
                value.color = Accent;
            }

            string tip = Tip(shown.Item, looked);
            HoverTrigger hover = row.gameObject.GetOrAddComponent<HoverTrigger>();
            hover.OnHoverStart += _ =>
            {
                if (shown.Tooltip != null)
                    shown.Tooltip.Show(tip);
            };
            hover.OnHoverEnd += _ =>
            {
                if (shown.Tooltip != null && shown.Tooltip.isActiveAndEnabled)
                    shown.Tooltip.Close();
            };
        }

        private static string Tip(Item item, List<KeyValuePair<Item, ItemTemplate>> looked)
        {
            var sb = new StringBuilder();
            sb.Append("<color=#BE96FF>Looks like</color>");
            foreach (KeyValuePair<Item, ItemTemplate> pair in looked)
            {
                sb.Append('\n');
                if (pair.Key != item)
                    sb.Append(pair.Key.Template.ShortNameLocalizationKey.Localized()).Append("  →  ").Append(Rules.ShortName(pair.Value));
                else
                    sb.Append(Rules.Name(pair.Value));
            }
            sb.Append("\n<color=#8C8C8C>Only the look changes.</color>");
            return sb.ToString();
        }
    }
}
