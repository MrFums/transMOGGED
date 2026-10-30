using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using Diz.DependencyManager;
using Diz.Jobs;
using Diz.Resources;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.WeaponModding;
using HarmonyLib;
using UnityEngine;

namespace Transmogged
{
    internal static class Visuals
    {
        private const float RetryAfter = 60f;

        private static readonly HashSet<string> Loading = new HashSet<string>();
        private static readonly Dictionary<string, float> FailedUntil = new Dictionary<string, float>();
        private static readonly List<DependencyGraph<IEasyBundle>.Token> Held = new List<DependencyGraph<IEasyBundle>.Token>();
        private static readonly Dictionary<WeaponPreview, Item> Previews = new Dictionary<WeaponPreview, Item>();
        private static readonly Queue<ResourceKey> Wanted = new Queue<ResourceKey>();
        private static readonly HashSet<string> Dirty = new HashSet<string>();
        private static bool _refreshAll;
        private static float _nextPreload;

        public static void Apply(Harmony harmony)
        {
            MethodInfo prefab = AccessTools.PropertyGetter(typeof(Item), nameof(Item.Prefab))
                                ?? throw new MissingMethodException(nameof(Item), nameof(Item.Prefab));
            harmony.Patch(prefab, postfix: new HarmonyMethod(typeof(Visuals), nameof(PrefabPostfix)));

            MethodInfo paths = AccessTools.Method(typeof(Profile), nameof(Profile.GetAllPrefabPaths))
                               ?? throw new MissingMethodException(nameof(Profile), nameof(Profile.GetAllPrefabPaths));
            harmony.Patch(paths, postfix: new HarmonyMethod(typeof(Visuals), nameof(PrefabPathsPostfix)));

            Plugin.Try("hook the lobby preview, it shows your real gear", () =>
                harmony.Patch(AccessTools.Method(typeof(Profile), nameof(Profile.GetVisualEquipmentState)),
                    postfix: new HarmonyMethod(typeof(Visuals), nameof(VisualStatePostfix))));

            Plugin.Try("hook the hideout shooting range, it shows your real gear", () =>
                harmony.Patch(AccessTools.Method(typeof(HideoutPlayer), nameof(HideoutPlayer.FastForwardFromPatrol)),
                    prefix: new HarmonyMethod(typeof(Visuals), nameof(ShootingRangePrefix))));

            Plugin.Try("hook the inspect preview, it updates when reopened", () =>
                harmony.Patch(AccessTools.Method(typeof(WeaponPreview), nameof(WeaponPreview.SetupItemPreview)),
                    postfix: new HarmonyMethod(typeof(Visuals), nameof(PreviewPostfix))));

            Plugin.Try("hook worn gear, hidden items still show", () =>
            {
                harmony.Patch(AccessTools.Method(typeof(PlayerBody.SlotView), nameof(PlayerBody.SlotView.CreateAndParent)),
                    postfix: new HarmonyMethod(typeof(Visuals), nameof(HidePostfix)));
                harmony.Patch(AccessTools.Method(typeof(PlayerBody.SlotView), nameof(PlayerBody.SlotView.DestroyCurrentModel)),
                    prefix: new HarmonyMethod(typeof(Visuals), nameof(UnhidePrefix)));
            });

            Looks.Changed += ids =>
            {
                foreach (string id in ids)
                    Dirty.Add(id);
            };
        }

        private static void PrefabPostfix(Item __instance, ref ResourceKey __result)
        {
            try
            {
                if (!Plugin.Enabled.Value || !Looks.TryGet(__instance, out string templateId))
                    return;

                ItemTemplate look = Rules.Template(templateId);
                if (look == null || !Rules.Fits(__instance, look))
                    return;

                if (!Ready(look.Prefab))
                {
                    Want(look.Prefab);
                    return;
                }
                __result = look.Prefab;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Look for {__instance?.Id} failed, showing the real model: {e}");
            }
        }

