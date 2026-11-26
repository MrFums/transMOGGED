using System;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using PlayerIcons;

namespace Transmogged
{
    // Your portrait on the trader screens. The game draws it from a copy of your gear, with new item ids, and keeps it
    // (also on disk) under a hash of the real items. So the copy gets your looks, and the hash gets them too, once
    // every look is loaded: a picture drawn while one isn't would be kept under the wrong hash for good.
    internal static class Portrait
    {
        private const int Salt = 0x5A1E7;

        private static readonly FieldInfo HashField = AccessTools.Field(typeof(PlayerIconRequest), nameof(PlayerIconRequest.hash));

        private static Item _source;

        public static void Apply(Harmony harmony)
        {
            if (HashField == null)
                throw new MissingFieldException(nameof(PlayerIconRequest), nameof(PlayerIconRequest.hash));

            harmony.Patch(AccessTools.Method(typeof(PlayerIconImage), nameof(PlayerIconImage.SetPresetIcon), new[] { typeof(Profile) }),
                prefix: new HarmonyMethod(typeof(Portrait), nameof(ProfilePrefix)),
                finalizer: new HarmonyMethod(typeof(Portrait), nameof(ProfileFinalizer)));
            harmony.Patch(AccessTools.Method(typeof(PlayerIconImage), nameof(PlayerIconImage.SetPresetIcon),
                    new[] { typeof(BodyCustomization), typeof(InventoryEquipment) }),
                prefix: new HarmonyMethod(typeof(Portrait), nameof(EquipmentPrefix)));
            harmony.Patch(AccessTools.Constructor(typeof(PlayerIconRequest), new[] { typeof(InventoryEquipment), typeof(BodyCustomization) }),
                postfix: new HarmonyMethod(typeof(Portrait), nameof(RequestPostfix)));
        }

        private static void ProfilePrefix(Profile profile) => _source = profile?.Inventory?.Equipment;

        private static void ProfileFinalizer() => _source = null;

        // the copy is made just before this, in SetPresetIcon(Profile)
        private static void EquipmentPrefix(InventoryEquipment equipment)
        {
            try
            {
                if (_source != null && equipment != null && Plugin.Enabled.Value)
                    Looks.Link(_source, equipment);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't carry looks over to your portrait: {e.Message}");
            }
        }

        private static void RequestPostfix(PlayerIconRequest __instance, InventoryEquipment equipment)
        {
            try
            {
                if (equipment == null || !Plugin.Enabled.Value)
                    return;

                int salt = 0;
                bool any = false;
                bool ready = true;
                foreach (Item item in equipment.GetAllItems((IContainer container) => container is Slot))
                {
                    if (!Looks.TryGet(item, out string templateId))
                        continue;
                    unchecked
                    {
                        if (Looks.IsHidden(item))
                        {
                            salt += item.TemplateId.GetHashCode() * 397 ^ Looks.Hidden.GetHashCode();
                            any = true;
                            continue;
                        }
                        ItemTemplate look = Rules.Template(templateId);
                        if (look == null || !Rules.Fits(item, look))
                            continue;
                        if (!Visuals.Current(item, out _))
                        {
                            ready = false;
                            continue;
                        }
                        salt += item.TemplateId.GetHashCode() * 397 ^ look._id.GetHashCode();
                        any = true;
                    }
                }

                if (!ready)
                {
                    // drawn with the real gear this time; the looks are loading for next time
                    Looks.Unlink(equipment);
                    return;
                }
                if (any)
                    HashField.SetValue(__instance, unchecked(__instance.hash ^ (Salt + salt * 31)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't put your looks on your portrait: {e.Message}");
            }
        }
    }
}
