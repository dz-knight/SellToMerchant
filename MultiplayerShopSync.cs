using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using Godot;

namespace SellToMerchant
{
    public sealed class SellToMerchantRelicSellMessage : INetMessage, IPacketSerializable, IRunLocationTargetedMessage
    {
        public bool ShouldBroadcast => true;

        public bool ShouldBuffer => false;

        public NetTransferMode Mode => NetTransferMode.Reliable;

        public LogLevel LogLevel => LogLevel.VeryDebug;

        public int RelicIndex { get; set; }

        public RunLocation Location { get; set; }

        public void Serialize(PacketWriter writer)
        {
            writer.WriteInt(RelicIndex);
            writer.Write(Location);
        }

        public void Deserialize(PacketReader reader)
        {
            RelicIndex = reader.ReadInt();
            Location = reader.Read<RunLocation>();
        }
    }

    public sealed class SellToMerchantPotionSellMessage : INetMessage, IPacketSerializable, IRunLocationTargetedMessage
    {
        public bool ShouldBroadcast => true;

        public bool ShouldBuffer => false;

        public NetTransferMode Mode => NetTransferMode.Reliable;

        public LogLevel LogLevel => LogLevel.VeryDebug;

        public int PotionIndex { get; set; }

        public RunLocation Location { get; set; }

        public void Serialize(PacketWriter writer)
        {
            writer.WriteInt(PotionIndex);
            writer.Write(Location);
        }

        public void Deserialize(PacketReader reader)
        {
            PotionIndex = reader.ReadInt();
            Location = reader.Read<RunLocation>();
        }
    }

    public sealed class SellToMerchantTransferGoldMessage : INetMessage, IPacketSerializable, IRunLocationTargetedMessage
    {
        public bool ShouldBroadcast => true;

        public bool ShouldBuffer => false;

        public NetTransferMode Mode => NetTransferMode.Reliable;

        public LogLevel LogLevel => LogLevel.VeryDebug;

        public ulong ReceiverId { get; set; }

        public int Amount { get; set; }

        public RunLocation Location { get; set; }

        public void Serialize(PacketWriter writer)
        {
            writer.WriteULong(ReceiverId);
            writer.WriteInt(Amount);
            writer.Write(Location);
        }

        public void Deserialize(PacketReader reader)
        {
            ReceiverId = reader.ReadULong();
            Amount = reader.ReadInt();
            Location = reader.Read<RunLocation>();
        }
    }

    public static class MultiplayerShopSync
    {
        private static RunLocationTargetedMessageBuffer? _registeredBuffer;

        public static void EnsureRegistered()
        {
            var buffer = RunManager.Instance.RunLocationTargetedBuffer;
            if (buffer == null || ReferenceEquals(buffer, _registeredBuffer))
                return;

            if (_registeredBuffer != null)
            {
                _registeredBuffer.UnregisterMessageHandler<SellToMerchantRelicSellMessage>(HandleRelicSellMessage);
                _registeredBuffer.UnregisterMessageHandler<SellToMerchantPotionSellMessage>(HandlePotionSellMessage);
                _registeredBuffer.UnregisterMessageHandler<SellToMerchantTransferGoldMessage>(HandleTransferGoldMessage);
            }

            buffer.RegisterMessageHandler<SellToMerchantRelicSellMessage>(HandleRelicSellMessage);
            buffer.RegisterMessageHandler<SellToMerchantPotionSellMessage>(HandlePotionSellMessage);
            buffer.RegisterMessageHandler<SellToMerchantTransferGoldMessage>(HandleTransferGoldMessage);
            _registeredBuffer = buffer;
        }

        public static async Task<bool> DoLocalSellRelicAsync(Player player, int relicIndex)
        {
            EnsureRegistered();
            var relics = player.Relics.ToList();
            if (relicIndex < 0 || relicIndex >= relics.Count)
                return false;

            RunManager.Instance.NetService.SendMessage(new SellToMerchantRelicSellMessage
            {
                RelicIndex = relicIndex,
                Location = RunManager.Instance.RunLocationTargetedBuffer.CurrentLocation
            });

            return await SellRelicByIndexAsync(player, relicIndex);
        }

