using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using Diz.Resources;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace Transmogged
{
    // The game hangs each mod (an NVG mount, ear chops, a face shield) on the bone named after its slot. A look of
    // another helmet may have no such bone, and the mod would be left off. So before the mods go on, a missing bone
    // is added to the look, where the real model has it.
    // A look may also have a bone of that name made for something else: the FAST's RAC headset slot is the coyote
    // Exfil's face shield slot. A mod the look's slot can't take gets a bone of its own, at the real model's pose, under
    // a name the game never looks for, and the game is handed that bone instead (AddBonePrefix).
    internal static class Bones
    {
        private const string OwnPrefix = "transMOGGED ";

        private static readonly Dictionary<string, Pose> Poses = new Dictionary<string, Pose>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // slots of the item being put together whose mod goes on a bone of its own; AttachMods picks the bones right
        // after AssembleMods starts, before anything else is assembled
        private static readonly Dictionary<IContainer, Transform> Redirects = new Dictionary<IContainer, Transform>();

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ObjectsFactory), nameof(ObjectsFactory.AssembleMods)),
                prefix: new HarmonyMethod(typeof(Bones), nameof(AssemblePrefix)));
            harmony.Patch(AccessTools.Method(typeof(ContainerCollectionView), nameof(ContainerCollectionView.AddBone)),
                prefix: new HarmonyMethod(typeof(Bones), nameof(AddBonePrefix)));
        }

        private static void AssemblePrefix(ContainerCollection collection, GameObject itemGameObject)
        {
            Redirects.Clear();
            try
            {
                // only looks: a real model without a bone for a mod stays as the game leaves it
                if (!(collection is CompoundItem item) || itemGameObject == null || !Plugin.Enabled.Value
                    || !Looks.TryGet(item, out _) || !Rules.IsGear(Rules.KindOf(item)) || !Visuals.Current(item, out ItemTemplate look))
                    return;

                Transform root = itemGameObject.transform;
                foreach (Slot slot in Rules.FilledSlots(item))
                {
                    Transform bone = TransformTools.FindTransformRecursive(root, slot.ID, ignoreCase: true);
                    // models are pooled: a bone added for one item may be reused by another
                    if (bone != null && bone.GetComponent<BorrowedBone>() == null)
                    {
                        // the look's own bone, made for what the look's slot takes
                        if (Rules.LookTakes(look, slot.ID, slot.ContainedItem))
                            continue;

                        string name = OwnPrefix + slot.ID;
                        bone = root.Find(name);
                        if (bone == null)
                        {
                            bone = new GameObject(name).transform;
                            bone.gameObject.AddComponent<BorrowedBone>();
                            bone.SetParent(root, false);
                        }
                        Redirects[slot] = bone;
                        if (Warned.Add(look._id + "|" + slot.ID + "|" + slot.ContainedItem.TemplateId))
                            Plugin.Log.LogInfo($"The look {look._id} has a {slot.ID} bone for its own mods, so {slot.ContainedItem.TemplateId} goes where the real model has it.");
                    }

                    if (bone == null)
                    {
                        bone = new GameObject(slot.ID).transform;
                        bone.gameObject.AddComponent<BorrowedBone>();
                        bone.SetParent(root, false);
                    }
                    Pose pose = RealPose(item.Template.Prefab, slot.ID);
                    bone.localPosition = pose.position;
                    bone.localRotation = pose.rotation;
                    bone.localScale = Vector3.one;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't add the mod bones to the look of {(collection as Item)?.TemplateId}: {e.Message}");
            }
        }

        // hands the game the look's bone of its own for a mod the look's slot can't take
        private static void AddBonePrefix(IContainer container, ref Transform bone)
        {
            if (Redirects.Count == 0 || container == null || !Redirects.TryGetValue(container, out Transform own))
                return;
            Redirects.Remove(container);
            if (own != null)
                bone = own;
        }

        // where the bone sits on the real model; the real model is loaded first (see Visuals.Current)
        private static Pose RealPose(ResourceKey real, string slotId)
        {
            string key = real?.path + "|" + slotId;
            if (Poses.TryGetValue(key, out Pose pose))
                return pose;

            pose = Pose.identity;
            bool found = false;
            if (real != null && Visuals.BundleLoaded(real.path))
            {
                GameObject asset = Singleton<IEasyAssets>.Instance.GetAsset<GameObject>(real);
                Transform bone = asset != null ? TransformTools.FindTransformRecursive(asset.transform, slotId, ignoreCase: true) : null;
                if (bone != null)
                {
                    pose = new Pose(asset.transform.InverseTransformPoint(bone.position),
                        Quaternion.Inverse(asset.transform.rotation) * bone.rotation);
                    found = true;
                }
            }

            if (found)
                Poses[key] = pose;
            else if (Warned.Add(key))
                Plugin.Log.LogWarning($"No {slotId} bone on the real model {real?.path}, so the mod sits at the middle of the look.");
            return pose;
        }
    }

    internal sealed class BorrowedBone : MonoBehaviour
    {
    }
}
