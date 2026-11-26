using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace Transmogged
{
    // 7Bpencil's Camo And Stickers, found at run time so nothing here needs it installed. Its paint is kept per item
    // and drawn onto whatever model the item has, so a look gets the paint too. Two things need a hand:
    // - its decals are drawn by the camera, not by the model, so hidden gear would still paint whatever is behind it
    // - look icons are drawn by transMOGGED, so they go through its icon drawing to show the paint
    internal static class Camo
    {
        public const string Guid = "7Bpencil.WeaponCamoAndStickers";

        private static bool _resolved;
        private static object _plugin;
        private static MethodInfo _count;
        private static MethodInfo _destroyed;
        private static MethodInfo _fill;

        private static bool Resolve()
        {
            if (_resolved)
                return _plugin != null;
            _resolved = true;
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(Guid, out PluginInfo info) || info.Instance == null)
                    return false;

                Type type = info.Instance.GetType();
                _count = type.GetMethod("GetDecalsCount", new[] { typeof(string) });
                _destroyed = type.GetMethod("OnItemDestroyed", new[] { typeof(int) });
                _fill = type.Assembly.GetType("SevenBoldPencil.WeaponCamoAndStickers.Patch_ItemIconCreator_GetItemIcon")
                    ?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "FillIconWithNewSpriteAsync" && m.GetParameters().Length == 6);
                if (_count == null || _destroyed == null)
                {
                    Plugin.Log.LogWarning("This Camo And Stickers version isn't one transMOGGED knows, hidden gear may still show its paint.");
                    return false;
                }
                _plugin = info.Instance;
                Plugin.Log.LogInfo("Camo And Stickers found, its paint goes on looks too.");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't hook up Camo And Stickers: {e.Message}");
                return false;
            }
        }

        private static bool HasPaint(Item item) => Resolve() && item != null && (int)_count.Invoke(_plugin, new object[] { item.Id }) > 0;

        // removes the decals it put on this model; a model made for the item later gets them again
        public static void Unpaint(GameObject model)
        {
            try
            {
                if (model != null && Resolve())
                    _destroyed.Invoke(_plugin, new object[] { model.GetInstanceID() });
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't take the paint off hidden gear: {e.Message}");
            }
        }

        // draws the icon with the item's paint on it; false when there is no paint, and the game draws it as usual
        public static bool FillIcon(ItemIconCreator creator, ItemIcon icon, Item item, IntVec2 size)
        {
            try
            {
                if (!HasPaint(item) || _fill == null)
                    return false;
                ((Task)_fill.Invoke(null, new object[] { creator, icon, item, size, false, true })).HandleExceptions();
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't draw the paint on the look icon of {item?.TemplateId}: {e.Message}");
                return false;
            }
        }
    }
}
