using System;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.CameraControl;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace Transmogged
{
    internal static class Menu
    {
        private const string EntryId = "transmogged.pick";

        public static IEftSession Session => Singleton<ClientApplication<IEftSession>>.Instance?.GetClientBackEndSession();

        public static bool InRaid => Singleton<GameWorld>.Instantiated && !(Singleton<GameWorld>.Instance is HideoutGameWorld);

        // the hideout's game world stays loaded after you leave it
        private static bool _inWorld;

        public static bool CanPick => !_inWorld && !InRaid;

        // Alt is a weapon-check modifier in raid, so it only swaps icons with the inventory open
        public static bool CanPeek => CanPick || (MonoBehaviourSingleton<CommonUI>.Instantiated && MonoBehaviourSingleton<CommonUI>.Instance.InventoryScreen.isActiveAndEnabled);

        private static Sprite Icon
        {
            get
            {
                try
                {
                    return EFTHardSettings.Instance?.StaticIcons?.GetAttributeIcon(EItemAttributeId.ArmorType);
                }
                catch
                {
                    return null;
                }
            }
        }

        public static void Apply(Harmony harmony)
        {
            var target = AccessTools.Method(typeof(ItemUiContext), nameof(ItemUiContext.GetItemContextInteractions),
                             new[] { typeof(ItemContext), typeof(Action) })
                         ?? throw new MissingMethodException(nameof(ItemUiContext), nameof(ItemUiContext.GetItemContextInteractions));
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(Menu), nameof(Postfix)));

            PlayerCameraController.OnPlayerCameraControllerCreated += (_, __) => _inWorld = true;
            PlayerCameraController.OnPlayerCameraControllerDestroyed += () => _inWorld = false;

            Plugin.Try("move Transmog up the menu, it stays at the bottom", () =>
            {
                var button = AccessTools.Method(typeof(InteractionButtonsContainer), nameof(InteractionButtonsContainer.CreateContextButton),
                    new[]
                    {
                        typeof(string), typeof(string), typeof(SimpleContextMenuButton), typeof(RectTransform), typeof(Sprite),
                        typeof(Action), typeof(Action), typeof(bool), typeof(bool)
                    });
                var dynamic = AccessTools.Method(typeof(InteractionButtonsContainer), nameof(InteractionButtonsContainer.CreateDynamicContextButton));
                harmony.Patch(button, postfix: new HarmonyMethod(typeof(Menu), nameof(ButtonPostfix)));
                harmony.Patch(dynamic, postfix: new HarmonyMethod(typeof(Menu), nameof(DynamicPostfix)));
            });
        }

        private static Transform _discard;

        private static void ButtonPostfix(string key, SimpleContextMenuButton __result)
        {
            if (key == nameof(EItemInfoButton.Discard))
                _discard = __result.Transform;
        }

        private static void DynamicPostfix(InteractionButtonsContainer __instance, DynamicContextInteraction interaction)
        {
            RectTransform container = __instance._buttonsContainer;
            if (interaction?.Id != EntryId || container == null || container.childCount == 0)
                return;
            if (_discard == null || _discard.parent != container)
                return;

            Transform entry = container.GetChild(container.childCount - 1);
            if (entry == _discard)
                return;
            int index = _discard.GetSiblingIndex();
            if (index > 0 && container.GetChild(index - 1).GetComponent<SimpleContextMenuButton>() == null)
                index--;
            entry.SetSiblingIndex(index);
        }

        private static void Postfix(ItemUiContext __instance, ItemContext itemContext, Action closeAction,
            ContextInteractions<EItemInfoButton> __result)
        {
            try
            {
                Item item = itemContext?.Item;
                if (__result == null || item == null || !CanPick || itemContext.ViewType != EItemViewType.Inventory)
                    return;
                if (!Rules.CanTransmog(item) || !IsMine(item))
                    return;

                foreach (var old in __result.DynamicInteractions.Where(d => d.Id == EntryId).ToList())
                    __result.RemoveDynamicInteraction(old.Key);

                Action close = closeAction ?? __instance.ContextMenu.Close;
                __result.method_2(EntryId, "Transmog", () =>
                {
                    close();
                    Picker.Open(item);
                }, Icon);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Couldn't add Transmog to the menu: {e}");
            }
        }

        public static bool IsMine(Item item)
        {
            IEftSession session = Session;
            if (session == null)
                return false;
            // GetRootItem stops at unmerged parents, like a backpack in its slot
            Item root = item;
            for (int depth = 0; depth < 64 && root.CurrentAddress?.Container?.ParentItem is Item parent && parent != root; depth++)
                root = parent;
            return Owns(session.Profile, root) || Owns(session.ProfileOfPet, root);
        }

        private static bool Owns(Profile profile, Item root)
        {
            Inventory inventory = profile?.Inventory;
            return inventory != null && (root == inventory.Equipment || root == inventory.Stash);
        }
    }
}
