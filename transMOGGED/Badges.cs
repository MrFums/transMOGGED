using System.Collections.Generic;
using System;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using EFT;
using HarmonyLib;
using TMPro;
using UnityEngine.UI;
using UnityEngine;

namespace Transmogged
{
    internal static class Badges
    {
        private const string BadgeName = "transMOGGED.Badge";
        private const float PlaceInterval = 0.5f;
        private const float CompactWidth = 100f;

        private static readonly HashSet<ItemView> Views = new HashSet<ItemView>();
        private static readonly HashSet<ItemView> Marked = new HashSet<ItemView>();
        private static float _nextPlace;

        public static void Apply(Harmony harmony)
        {
            var target = AccessTools.Method(typeof(ItemView), nameof(ItemView.NewItemView))
                         ?? throw new MissingMethodException(nameof(ItemView), nameof(ItemView.NewItemView));
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(Badges), nameof(Postfix)));
        }

        private static void Postfix(ItemView __instance)
        {
            Views.Add(__instance);
            Guarded(__instance, false);
        }

        public static void Refresh()
        {
            Views.RemoveWhere(v => v == null);
            foreach (ItemView view in Views)
                Guarded(view, true);
        }

        private static void Guarded(ItemView view, bool refreshIcon)
        {
            try
            {
                Update(view, refreshIcon);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't mark an item view: {e.Message}");
            }
        }

        private static void Update(ItemView view, bool refreshIcon)
        {
            Item item = view.Item;
            bool marked = Plugin.Enabled.Value && ShowsLook(item);
            bool wasMarked = Marked.Contains(view);
            if (marked)
                Marked.Add(view);
            else
                Marked.Remove(view);

            SetTag(view, marked && Plugin.ShowBadge.Value);
            if (marked)
                Place(view);

            if (refreshIcon && item != null && (wasMarked || Looked(item).Count > 0))
                view.RefreshIcon();
        }

        public static void Tick()
        {
            bool peeking = Menu.CanPeek && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));
            if (peeking != Icons.Peeking)
            {
                Icons.Peeking = peeking;
                Marked.RemoveWhere(v => v == null);
                foreach (ItemView view in new List<ItemView>(Marked))
                {
                    if (view.isActiveAndEnabled)
                        Guarded(view, true);
                }
            }

            LookTip.Update(Marked, BadgeName);

            if (Time.realtimeSinceStartup < _nextPlace)
                return;
            _nextPlace = Time.realtimeSinceStartup + PlaceInterval;
            Marked.RemoveWhere(v => v == null);
            foreach (ItemView view in Marked)
            {
                if (view.isActiveAndEnabled)
                    Place(view);
            }
        }

        private static void Place(ItemView view)
        {
            float x = 3f;
            if (view is GridItemView grid && grid._bindPanel != null && grid._bindPanel.gameObject.activeInHierarchy
                && grid._bindPanel.transform is RectTransform bind)
                x += bind.rect.width + 2f;

            Transform badge = view.transform.Find(BadgeName);
            if (badge == null)
                return;
            bool compact = ((RectTransform)view.transform).rect.width < CompactWidth;
            foreach (Transform part in badge)
                part.gameObject.SetActive(part.name == "Corner" == compact);
            ((RectTransform)badge).anchoredPosition = compact ? new Vector2(x - 2f, -1f) : new Vector2(x, -2f);
            badge.SetAsLastSibling();

        }

        private static bool ShowsLook(Item item)
        {
            if (item == null)
                return false;
            if (Looks.IsHidden(item))
                return true;
            if (!(item.CurrentAddress?.Container is Slot slot && (slot.ParentItem is Mod || slot.ParentItem is Weapon)))
                return Looked(item).Count > 0;
            return Looks.TryGet(item, out string templateId) && Rules.Fits(item, Rules.Template(templateId));
        }

        public static List<KeyValuePair<Item, ItemTemplate>> Looked(Item item)
        {
            var result = new List<KeyValuePair<Item, ItemTemplate>>();
            if (item == null)
                return result;
            foreach (Item each in item.GetAllItems((IContainer container) => container is Slot))
            {
                if (!Looks.TryGet(each, out string templateId))
                    continue;
                ItemTemplate look = Rules.Template(templateId);
                if (look != null && Rules.Fits(each, look))
                    result.Add(new KeyValuePair<Item, ItemTemplate>(each, look));
            }
            return result;
        }

        private static void SetTag(ItemView view, bool show)
        {
            Transform tag = view.transform.Find(BadgeName);
            if (tag == null && show)
                tag = CreateTag(view);
            if (tag != null)
                tag.gameObject.SetActive(show);
        }

        private static Transform CreateTag(ItemView view)
        {
            var go = new GameObject(BadgeName, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(view.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(18f, 16f);

            Image glow = Child(rect, "Glow", Art.Glow, Art.Violet, new Vector2(-8f, -8f), new Vector2(8f, 8f));
            Child(rect, "Hex", Art.Hex, Color.white, Vector2.zero, Vector2.zero).preserveAspect = true;
            Sprite arrows = Art.Arrows;
            if (arrows != null)
                Child(rect, "Arrows", arrows, Color.white, new Vector2(4f, 3.5f), new Vector2(-4f, -3.5f)).preserveAspect = true;
            Image corner = Child(rect, "Corner", Art.Corner, Art.Violet, Vector2.zero, Vector2.zero);
            corner.rectTransform.anchorMin = corner.rectTransform.anchorMax = corner.rectTransform.pivot = new Vector2(0f, 1f);
            corner.rectTransform.sizeDelta = new Vector2(11f, 11f);
            rect.gameObject.AddComponent<Breathe>().Glow = glow;
            return rect;
        }

        private static Image Child(RectTransform parent, string name, Sprite sprite, Color color, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }
    }

    internal class Breathe : MonoBehaviour
    {
        public Image Glow;
        private float _offset;

        private void Awake() => _offset = UnityEngine.Random.value * 6f;

        private void Update()
        {
            if (Glow == null)
                return;
            float t = (Mathf.Sin((Time.unscaledTime + _offset) * 2.2f) + 1f) * 0.5f;
            Color c = Glow.color;
            c.a = Mathf.Lerp(0.18f, 0.6f, t);
            Glow.color = c;
        }
    }

    internal static class LookTip
    {
        private const float Width = 196f;
        private const float Height = 132f;
        private const float Padding = 8f;

        private static GameObject _root;
        private static RectTransform _panel;
        private static Canvas _canvas;
        private static Image _icon;
        private static TextMeshProUGUI _title;
        private static TextMeshProUGUI _name;
        private static ItemView _shown;

        public static void Update(IEnumerable<ItemView> views, string tagName)
        {
            ItemView hovered = null;
            foreach (ItemView view in views)
            {
                if (view != null && view.isActiveAndEnabled && Over(view, tagName))
                {
                    hovered = view;
                    break;
                }
            }

            if (hovered != _shown)
            {
                _shown = hovered;
                if (hovered == null)
                    _root.SetActive(false);
                else
                    Show(hovered);
            }
            if (hovered != null)
                Follow();
        }

        private static bool Over(ItemView view, string tagName)
        {
            var tag = view.transform.Find(tagName) as RectTransform;
            if (tag == null || !tag.gameObject.activeInHierarchy)
                return false;
            Canvas canvas = tag.GetComponentInParent<Canvas>()?.rootCanvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(tag, Input.mousePosition, camera);
        }

        private static void Show(ItemView view)
        {
            if (_root == null)
                Build(view.GetComponentInChildren<TextMeshProUGUI>(true)?.font);

            Item item = view.Item;
            if (Looks.IsHidden(item))
            {
                _title.text = "HIDDEN";
                _name.text = "Not shown on your character";
                _icon.color = Color.clear;
                _icon.sprite = null;
                _icon.GetComponent<IconBinder>().Bind(_icon, null);
                _root.SetActive(true);
                return;
            }

            bool real = Icons.ShowLooks;
            List<KeyValuePair<Item, ItemTemplate>> looked = Badges.Looked(item);
            _title.text = real ? "REAL ITEM" : "LOOKS LIKE";
            _name.text = real ? item.LocalizedName() : looked.Count == 1 ? Rules.Name(looked[0].Value) : $"{looked.Count} PARTS";
            _icon.color = Color.clear;
            _icon.sprite = null;
            _icon.GetComponent<IconBinder>().Bind(_icon, real ? Icons.Real(item) : Icons.Looked(item));
            _root.SetActive(true);
        }

        private static void Follow()
        {
            float scale = _canvas.scaleFactor;
            Vector2 mouse = Input.mousePosition;
            float x = mouse.x + 20f * scale;
            if (x + Width * scale > Screen.width)
                x = mouse.x - (20f + Width) * scale;
            float y = Mathf.Max(mouse.y - 8f * scale, Height * scale);
            _panel.position = new Vector3(x, y, 0f);
        }

        private static void Build(TMP_FontAsset font)
        {
            _root = new GameObject("transMOGGED.LookTip", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 30001;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            _panel = Rect("Frame", _root.transform);
            _panel.anchorMin = _panel.anchorMax = Vector2.zero;
            _panel.pivot = new Vector2(0f, 1f);
            _panel.sizeDelta = new Vector2(Width, Height);
            Fill(_panel, Picker.Frame);

            RectTransform body = Rect("Body", _panel);
            Stretch(body, 1f, 1f, 1f, 1f);
            Fill(body, Picker.Body);

            _title = Text(body, font, 11f, Picker.TextDim, "");
            Stretch(_title.rectTransform, Padding, Padding, Height - 24f, 6f);

            RectTransform icon = Rect("Icon", body);
            Stretch(icon, Padding, Padding, 26f, 24f);
            _icon = icon.gameObject.AddComponent<Image>();
            _icon.preserveAspect = true;
            _icon.raycastTarget = false;
            icon.gameObject.AddComponent<IconBinder>();

            _name = Text(body, font, 13f, Picker.TextMain, "");
            Stretch(_name.rectTransform, Padding, Padding, 6f, Height - 26f);
            _name.enableWordWrapping = false;
            _name.overflowMode = TextOverflowModes.Ellipsis;
            _root.SetActive(false);
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Fill(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static void Stretch(RectTransform rect, float left, float right, float bottom, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static TextMeshProUGUI Text(RectTransform parent, TMP_FontAsset font, float size, Color color, string value)
        {
            RectTransform rect = Rect("Text", parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
                text.font = font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;
            return text;
        }
    }
}
