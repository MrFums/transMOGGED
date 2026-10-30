using System.Collections.Generic;
using System.Linq;
using System;
using EFT.InventoryLogic;
using EFT.UI;
using EFT;
using TMPro;
using UnityEngine.UI;
using UnityEngine;

namespace Transmogged
{
    internal static class SpotlightCard
    {
        private const string CardName = "transMOGGED.Card";
        private const float RowHeight = 46f;
        private const float CompactRowHeight = 32f;
        private const int CompactFrom = 4;
        private const float HeaderHeight = 20f;
        private const float Padding = 8f;
        private const float StripHeight = 28f;
        private const float TopClearance = 42f;

        private static bool _expanded;

        public static void Remove(ItemSpecificationPanel panel)
        {
            Transform parent = panel._compactPanel != null ? panel._compactPanel.parent : null;
            Remove(parent);
            Remove(panel._cameraImage != null ? panel._cameraImage.transform : null);
        }

        private static void Remove(Transform parent)
        {
            Transform old = parent != null ? parent.Find(CardName) : null;
            if (old == null)
                return;
            old.name = CardName + " (old)";
            UnityEngine.Object.Destroy(old.gameObject);
        }

        public static void Build(ItemSpecificationPanel panel, Item item)
        {
            Transform anchor = panel._compactPanel;
            Transform parent = anchor != null ? anchor.parent : null;
            if (parent == null)
                return;
            var preview = panel._cameraImage != null ? panel._cameraImage.transform as RectTransform : null;
            Remove(panel);

            if (!Plugin.Enabled.Value)
                return;
            List<KeyValuePair<Item, ItemTemplate>> looked = Badges.Looked(item);
            if (looked.Count == 0)
                return;

            TMP_FontAsset font = panel.GetComponentInChildren<TextMeshProUGUI>(true)?.font;
            bool floating = item is Weapon && preview != null;

            var card = new GameObject(CardName, typeof(RectTransform));
            var rect = (RectTransform)card.transform;
            if (floating)
            {
                rect.SetParent(preview, false);
                rect.SetAsLastSibling();
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                card.AddComponent<LayoutElement>().ignoreLayout = true;
            }
            else
            {
                rect.SetParent(parent, false);
                Transform mods = parent.Find("ModsPanel");
                rect.SetSiblingIndex(mods != null ? mods.GetSiblingIndex() : anchor.GetSiblingIndex() + 1);
            }
            var bg = card.AddComponent<Image>();
            bg.sprite = Art.Panel;
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;

            var layout = card.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset((int)Padding + 2, (int)Padding, (int)Padding - 2, (int)Padding);
            layout.spacing = 2f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            bool foldable = floating || looked.Count >= CompactFrom;
            bool open = !foldable || _expanded;
            float rowHeight = foldable ? CompactRowHeight : RowHeight;
            if (floating && open)
            {
                float room = preview.rect.height > 0f ? preview.rect.height - TopClearance : 240f;
                float fit = (room - Padding * 2f - HeaderHeight) / looked.Count - 2f;
                rowHeight = Mathf.Clamp(fit, 20f, CompactRowHeight);
            }
            float height = Padding * 2f + HeaderHeight + (open ? looked.Count * (rowHeight + 2f) : StripHeight + 2f);
            if (floating)
            {
                rect.sizeDelta = new Vector2(-12f, height);
                rect.anchoredPosition = new Vector2(0f, 6f);
            }
            else
            {
                var size = card.AddComponent<LayoutElement>();
                size.minHeight = height;
                size.preferredHeight = height;
                size.flexibleWidth = 1f;
            }

            Header(rect, font, foldable ? (open ? "HIDE" : $"{looked.Count} PARTS  ·  SHOW") : null, () =>
            {
                _expanded = !_expanded;
                Build(panel, item);
            });
            if (open)
            {
                foreach (KeyValuePair<Item, ItemTemplate> pair in looked)
                    Row(rect, pair.Key, pair.Value, font, rowHeight);
            }
            else
            {
                Strip(rect, looked);
            }
        }

