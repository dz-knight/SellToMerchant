using MegaCrit.Sts2.Core.Entities.Players;
using Godot;

namespace SellToMerchant
{
    /// <summary>
    /// 联机同步管理器。
    /// 通过 affects_gameplay: true 确保多人游戏时所有玩家一致安装此 mod。
    /// 卖出和转账操作均为本地执行（各玩家牌组/金币独立），
    /// 但执行后通过 GD.Print 记录日志供调试。
    ///
    /// 如需真正的 RPC 同步（队友实时看到卖出提示），
    /// 需要继承 Godot.Node 并通过场景树调用 Rpc 方法。
    /// </summary>
    public static class NetworkSync
    {
        /// <summary>判断是否在联机</summary>
        public static bool IsMultiplayer()
        {
            var player = ShopSellManager.GetShopPlayer();
            return player?.RunState?.Players?.Count > 1;
        }
    }
}
