====================================================
  SellToMerchant - STS2 商店出售 Mod
====================================================

功能: 在商店中向商人出售卡牌或遗物换取金币
  - 卖价 = 原价的一半
  - 诅咒牌和初始牌(Strike/Defend/Basic)不可出售
  - 每个商店最多 1 张卡牌 + 1 个遗物
  - 支持多人联机

====================================================
  编译方法
====================================================

1. 环境:
   - Visual Studio 2022 (.NET 桌面开发)
   - .NET 9.0 SDK

2. 双击 SellToMerchant.csproj 打开项目

3. Ctrl+Shift+B 编译

4. 产出 SellToMerchant.dll 在同目录下

5. 安装: 将以下 2 个文件放入
   游戏根目录/mods/SellToMerchant/
   ├── SellToMerchant.json
   └── SellToMerchant.dll

   注意: 本 Mod 不需要 .pck 文件（所有 UI 均为纯 C# 动态创建）。

6. 启动游戏 → 主菜单「模组」→ 确认已加载 (共 3 个 mod)
   如果日志显示 "Pack created with a newer version of the engine"，
   说明误放了旧 .pck 文件，删除即可。

====================================================
  API 确认清单 (已完成！)
====================================================

✅ API点1: 获取当前玩家
   方式: NMerchantRoom.Instance.Room.Inventory.Player
   实际类型: MegaCrit.Sts2.Core.Entities.Players.Player

✅ API点2: 获取牌组 / 遗物列表
   牌组: player.Deck.Cards → IReadOnlyList<CardModel>
   遗物: player.Relics → IReadOnlyList<RelicModel>

✅ API点3: 卡牌选择 / 遗物选择
   卡牌选择: CardSelectCmd.FromDeckForRemoval(player, prefs, filter)
            返回 Task<IReadOnlyList<CardModel>>
   遗物选择: RelicSelectCmd.FromChooseARelicScreen(player, relics)
            返回 Task<IReadOnlyList<RelicModel>>

✅ API点4: 移除卡牌 / 遗物 / 加金币
   移除卡牌: CardPileCmd.RemoveFromDeck(card, showPreview)
   移除遗物: RelicCmd.Remove(relic)
   增加金币: PlayerCmd.GainGold(amount, player, wasStolenBack: false)

====================================================
  售价表
====================================================

  卡牌 (按稀有度, 原价的一半):
    Common   (白) → 25 金币
    Uncommon (蓝) → 37 金币
    Rare     (金) → 75 金币
    诅咒牌 / Basic 牌 → 不可出售

  遗物 (原价的一半):
    价格 = RelicModel.MerchantCost / 2
    Boss 遗物 / 起始遗物 / Special → 不可出售

====================================================
  文件说明
====================================================

  ModEntry.cs              - [ModInitializer] 入口, Harmony 注册
  ShopSellManager.cs       - 核心逻辑 (检查/算价/执行出售)
  MerchantRoomPatches.cs   - Harmony 补丁 (给商店加按钮+选择界面)
  NetworkSync.cs           - 联机 RPC 同步