        private static void Header(RectTransform card, TMP_FontAsset font, string toggle, Action onToggle)
        {
            RectTransform row = Line(card, "Header", HeaderHeight, 6f);
            Sprite arrows = Art.Arrows;
            if (arrows != null)
                Icon(row, arrows, Art.Violet, 16f, 16f);
            Text(row, "TRANSMOG", font, 13f, Art.Violet, FontStyles.Bold, 1f);
            if (toggle == null)
                return;

            Text(row, toggle, font, 11f, Art.VioletEdge, FontStyles.Bold, 0f);
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0.001f);
            var button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.onClick.AddListener(() => onToggle());
        }

        private static void Strip(RectTransform card, List<KeyValuePair<Item, ItemTemplate>> looked)
        {
            RectTransform row = Line(card, "Strip", StripHeight, 4f);
            row.gameObject.AddComponent<RectMask2D>();
            foreach (KeyValuePair<Item, ItemTemplate> pair in looked)
            {
                Image icon = Icon(row, null, Color.white, StripHeight * 1.4f, StripHeight - 2f);
                icon.gameObject.AddComponent<IconBinder>().Bind(icon, Icons.Of(pair.Value));
            }
        }

        private static void Row(RectTransform card, Item real, ItemTemplate look, TMP_FontAsset font, float height)
        {
            RectTransform row = Line(card, "Row", height, 8f);
            float iconWidth = height * 1.65f;

            Image from = Icon(row, null, Color.white, iconWidth, height - 4f);
            from.gameObject.AddComponent<IconBinder>().Bind(from, Icons.Real(real));

            Sprite arrows = Art.Arrows;
            if (arrows != null)
                Icon(row, arrows, Art.VioletEdge, 18f, 18f);

            Image to = Icon(row, null, Color.white, iconWidth, height - 4f);
            to.gameObject.AddComponent<IconBinder>().Bind(to, Icons.Of(look));

            Text(row, $"{real.Template.ShortNameLocalizationKey.Localized()}  <color=#B98CFF>→</color>  {Rules.ShortName(look)}\n"
                      + $"<size=10><color=#A8A0B8>{Rules.Name(look)}</color></size>", font, 12f, Color.white, FontStyles.Normal, 1f);
        }

        private static RectTransform Line(RectTransform parent, string name, float height, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            var size = go.AddComponent<LayoutElement>();
            size.minHeight = height;
            size.preferredHeight = height;
            return rect;
        }

