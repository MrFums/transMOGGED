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
        Belt
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
                default: return Kind.None;
            }
        }

        public static Kind KindOf(Item item) => item?.Template == null ? Kind.None : KindOf(item.Template);

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
            Kind kind = KindOf(look);
            if (kind == Kind.None || kind != KindOf(item.Template) || !Plugin.KindEnabled(kind))
                return false;

            if (kind == Kind.Magazine && !SameMagazine((MagazineTemplate)item.Template, (MagazineTemplate)look))
                return false;

            if (item.CurrentAddress?.Container is Slot parent && parent.Filters != null && parent.Filters.Length > 0
                && !Allowed(parent.Filters, look))
                return false;

            // mods attach by slot name, so every filled slot has to exist on the look
            if (item is CompoundItem compound)
            {
                Slot[] lookSlots = (look as CompoundItemTemplate)?.Slots ?? Array.Empty<Slot>();
                foreach (Slot slot in compound.Slots)
                {
                    if (slot.ContainedItem == null || slot is ArmorSlot || slot.ContainedItem is ArmorPlate)
                        continue;
                    if (!lookSlots.Any(s => string.Equals(s.ID, slot.ID, StringComparison.OrdinalIgnoreCase)))
                        return false;
                }
            }
            return true;
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
                .Where(t => KindOf(t) == kind && HasModel(t) && !t.QuestItem && !string.IsNullOrWhiteSpace(Name(t)))
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
