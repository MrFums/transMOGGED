using System;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using JsonType;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Transmogged
{
    internal class Picker : MonoBehaviour
    {
        private const int Columns = 6;
        private const float CellWidth = 132f;
        private const float CellHeight = 104f;
        private const float Gap = 3f;
        private const float ActionWidth = 120f;

        internal static readonly Color Frame = new Color32(58, 60, 61, 255);
        internal static readonly Color Body = new Color32(13, 14, 14, 250);
        private static readonly Color Bar = new Color32(27, 28, 28, 255);
        private static readonly Color Cell = new Color32(18, 19, 19, 255);
        private static readonly Color CellEdge = new Color32(54, 56, 57, 255);
        private static readonly Color CellHover = new Color32(110, 112, 110, 255);
        private static readonly Color Selected = new Color32(197, 195, 178, 255);
        internal static readonly Color TextMain = new Color32(200, 200, 196, 255);
        internal static readonly Color TextDim = new Color32(132, 134, 132, 255);
        private static readonly Color ButtonFill = new Color32(36, 38, 38, 255);

        private static Picker _instance;
        private static Vector2 _windowPosition;

        private Item _item;
        private GameObject _root;
        private TMP_FontAsset _font;
        private RectTransform _content;
        private RectTransform _viewport;
        private TextMeshProUGUI _state;
        private TextMeshProUGUI _count;
        private Button _apply;
        private string _pending;
        private readonly List<Tile> _tiles = new List<Tile>();

        private class Tile
        {
            public string Id;
            public ItemTemplate Look;
            public GameObject Go;
            public Image Edge;
            public Image Icon;
            public bool Requested;
            public string Search;
        }

        private void Awake()
        {
            _instance = this;
        }

        public static void Open(Item item)
        {
            if (Plugin.Style.Value == MenuStyle.Spotlight)
            {
                SpotlightPicker.Open(item);
                return;
            }
            if (_instance == null || item == null)
                return;
            try
            {
                _instance.Show(item);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Couldn't open the look picker: {e}");
                _instance.Close();
            }
        }

        private void Show(Item item)
        {
            Close();
            _item = item;
            SelectionBox.Allow(false);
            _font = FindFont();
            Looks.TryGet(item, out _pending);
            Build(Rules.Candidates(item));
            UpdateState();
        }

        private void Close()
        {
            _item = null;
            _tiles.Clear();
            if (_root != null)
                Destroy(_root);
            _root = null;
            SelectionBox.Allow(true);
        }

        private void Update()
        {
            if (_item == null)
                return;
            if (!Menu.CanPick || !Menu.IsMine(_item) || Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }
            LoadVisibleIcons();
        }


        private void Build(List<ItemTemplate> looks)
        {
            _root = new GameObject("transMOGGED.Picker", typeof(RectTransform));
            DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            _root.AddComponent<GraphicRaycaster>();
            var root = (RectTransform)_root.transform;

            Image dim = Panel(root, "Dim", new Color(0f, 0f, 0f, 0.55f));
            Stretch(dim.rectTransform);

            float width = Columns * (CellWidth + Gap) - Gap + 2 * 12f + 10f;
            Image frame = Panel(root, "Window", Frame);
            RectTransform win = frame.rectTransform;
            win.sizeDelta = new Vector2(width, 720f);
            win.anchoredPosition = _windowPosition;

            Image body = Panel(win, "Body", Body);
            Stretch(body.rectTransform, 1f);
            RectTransform b = body.rectTransform;

            Image bar = Panel(b, "Title", Bar);
            Top(bar.rectTransform, 0f, 30f);
            Sprite arrows = Art.Arrows;
            if (arrows != null)
            {
                Image icon = Img(bar.rectTransform, "Icon", arrows, TextMain);
                icon.preserveAspect = true;
                Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(18f, 18f), new Vector2(0f, 0.5f));
            }
            TextMeshProUGUI title = Text(bar.rectTransform, "TRANSMOG", 16f, Color.white, TextAlignmentOptions.MidlineLeft);
            Place(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(36f, 0f), new Vector2(width - 120f, 28f), new Vector2(0f, 0.5f));
            TitleButton(bar.rectTransform).onClick.AddListener(Close);
            var drag = bar.gameObject.AddComponent<DragWindow>();
            drag.Window = win;
            drag.Scale = canvas;
            drag.Moved = position => _windowPosition = position;

            TextMeshProUGUI name = Text(b, _item.LocalizedName(), 15f, TextMain, TextAlignmentOptions.TopLeft);
            Top(name.rectTransform, 40f, 20f, 12f);
            _state = Text(b, "", 13f, TextDim, TextAlignmentOptions.TopLeft);
            Top(_state.rectTransform, 62f, 18f, 12f);

            Image edge = Panel(b, "SearchEdge", CellEdge);
            Top(edge.rectTransform, 88f, 28f, 12f);
            Image field = Panel(edge.rectTransform, "Search", new Color32(8, 9, 9, 255));
            Stretch(field.rectTransform, 1f);
            Search(field.rectTransform);

            RectTransform scroll = Grid(b, looks);
            scroll.anchorMin = Vector2.zero;
            scroll.anchorMax = Vector2.one;
            scroll.offsetMin = new Vector2(12f, 54f);
            scroll.offsetMax = new Vector2(-12f, -126f);

            Button(b, "CLOSE", new Vector2(1f, 0f), new Vector2(-12f, 12f), new Vector2(1f, 0f), ActionWidth).onClick.AddListener(Close);
            _apply = Button(b, "APPLY", new Vector2(1f, 0f), new Vector2(-18f - ActionWidth, 12f), new Vector2(1f, 0f), ActionWidth);
            _apply.onClick.AddListener(ApplyPending);
            _count = Text(b, looks.Count == 0 ? "" : $"{looks.Count} LOOKS", 13f, TextDim, TextAlignmentOptions.TopRight);
            Place(_count.rectTransform, new Vector2(1f, 1f), new Vector2(-12f, -40f), new Vector2(200f, 20f), new Vector2(1f, 1f));

            if (looks.Count == 0)
            {
                TextMeshProUGUI none = Text(scroll, "NOTHING ELSE FITS WHERE THIS SITS\n<size=12>Parts only take looks that could really go in the same slot, with room for everything mounted on them.</size>",
                    15f, TextDim, TextAlignmentOptions.Center);
                Stretch(none.rectTransform);
            }
        }

        private RectTransform Grid(RectTransform parent, List<ItemTemplate> looks)
        {
            var go = new GameObject("Grid", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var scroll = go.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var view = new GameObject("Viewport", typeof(RectTransform));
            _viewport = (RectTransform)view.transform;
            _viewport.SetParent(rect, false);
            Stretch(_viewport);
            _viewport.offsetMax = new Vector2(-10f, 0f);
            view.AddComponent<RectMask2D>();
            view.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);

            var content = new GameObject("Content", typeof(RectTransform));
            _content = (RectTransform)content.transform;
            _content.SetParent(_viewport, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = Vector2.zero;
            var layout = content.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(CellWidth, CellHeight);
            layout.spacing = new Vector2(Gap, Gap);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = Columns;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = _viewport;
            scroll.content = _content;
            scroll.verticalScrollbar = Scrollbar(rect);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            Tile original = NewTile(null, "Original", new Color(0f, 0f, 0f, 0f), "original");
            original.Icon.gameObject.AddComponent<IconBinder>().Bind(original.Icon, Icons.Real(_item));
            _tiles.Add(original);

            if (Rules.IsGear(Rules.KindOf(_item)))
            {
                Tile hide = NewTile(Looks.Hidden, "Hide", new Color(0f, 0f, 0f, 0f), "hide");
                hide.Icon.sprite = Art.HiddenIcon;
                hide.Icon.color = TextDim;
                hide.Icon.rectTransform.offsetMin = new Vector2(40f, 20f);
                hide.Icon.rectTransform.offsetMax = new Vector2(-40f, -24f);
                _tiles.Add(hide);
            }

            foreach (ItemTemplate look in looks)
                _tiles.Add(TileFor(look));
            return rect;
        }

        private Tile TileFor(ItemTemplate look)
        {
            Tile tile = NewTile(look._id.ToString(), Rules.ShortName(look), RarityColour(look), (Rules.Name(look) + " " + Rules.ShortName(look)).ToLowerInvariant());
            tile.Look = look;
            return tile;
        }

        private Tile NewTile(string id, string caption, Color rarity, string search)
        {
            var tile = new Tile { Id = id, Search = search };

            Image edge = Panel(_content, "Cell", CellEdge);
            tile.Go = edge.gameObject;
            tile.Edge = edge;
            Image face = Panel(edge.rectTransform, "Face", Cell);
            Stretch(face.rectTransform, 1f);
            face.raycastTarget = false;
            Image tint = Img(face.rectTransform, "Rarity", null, rarity);
            Stretch(tint.rectTransform);

            tile.Icon = Img(face.rectTransform, "Icon", null, Color.clear);
            tile.Icon.preserveAspect = true;
            Stretch(tile.Icon.rectTransform);
            tile.Icon.rectTransform.offsetMin = new Vector2(8f, 6f);
            tile.Icon.rectTransform.offsetMax = new Vector2(-8f, -18f);

            TextMeshProUGUI label = Text(face.rectTransform, caption, 12f, TextMain, TextAlignmentOptions.TopRight);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(4f, 0f);
            label.rectTransform.offsetMax = new Vector2(-4f, -2f);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;

            var button = edge.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => Select(id));
            edge.gameObject.AddComponent<Hover>().OnChange = over => Paint(tile, over);
            return tile;
        }

        private static Color RarityColour(ItemTemplate look)
        {
            Color c = look.BackgroundColor.ToColor();
            c.a = 0.22f;
            return c;
        }

        private void Paint(Tile tile, bool over)
        {
            bool selected = _pending == tile.Id;
            tile.Edge.color = selected ? Selected : over ? CellHover : CellEdge;
        }

        private void Select(string templateId)
        {
            _pending = templateId;
            UpdateState();
        }

        private void ApplyPending()
        {
            Looks.Apply(_item, _pending);
            Close();
        }

        private void UpdateState()
        {
            Looks.TryGet(_item, out string currentId);
            ItemTemplate pending = Rules.Template(_pending);
            if (_state != null)
                _state.text = _pending == Looks.Hidden
                    ? "HIDDEN   <color=#6E706E>Not shown on your character. Stats, weight and everything else stay the real item's.</color>"
                    : pending == null
                    ? "ITS OWN LOOK   <color=#6E706E>Only the look changes. Stats, weight and everything else stay the real item's.</color>"
                    : Rules.Fits(_item, pending)
                        ? $"LOOKS LIKE  <color=#C5C3B2>{Rules.Name(pending).ToUpperInvariant()}</color>"
                        : $"<color=#C5C3B2>{Rules.Name(pending).ToUpperInvariant()}</color>  DOESN'T FIT WHERE IT IS NOW, SO ITS OWN LOOK SHOWS";
            if (_apply != null)
            {
                _apply.interactable = _pending != currentId;
                Color text = TextMain;
                text.a = _apply.interactable ? 1f : 0.3f;
                _apply.GetComponentInChildren<TextMeshProUGUI>().color = text;
            }
            foreach (Tile tile in _tiles)
                Paint(tile, false);
        }

        private void Filter(string text)
        {
            string needle = (text ?? "").Trim().ToLowerInvariant();
            int shown = 0, total = 0;
            foreach (Tile tile in _tiles)
            {
                bool on = needle.Length == 0 || tile.Search.Contains(needle);
                tile.Go.SetActive(on);
                if (tile.Look == null)
                    continue;
                total++;
                if (on)
                    shown++;
            }
            if (_count != null && total > 0)
                _count.text = needle.Length == 0 ? $"{total} LOOKS" : $"{shown} OF {total} LOOKS";
        }

        private void LoadVisibleIcons()
        {
            if (_viewport == null)
                return;
            var corners = new Vector3[4];
            _viewport.GetWorldCorners(corners);
            float top = corners[1].y, bottom = corners[0].y;
            foreach (Tile tile in _tiles)
            {
                if (tile.Look == null || tile.Requested || !tile.Go.activeInHierarchy)
                    continue;
                ((RectTransform)tile.Go.transform).GetWorldCorners(corners);
                if (corners[0].y > top || corners[1].y < bottom)
                    continue;
                tile.Requested = true;
                tile.Icon.gameObject.AddComponent<IconBinder>().Bind(tile.Icon, Icons.Of(tile.Look));
            }
        }


        private void Search(RectTransform field)
        {
            var area = new GameObject("Text Area", typeof(RectTransform));
            var areaRect = (RectTransform)area.transform;
            areaRect.SetParent(field, false);
            Stretch(areaRect);
            areaRect.offsetMin = new Vector2(10f, 2f);
            areaRect.offsetMax = new Vector2(-10f, -2f);
            area.AddComponent<RectMask2D>();

            TextMeshProUGUI placeholder = Text(areaRect, "SEARCH", 14f, TextDim, TextAlignmentOptions.MidlineLeft);
            Stretch(placeholder.rectTransform);
            placeholder.margin = new Vector4(3f, 0f, 0f, 0f);
            TextMeshProUGUI text = Text(areaRect, "", 14f, Color.white, TextAlignmentOptions.MidlineLeft);
            Stretch(text.rectTransform);
            text.enableWordWrapping = false;
            text.margin = new Vector4(3f, 0f, 0f, 0f);

            // inactive while it's set up: it makes its caret in OnEnable, which needs the text component
            field.gameObject.SetActive(false);
            var input = field.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = field.GetComponent<Image>();
            input.textViewport = areaRect;
            input.textComponent = text;
            input.placeholder = placeholder;
            if (_font != null)
                input.fontAsset = _font;
            input.customCaretColor = true;
            input.caretColor = TextMain;
            input.caretWidth = 2;
            input.selectionColor = new Color(0.5f, 0.5f, 0.45f, 0.5f);
            input.onValueChanged.AddListener(Filter);
            field.gameObject.SetActive(true);
        }

        private Button Button(RectTransform parent, string label, Vector2 anchor, Vector2 position, Vector2 pivot, float width = 180f)
        {
            Image edge = Panel(parent, label, CellEdge);
            Place(edge.rectTransform, anchor, position, new Vector2(width, 30f), pivot);
            Image fill = Panel(edge.rectTransform, "Fill", ButtonFill);
            Stretch(fill.rectTransform, 1f);
            TextMeshProUGUI text = Text(fill.rectTransform, label, 14f, TextMain, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);

            var button = edge.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            button.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(1.8f, 1.8f, 1.75f, 1f),
                pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f),
                selectedColor = Color.white,
                disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };
            return button;
        }

        private Button TitleButton(RectTransform bar)
        {
            Image fill = Panel(bar, "Close", new Color32(116, 22, 18, 255));
            Place(fill.rectTransform, new Vector2(1f, 0.5f), new Vector2(-3f, 0f), new Vector2(26f, 24f), new Vector2(1f, 0.5f));
            TextMeshProUGUI cross = Text(fill.rectTransform, "X", 16f, Color.white, TextAlignmentOptions.Center);
            Stretch(cross.rectTransform);
            var button = fill.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            return button;
        }

        private static Scrollbar Scrollbar(RectTransform parent)
        {
            Image track = Panel(parent, "Scrollbar", new Color32(22, 23, 23, 255));
            RectTransform rect = track.rectTransform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(6f, 0f);
            Image handle = Panel(rect, "Handle", new Color32(84, 86, 86, 255));
            Stretch(handle.rectTransform);
            var bar = track.gameObject.AddComponent<Scrollbar>();
            bar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
            bar.handleRect = handle.rectTransform;
            bar.targetGraphic = handle;
            return bar;
        }

        private static Image Panel(RectTransform parent, string name, Color color)
        {
            Image image = Img(parent, name, null, color);
            image.raycastTarget = true;
            return image;
        }

        private static Image Img(RectTransform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private TextMeshProUGUI Text(RectTransform parent, string value, float size, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (_font != null)
                text.font = _font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.richText = true;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void Top(RectTransform rect, float y, float height, float inset = 0f)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(-2f * inset, height);
            rect.anchoredPosition = new Vector2(0f, -y);
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static TMP_FontAsset FindFont()
        {
            foreach (TextMeshProUGUI text in FindObjectsOfType<TextMeshProUGUI>())
            {
                if (text != null && text.font != null && text.isActiveAndEnabled)
                    return text.font;
            }
            return null;
        }
    }

    // UI Fixes can't see our canvas and draws its selection box over it
    internal static class SelectionBox
    {
        private static readonly Type Drawer = AccessTools.TypeByName("UIFixes.DrawMultiSelect");

        public static void Allow(bool allowed)
        {
            if (Drawer == null)
                return;
            foreach (UnityEngine.Object drawer in Resources.FindObjectsOfTypeAll(Drawer))
                ((Behaviour)drawer).enabled = allowed;
        }
    }

    internal class DragWindow : MonoBehaviour, IDragHandler
    {
        public RectTransform Window;
        public Canvas Scale;
        public Action<Vector2> Moved;

        public void OnDrag(PointerEventData eventData)
        {
            var canvas = (RectTransform)Scale.transform;
            Vector2 room = (canvas.rect.size - Window.rect.size) / 2f;
            Vector2 position = Window.anchoredPosition + eventData.delta / Scale.scaleFactor;
            Window.anchoredPosition = new Vector2(Mathf.Clamp(position.x, -room.x, room.x), Mathf.Clamp(position.y, -room.y, room.y));
            Moved(Window.anchoredPosition);
        }
    }

    internal class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action<bool> OnChange;

        public void OnPointerEnter(PointerEventData eventData) => OnChange?.Invoke(true);

        public void OnPointerExit(PointerEventData eventData) => OnChange?.Invoke(false);
    }
}
