# SellToMerchant

Slay the Spire 2 商店出售功能 Mod。

## 功能

- 在商店内出售卡牌、遗物和药水换取金币。
- 卡牌售价仅在卡牌出售选择界面悬停时显示。
- 遗物和药水出售窗口支持拖动和滚轮滚动。
- 联机时支持向队友转账金币，单次上限 200G。
- 自动根据游戏分支选择 `stable` 或 `public-beta` 更新包。

## 价格规则

- 普通卡牌或药水：25 金币
- 罕见卡牌或药水：37 金币
- 稀有卡牌或药水：75 金币
- 遗物：商店价格的一半
- 基础牌、状态牌、诅咒牌、任务牌、事件牌、Token 牌、初始遗物和部分不可交易遗物不可出售

## 安装

1. 从 GitHub Release 下载对应版本：
   - 正式版：`SellToMerchant-stable.zip`
   - 公测版：`SellToMerchant-public-beta.zip`
2. 解压到 `Slay the Spire 2/mods/SellToMerchant`
3. 确认目录内包含：
   - `SellToMerchant.dll`
   - `SellToMerchant.json`
4. 重启游戏

## 构建

项目目标框架为 `.NET 9`。

构建前需要在 `libs/` 中放入：

- `sts2.dll`
- `0Harmony.dll`

构建命令：

```powershell
dotnet build .\SellToMerchant.csproj -c Release
```

## 版本说明

### v1.0.3

- 修复大量可售卡牌时卡牌售价显示错乱的问题。
- 修复 `TryResolveModel` 只做精确类型匹配，导致部分卡牌售价错误的问题。
- 优化卡牌售价浮窗与拖动逻辑，减少干扰。
- 清理不再使用的旧代码，降低后续维护成本。
- 补充并同步 `README` 与 `CHANGELOG` 版本说明。

### v1.0.2

- 将卡牌售价显示改为悬停小浮窗。
- 移除旧的右侧 HoverTip 售价显示逻辑，避免和关键词提示冲突。
- 优化悬停命中检测，优先使用控件自身矩形，减少递归扫描。
- 保留遗物和药水窗口的拖动与滚动支持。
- 更新自动更新仓库与版本号。

## 兼容性

面向 Slay the Spire 2 正式版与 `public-beta` 分支。若游戏接口更新，可能需要重新编译。
