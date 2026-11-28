using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;

namespace Transmogged
{
    public enum Kind
    {
        None,
        Stock,
        PistolGrip,
        Foregrip,
        Handguard,
        Receiver,
        GasBlock,
        ChargingHandle,
        Muzzle,
        Suppressor,
        Barrel,
        Mount,
        RailCover,
        Magazine,
        IronSight,
        Headwear,
        Rig,
        Backpack,
        BodyArmor,
        FaceCover,
        Eyewear,
        Headset,
        Armband,
        Belt,
        // NVGs, thermals, their mounts, ear pieces and face shields on a helmet
        HelmetPart
    }

    internal static class Rules
    {
        // WTT Pack n Strap's belts are vests underneath, under this parent node
        private static readonly MongoID BeltParent = new MongoID("6815465859b8c6ff13f94026");

        private static readonly Dictionary<Kind, List<ItemTemplate>> ByKind = new Dictionary<Kind, List<ItemTemplate>>();

        public static bool IsGear(Kind kind) => kind >= Kind.Headwear;

        public static string Label(Kind kind)
        {
            switch (kind)
            {
                case Kind.PistolGrip: return "pistol grip";
                case Kind.GasBlock: return "gas block";
                case Kind.ChargingHandle: return "charging handle";
                case Kind.RailCover: return "rail cover";
                case Kind.BodyArmor: return "body armor";
                case Kind.FaceCover: return "face cover";
                case Kind.HelmetPart: return "helmet part";
                default: return kind.ToString().ToLowerInvariant();
            }
        }

        public static Kind KindOf(ItemTemplate t)
        {
            if (t.Parent != null && t.Parent._id == BeltParent)
                return Kind.Belt;

            switch (t)
            {
                case SilencerTemplate _: return Kind.Suppressor;
                case MuzzleModTemplate _: return Kind.Muzzle;
                case StockTemplate _: return Kind.Stock;
                case PistolGripTemplate _: return Kind.PistolGrip;
                case ForegripTemplate _: return Kind.Foregrip;
                case HandguardTemplate _: return Kind.Handguard;
                case ReceiverTemplate _: return Kind.Receiver;
                case GasblockTemplate _: return Kind.GasBlock;
                case ChargeTemplate _: return Kind.ChargingHandle;
                case BarrelTemplate _: return Kind.Barrel;
                case MountTemplate _: return Kind.Mount;
                case RailCoversTemplate _: return Kind.RailCover;
                case IronSightTemplate _: return Kind.IronSight;
                case CylinderMagazineTemplate _: return Kind.None;
                case MagazineTemplate _: return Kind.Magazine;
                case HeadwearTemplate _: return Kind.Headwear;
                case VestTemplate _: return Kind.Rig;
                case BackpackTemplate _: return Kind.Backpack;
                case ArmorTemplate _: return Kind.BodyArmor;
                case FaceCoverTemplate _: return Kind.FaceCover;
                case VisorsTemplate _: return Kind.Eyewear;
                case HeadphonesTemplate _: return Kind.Headset;
                case ArmBandTemplate _: return Kind.Armband;
                case ArmorPlateTemplate _: return Kind.None;
                case NightVisionTemplate _: return Kind.HelmetPart;
                case ThermalVisionTemplate _: return Kind.HelmetPart;
                // what's left of armored equipment: ear pieces and face shields that go on helmets
                case ArmoredEquipmentTemplate _: return Kind.HelmetPart;
                default: return Kind.None;
            }
        }

        // a mount is a helmet part on a helmet and a gun part anywhere else; a night vision or thermal device on a gun
        // is a scope, and those keep their real look
        public static Kind KindOf(Item item)
        {
            if (item?.Template == null)
                return Kind.None;
            Kind kind = KindOf(item.Template);
            if (kind == Kind.Mount && Host(item) is Item host && IsGear(KindOf(host.Template)))
                return Kind.HelmetPart;
            if (kind == Kind.HelmetPart && Family(item.Template) == 1 && Host(item) is Weapon)
                return Kind.None;
            return kind;
        }

        // the first item above this one that isn't a mod: the gun or the helmet it sits on. Null when there is none,
        // like a loose mount lying in a raid, whose address points back at itself.
        private static Item Host(Item item)
        {
            Item current = item;
            for (int depth = 0; depth < 32; depth++)
            {
                Item parent = current.CurrentAddress?.Container?.ParentItem;
                if (parent == null || ReferenceEquals(parent, current))
                    return null;
                if (!(parent is Mod))
                    return parent;
                current = parent;
            }
            return null;
        }

        // helmet parts only look like their own sort: armor (ear pieces, face shields), vision devices, or mounts
        private static int Family(ItemTemplate t)
        {
            switch (t)
            {
                case NightVisionTemplate _:
                case ThermalVisionTemplate _:
                    return 1;
                case MountTemplate _:
                    return 2;
                default:
                    return 0;
            }
        }

        // the kinds a look can have for an item of this kind
        private static bool LooksLike(Kind itemKind, ItemTemplate look)
        {
            Kind kind = KindOf(look);
            return kind == itemKind || (itemKind == Kind.HelmetPart && kind == Kind.Mount);
        }

        public static bool CanTransmog(Item item)
        {
            Kind kind = KindOf(item);
            return kind != Kind.None && Plugin.KindEnabled(kind);
        }

