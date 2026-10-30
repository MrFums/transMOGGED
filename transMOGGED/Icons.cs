using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Transmogged
{
    // never saved to disk: the game's icon cache outlives the mod
    internal static class Icons
    {
        private const int Salt = 0x7A4D06;

        private static readonly Dictionary<string, Item> Dummies = new Dictionary<string, Item>();
        private static bool _real;
        private static bool _look;
        private static Item _rendering;

        public static bool Peeking { get; set; }

        public static bool ShowLooks => Plugin.Enabled.Value && Plugin.ItemIcons.Value == IconMode.Look != Peeking;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ItemViewFactory), nameof(ItemViewFactory.LoadItemIcon)),
                prefix: new HarmonyMethod(typeof(Icons), nameof(LoadPrefix)));
            harmony.Patch(AccessTools.Method(typeof(ItemExtensions), nameof(ItemExtensions.CloneVisibleItem)).MakeGenericMethod(typeof(Item)),
                postfix: new HarmonyMethod(typeof(Icons), nameof(ClonePostfix)));
        }

        public static ItemIcon Real(Item item)
        {
            _real = true;
            try
            {
                return ItemViewFactory.LoadItemIcon(item);
            }
            finally
            {
                _real = false;
            }
        }

        public static ItemIcon Looked(Item item)
        {
            _look = true;
            try
            {
                return ItemViewFactory.LoadItemIcon(item);
            }
            finally
            {
                _look = false;
            }
        }

        public static ItemIcon Of(ItemTemplate look)
        {
            Item dummy = Dummy(look);
            return dummy != null ? ItemViewFactory.LoadItemIcon(dummy) : null;
        }

        private static Item Dummy(ItemTemplate look)
        {
            string id = look._id.ToString();
            if (Dummies.TryGetValue(id, out Item item))
                return item;
            try
            {
                item = Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(false), id, null);
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"No icon for {id}: {e.Message}");
            }
            Dummies[id] = item;
            return item;
        }

        private static bool LoadPrefix(Item item, ref ItemIcon __result)
        {
            try
            {
                if (_real || !(_look || ShowLooks))
                    return true;
                List<KeyValuePair<Item, ItemTemplate>> looked = Badges.Looked(item);
                if (looked.Count == 0 || !AllLoaded(looked))
                    return true;
                __result = Render(item, looked);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't draw the look icon of {item?.TemplateId}: {e.Message}");
                return true;
            }
        }

        // a model that isn't loaded renders as the real item and sticks in the cache
        private static bool AllLoaded(List<KeyValuePair<Item, ItemTemplate>> looked)
        {
            bool loaded = true;
            foreach (KeyValuePair<Item, ItemTemplate> pair in looked)
            {
                if (Visuals.Ready(pair.Value.Prefab))
                    continue;
                Visuals.Want(pair.Value.Prefab);
                loaded = false;
            }
            return loaded;
        }

        private static ItemIcon Render(Item item, List<KeyValuePair<Item, ItemTemplate>> looked)
        {
            ItemIconCreator creator = Singleton<ItemIconCreator>.Instance;
            int hash = Hash(item, looked);
            bool inRaid = InGameStatus.InRaid;
            if (creator.TryGetCachedIcon(hash, out ItemIcon cached) && (inRaid || !cached.IsGeneratedInRaid))
                return cached;

            var icon = new ItemIcon(hash) { IsGeneratedInRaid = inRaid };
            IntVec2 size = ItemViewFactory.GetCellPixelSize(item.CalculateCellSize());
            _rendering = item;
            try
            {
                creator.FillIconWithNewSpriteAsync(icon, item, size, false, true).HandleExceptions();
            }
            finally
            {
                _rendering = null;
            }
            return icon;
        }

        private static int Hash(Item item, List<KeyValuePair<Item, ItemTemplate>> looked)
        {
            unchecked
            {
                int hash = IconsHash.GetItemHash(item) ^ Salt;
                foreach (KeyValuePair<Item, ItemTemplate> pair in looked)
                    hash += pair.Key.TemplateId.GetHashCode() * 397 ^ pair.Value._id.GetHashCode();
                return hash;
            }
        }

        // the clone happens synchronously inside the Fill call
        private static void ClonePostfix(Item originalItem, Item __result)
        {
            if (_rendering != null && ReferenceEquals(_rendering, originalItem))
                Looks.Link(originalItem, __result);
        }
    }

    internal class IconBinder : MonoBehaviour
    {
        private Image _image;
        private ItemIcon _icon;

        public void Bind(Image image, ItemIcon icon)
        {
            _image = image;
            _icon = icon;
            Update();
        }

        private void Update()
        {
            Sprite sprite = _icon?.Sprite;
            if (_image == null || sprite == null || _image.sprite == sprite)
                return;
            _image.sprite = sprite;
            _image.color = Color.white;
        }
    }
}
