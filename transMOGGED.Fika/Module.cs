using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Fika.Core.Main.Utils;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using Fika.Core.Networking.LiteNetLib.Utils;

namespace Transmogged.Fika
{
    // embedded in transMOGGED.dll and only loaded with Fika: WTT's GetTypes() chokes on Fika types otherwise
    public static class Module
    {
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        internal static Func<Item, Dictionary<string, string>> OwnLooks;
        internal static Action<string, Dictionary<string, string>> SetRemoteLooks;
        internal static Action ClearRemoteLooks;

        public static void Start(ManualLogSource log, Func<Item, Dictionary<string, string>> ownLooks,
            Action<string, Dictionary<string, string>> setRemoteLooks, Action clearRemoteLooks)
        {
            Log = log;
            OwnLooks = ownLooks;
            SetRemoteLooks = setRemoteLooks;
            ClearRemoteLooks = clearRemoteLooks;
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(Sync.Created);
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerDestroyedEvent>(Sync.Destroyed);
            FikaEventDispatcher.SubscribeEvent<PeerConnectedEvent>(Sync.PeerConnected);
            Log.LogInfo($"Fika part {Version} loaded, looks are shared in Fika raids.");
        }
    }

    internal static class Sync
    {
        private static IFikaNetworkManager _manager;
        private static readonly Dictionary<string, LooksPacket> Collected = new Dictionary<string, LooksPacket>();

        public static void Created(FikaNetworkManagerCreatedEvent e)
        {
            Collected.Clear();
            Module.ClearRemoteLooks();
            _manager = e.Manager;
            try
            {
                _manager.RegisterPacket<LooksPacket, NetPeer>(Received);
            }
            catch (Exception ex)
            {
                Module.Log.LogError($"Couldn't register the looks packet, looks won't be shared this raid: {ex}");
                _manager = null;
            }
        }

        public static void Destroyed(FikaNetworkManagerDestroyedEvent e)
        {
            _manager = null;
            Collected.Clear();
            Module.ClearRemoteLooks();
        }

        public static void PeerConnected(PeerConnectedEvent e)
        {
            if (_manager == null)
                return;
            try
            {
                LooksPacket own = Own();
                if (FikaBackendUtils.IsServer)
                {
                    e.NetworkManager.SendDataToPeer(ref own, DeliveryMethod.ReliableUnordered, e.Peer);
                    foreach (LooksPacket collected in Collected.Values)
                    {
                        LooksPacket copy = collected;
                        e.NetworkManager.SendDataToPeer(ref copy, DeliveryMethod.ReliableUnordered, e.Peer);
                    }
                }
                else
                {
                    e.NetworkManager.SendData(ref own, DeliveryMethod.ReliableUnordered, true);
                }
            }
            catch (Exception ex)
            {
                Module.Log.LogError($"Couldn't send looks: {ex}");
            }
        }

        private static void Received(LooksPacket packet, NetPeer peer)
        {
            try
            {
                if (string.IsNullOrEmpty(packet.ProfileId) || packet.ProfileId == OwnProfile()?.ProfileId)
                    return;

                var looks = new Dictionary<string, string>();
                for (int i = 0; i < packet.ItemIds.Length; i++)
                    looks[packet.ItemIds[i]] = packet.TemplateIds[i];
                Module.SetRemoteLooks(packet.ProfileId, looks);

                if (FikaBackendUtils.IsServer)
                    Collected[packet.ProfileId] = packet;
                Module.Log.LogInfo($"Got {looks.Count} looks from another player.");
            }
            catch (Exception ex)
            {
                Module.Log.LogError($"Couldn't read looks from another player: {ex}");
            }
        }

        private static Profile OwnProfile()
        {
            IEftSession session = Singleton<ClientApplication<IEftSession>>.Instance?.GetClientBackEndSession();
            if (session == null)
                return null;
            return FikaBackendUtils.IsScav ? session.ProfileOfPet : session.Profile;
        }

        private static LooksPacket Own()
        {
            Profile profile = OwnProfile();
            Dictionary<string, string> looks = Module.OwnLooks(profile?.Inventory?.Equipment);
            var packet = new LooksPacket
            {
                ProfileId = profile?.ProfileId ?? "",
                ItemIds = new string[looks.Count],
                TemplateIds = new string[looks.Count]
            };
            int i = 0;
            foreach (KeyValuePair<string, string> look in looks)
            {
                packet.ItemIds[i] = look.Key;
                packet.TemplateIds[i] = look.Value;
                i++;
            }
            return packet;
        }
    }

    public struct LooksPacket : INetSerializable
    {
        public string ProfileId;
        public string[] ItemIds;
        public string[] TemplateIds;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(ProfileId ?? "");
            int count = ItemIds?.Length ?? 0;
            writer.Put(count);
            for (int i = 0; i < count; i++)
            {
                writer.Put(ItemIds[i]);
                writer.Put(TemplateIds[i]);
            }
        }

        public void Deserialize(NetDataReader reader)
        {
            ProfileId = reader.GetString();
            int count = reader.GetInt();
            if (count < 0 || count > 10000)
                throw new InvalidOperationException($"Bad looks packet ({count} entries).");
            ItemIds = new string[count];
            TemplateIds = new string[count];
            for (int i = 0; i < count; i++)
            {
                ItemIds[i] = reader.GetString();
                TemplateIds[i] = reader.GetString();
            }
        }
    }
}
