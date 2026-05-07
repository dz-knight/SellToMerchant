# SellToMerchant

Slay the Spire 2 商店出售功能 Mod。

## 功能

- 在商店内出售卡牌、遗物和药水获得金币。
- 卡牌售价只在卡牌选择界面悬停时显示，避免大量卡牌时持续绘制造成卡顿。
- 遗物和药水出售窗口支持拖动，列表内容过多时支持滚轮滚动。
- 联机时支持向队友转账金币，单次上限 200G。
- 自动按游戏分支选择 stable 或 public-beta 发布包。

## 价格规则

- 普通卡牌/药水：25 金币。
- 罕见卡牌/药水：37 金币。
- 稀有卡牌/药水：75 金币。
- 遗物：商店价格的一半。
- 基础牌、状态牌、诅咒牌、任务牌、事件牌、Token 牌、初始遗物、事件遗物等不可出售。

## 安装

1. 从 GitHub Release 下载对应版本：
   - 正式版：`SellToMerchant-stable.zip`
   - public-beta：`SellToMerchant-public-beta.zip`
2. 解压到游戏 Mod 目录：
   `Slay the Spire 2/mods/SellToMerchant`
3. 确认目录内存在：
   - `SellToMerchant.dll`
   - `SellToMerchant.json`
4. 重启游戏。

## 构建

项目目标框架为 `.NET 9`。

本地构建前需要在 `libs/` 目录放入游戏运行时依赖：

- `sts2.dll`
- `0Harmony.dll`

构建命令：

```powershell
dotnet build .\SellToMerchant.csproj -c Release
```

Release DLL 输出位置：

```text
bin/Release/net9.0/SellToMerchant.dll
```

## v1.0.2

- 修复卡牌售价显示路径，统一改为悬停小浮窗。
- 删除旧的右侧 HoverTip 售价显示逻辑，避免与卡牌关键词提示冲突。
- 优化悬停检测热路径，优先使用控件自身矩形，减少子树递归扫描。
- 更新自动更新仓库与版本号。

## 兼容性

该 Mod 面向 Slay the Spire 2 当前正式版和 public-beta 分支。由于游戏仍在更新，若游戏 API 发生变化，可能需要重新编译。