        private static void HidePostfix(PlayerBody.SlotView __instance, GameObject model)
        {
            if (Plugin.Enabled.Value && Looks.IsHidden(__instance._item))
                SetRendering(model, false);
        }

        // models go back to a pool and get reused
        private static void UnhidePrefix(PlayerBody.SlotView __instance)
        {
            if (__instance.Model != null)
                SetRendering(__instance.Model, true);
        }

        private static void SetRendering(GameObject model, bool on)
        {
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                renderer.forceRenderingOff = !on;
        }

        private static void PrefabPathsPostfix(Profile __instance, ref IEnumerable<ResourceKey> __result)
        {
            if (Plugin.Enabled.Value && __result != null)
                __result = __result.Concat(LookResources(__instance));
        }

        private static IEnumerable<ResourceKey> LookResources(Profile profile)
        {
            Item equipment = profile?.Inventory?.Equipment;
            if (equipment == null)
                yield break;

            foreach (Item item in equipment.GetAllItems())
            {
                if (!Looks.TryGetFor(profile.ProfileId, item.Id, out string templateId))
                    continue;
                ItemTemplate look = Rules.Template(templateId);
                if (!Rules.HasModel(look))
                    continue;
                foreach (ResourceKey key in look.AllResources)
                    yield return key;
            }
        }

        private static void VisualStatePostfix(Profile __instance, bool firstLevelItemsOnly, PlayerVisualRepresentation __result)
        {
            try
            {
                if (firstLevelItemsOnly)
                    Looks.Link(__instance?.Inventory?.Equipment, __result?.Equipment);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't carry looks over to the preview: {e.Message}");
            }
        }

        private static void ShootingRangePrefix(HideoutPlayer __instance)
        {
            try
            {
                Item original = __instance?.OriginalInventory?.Inventory?.Equipment;
                Item copy = __instance?.ShootingRangeInventory?.Inventory?.Equipment;
                if (original == null || copy == null || original == copy)
                    return;
                Looks.Link(original, copy);
                foreach (Item item in copy.GetAllItems())
                    Dirty.Add(item.Id);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't carry looks over to the shooting range: {e.Message}");
            }
        }

        private static void PreviewPostfix(WeaponPreview __instance, Item item)
        {
            if (__instance == null)
                return;
            if (item == null)
                Previews.Remove(__instance);
            else
                Previews[__instance] = item;
        }

        public static bool Ready(ResourceKey key)
        {
            if (key == null || string.IsNullOrEmpty(key.path) || !Singleton<ObjectsFactory>.Instantiated)
                return false;

            ObjectsFactory.Pools pools = Singleton<ObjectsFactory>.Instance.GetPools(ObjectsFactory.PoolsCategory.Raid);
            if (pools != null && pools.PoolsDictionary.TryGetValue(key, out ObjectsFactory.Pools.AssetPoolData data) && data?.Source != null)
            {
                var task = data.Source.Task;
                if (!task.IsCompleted)
                    return false;
                if (!task.IsFaulted && !task.IsCanceled)
                    return true;
            }
            return BundleLoaded(key.path);
        }

        private static bool BundleLoaded(string path)
        {
            if (!Singleton<IEasyAssets>.Instantiated)
                return false;
            var nodes = Singleton<IEasyAssets>.Instance.System?.Nodes;
            if (nodes == null || !nodes.TryGetValue(path, out Node<IEasyBundle> node))
                return false;
            return Loaded(node, new HashSet<Node<IEasyBundle>>());
        }

        private static bool Loaded(Node<IEasyBundle> node, HashSet<Node<IEasyBundle>> seen)
        {
            if (node == null || !seen.Add(node))
                return true;
            if (node.Data?.LoadState?.Value != ELoadState.Loaded)
                return false;
            return node.Dependencies == null || node.Dependencies.All(d => Loaded(d, seen));
        }

