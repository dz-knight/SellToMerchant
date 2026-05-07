using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using Godot;

namespace SellToMerchant
{
    public static class ShopSellManager
    {
        public static bool CardSoldThisShop { get; set; }
        public static bool RelicSoldThisShop { get; set; }
        public static bool PotionSoldThisShop { get; set; }
        public static bool TransferUsedThisShop { get; set; }

        // ──── 不可出售的遗物 ID ────
        private static readonly HashSet<string> UnsellableRelicIds = new()
        {
            "Burning Blood", "Ring of the Snake", "Cracked Core",
            "PureWater", "HolyWater", "VioletLotus"
        };

        private const int CommonPrice   = 25;
        private const int UncommonPrice = 37;
        private const int RarePrice     = 75;

        public const int MaxGoldTransfer = 200;

        // ═══════════════════════════════════════
        //  卡牌可售性
        // ═══════════════════════════════════════

        public static bool CanSellCard(CardModel card)
        {
            if (CardSoldThisShop) return false;
            if (card.Rarity == CardRarity.Curse) return false;
            if (card.Rarity == CardRarity.Basic) return false;
            if (card.Rarity == CardRarity.Status) return false;
            if (card.Rarity == CardRarity.Quest) return false;
            if (card.Rarity == CardRarity.Event) return false;
            if (card.Rarity == CardRarity.Token) return false;
            if (card.IsBasicStrikeOrDefend) return false;
            if (!card.IsRemovable) return false;
            return true;
        }

        // ═══════════════════════════════════════
        //  遗物可售性 — 只卖商店里能出现的遗物
        // ═══════════════════════════════════════

        public static bool CanSellRelic(RelicModel relic)
        {
            if (RelicSoldThisShop) return false;
            string id = relic.Id.ToString();
            if (UnsellableRelicIds.Contains(id)) return false;
            if (relic.Rarity == RelicRarity.Starter) return false;
            if (relic.Rarity == RelicRarity.Event) return false;
            if (relic.Rarity == RelicRarity.Ancient) return false;
            if (relic.Rarity == RelicRarity.None) return false;
            if (!relic.IsTradable) return false;
            if (relic.MerchantCost <= 0) return false;
            return true;
        }

        // ═══════════════════════════════════════
        //  药水可售性
        // ═══════════════════════════════════════

        public static bool CanSellPotion(PotionModel potion)
        {
            if (PotionSoldThisShop) return false;
            if (potion.Rarity == PotionRarity.None) return false;
            if (potion.HasBeenRemovedFromState) return false;
            return true;
        }

        // ═══════════════════════════════════════
        //  售价
        // ═══════════════════════════════════════

        public static int GetCardSellPrice(CardModel card)
        {
            return card.Rarity switch
            {
                CardRarity.Common   => CommonPrice,
                CardRarity.Uncommon => UncommonPrice,
                CardRarity.Rare     => RarePrice,
                _ => 0
            };
        }

        public static int GetRelicSellPrice(RelicModel relic) => relic.MerchantCost / 2;

        public static int GetPotionSellPrice(PotionModel potion) => potion.Rarity switch
        {
            PotionRarity.Common   => CommonPrice,
            PotionRarity.Uncommon => UncommonPrice,
            PotionRarity.Rare     => RarePrice,
            _ => 0
        };

        // ═══════════════════════════════════════
        //  执行出售
        // ═══════════════════════════════════════

        public static async Task SellCardAsync(CardModel card, Player player)
        {
            int price = GetCardSellPrice(card);
            if (price <= 0) return;

            await CardPileCmd.RemoveFromDeck(card, showPreview: true);
            await PlayerCmd.GainGold(price, player, wasStolenBack: false);
            CardSoldThisShop = true;

            GD.Print($"[SellToMerchant] Sold card '{card.Title}' for {price} gold.");
        }

        public static async Task SellRelicAsync(RelicModel relic, Player player)
        {
            int price = GetRelicSellPrice(relic);
            if (price <= 0) return;

            await RelicCmd.Remove(relic);
            await PlayerCmd.GainGold(price, player, wasStolenBack: false);
            RelicSoldThisShop = true;

            GD.Print($"[SellToMerchant] Sold relic '{relic.Title}' for {price} gold.");
        }

        public static async Task SellPotionAsync(PotionModel potion, Player player)
        {
            int price = GetPotionSellPrice(potion);
            if (price <= 0) return;

            await PotionCmd.Discard(potion);
            await PlayerCmd.GainGold(price, player, wasStolenBack: false);
            PotionSoldThisShop = true;

            GD.Print($"[SellToMerchant] Sold potion '{potion.Title}' for {price} gold.");
        }

        // ═══════════════════════════════════════
        //  联机转账（每店限一次）
        // ═══════════════════════════════════════

        public static async Task<bool> TransferGoldAsync(Player sender, Player receiver, int amount)
        {
            if (TransferUsedThisShop) return false;
            if (amount <= 0) return false;
            if (amount > MaxGoldTransfer) return false;
            if (sender.Gold < amount) return false;

            await PlayerCmd.LoseGold(amount, sender, GoldLossType.Spent);
            await PlayerCmd.GainGold(amount, receiver, wasStolenBack: false);
            TransferUsedThisShop = true;

            GD.Print($"[SellToMerchant] Player transferred {amount} gold to teammate.");
            return true;
        }

        public static IReadOnlyList<Player> GetTeammates(Player self)
        {
            var runState = self.RunState;
            if (runState == null) return new List<Player>();
            return runState.Players.Where(p => p.NetId != self.NetId).ToList();
        }

        // ═══════════════════════════════════════
        //  辅助
        // ═══════════════════════════════════════

        public static void ResetShopLimits()
        {
            CardSoldThisShop = false;
            RelicSoldThisShop = false;
            PotionSoldThisShop = false;
            TransferUsedThisShop = false;
        }

        public static Player? GetShopPlayer()
            => NMerchantRoom.Instance?.Room?.Inventory?.Player;

        public static List<CardModel> GetSellableCards(Player player)
            => player.Deck.Cards.Where(c => CanSellCard(c)).ToList();

        public static List<RelicModel> GetSellableRelics(Player player)
            => player.Relics.Where(r => CanSellRelic(r)).ToList();

        public static List<PotionModel> GetSellablePotions(Player player)
            => player.Potions.Where(p => CanSellPotion(p)).ToList();
    }
}
