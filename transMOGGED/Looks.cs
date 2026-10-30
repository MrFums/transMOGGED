using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using Comfort.Common;
using EFT.InventoryLogic;
using EFT;
using Newtonsoft.Json;
using UnityEngine;

namespace Transmogged
{
    internal static class Looks
    {
        public const string Hidden = "hidden";

        private const int MaxLinked = 4000;

        private static readonly string FilePath = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "transMOGGED.looks.json");

        private static Dictionary<string, string> _own = new Dictionary<string, string>();

        private static readonly Dictionary<string, Dictionary<string, string>> Remote = new Dictionary<string, Dictionary<string, string>>();

        private static readonly Dictionary<string, string> Linked = new Dictionary<string, string>();

        private static int _count;

        public static event Action<IEnumerable<string>> Changed;

        public static int OwnCount => _own.Count;

        public static IEnumerable<string> OwnTemplates() => _own.Values.Distinct().ToList();

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    _own = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(FilePath))
                           ?? new Dictionary<string, string>();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Couldn't read {FilePath}, starting with no looks: {e.Message}");
                _own = new Dictionary<string, string>();
            }
            Recount();
        }

        private static void Save()
        {
            try
            {
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(_own, Formatting.Indented));
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
                File.Move(temp, FilePath);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Couldn't save looks to {FilePath}: {e.Message}");
            }
        }

        private static void Recount()
        {
            _count = _own.Count + Linked.Count + Remote.Values.Sum(r => r.Count);
        }

        // hot path, called from Item.Prefab
        public static bool TryGet(string itemId, out string templateId)
        {
            templateId = null;
            if (_count == 0 || itemId == null)
                return false;
            if (_own.TryGetValue(itemId, out templateId) || Linked.TryGetValue(itemId, out templateId))
                return true;
            return Plugin.ShowOthers.Value && TryRemote(itemId, out templateId);
        }

        // copied profiles share item ids
        public static bool TryGet(Item item, out string templateId)
        {
            templateId = null;
            if (item == null || !TryGet(item.Id, out templateId))
                return false;

            string owner = (item.Owner as ItemController)?.ID;
            if (owner == null)
                return true;
            if (Remote.TryGetValue(owner, out Dictionary<string, string> theirs))
                return Plugin.ShowOthers.Value && theirs.TryGetValue(item.Id, out templateId);
            if (IsOtherPlayer(owner))
            {
                templateId = null;
                return false;
            }
            return true;
        }

        public static bool TryGetFor(string profileId, string itemId, out string templateId)
        {
            if (profileId != null && Remote.TryGetValue(profileId, out Dictionary<string, string> theirs))
                return theirs.TryGetValue(itemId, out templateId);
            return TryGet(itemId, out templateId);
        }

        private static bool IsOtherPlayer(string profileId)
        {
            if (!Singleton<GameWorld>.Instantiated)
                return false;
            return Singleton<GameWorld>.Instance.allAlivePlayersByID.TryGetValue(profileId, out Player player)
                   && player != null && !player.IsYourPlayer;
        }

        public static bool IsHidden(Item item) => TryGet(item, out string id) && id == Hidden && Rules.IsGear(Rules.KindOf(item));

        public static bool IsOwn(string itemId) => itemId != null && _own.ContainsKey(itemId);

        public static void Set(Item item, string templateId)
        {
            if (string.IsNullOrEmpty(templateId) || templateId == item.TemplateId.ToString())
            {
                Clear(item);
                return;
            }
            _own[item.Id] = templateId;
            Recount();
            Save();
            Changed?.Invoke(new[] { item.Id });
        }

        public static void Apply(Item item, string templateId)
        {
            if (templateId != null)
            {
                Set(item, templateId);
                return;
            }
            Clear(item);
            Kept.Forget(item);
        }

        public static void Clear(Item item)
        {
            if (!_own.Remove(item.Id))
                return;
            Recount();
            Save();
            Changed?.Invoke(new[] { item.Id });
        }

        public static Dictionary<string, string> OwnFor(Item equipment)
        {
            var result = new Dictionary<string, string>();
            if (equipment == null)
                return result;
            foreach (Item item in equipment.GetAllItems())
            {
                if (_own.TryGetValue(item.Id, out string templateId))
                    result[item.Id] = templateId;
            }
            return result;
        }

        public static void SetRemote(string profileId, Dictionary<string, string> looks)
        {
            if (string.IsNullOrEmpty(profileId))
                return;
            Remote.TryGetValue(profileId, out Dictionary<string, string> old);
            Remote[profileId] = looks ?? new Dictionary<string, string>();
            Recount();

            IEnumerable<string> touched = Remote[profileId].Keys;
            if (old != null)
                touched = touched.Union(old.Keys);
            Changed?.Invoke(touched.ToList());
        }

        public static void ClearRemote()
        {
            if (Remote.Count == 0)
                return;
            Remote.Clear();
            Recount();
        }

        public static void Link(Item original, Item copy)
        {
            if (_count == 0 || original == null || copy == null || ReferenceEquals(original, copy))
                return;
            if (Linked.Count > MaxLinked)
                Linked.Clear();

            LinkTree(original, copy);
            Recount();
        }

        private static void LinkTree(Item original, Item copy)
        {
            if (original.Id != copy.Id && (_own.TryGetValue(original.Id, out string templateId) || TryRemote(original.Id, out templateId))
                && Rules.Shows(original, templateId))
                Linked[copy.Id] = templateId;

            if (!(original is CompoundItem from) || !(copy is CompoundItem to))
                return;
            foreach (Slot slot in from.Slots)
            {
                if (slot.ContainedItem == null)
                    continue;
                Slot other = to.Slots.FirstOrDefault(s => s.ID == slot.ID);
                if (other?.ContainedItem != null)
                    LinkTree(slot.ContainedItem, other.ContainedItem);
            }
        }

        private static bool TryRemote(string itemId, out string templateId)
        {
            foreach (Dictionary<string, string> looks in Remote.Values)
            {
                if (looks.TryGetValue(itemId, out templateId))
                    return true;
            }
            templateId = null;
            return false;
        }
    }

    // looks tied to a slot, passed on to whatever replaces the item
    internal static class Kept
    {
        private const float Interval = 1f;

        private static readonly string FilePath = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "transMOGGED.kept.json");

        private static Dictionary<string, string> _rules = new Dictionary<string, string>();
        private static float _next;

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    _rules = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(FilePath))
                             ?? new Dictionary<string, string>();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Couldn't read {FilePath}, starting with no kept looks: {e.Message}");
            }
        }

        private static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(_rules, Formatting.Indented));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Couldn't save kept looks to {FilePath}: {e.Message}");
            }
        }

        public static string KeyFor(Item item)
        {
            string path = "";
            string key = null;
            for (Item node = item; node?.CurrentAddress?.Container is Slot slot; node = slot.ParentItem)
            {
                path = "/" + slot.ID + path;
                if (slot.ParentItem is Weapon weapon)
                    key = weapon.TemplateId + path;
                if (slot.ParentItem is InventoryEquipment)
                    return key ?? "gear" + path;
            }
            return key;
        }

        public static void Remember(Item item, string templateId)
        {
            string key = KeyFor(item);
            if (key == null || (_rules.TryGetValue(key, out string current) && current == templateId))
                return;
            _rules[key] = templateId;
            Save();
        }

        public static void Forget(Item item)
        {
            string key = KeyFor(item);
            if (key != null && _rules.Remove(key))
                Save();
        }

        public static void Tick()
        {
            if (Time.realtimeSinceStartup < _next)
                return;
            _next = Time.realtimeSinceStartup + Interval;
            if (Plugin.Follow.Value != LooksFollow.Slot || Menu.InRaid)
                return;

            Item equipment = Menu.Session?.Profile?.Inventory?.Equipment;
            if (equipment == null)
                return;
            foreach (Item item in equipment.GetAllItems((IContainer container) => container is Slot).ToList())
            {
                if (!Rules.CanTransmog(item))
                    continue;
                string key = KeyFor(item);
                if (key == null)
                    continue;
                if (Looks.IsOwn(item.Id))
                {
                    if (Looks.TryGet(item.Id, out string own) && Rules.Shows(item, own))
                        Remember(item, own);
                }
                else if (_rules.TryGetValue(key, out string templateId) && Rules.Shows(item, templateId))
                {
                    Looks.Set(item, templateId);
                }
            }
        }
    }
}