        public static async Task<bool> DoLocalSellPotionAsync(Player player, int potionIndex)
        {
            EnsureRegistered();
            var potions = player.Potions.ToList();
            if (potionIndex < 0 || potionIndex >= potions.Count)
                return false;

            RunManager.Instance.NetService.SendMessage(new SellToMerchantPotionSellMessage
            {
                PotionIndex = potionIndex,
                Location = RunManager.Instance.RunLocationTargetedBuffer.CurrentLocation
            });

            return await SellPotionByIndexAsync(player, potionIndex);
        }

        public static async Task<bool> DoLocalTransferGoldAsync(Player sender, Player receiver, int amount)
        {
            EnsureRegistered();
            if (amount <= 0)
                return false;

            RunManager.Instance.NetService.SendMessage(new SellToMerchantTransferGoldMessage
            {
                ReceiverId = receiver.NetId,
                Amount = amount,
                Location = RunManager.Instance.RunLocationTargetedBuffer.CurrentLocation
            });

            return await ShopSellManager.TransferGoldAsync(sender, receiver, amount, allowSynchronizedMultiplayer: true);
        }

        private static void HandleRelicSellMessage(SellToMerchantRelicSellMessage message, ulong senderId)
        {
            var player = ResolveRemotePlayer(senderId);
            if (player == null)
                return;

            TaskHelper.RunSafely(SellRelicByIndexAsync(player, message.RelicIndex));
        }

        private static void HandlePotionSellMessage(SellToMerchantPotionSellMessage message, ulong senderId)
        {
            var player = ResolveRemotePlayer(senderId);
            if (player == null)
                return;

            TaskHelper.RunSafely(SellPotionByIndexAsync(player, message.PotionIndex));
        }

        private static void HandleTransferGoldMessage(SellToMerchantTransferGoldMessage message, ulong senderId)
        {
            var sender = ResolveRemotePlayer(senderId);
            if (sender == null)
                return;

            var receiver = sender.RunState?.Players.FirstOrDefault(p => p.NetId == message.ReceiverId);
            if (receiver == null)
            {
                GD.Print($"[SellToMerchant] Failed to resolve gold transfer receiver: {message.ReceiverId}.");
                return;
            }

            TaskHelper.RunSafely(ShopSellManager.TransferGoldAsync(sender, receiver, message.Amount, allowSynchronizedMultiplayer: true));
        }

        private static Player? ResolveRemotePlayer(ulong senderId)
        {
            EnsureRegistered();

            var runState = RunManager.Instance.DebugOnlyGetState();
            if (runState == null)
                return null;

            var localPlayer = LocalContext.GetMe(runState);
            if (localPlayer != null && senderId == localPlayer.NetId)
                return null;

            var player = runState.Players.FirstOrDefault(p => p.NetId == senderId);
            if (player == null)
                GD.Print($"[SellToMerchant] Failed to resolve remote shop action player: {senderId}.");

            return player;
        }

        private static async Task<bool> SellRelicByIndexAsync(Player player, int relicIndex)
        {
            var relics = player.Relics.ToList();
            if (relicIndex < 0 || relicIndex >= relics.Count)
            {
                GD.Print($"[SellToMerchant] Invalid relic sell index {relicIndex} for player {player.NetId}.");
                return false;
            }

            var relic = relics[relicIndex];
            await ShopSellManager.SellRelicAsync(relic, player, allowSynchronizedMultiplayer: true);
            return true;
        }

        private static async Task<bool> SellPotionByIndexAsync(Player player, int potionIndex)
        {
            var potions = player.Potions.ToList();
            if (potionIndex < 0 || potionIndex >= potions.Count)
            {
                GD.Print($"[SellToMerchant] Invalid potion sell index {potionIndex} for player {player.NetId}.");
                return false;
            }

            var potion = potions[potionIndex];
            await ShopSellManager.SellPotionAsync(potion, player, allowSynchronizedMultiplayer: true);
            return true;
        }
    }
}