        private static Image Icon(RectTransform parent, Sprite sprite, Color color, float width, float height)
        {
            var go = new GameObject("Icon", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = sprite == null ? Color.clear : color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            var size = go.AddComponent<LayoutElement>();
            size.minWidth = size.preferredWidth = width;
            size.minHeight = size.preferredHeight = height;
            return image;
        }

        private static void Text(RectTransform parent, string value, TMP_FontAsset font, float fontSize, Color color, FontStyles style, float flexible)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
                text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.richText = true;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;
            var size = go.AddComponent<LayoutElement>();
            size.flexibleWidth = flexible;
        }
    }

    internal class SpotlightPicker : MonoBehaviour
    {
        private const int Columns = 5;
        private const float TileWidth = 132f;
        private const float TileHeight = 104f;
        private const float IconHeight = 72f;

        private static SpotlightPicker _instance;

        private Item _item;
        private List<ItemTemplate> _all = new List<ItemTemplate>();
        private List<ItemTemplate> _shown = new List<ItemTemplate>();
        private string _search = "";
        private string _lastSearch;
        private Vector2 _scroll;
        private string _pending;
        private Rect _window;
        private GameObject _blocker;

        private readonly Dictionary<string, ItemIcon> _icons = new Dictionary<string, ItemIcon>();

        private GUIStyle _windowStyle;
        private GUIStyle _tile;
        private GUIStyle _tileSelected;
        private GUIStyle _caption;
        private GUIStyle _header;
        private GUIStyle _note;

        private void Awake()
        {
            _instance = this;
        }

        public static void Open(Item item)
        {
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
            _item = item;
            Looks.TryGet(item, out _pending);
            _all = Rules.Candidates(item);
            _search = "";
            _lastSearch = null;
            _scroll = Vector2.zero;

            float width = Columns * (TileWidth + 6f) + 46f;
            float height = Mathf.Min(680f, VirtualHeight - 80f);
            _window = new Rect((VirtualWidth - width) / 2f, (VirtualHeight - height) / 2f, width, height);
            Block(true);
        }

        private void Close()
        {
            _item = null;
            _all.Clear();
            _shown.Clear();
            _icons.Clear();
            Block(false);
        }

        private void Block(bool on)
        {
            SelectionBox.Allow(!on);
            if (!on)
            {
                if (_blocker != null)
                    Destroy(_blocker);
                _blocker = null;
                return;
            }
            if (_blocker != null)
                return;

            _blocker = new GameObject("transMOGGED.Blocker", typeof(RectTransform));
            DontDestroyOnLoad(_blocker);
            var canvas = _blocker.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            _blocker.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel", typeof(RectTransform));
            var rect = (RectTransform)panel.transform;
            rect.SetParent(_blocker.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = panel.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.35f);
            image.raycastTarget = true;
        }

        private void Update()
        {
            if (_item == null)
                return;
            if (!Menu.CanPick || !Menu.IsMine(_item) || Input.GetKeyDown(KeyCode.Escape))
                Close();
        }

        private void OnGUI()
        {
            if (_item == null)
                return;
            Styles();
            GUI.depth = -1000;
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Scale, Scale, 1f));
            _window = GUI.Window(0x7A4D06, _window, Draw, "transMOGGED", _windowStyle);
            GUI.matrix = old;
        }

        private static float Scale => Mathf.Max(1f, Screen.height / 1080f);
        private static float VirtualWidth => Screen.width / Scale;
        private static float VirtualHeight => Screen.height / Scale;

        private void Draw(int id)
        {
            ItemTemplate current = Rules.Template(_pending);

            GUILayout.Space(4f);
            GUILayout.Label($"{_item.LocalizedName()}", _header);
            GUILayout.Label(_pending == Looks.Hidden
                ? "Hidden on your character."
                : current == null
                ? "Its own look."
                : Rules.Fits(_item, current)
                    ? $"Looks like: {Rules.Name(current)}"
                    : $"Picked look {Rules.Name(current)} doesn't fit where it is now, so its own look shows.", _note);
            GUILayout.Label("Only the look changes. Stats, weight and everything else stay the real item's.", _note);

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", GUILayout.Width(50f));
            _search = GUILayout.TextField(_search ?? "", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            Filter();

            GUILayout.Space(6f);
            if (_shown.Count == 0)
            {
                GUILayout.Label(_all.Count == 0
                    ? "Nothing else fits where this sits. Parts only take looks that could really go in the same slot, "
                      + "with room for everything mounted on them."
                    : "No matches.", _note);
                GUILayout.FlexibleSpace();
            }
            else
            {
                Grid(_pending);
            }

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUI.enabled = _pending != null;
            if (GUILayout.Button("Original look", GUILayout.Height(28f), GUILayout.Width(140f)))
                _pending = null;
            GUI.enabled = true;
            if (Rules.IsGear(Rules.KindOf(_item)) && GUILayout.Button("Hide", GUILayout.Height(28f), GUILayout.Width(90f)))
                _pending = Looks.Hidden;
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{_shown.Count} looks", _note);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Apply", GUILayout.Height(28f), GUILayout.Width(110f)))
                ApplyPending();
            if (_item != null && GUILayout.Button("Close", GUILayout.Height(28f), GUILayout.Width(110f)))
                Close();
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        private void ApplyPending()
        {
            Looks.Apply(_item, _pending);
            Close();
        }

        private void Filter()
        {
            if (_search == _lastSearch)
                return;
            _lastSearch = _search;
            string needle = _search.Trim();
            _shown = needle.Length == 0
                ? _all
                : _all.Where(t => Rules.Name(t).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
                                  || Rules.ShortName(t).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        private void Grid(string currentId)
        {
            int rows = (_shown.Count + Columns - 1) / Columns;
            _scroll = GUILayout.BeginScrollView(_scroll, false, true, GUILayout.ExpandHeight(true));

            float rowHeight = TileHeight + 6f;
            float viewHeight = _window.height - 220f;
            int first = Mathf.Max(0, (int)(_scroll.y / rowHeight) - 1);
            int last = Mathf.Min(rows - 1, (int)((_scroll.y + viewHeight) / rowHeight) + 1);

            GUILayout.Space(first * rowHeight);
            for (int row = first; row <= last; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < Columns; col++)
                {
                    int index = row * Columns + col;
                    if (index >= _shown.Count)
                        break;
                    Tile(_shown[index], currentId);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(6f);
            }
            GUILayout.Space(Mathf.Max(0, rows - last - 1) * rowHeight);
            GUILayout.EndScrollView();
        }

        private void Tile(ItemTemplate look, string currentId)
        {
            bool selected = look._id.ToString() == currentId;
            Rect rect = GUILayoutUtility.GetRect(TileWidth, TileHeight, GUILayout.Width(TileWidth), GUILayout.Height(TileHeight));
            if (GUI.Button(rect, GUIContent.none, selected ? _tileSelected : _tile))
                _pending = look._id.ToString();

            Sprite sprite = IconFor(look);
            if (sprite != null && sprite.texture != null)
                Draw(new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, IconHeight), sprite);
            GUI.Label(new Rect(rect.x + 4f, rect.y + IconHeight + 8f, rect.width - 8f, TileHeight - IconHeight - 10f),
                Rules.ShortName(look), _caption);
        }

        private static void Draw(Rect area, Sprite sprite)
        {
            Rect texRect = sprite.textureRect;
            Texture tex = sprite.texture;
            float aspect = texRect.width / Mathf.Max(1f, texRect.height);
            float w = area.width, h = area.width / aspect;
            if (h > area.height)
            {
                h = area.height;
                w = h * aspect;
            }
            var target = new Rect(area.x + (area.width - w) / 2f, area.y + (area.height - h) / 2f, w, h);
            var uv = new Rect(texRect.x / tex.width, texRect.y / tex.height, texRect.width / tex.width, texRect.height / tex.height);
            GUI.DrawTextureWithTexCoords(target, tex, uv);
        }

        private Sprite IconFor(ItemTemplate look)
        {
            string key = look._id.ToString();
            if (!_icons.TryGetValue(key, out ItemIcon icon))
            {
                icon = Icons.Of(look);
                _icons[key] = icon;
            }
            return icon?.Sprite;
        }

        private static Texture2D Solid(Color color)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private void Styles()
        {
            if (_tile != null)
                return;
            _tile = new GUIStyle(GUI.skin.button);
            _tileSelected = new GUIStyle(GUI.skin.button);
            Texture2D purple = Solid(new Color(0.45f, 0.25f, 0.7f, 0.9f));
            _tileSelected.normal.background = purple;
            _tileSelected.hover.background = purple;
            _windowStyle = new GUIStyle(GUI.skin.window);
            Texture2D dark = Solid(new Color(0.09f, 0.1f, 0.1f, 0.98f));
            _windowStyle.normal.background = dark;
            _windowStyle.onNormal.background = dark;
            _windowStyle.normal.textColor = Color.white;
            _windowStyle.onNormal.textColor = Color.white;
            _caption = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                wordWrap = true,
                fontSize = 11,
                clipping = TextClipping.Clip
            };
            _header = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            _note = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            _note.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        }
    }
}
