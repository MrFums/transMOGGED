using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT.InventoryLogic;
using HarmonyLib;

namespace Transmogged
{
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? Browsable;
        public int? Order;
    }

    public enum MenuStyle
    {
        Integrated,
        Spotlight
    }

    public enum LooksFollow
    {
        Slot,
        Item
    }

    public enum IconMode
    {
        RealItem,
        Look
    }

    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(FikaGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(BeltSlotGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.fums.transmogged";
        public const string Name = "Fums-transMOGGED";
        public const string Version = "1.0.0";
        private const string FikaGuid = "com.fika.core";
        private const string BeltSlotGuid = "com.trenchfoot.beltslot";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> ShowOthers;
        internal static ConfigEntry<bool> ShowBadge;
        internal static ConfigEntry<MenuStyle> Style;
        internal static ConfigEntry<IconMode> ItemIcons;
        internal static ConfigEntry<LooksFollow> Follow;
        internal static ConfigEntry<bool> ExaminedOnly;

        private static readonly Dictionary<Kind, ConfigEntry<bool>> Kinds = new Dictionary<Kind, ConfigEntry<bool>>();
        private bool _patched;

        internal static bool KindEnabled(Kind kind) => Kinds.TryGetValue(kind, out ConfigEntry<bool> on) && on.Value;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("1. General", "Enabled", true,
                new ConfigDescription("Show transmogged items with the look you picked. Off shows every item as it really is. "
                    + "Your picks are kept either way.", null, Order(100)));
            ShowOthers = Config.Bind("1. General", "Show other players looks", true,
                new ConfigDescription("In Fika raids, show the looks other players running transMOGGED picked for their gear.",
                    null, Order(90)));
            ShowBadge = Config.Bind("1. General", "Mark transmogged items", true,
                new ConfigDescription("Show a small violet tag with the barter arrows on transmogged items in the inventory, including on other "
                    + "players' items when you loot or inspect them.", null, Order(80)));

            Style = Config.Bind("1. General", "Menu style", MenuStyle.Integrated,
                new ConfigDescription("Integrated: a Transmog line in the inspect window's stats and a picker that looks like "
                    + "the game's own windows. Spotlight: a violet Transmog card in the inspect window with each item's icon "
                    + "next to its look, and a standalone picker with big tiles.", null, Order(75)));

            ItemIcons = Config.Bind("1. General", "Inventory icons", IconMode.RealItem,
                new ConfigDescription("What item icons show: the real item, or its look. Hold Alt in the inventory to see the other one.",
                    null, Order(74)));
            Follow = Config.Bind("1. General", "Looks follow", LooksFollow.Slot,
                new ConfigDescription("Slot: a plain item put in an equipment slot or gun slot takes the look last used there, if it fits. "
                    + "Item: a look stays on its one item, and replacements start plain. Either way, an item with its own look keeps it.",
                    null, Order(73)));

            ExaminedOnly = Config.Bind("1. General", "Examined items only", false,
                new ConfigDescription("Only offer looks of items your character has examined. Looks you already picked stay on.",
                    null, Order(72)));

            BindKind("2. Weapon parts", Kind.Stock, true, 70);
            BindKind("2. Weapon parts", Kind.PistolGrip, true, 69);
            BindKind("2. Weapon parts", Kind.Foregrip, true, 68);
            BindKind("2. Weapon parts", Kind.Handguard, true, 67);
            BindKind("2. Weapon parts", Kind.Receiver, true, 66, "Upper receivers and dust covers.");
            BindKind("2. Weapon parts", Kind.ChargingHandle, true, 65);
            BindKind("2. Weapon parts", Kind.GasBlock, true, 64);
            BindKind("2. Weapon parts", Kind.Muzzle, true, 63,
                "Flash hiders, brakes, compensators and adapters. They only look like other non-suppressors.");
            BindKind("2. Weapon parts", Kind.Suppressor, true, 62, "Suppressors only look like other suppressors.");
            BindKind("2. Weapon parts", Kind.Mount, true, 61, "Rail mounts. The scope on top stays the real one.");
            BindKind("2. Weapon parts", Kind.RailCover, true, 60);
            BindKind("2. Weapon parts", Kind.IronSight, true, 57, "Only the same kind of sight, front or rear, that fits the slot.");
            BindKind("2. Weapon parts", Kind.Barrel, false, 59,
                "Off by default: a barrel of another length moves where the muzzle (and the shot) sits on your gun.");

            BindKind("2. Weapon parts", Kind.Magazine, true, 58, "Only same-capacity magazines of the same caliber.");

            BindKind("3. Gear", Kind.Headwear, true, 50, "Helmets and hats.");
            BindKind("3. Gear", Kind.Rig, true, 49, "Chest rigs, armored or not.");
            BindKind("3. Gear", Kind.Backpack, true, 48);
            BindKind("3. Gear", Kind.BodyArmor, true, 47);
            BindKind("3. Gear", Kind.FaceCover, true, 46);
            BindKind("3. Gear", Kind.Eyewear, true, 45);
            BindKind("3. Gear", Kind.Headset, true, 44);
            BindKind("3. Gear", Kind.Armband, true, 43);
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(BeltSlotGuid))
                BindKind("3. Gear", Kind.Belt, true, 42, "WTT Pack n Strap belts.");

            Enabled.SettingChanged += (_, __) => Visuals.RefreshAll();
            ShowOthers.SettingChanged += (_, __) => Visuals.RefreshAll();
            ShowBadge.SettingChanged += (_, __) => Badges.Refresh();
            Style.SettingChanged += (_, __) => Card.MarkDirty();
            ItemIcons.SettingChanged += (_, __) => Badges.Refresh();

            Looks.Load();
            Kept.Load();

            var harmony = new Harmony(Guid);
            try
            {
                Visuals.Apply(harmony);
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                Logger.LogError($"{Name} {Version} couldn't hook item models, nothing will be transmogged: {e}");
                return;
            }

            Try("add Transmog to the item menu, saved looks still show", () => Menu.Apply(harmony));
            Try("hook the inspect window, it won't show the transmog line", () => Card.Apply(harmony));
            Try("hook item views, transmogged items won't be tagged", () => Badges.Apply(harmony));
            Try("hook item icons, they show the real item", () => Icons.Apply(harmony));

            gameObject.AddComponent<Picker>();
            gameObject.AddComponent<SpotlightPicker>();
            _patched = true;
            Logger.LogInfo($"{Name} {Version} loaded ({Looks.OwnCount} saved looks).");
            LoadFikaPart();
        }

        internal static void Try(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.LogWarning($"Couldn't {what}: {e.Message}");
            }
        }

        private void LoadFikaPart()
        {
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(FikaGuid))
                return;

            try
            {
                Assembly module;
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("transMOGGED.Fika.dll"))
                using (var bytes = new MemoryStream())
                {
                    stream.CopyTo(bytes);
                    module = Assembly.Load(bytes.ToArray());
                }
                MethodInfo start = module.GetType("Transmogged.Fika.Module")?.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)
                                   ?? throw new MissingMethodException("Transmogged.Fika.Module", "Start");
                start.Invoke(null, new object[]
                {
                    Logger, new Func<Item, Dictionary<string, string>>(Looks.OwnFor),
                    new Action<string, Dictionary<string, string>>(Looks.SetRemote), new Action(Looks.ClearRemote)
                });
            }
            catch (Exception e)
            {
                Logger.LogError($"Couldn't load the Fika part, looks won't be shared: {e}");
            }
        }

        private void BindKind(string section, Kind kind, bool on, int order, string extra = null)
        {
            string label = Rules.Label(kind);
            string plural = label.EndsWith("wear") || label.EndsWith("armor") ? label : label + "s";
            string text = $"Allow transmogging {plural}." + (extra == null ? "" : " " + extra);
            ConfigEntry<bool> entry = Config.Bind(section, char.ToUpperInvariant(plural[0]) + plural.Substring(1), on,
                new ConfigDescription(text, null, Order(order)));
            entry.SettingChanged += (_, __) => Visuals.RefreshAll();
            Kinds[kind] = entry;
        }

        private static ConfigurationManagerAttributes Order(int order) => new ConfigurationManagerAttributes { Order = order };

        private void Update()
        {
            if (!_patched)
                return;
            Visuals.Update();
            Badges.Tick();
            Kept.Tick();
            Card.Refresh();
        }
    }
}
