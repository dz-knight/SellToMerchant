using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using Godot;

namespace SellToMerchant
{
    public sealed class SellToMerchantCardSellMessage : INetMessage, IPacketSerializable, IRunLocationTargetedMessage
    {
        public bool ShouldBroadcast => true;

        public bool ShouldBuffer => false;

        public NetTransferMode Mode => NetTransferMode.Reliable;

        public LogLevel LogLevel => LogLevel.VeryDebug;

        public RunLocation Location { get; set; }

        public void Serialize(PacketWriter writer)
        {
            writer.Write(Location);
        }

        public void Deserialize(PacketReader reader)
        {
            Location = reader.Read<RunLocation>();
        }
    }

    public static class MultiplayerCardSellSync
    {
        private static RunLocationTargetedMessageBuffer? _registeredBuffer;

        public static void EnsureRegistered()
        {
            var buffer = RunManager.Instance.RunLocationTargetedBuffer;
            if (buffer == null || ReferenceEquals(buffer, _registeredBuffer))
                return;

            if (_registeredBuffer != null)
                _registeredBuffer.UnregisterMessageHandler<SellToMerchantCardSellMessage>(HandleSellCardMessage);

            buffer.RegisterMessageHandler<SellToMerchantCardSellMessage>(HandleSellCardMessage);
            _registeredBuffer = buffer;
        }

        public static async Task<bool> DoLocalSellCardAsync(
            Player player,
            CardSelectorPrefs prefs,
            System.Func<CardModel, bool>? filter = null)
        {
            EnsureRegistered();

            var runManager = RunManager.Instance;
            var message = new SellToMerchantCardSellMessage
            {
                Location = runManager.RunLocationTargetedBuffer.CurrentLocation
            };

            runManager.NetService.SendMessage(message);
            return await SellCardForPlayerAsync(player, prefs, filter);
        }

        private static void HandleSellCardMessage(SellToMerchantCardSellMessage message, ulong senderId)
        {
            EnsureRegistered();

            var runState = RunManager.Instance.DebugOnlyGetState();
            if (runState == null)
                return;

            var localPlayer = LocalContext.GetMe(runState);
            if (localPlayer != null && senderId == localPlayer.NetId)
                return;

            var player = runState.Players.FirstOrDefault(p => p.NetId == senderId);
            if (player == null)
            {
                GD.Print($"[SellToMerchant] Failed to resolve remote player for sell-card sync: {senderId}.");
                return;
            }

            TaskHelper.RunSafely(SellCardForPlayerAsync(player, BuildPrefs(), ShopSellManager.CanSellCard));
        }

        private static async Task<bool> SellCardForPlayerAsync(
            Player player,
            CardSelectorPrefs prefs,
            System.Func<CardModel, bool>? filter)
        {
            var selected = await CardSelectCmd.FromDeckForRemoval(player, prefs, filter);
            var card = selected?.FirstOrDefault();
            if (card == null)
                return false;

            await ShopSellManager.SellCardAsync(card, player, allowSynchronizedMultiplayer: true);
            return true;
        }

        private static CardSelectorPrefs BuildPrefs()
        {
            return new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, 1)
            {
                Cancelable = true,
                RequireManualConfirmation = true,
                ShouldGlowGold = ShopSellManager.CanSellCard,
            };
        }
    }
}
