# SellToMerchant

Sell cards, relics, and potions at the merchant in Slay the Spire 2.

## Features

- Sell removable cards for gold at the merchant.
- Sell eligible relics and potions for gold.
- Show card sell price only while hovering in the native deck-removal selector.
- Support draggable relic, potion, and update popup windows.
- Support branch-aware auto update for `stable` and `public-beta`.
- Support synchronized multiplayer card selling, relic selling, potion selling, and teammate gold transfer.

## Pricing

- Common card or potion: 25 gold
- Uncommon card or potion: 37 gold
- Rare card or potion: 75 gold
- Relic: half of merchant cost

Cards that are basic, status, curse, quest, event, token, or otherwise not removable are excluded.
Starter relics, event relics, ancient relics, and specific protected relics are excluded.

## Installation

1. Download the correct package from GitHub Releases:
   - `SellToMerchant-stable.zip`
   - `SellToMerchant-public-beta.zip`
2. Extract to `Slay the Spire 2/mods/SellToMerchant`
3. Make sure the folder contains:
   - `SellToMerchant.dll`
   - `SellToMerchant.json`
4. Restart the game.

## Build

Target framework: `.NET 9`

Required local build dependencies in `libs/`:

- `sts2.dll`
- `0Harmony.dll`

Build command:

```powershell
dotnet build .\SellToMerchant.csproj -c Release
```

## Release Notes

### v1.0.5

- Added an in-game auto-update prompt with automatic GitHub download, manual GitHub open, and skip options.
- Reworked the update popup to be centered, draggable, and progress-aware.
- Switched auto-update downloads to the GitHub asset API for more reliable package retrieval.
- Restored multiplayer card selling through the native synchronized selector flow.
- Re-enabled multiplayer relic selling, potion selling, and gold transfer through synchronized network messages.
- Improved teammate name display in the transfer popup.
- Fixed false-positive update prompts after manually updating to `v1.0.5`.
- Moved updater state/result markers away from `.json` manifest-looking filenames.

### v1.0.4

- Fixed public-beta v0.105.0 compatibility for native card sell price binding.
- Normalized resolved card models against the actual sellable deck list to prevent wrong 25/37/75 gold prices.
- Restricted native price binding to the real deck selection holder instead of unrelated card-like controls.
- Added protection for new Neow relics so they are never treated as sellable relics.
- Rebuilt and revalidated the mod package against public-beta v0.105.0.

### v1.0.3

- Fixed wrong card sell prices when many sellable cards were present in the merchant card selector.
- Improved `TryResolveModel` matching for wrapped or interface-typed card models.
- Adjusted price hover and drag behavior.
- Removed more obsolete tooltip code paths.
- Synced repository documentation for the release.

### v1.0.2

- Switched card sell price display to a compact hover panel.
- Removed the old right-side hover tooltip path.
- Optimized hover hit testing.
- Kept relic and potion drag/scroll support.
- Updated auto update repository metadata.

## Compatibility

Validated against Slay the Spire 2 `public-beta v0.105.1`.
Future game updates may require another rebuild if UI or model structures change.

---

## 中文说明

### 功能

- 在商店中出售可移除卡牌换取金币。
- 在商店中出售可交易遗物和药水换取金币。
- 仅在原版移除卡牌选择界面悬停时显示卡牌售价。
- 支持拖动遗物、药水和更新弹窗。
- 支持根据 `stable` / `public-beta` 分支自动检测并匹配更新包。
- 支持联机模式下同步卖卡、卖遗物、卖药水和队友转账。

### 售价规则

- 普通卡牌 / 药水：25 金币
- 非普通卡牌 / 药水：37 金币
- 稀有卡牌 / 药水：75 金币
- 遗物：商店原价的一半

以下内容不可出售：基础牌、状态牌、诅咒牌、任务牌、事件牌、Token 牌，以及其他不可移除卡牌。
以下遗物不可出售：起始遗物、事件遗物、Ancient 遗物，以及特定受保护遗物。

### 安装方法

1. 从 GitHub Releases 下载与你当前游戏分支对应的压缩包：
   - `SellToMerchant-stable.zip`
   - `SellToMerchant-public-beta.zip`
2. 解压到 `Slay the Spire 2/mods/SellToMerchant`
3. 确认目录中包含：
   - `SellToMerchant.dll`
   - `SellToMerchant.json`
4. 重新启动游戏。

### 构建方法

目标框架：`.NET 9`

本地构建依赖 `libs/` 目录中的：

- `sts2.dll`
- `0Harmony.dll`

构建命令：

```powershell
dotnet build .\SellToMerchant.csproj -c Release
```

### 兼容性

当前已针对 Slay the Spire 2 `public-beta v0.105.1` 验证。
若游戏后续更新了 UI 或数据结构，模组可能需要重新适配和重编译。