        public static void Want(ResourceKey key)
        {
            if (FailedUntil.TryGetValue(key.path, out float until) && Time.realtimeSinceStartup < until)
                return;
            if (Loading.Add(key.path))
                Wanted.Enqueue(key);
        }

        private static void Preload()
        {
            foreach (string templateId in Looks.OwnTemplates())
            {
                ItemTemplate look = Rules.Template(templateId);
                if (Rules.HasModel(look) && !Ready(look.Prefab))
                    Want(look.Prefab);
            }
        }

        public static void Update()
        {
            if (Plugin.Enabled.Value && Time.realtimeSinceStartup >= _nextPreload && Singleton<ObjectsFactory>.Instantiated
                && Singleton<ItemFactory>.Instantiated && Menu.Session?.Profile != null && !Menu.InRaid)
            {
                _nextPreload = Time.realtimeSinceStartup + 10f;
                Preload();
            }

            while (Wanted.Count > 0)
                LoadLook(Wanted.Dequeue());

            if (_refreshAll || Dirty.Count > 0)
            {
                var ids = _refreshAll ? null : new HashSet<string>(Dirty);
                _refreshAll = false;
                Dirty.Clear();
                Rebuild(ids);
                Badges.Refresh();
            }
        }

        private static async void LoadLook(ResourceKey key)
        {
            try
            {
                ObjectsFactory factory = Singleton<ObjectsFactory>.Instance;
                if (factory.GetPools(ObjectsFactory.PoolsCategory.Raid) != null)
                {
                    await factory.LoadBundlesAndCreatePools(ObjectsFactory.PoolsCategory.Raid,
                        ObjectsFactory.AssemblyType.Local, new[] { key }, JobYieldPriority.General);
                }
                else
                {
                    // no pools yet, the game builds straight from the bundle
                    var token = Singleton<IEasyAssets>.Instance.System.Retain(new[] { key.path }, null);
                    await EasyAssetsExtensions.LoadBundles(token);
                    Held.Add(token);
                }
            }
            catch (Exception e)
            {
                FailedUntil[key.path] = Time.realtimeSinceStartup + RetryAfter;
                Plugin.Log.LogWarning($"Couldn't load the model {key.path}, items using it keep their real look for now: {e.Message}");
            }
            finally
            {
                Loading.Remove(key.path);
            }
            _refreshAll = true;
        }

        public static void RefreshAll() => _refreshAll = true;

        private static void Rebuild(HashSet<string> ids)
        {
            int rebuilt = 0;
            foreach (PlayerBody body in UnityEngine.Object.FindObjectsOfType<PlayerBody>())
            {
                if (body?.SlotViews == null)
                    continue;
                foreach (PlayerBody.SlotView view in body.SlotViews.GetValuesEnumerator())
                {
                    Item item = view?._item;
                    if (item == null || !Touches(item, ids))
                        continue;
                    try
                    {
                        view.CancelCreating();
                        view.LoadItem();
                        rebuilt++;
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"Couldn't rebuild the model of {item.TemplateId}: {e.Message}");
                    }
                }
            }

            foreach (KeyValuePair<WeaponPreview, Item> preview in Previews.ToList())
            {
                if (preview.Key == null)
                {
                    Previews.Remove(preview.Key);
                    continue;
                }
                if (!preview.Key.isActiveAndEnabled || !Touches(preview.Value, ids))
                    continue;
                try
                {
                    preview.Key.SetupItemPreview(preview.Value);
                    rebuilt++;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Couldn't refresh the item preview: {e.Message}");
                }
            }

            if (rebuilt > 0)
                Plugin.Log.LogDebug($"Rebuilt {rebuilt} models.");
        }

        private static bool Touches(Item item, HashSet<string> ids)
        {
            foreach (Item each in item.GetAllItems())
            {
                if (ids == null ? Looks.TryGet(each.Id, out _) : ids.Contains(each.Id))
                    return true;
            }
            return false;
        }
    }
}