        public static ItemTemplate Template(string id)
        {
            if (string.IsNullOrEmpty(id) || id == Looks.Hidden || !Singleton<ItemFactory>.Instantiated)
                return null;
            return Singleton<ItemFactory>.Instance.ItemTemplates.TryGetValue(id, out ItemTemplate t) ? t : null;
        }

        public static bool HasModel(ItemTemplate t) => t?.Prefab != null && !string.IsNullOrEmpty(t.Prefab.path);

        public static bool Fits(Item item, ItemTemplate look)
        {
            if (item?.Template == null || !HasModel(look) || look._id == item.TemplateId)
                return false;
            Kind kind = KindOf(item);
            if (kind == Kind.None || !LooksLike(kind, look) || !Plugin.KindEnabled(kind))
                return false;
            if (kind == Kind.HelmetPart && Family(look) != Family(item.Template))
                return false;

            if (kind == Kind.Magazine && !SameMagazine((MagazineTemplate)item.Template, (MagazineTemplate)look))
                return false;

            if (item.CurrentAddress?.Container is Slot parent && parent.Filters != null && parent.Filters.Length > 0
                && !Allowed(parent.Filters, look))
                return false;

            // mods attach to the bone named after their slot, so on a gun every filled slot has to exist on the look.
            // Gear borrows the bone from its real model when the look has none (see Bones).
            if (!IsGear(kind) && item is CompoundItem compound)
            {
                Slot[] lookSlots = (look as CompoundItemTemplate)?.Slots ?? Array.Empty<Slot>();
                foreach (Slot slot in FilledSlots(compound))
                {
                    if (!lookSlots.Any(s => string.Equals(s.ID, slot.ID, StringComparison.OrdinalIgnoreCase)))
                        return false;
                }
            }
            return true;
        }

        // slots with a mod in them that the game attaches to a bone; armor plates don't need one
        public static IEnumerable<Slot> FilledSlots(CompoundItem item) =>
            item.Slots.Where(s => s.ContainedItem != null && !(s is ArmorSlot) && !(s.ContainedItem is ArmorPlate));

        public static bool HasMods(Item item) => item is CompoundItem compound && FilledSlots(compound).Any();

        // whether the look has a slot of this name that takes this mod, so the look's bone for it is made for the mod.
        // Slot names are shared between helmets for different things: the FAST's ear piece slot is the Exfil's face
        // shield slot.
        public static bool LookTakes(ItemTemplate look, string slotId, Item mod)
        {
            Slot own = (look as CompoundItemTemplate)?.Slots?
                .FirstOrDefault(s => string.Equals(s.ID, slotId, StringComparison.OrdinalIgnoreCase));
            if (own == null || mod?.Template == null)
                return false;
            return own.Filters == null || own.Filters.Length == 0 || Allowed(own.Filters, mod.Template);
        }

        public static bool Shows(Item item, string templateId) =>
            templateId == Looks.Hidden ? IsGear(KindOf(item)) : Fits(item, Template(templateId));

        private static bool SameMagazine(MagazineTemplate real, MagazineTemplate look)
        {
            StackSlot own = real.Cartridges?.FirstOrDefault();
            StackSlot other = look.Cartridges?.FirstOrDefault();
            if (own == null || other == null || own.MaxCount != other.MaxCount || real.ReloadMagType != look.ReloadMagType)
                return false;
            return own.Filters.SelectMany(f => f.Filter).Intersect(other.Filters.SelectMany(f => f.Filter)).Any();
        }

        private static bool Allowed(ItemFilter[] filters, ItemTemplate t)
        {
            foreach (ItemFilter filter in filters)
            {
                if (filter?.Filter == null || !Matches(filter.Filter, t))
                    continue;
                if (filter.ExcludedFilter == null || !Matches(filter.ExcludedFilter, t))
                    return true;
            }
            return false;
        }

        private static bool Matches(MongoID[] ids, ItemTemplate t)
        {
            for (ItemTemplate node = t; node != null; node = node.Parent)
            {
                MongoID id = node._id;
                if (ids.Contains(id))
                    return true;
            }
            return false;
        }

        public static List<ItemTemplate> Candidates(Item item)
        {
            Kind kind = KindOf(item);
            if (kind == Kind.None)
                return new List<ItemTemplate>();

            string own = item.Template.Prefab?.path;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { own ?? "" };
            return All(kind)
                .Where(t => Fits(item, t) && Known(t) && seen.Add(t.Prefab.path))
                .ToList();
        }

        private static bool Known(ItemTemplate t)
        {
            if (!Plugin.ExaminedOnly.Value || t.ExaminedByDefault)
                return true;
            return Menu.Session?.Profile?.Encyclopedia?.ContainsKey(t._id) == true;
        }

        private static List<ItemTemplate> All(Kind kind)
        {
            if (ByKind.TryGetValue(kind, out List<ItemTemplate> list))
                return list;

            list = Singleton<ItemFactory>.Instance.ItemTemplates.Values
                .Where(t => LooksLike(kind, t) && HasModel(t) && !t.QuestItem && !string.IsNullOrWhiteSpace(Name(t)))
                .OrderBy(Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            ByKind[kind] = list;
            return list;
        }

        public static string Name(ItemTemplate t)
        {
            if (t == null)
                return "";
            string name = t.NameLocalizationKey.Localized();
            return name == t.NameLocalizationKey ? t.Name : name;
        }

        public static string ShortName(ItemTemplate t)
        {
            if (t == null)
                return "";
            string name = t.ShortNameLocalizationKey.Localized();
            return name == t.ShortNameLocalizationKey ? t.ShortName : name;
        }
    }
}
