using System.Linq;
using System.Collections.Generic;
using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Localization;
using Godot;

namespace SellToMerchant
{
    [HarmonyPatch(typeof(NMerchantRoom))]
    public static class MerchantRoomPatches
    {
        private static Control? _sidePanel;
        private static Control? _sellCardBtn, _sellRelicBtn, _sellPotionBtn, _transferBtn;
        private static Label? _sellCardLbl, _sellRelicLbl, _sellPotionLbl, _transferLbl;
        private static Label? _cardInfoLbl, _relicInfoLbl, _potionInfoLbl;
        private static Control? _popup;
        private static NMegaLineEdit? _amountInput;
        private static Player? _selectedReceiver;
        private static CanvasLayer? _uiLayer;
        private static Control? _draggedControl;
        private static PanelContainer? _fallbackSellPricePanel;
        private static Label? _fallbackSellPriceTitle;
        private static Label? _fallbackSellPriceBody;
        private static readonly HashSet<ulong> _nativeSelectionBoundNodes = new();
        private static readonly HashSet<ulong> _nativeSelectionScannedNodes = new();
        private static readonly Dictionary<ulong, (Control displayControl, Control hitControl, int price)> _nativeSelectionPriceTargets = new();
        private static IReadOnlyList<CardModel> _currentSellableCards = Array.Empty<CardModel>();
        private static NativeSelectionKind _nativeSelectionKind;

        private static RelicModel? _selectedRelic;
        private static PotionModel? _selectedPotion;

        private enum NativeSelectionKind
        {
            None,
            Card,
        }

        private sealed class RowState
        {
            public Control Row = null!;
            public StyleBoxFlat Style = null!;
            public Color DefaultBg;
            public Color DefaultBorder;
            public Label? PriceLabel;
            public bool IsSelected;
        }

        private static readonly List<RowState> _activeRows = new();

        [HarmonyPostfix]
        [HarmonyPatch("OpenInventory")]
        public static void AfterOpenInventory(NMerchantRoom __instance)
        {
            if (_uiLayer != null && _uiLayer.IsInsideTree())
            {
                RefreshInfoLabels();
                UpdateButtonStates();
                return;
            }

            ShopSellManager.ResetShopLimits();

            _uiLayer?.QueueFree();
            ClosePopup();
            _sidePanel = null;
            _sellCardBtn = _sellRelicBtn = _sellPotionBtn = _transferBtn = null;

            _uiLayer = new CanvasLayer { Layer = 100 };

            BuildSidePanel(__instance);

            __instance.AddChild(_uiLayer);

            RefreshInfoLabels();
            UpdateButtonStates();
            GD.Print("[SellToMerchant] UI ready.");
        }

        [HarmonyPrefix]
        [HarmonyPatch("_ExitTree")]
        public static void BeforeExit()
        {
            ShopSellManager.ResetShopLimits();
            _uiLayer?.QueueFree();
            _uiLayer = null;
            _sidePanel = null;
            _sellCardBtn = _sellRelicBtn = _sellPotionBtn = _transferBtn = null;
            StopNativeSelectionPriceWatcher();
            ClosePopup();
        }

        // ═══════════════════════════════════════════
        //  右侧竖排出售面板
        // ═══════════════════════════════════════════

        private static void BuildSidePanel(NMerchantRoom room)
        {
            float panelW = 160, btnW = 140, btnH = 30, gap = 6, infoH = 14;
            float margin = 12;
            float panelX = room.Size.X - panelW - margin;
            float panelY = 130;

            var player = ShopSellManager.GetShopPlayer();
            bool isMulti = player != null && ShopSellManager.GetTeammates(player).Count > 0;

            int rowCount = isMulti ? 4 : 3;
            float panelH = 32 + rowCount * (btnH + infoH + gap) + gap;

            var panelBg = new ColorRect();
            panelBg.Color = new Color(0.06f, 0.06f, 0.06f, 0.82f);
            panelBg.Size = new Vector2(panelW, panelH);

            var border = new ColorRect();
            border.Color = new Color(0.7f, 0.55f, 0.15f, 0.5f);
            border.Size = new Vector2(panelW, panelH);

            _sidePanel = new Control
            {
                Position = new Vector2(panelX, panelY),
                Size = new Vector2(panelW, panelH)
            };
            _sidePanel.AddChild(panelBg);
            _sidePanel.AddChild(border);

            var title = new Label
            {
                Text = "— 出售 —",
                Position = new Vector2(0, 5),
                Size = new Vector2(panelW, 20),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _sidePanel.AddChild(title);

            float curY = 28;
            float btnX = (panelW - btnW) / 2;

            (_sellCardBtn, _sellCardLbl) = MakeButton("出售卡牌",
                new Vector2(btnX, curY), new Vector2(btnW, btnH), OnSellCardClicked);
            _sidePanel.AddChild(_sellCardBtn);
            curY += btnH + 2;
            _cardInfoLbl = MakeInfoLabel(new Vector2(btnX, curY), new Vector2(btnW, infoH));
            _sidePanel.AddChild(_cardInfoLbl);
            curY += infoH + gap;

            (_sellRelicBtn, _sellRelicLbl) = MakeButton("出售遗物",
                new Vector2(btnX, curY), new Vector2(btnW, btnH), OnSellRelicClicked);
            _sidePanel.AddChild(_sellRelicBtn);
            curY += btnH + 2;
            _relicInfoLbl = MakeInfoLabel(new Vector2(btnX, curY), new Vector2(btnW, infoH));
            _sidePanel.AddChild(_relicInfoLbl);
            curY += infoH + gap;

            (_sellPotionBtn, _sellPotionLbl) = MakeButton("出售药水",
                new Vector2(btnX, curY), new Vector2(btnW, btnH), OnSellPotionClicked);
            _sidePanel.AddChild(_sellPotionBtn);
            curY += btnH + 2;
            _potionInfoLbl = MakeInfoLabel(new Vector2(btnX, curY), new Vector2(btnW, infoH));
            _sidePanel.AddChild(_potionInfoLbl);
            curY += infoH + gap;

            if (isMulti)
            {
                (_transferBtn, _transferLbl) = MakeButton("转账金币",
                    new Vector2(btnX, curY), new Vector2(btnW, btnH), OnTransferClicked);
                _sidePanel.AddChild(_transferBtn);
                curY += btnH + gap;
            }

            // 使面板可通过背景和标题拖动
            MakePopupDraggable(_sidePanel, panelBg, border, title);

            _uiLayer!.AddChild(_sidePanel);
        }

        private static Label MakeInfoLabel(Vector2 pos, Vector2 size)
        {
            return new Label
            {
                Position = pos,
                Size = size,
                HorizontalAlignment = HorizontalAlignment.Center,
                SelfModulate = new Color(0.7f, 0.7f, 0.7f)
            };
        }

        private static void RefreshInfoLabels()
        {
            var player = ShopSellManager.GetShopPlayer();
            if (player == null) return;

            var cards = ShopSellManager.GetSellableCards(player);
            var relics = ShopSellManager.GetSellableRelics(player);
            var potions = ShopSellManager.GetSellablePotions(player);

            SetInfo(_cardInfoLbl, cards, "卡");
            SetInfo(_relicInfoLbl, relics, "遗物");
            SetInfo(_potionInfoLbl, potions, "药水");
        }

        private static void SetInfo(Label? lbl, IReadOnlyCollection<object> items, string unit)
        {
            if (lbl == null) return;
            lbl.Text = items.Count == 0
                ? $"没有{unit}可卖"
                : $"{items.Count}个 {unit}";
        }

        // ═══════════════════════════════════════════
        //  出售卡牌 — 自定义弹窗 + 悬停显示价格
        // ═══════════════════════════════════════════

        private static async void OnSellCardClicked()
        {
            if (ShopSellManager.CardSoldThisShop) return;
            var player = ShopSellManager.GetShopPlayer();
            if (player == null) return;

            var cards = ShopSellManager.GetSellableCards(player);
            if (cards.Count == 0)
            {
                ShowInfoPopup("没有卡牌可以出售");
                return;
            }

            ClosePopup();

            var prefs = BuildCardSellSelectorPrefs();
            _currentSellableCards = cards;
            StartNativeSelectionPriceWatcher(NativeSelectionKind.Card);
            GD.Print("[SellToMerchant] Opening native card sell selector.");
            try
            {
                var selected = await CardSelectCmd.FromDeckForRemoval(player, prefs, ShopSellManager.CanSellCard);
                var card = selected?.FirstOrDefault();
                if (card == null) return;

                await ShopSellManager.SellCardAsync(card, player);
                RefreshInfoLabels();
                UpdateButtonStates();
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
            }
            finally
            {
                StopNativeSelectionPriceWatcher();
            }
        }

        // ═══════════════════════════════════════════
        //  出售遗物 — 自定义弹窗 + 悬停显示价格
        // ═══════════════════════════════════════════

        private static void OnSellRelicClicked()
        {
            if (ShopSellManager.RelicSoldThisShop) return;
            var player = ShopSellManager.GetShopPlayer();
            if (player == null) return;

            var relics = ShopSellManager.GetSellableRelics(player);
            if (relics.Count == 0)
            {
                ShowInfoPopup("没有遗物可以出售");
                return;
            }

            GD.Print($"[SellToMerchant] Opening scrollable relic sell selector with {relics.Count} relics.");
            ShowRelicSellPopup(player, relics);
        }

        private static void ShowRelicSellPopup(Player player, List<RelicModel> relics)
        {
            ClosePopup();
            _selectedRelic = null;

            float popupW = 520, btnH = 30, gap = 6;
            int columns = 2;
            int visibleRows = 8;
            int rows = (int)Math.Ceiling(relics.Count / (float)columns);
            float tileW = (popupW - 28 - gap) / columns;
            float listH = Math.Min(rows, visibleRows) * (btnH + gap) + gap;
            float totalListH = rows * (btnH + gap) + gap;
            float popupH = 56 + listH + (btnH + 16);

            _popup = new Control();
            _popup.Position = new Vector2(190, 72);

            var bg = new ColorRect();
            bg.Color = new Color(0, 0, 0, 0.92f);
            bg.Size = new Vector2(popupW, popupH);
            _popup.AddChild(bg);

            var title = new Label
            {
                Text = "选择要出售的遗物（悬停查看价格）",
                Position = new Vector2(10, 8),
                Size = new Vector2(popupW - 20, 20)
            };
            _popup.AddChild(title);

            MakePopupDraggable(_popup, bg, title);

            float listY = 34;

            var scroll = new ScrollContainer
            {
                Position = new Vector2(8, listY),
                Size = new Vector2(popupW - 16, listH)
            };
            scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
            scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
            AttachManualWheelScroll(scroll, totalListH - listH, scroll);

            var listContent = new Control
            {
                Size = new Vector2(popupW - 32, totalListH),
                CustomMinimumSize = new Vector2(popupW - 32, totalListH)
            };
            AttachManualWheelScroll(scroll, totalListH - listH, listContent);

            for (int i = 0; i < relics.Count; i++)
            {
                int col = i % columns;
                int rowIndex = i / columns;
                var pos = new Vector2(col * (tileW + gap), rowIndex * (btnH + gap));
                var row = BuildRelicRow(relics[i], pos, new Vector2(tileW, btnH));
                row.CustomMinimumSize = new Vector2(tileW, btnH);
                AttachManualWheelScroll(scroll, totalListH - listH, row);
                listContent.AddChild(row);
            }

            scroll.AddChild(listContent);
            _popup.AddChild(scroll);

            float btnY = listY + listH + 8;

            var (confirmBtn, _) = MakeButton("确定出售",
                new Vector2(155, btnY), new Vector2(100, btnH), async () =>
                {
                    if (_selectedRelic != null)
                    {
                        var r = _selectedRelic;
                        ClosePopup();
                        await ShopSellManager.SellRelicAsync(r, player);
                        RefreshInfoLabels();
                        UpdateButtonStates();
                    }
                });
            _popup.AddChild(confirmBtn);

            var (cancelBtn, _) = MakeButton("取消",
                new Vector2(275, btnY), new Vector2(90, btnH), () => ClosePopup());
            _popup.AddChild(cancelBtn);

            _uiLayer?.AddChild(_popup);
        }

        private static Control BuildRelicRow(RelicModel relic, Vector2 pos, Vector2 size)
        {
            return BuildSelectableRow(
                SafeItemName(relic.Title, relic.Id),
                ShopSellManager.GetRelicSellPrice(relic),
                pos, size,
                () => _selectedRelic = relic);
        }

        // ═══════════════════════════════════════════
        //  通用可选行（卡牌 / 遗物 / 药水共用）
        // ═══════════════════════════════════════════

        private static Control BuildSelectableRow(string itemName, int price, Vector2 pos, Vector2 size, Action onSelect)
        {
            var row = new Panel { Position = pos, Size = size };

            var style = new StyleBoxFlat();
            Color defaultBg = new Color(0.15f, 0.15f, 0.15f, 0.7f);
            Color defaultBorder = new Color(0.4f, 0.4f, 0.4f, 0.6f);
            style.BgColor = defaultBg;
            style.BorderColor = defaultBorder;
            style.BorderWidthLeft = style.BorderWidthRight = 1;
            style.BorderWidthTop = style.BorderWidthBottom = 1;
            style.CornerRadiusTopLeft = style.CornerRadiusTopRight = 3;
            style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 3;
            row.AddThemeStyleboxOverride("panel", style);

            var nameLbl = new Label
            {
                Text = itemName,
                Position = new Vector2(8, 2),
                Size = new Vector2(size.X - 70, size.Y - 4),
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            row.AddChild(nameLbl);

            var priceLbl = new Label
            {
                Text = $"{price}G",
                Position = new Vector2(size.X - 58, 2),
                Size = new Vector2(50, size.Y - 4),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                SelfModulate = new Color(1f, 0.85f, 0.3f),
                Visible = false,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            row.AddChild(priceLbl);

            var rs = new RowState
            {
                Row = row,
                Style = style,
                DefaultBg = defaultBg,
                DefaultBorder = defaultBorder,
                PriceLabel = priceLbl
            };
            _activeRows.Add(rs);

            row.MouseEntered += () =>
            {
                if (!rs.IsSelected)
                {
                    rs.Style.BgColor = new Color(0.22f, 0.22f, 0.18f, 0.75f);
                    rs.Style.BorderColor = new Color(0.6f, 0.55f, 0.2f, 0.8f);
                }
                rs.PriceLabel!.Visible = true;
            };

            row.MouseExited += () =>
            {
                if (!rs.IsSelected)
                {
                    rs.Style.BgColor = rs.DefaultBg;
                    rs.Style.BorderColor = rs.DefaultBorder;
                }
                rs.PriceLabel!.Visible = false;
            };

            row.GuiInput += (InputEvent e) =>
            {
                if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
                {
                    foreach (var r in _activeRows)
                    {
                        r.Style.BgColor = r.DefaultBg;
                        r.Style.BorderColor = r.DefaultBorder;
                        r.IsSelected = false;
                    }
                    rs.Style.BgColor = new Color(0.25f, 0.22f, 0.1f, 0.8f);
                    rs.Style.BorderColor = new Color(1f, 0.85f, 0.3f, 1f);
                    rs.IsSelected = true;
                    onSelect();
                }
            };

            return row;
        }

        // ═══════════════════════════════════════════
        //  出售药水 — 列表弹窗 + 悬停显示价格
        // ═══════════════════════════════════════════

        private static void OnSellPotionClicked()
        {
            if (ShopSellManager.PotionSoldThisShop) return;
            var player = ShopSellManager.GetShopPlayer();
            if (player == null) return;

            var potions = ShopSellManager.GetSellablePotions(player);
            if (potions.Count == 0)
            {
                ShowInfoPopup("没有药水可以出售");
                return;
            }

            ShowPotionSellPopup(player, potions);
        }

        private static void ShowPotionSellPopup(Player player, List<PotionModel> potions)
        {
            ClosePopup();
            _selectedPotion = null;

            float popupW = 280, btnH = 30, gap = 4;
            int count = potions.Count;
            int visibleRows = 8;
            float listH = Math.Min(count, visibleRows) * (btnH + gap) + gap;
            float totalListH = count * (btnH + gap) + gap;
            float popupH = 56 + listH + (btnH + 16);

            _popup = new Control();
            _popup.Position = new Vector2(260, 120);

            var bg = new ColorRect();
            bg.Color = new Color(0, 0, 0, 0.92f);
            bg.Size = new Vector2(popupW, popupH);
            _popup.AddChild(bg);

            var title = new Label
            {
                Text = "选择要出售的药水（悬停查看价格）",
                Position = new Vector2(10, 8),
                Size = new Vector2(popupW - 20, 20)
            };
            _popup.AddChild(title);
            MakePopupDraggable(_popup, bg, title);

            float listY = 34;
            var scroll = new ScrollContainer
            {
                Position = new Vector2(8, listY),
                Size = new Vector2(popupW - 16, listH)
            };
            scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
            scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
            AttachManualWheelScroll(scroll, totalListH - listH, scroll);

            var listContent = new Control
            {
                Size = new Vector2(popupW - 32, totalListH),
                CustomMinimumSize = new Vector2(popupW - 32, totalListH)
            };
            AttachManualWheelScroll(scroll, totalListH - listH, listContent);

            float rowY = 0;
            foreach (var potion in potions)
            {
                var row = BuildPotionRow(potion, new Vector2(0, rowY), new Vector2(popupW - 32, btnH));
                row.CustomMinimumSize = new Vector2(popupW - 32, btnH);
                AttachManualWheelScroll(scroll, totalListH - listH, row);
                listContent.AddChild(row);
                rowY += btnH + gap;
            }

            scroll.AddChild(listContent);
            _popup.AddChild(scroll);

            float btnY = listY + listH + 8;

            var (confirmBtn, _) = MakeButton("确定出售",
                new Vector2(40, btnY), new Vector2(100, btnH), async () =>
                {
                    if (_selectedPotion != null)
                    {
                        var p = _selectedPotion;
                        ClosePopup();
                        await ShopSellManager.SellPotionAsync(p, player);
                        RefreshInfoLabels();
                        UpdateButtonStates();
                    }
                });
            _popup.AddChild(confirmBtn);

            var (cancelBtn, _) = MakeButton("取消",
                new Vector2(150, btnY), new Vector2(90, btnH), () => ClosePopup());
            _popup.AddChild(cancelBtn);

            _uiLayer?.AddChild(_popup);
        }

        private static Control BuildPotionRow(PotionModel potion, Vector2 pos, Vector2 size)
        {
            return BuildSelectableRow(
                SafeItemName(potion.Title, potion.Id),
                ShopSellManager.GetPotionSellPrice(potion),
                pos, size,
                () => _selectedPotion = potion);
        }

        private static string SafeItemName(LocString? title, object fallbackId)
        {
            var text = title?.GetFormattedText();
            if (!string.IsNullOrWhiteSpace(text))
                return text;

            text = title?.GetRawText();
            if (!string.IsNullOrWhiteSpace(text))
                return text;

            return SafeItemName((string?)null, fallbackId);
        }

        private static string SafeItemName(string? title, object fallbackId)
        {
            if (!string.IsNullOrWhiteSpace(title))
                return title;

            var fallback = fallbackId.ToString();
            return string.IsNullOrWhiteSpace(fallback) ? "Unknown" : fallback;
        }

        private static CardSelectorPrefs BuildCardSellSelectorPrefs()
        {
            return new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, 1)
            {
                Cancelable = true,
                RequireManualConfirmation = true,
                ShouldGlowGold = ShopSellManager.CanSellCard,
            };
        }

        private static void StartNativeSelectionPriceWatcher(NativeSelectionKind kind)
        {
            _nativeSelectionKind = kind;
            _nativeSelectionBoundNodes.Clear();
            _nativeSelectionScannedNodes.Clear();
            _nativeSelectionPriceTargets.Clear();
            HideFallbackSellPricePanel();

            GD.Print($"[SellToMerchant] Starting native {kind} price watcher.");
            _ = WatchNativeSelectionPricesAsync(kind);
        }

        private static void StopNativeSelectionPriceWatcher()
        {
            if (_nativeSelectionKind != NativeSelectionKind.None)
                GD.Print($"[SellToMerchant] Stopping native {_nativeSelectionKind} price watcher.");

            _nativeSelectionKind = NativeSelectionKind.None;
            _nativeSelectionBoundNodes.Clear();
            _nativeSelectionScannedNodes.Clear();
            _nativeSelectionPriceTargets.Clear();
            _currentSellableCards = Array.Empty<CardModel>();
            HideFallbackSellPricePanel();
        }

        private static async System.Threading.Tasks.Task WatchNativeSelectionPricesAsync(NativeSelectionKind kind)
        {
            var tree = _uiLayer?.GetTree();
            if (tree == null)
            {
                GD.Print("[SellToMerchant] Native price watcher could not start: SceneTree unavailable.");
                return;
            }

            int tick = 0;
            while (_nativeSelectionKind == kind && _uiLayer != null && _uiLayer.IsInsideTree())
            {
                try
                {
                    bool shouldScan = tick < 12 || tick % 8 == 0;
                    if (shouldScan)
                        BindNativeSelectionPriceHover(kind);

                    UpdateNativeSelectionPricePanels();
                    UpdateNativeSellPriceTip(kind);
                }
                catch (Exception ex)
                {
                    GD.Print($"[SellToMerchant] Native price watcher error: {ex.Message}");
                }
                tick++;
                await _uiLayer.ToSignal(tree.CreateTimer(0.12), SceneTreeTimer.SignalName.Timeout);
            }
        }

        private static void BindNativeSelectionPriceHover(NativeSelectionKind kind)
        {
            if (_uiLayer?.GetTree()?.Root is not Node root)
                return;

            BindNativeSelectionPriceHoverRecursive(root, kind);
        }

        private static void BindNativeSelectionPriceHoverRecursive(Node node, NativeSelectionKind kind)
        {
            if (node is Control control && control.IsInsideTree())
            {
                var scannedId = control.GetInstanceId();
                if (!_nativeSelectionScannedNodes.Contains(scannedId) &&
                    TryGetNativeSelectionPriceTarget(control, kind, out var displayControl, out var hitControl, out var price))
                {
                    var id = displayControl.GetInstanceId();
                    if (!_nativeSelectionBoundNodes.Contains(id))
                    {
                        _nativeSelectionBoundNodes.Add(id);
                        _nativeSelectionPriceTargets[id] = (displayControl, hitControl, price);
                        GD.Print($"[SellToMerchant] Bound native {kind} price hover: {displayControl.GetType().FullName} hit {hitControl.GetType().FullName} -> {price}G");
                    }
                }

                _nativeSelectionScannedNodes.Add(scannedId);
            }

            foreach (var child in node.GetChildren())
            {
                if (child is Node childNode)
                    BindNativeSelectionPriceHoverRecursive(childNode, kind);
            }
        }

        private static bool TryGetNativeSelectionPriceTarget(Control control, NativeSelectionKind kind, out Control displayControl, out Control hitControl, out int price)
        {
            displayControl = control;
            hitControl = control;
            price = 0;

            if (kind == NativeSelectionKind.Card)
            {
                var holder = FindNativeCardSelectionHolder(control);
                if (holder == null || !IsDeckCardSelectionHolder(holder))
                    return false;

                if (!TryResolveDisplayedCardModel(control, holder, out var card))
                    return false;

                if (!ShopSellManager.CanSellCard(card))
                    return false;

                price = ShopSellManager.GetCardSellPrice(card);
                if (price <= 0)
                {
                    GD.Print($"[SellToMerchant] Card sell price is zero: title={SafeItemName(card.Title, card.Id)}, id={card.Id}, rarity={card.Rarity}");
                    return false;
                }

                displayControl = holder;
                hitControl = holder;
                return true;
            }

            return false;
        }

        private static Control? FindNativeCardSelectionHolder(Control control)
        {
            Node? current = control;
            for (int depth = 0; depth < 10 && current != null; depth++)
            {
                if (current is Control currentControl)
                {
                    var typeName = currentControl.GetType().FullName ?? currentControl.GetType().Name;
                    if (typeName.Contains("Cards.Holders.NGridCardHolder", StringComparison.Ordinal))
                        return currentControl;
                }

                current = current.GetParent();
            }

            return null;
        }

        private static bool IsDeckCardSelectionHolder(Control holder)
        {
            Node? current = holder;
            for (int depth = 0; depth < 12 && current != null; depth++)
            {
                var typeName = current.GetType().FullName ?? current.GetType().Name;
                if (typeName.Contains("Screens.CardSelection.NDeckCardSelectScreen", StringComparison.Ordinal))
                    return true;

                current = current.GetParent();
            }

            return false;
        }

        private static bool TryResolveDisplayedCardModel(Control control, Control holder, out CardModel card)
        {
            foreach (var candidate in EnumerateCandidateCardControls(control, holder))
            {
                if (TryResolveDirectModel<CardModel>(candidate, out var resolved) &&
                    TryNormalizeSellableCard(resolved, out card))
                    return true;
            }

            foreach (var candidate in EnumerateCandidateCardControls(control, holder))
            {
                if (TryResolveModel<CardModel>(candidate, out var resolved) &&
                    TryNormalizeSellableCard(resolved, out card))
                    return true;
            }

            card = null!;
            return false;
        }

        private static IEnumerable<Control> EnumerateCandidateCardControls(Control control, Control holder)
        {
            var ordered = new List<Control>();
            var seen = new HashSet<ulong>();

            AddCandidateControl(control, ordered, seen);
            AddCandidateControl(holder, ordered, seen);
            CollectCandidateCardControls(holder, ordered, seen, depth: 0);

            return ordered;
        }

        private static void AddCandidateControl(Control control, List<Control> ordered, HashSet<ulong> seen)
        {
            var id = control.GetInstanceId();
            if (seen.Add(id))
                ordered.Add(control);
        }

        private static void CollectCandidateCardControls(Node node, List<Control> ordered, HashSet<ulong> seen, int depth)
        {
            if (depth > 3)
                return;

            if (node is Control control)
            {
                var typeName = control.GetType().FullName ?? control.GetType().Name;
                if (typeName.Contains(".Cards.NCard", StringComparison.Ordinal) ||
                    typeName.Contains("NCardHolderHitbox", StringComparison.Ordinal) ||
                    typeName.Contains("NGridCardHolder", StringComparison.Ordinal))
                {
                    AddCandidateControl(control, ordered, seen);
                }
            }

            foreach (var child in node.GetChildren())
            {
                if (child is Node childNode)
                    CollectCandidateCardControls(childNode, ordered, seen, depth + 1);
            }
        }

        private static bool TryNormalizeSellableCard(CardModel candidate, out CardModel card)
        {
            if (_currentSellableCards.Count == 0)
            {
                card = candidate;
                return true;
            }

            if (_currentSellableCards.Contains(candidate))
            {
                card = candidate;
                return true;
            }

            var candidateId = candidate.Id?.ToString() ?? string.Empty;
            var candidateTitle = SafeItemName(candidate.Title, candidate.Id?.ToString() ?? string.Empty);

            var matches = _currentSellableCards
                .Where(current =>
                    string.Equals(current.Id?.ToString(), candidateId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(SafeItemName(current.Title, current.Id?.ToString() ?? string.Empty), candidateTitle, StringComparison.Ordinal))
                .ToList();

            if (matches.Count == 1)
            {
                card = matches[0];
                return true;
            }

            var sameRarity = matches.FirstOrDefault(current => current.Rarity == candidate.Rarity);
            if (sameRarity != null)
            {
                card = sameRarity;
                return true;
            }

            card = null!;
            return false;
        }

        private static bool TryResolveDirectModel<TModel>(object source, out TModel model) where TModel : class
        {
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            foreach (var memberName in new[] { "Card", "CardModel", "_card", "_cardModel", "Model", "_model" })
            {
                var property = source.GetType().GetProperty(memberName, flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        if (property.GetValue(source) is TModel foundFromProperty)
                        {
                            model = foundFromProperty;
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }

                var field = source.GetType().GetField(memberName, flags);
                if (field != null)
                {
                    try
                    {
                        if (field.GetValue(source) is TModel foundFromField)
                        {
                            model = foundFromField;
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            model = null!;
            return false;
        }

        private static void UpdateNativeSelectionPricePanels()
        {
            foreach (var pair in _nativeSelectionPriceTargets.ToArray())
            {
                var id = pair.Key;
                var (displayControl, hitControl, _) = pair.Value;
                if (!GodotObject.IsInstanceValid(displayControl) || !displayControl.IsInsideTree() || !displayControl.Visible ||
                    !GodotObject.IsInstanceValid(hitControl) || !hitControl.IsInsideTree() || !hitControl.Visible)
                {
                    _nativeSelectionPriceTargets.Remove(id);
                }
            }
        }

        private static bool TryResolveModel<TModel>(object source, out TModel model) where TModel : class
        {
            var type = source.GetType();
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            foreach (var property in type.GetProperties(flags))
            {
                if (property.GetIndexParameters().Length > 0) continue;
                var propType = property.PropertyType;
                if (!typeof(TModel).IsAssignableFrom(propType) &&
                    !propType.IsAssignableFrom(typeof(TModel))) continue;
                try
                {
                    if (property.GetValue(source) is TModel foundFromProperty)
                    {
                        model = foundFromProperty;
                        return true;
                    }
                }
                catch
                {
                }
            }

            foreach (var field in type.GetFields(flags))
            {
                var fieldType = field.FieldType;
                if (!typeof(TModel).IsAssignableFrom(fieldType) &&
                    !fieldType.IsAssignableFrom(typeof(TModel))) continue;
                try
                {
                    if (field.GetValue(source) is TModel foundFromField)
                    {
                        model = foundFromField;
                        return true;
                    }
                }
                catch
                {
                }
            }

            model = null!;
            return false;
        }

        private static void UpdateNativeSellPriceTip(NativeSelectionKind kind)
        {
            if (TryFindHoveredPriceTarget(out var hoveredControl, out var hoveredPrice))
            {
                EnsureFallbackSellPricePanel();
                if (_fallbackSellPricePanel != null && _fallbackSellPriceTitle != null && _fallbackSellPriceBody != null)
                {
                    _fallbackSellPriceTitle.Text = "\u552e\u4ef7";
                    _fallbackSellPriceBody.Text = $"{hoveredPrice} \u91d1\u5e01";
                    _fallbackSellPricePanel.Position = GetSellPricePanelPosition(hoveredControl);
                    _fallbackSellPricePanel.Visible = true;
                }

                return;
            }

            HideFallbackSellPricePanel();
        }

        private static Vector2 GetSellPricePanelPosition(Control cardControl)
        {
            var rect = GetUsableGlobalRect(cardControl);
            var panelSize = _fallbackSellPricePanel?.Size ?? new Vector2(112f, 76f);
            var position = rect.Position + new Vector2(rect.Size.X * 0.58f, rect.Size.Y * 0.52f);

            if (_uiLayer?.GetViewport() != null)
            {
                var viewportSize = _uiLayer.GetViewport().GetVisibleRect().Size;
                position.X = Mathf.Clamp(position.X, 8f, Mathf.Max(8f, viewportSize.X - panelSize.X - 8f));
                position.Y = Mathf.Clamp(position.Y, 8f, Mathf.Max(8f, viewportSize.Y - panelSize.Y - 8f));
            }

            return position;
        }


        private static void EnsureFallbackSellPricePanel()
        {
            if (_fallbackSellPricePanel != null && GodotObject.IsInstanceValid(_fallbackSellPricePanel))
                return;

            _fallbackSellPricePanel = new PanelContainer
            {
                Visible = false,
                TopLevel = true,
                CustomMinimumSize = new Vector2(112f, 76f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZIndex = 3000
            };

            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.22f, 0.12f, 0.12f, 0.95f);
            style.BorderColor = new Color(0.6f, 0.34f, 0.30f, 1f);
            style.BorderWidthLeft = 2;
            style.BorderWidthRight = 2;
            style.BorderWidthTop = 2;
            style.BorderWidthBottom = 2;
            style.CornerRadiusTopLeft = 8;
            style.CornerRadiusTopRight = 8;
            style.CornerRadiusBottomLeft = 8;
            style.CornerRadiusBottomRight = 8;
            style.ContentMarginLeft = 12;
            style.ContentMarginRight = 12;
            style.ContentMarginTop = 8;
            style.ContentMarginBottom = 8;
            _fallbackSellPricePanel.AddThemeStyleboxOverride("panel", style);

            var vbox = new VBoxContainer
            {
                MouseFilter = Control.MouseFilterEnum.Ignore
            };

            _fallbackSellPriceTitle = new Label
            {
                SelfModulate = new Color(1f, 0.86f, 0.33f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _fallbackSellPriceBody = new Label
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            vbox.AddChild(_fallbackSellPriceTitle);
            vbox.AddChild(_fallbackSellPriceBody);
            _fallbackSellPricePanel.AddChild(vbox);
            _uiLayer?.AddChild(_fallbackSellPricePanel);
        }

        private static void HideFallbackSellPricePanel()
        {
            if (_fallbackSellPricePanel != null && GodotObject.IsInstanceValid(_fallbackSellPricePanel))
                _fallbackSellPricePanel.Visible = false;
        }

        private static bool TryFindHoveredPriceTarget(out Control control, out int price)
        {
            control = null!;
            price = 0;

            if (_uiLayer?.GetViewport() == null)
                return false;

            var mouse = _uiLayer.GetViewport().GetMousePosition();
            float bestArea = 0f;

            foreach (var target in _nativeSelectionPriceTargets.Values)
            {
                if (!GodotObject.IsInstanceValid(target.displayControl) || !target.displayControl.IsInsideTree() || !target.displayControl.Visible ||
                    !GodotObject.IsInstanceValid(target.hitControl) || !target.hitControl.IsInsideTree() || !target.hitControl.Visible)
                    continue;

                var rect = GetUsableGlobalRect(target.hitControl);
                if (rect.Size.X <= 1f || rect.Size.Y <= 1f)
                    rect = GetUsableGlobalRect(target.displayControl);
                if (!rect.HasPoint(mouse))
                    continue;

                var area = rect.Size.X * rect.Size.Y;
                if (area > bestArea)
                {
                    bestArea = area;
                    control = target.displayControl;
                    price = target.price;
                }
            }

            return bestArea > 0f;
        }

        private static Rect2 GetUsableGlobalRect(Control control)
        {
            var direct = control.GetGlobalRect();
            if (direct.Size.X > 1f && direct.Size.Y > 1f)
                return direct;

            Rect2? aggregate = null;
            CollectGlobalRects(control, ref aggregate, depth: 0);
            if (aggregate.HasValue && aggregate.Value.Size.X > 1f && aggregate.Value.Size.Y > 1f)
                return aggregate.Value;

            return new Rect2(control.GlobalPosition - new Vector2(120f, 170f), new Vector2(240f, 340f));
        }

        private static void CollectGlobalRects(Node node, ref Rect2? aggregate, int depth)
        {
            if (depth > 8)
                return;

            if (node is Control control && control.Visible)
            {
                var rect = control.GetGlobalRect();
                if (rect.Size.X > 1f && rect.Size.Y > 1f && rect.Size.X < 2000f && rect.Size.Y < 2000f)
                    aggregate = aggregate.HasValue ? aggregate.Value.Merge(rect) : rect;
            }

            foreach (var child in node.GetChildren())
            {
                if (child is Node childNode)
                    CollectGlobalRects(childNode, ref aggregate, depth + 1);
            }
        }


        // ═══════════════════════════════════════════
        //  联机转账弹窗
        // ═══════════════════════════════════════════

        private static void OnTransferClicked()
        {
            if (ShopSellManager.TransferUsedThisShop) return;
            var me = ShopSellManager.GetShopPlayer();
            if (me == null) return;

            var teammates = ShopSellManager.GetTeammates(me);
            if (teammates.Count == 0) return;

            ClosePopup();

            _selectedReceiver = teammates[0];

            _popup = new Control();
            _popup.Position = new Vector2(200, 100);

            var bg = new ColorRect();
            bg.Color = new Color(0, 0, 0, 0.9f);
            bg.Size = new Vector2(300, 180);
            _popup.AddChild(bg);

            var title = new Label
            {
                Text = $"转账给: {_selectedReceiver.Character.Title}  (上限 {ShopSellManager.MaxGoldTransfer} G)",
                Position = new Vector2(10, 8)
            };
            _popup.AddChild(title);

            _amountInput = new NMegaLineEdit
            {
                Position = new Vector2(80, 60),
                Size = new Vector2(140, 32),
                PlaceholderText = "输入金额(1-200)"
            };
            _popup.AddChild(_amountInput);

            var (confirmBtn, _) = MakeButton("确认转账",
                new Vector2(50, 120), new Vector2(100, 28), () => DoTransfer());
            _popup.AddChild(confirmBtn);

            var (cancelBtn, _) = MakeButton("取消",
                new Vector2(170, 120), new Vector2(80, 28), () => ClosePopup());
            _popup.AddChild(cancelBtn);

            _uiLayer?.AddChild(_popup);
        }

        private static async void DoTransfer()
        {
            if (_selectedReceiver == null || _amountInput == null) return;

            if (!int.TryParse(_amountInput.Text, out int amount) || amount <= 0 || amount > ShopSellManager.MaxGoldTransfer)
            {
                GD.Print("[SellToMerchant] Invalid transfer amount.");
                return;
            }

            var sender = ShopSellManager.GetShopPlayer();
            if (sender == null) return;

            bool ok = await ShopSellManager.TransferGoldAsync(sender, _selectedReceiver, amount);
            GD.Print($"[SellToMerchant] Transfer {(ok ? "OK" : "FAILED")}: {amount}G");

            ClosePopup();
            UpdateButtonStates();
        }

        // ═══════════════════════════════════════════
        //  提示弹窗
        // ═══════════════════════════════════════════

        private static void ShowInfoPopup(string message)
        {
            ClosePopup();

            _popup = new Control();
            _popup.Position = new Vector2(300, 200);

            var bg = new ColorRect();
            bg.Color = new Color(0, 0, 0, 0.88f);
            bg.Size = new Vector2(240, 80);
            _popup.AddChild(bg);

            var lbl = new Label
            {
                Text = message,
                Position = new Vector2(10, 14),
                Size = new Vector2(220, 24),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _popup.AddChild(lbl);

            var (okBtn, _) = MakeButton("确定",
                new Vector2(80, 44), new Vector2(80, 26), () => ClosePopup());
            _popup.AddChild(okBtn);

            _uiLayer?.AddChild(_popup);
        }

        private static void ClosePopup()
        {
            _draggedControl = null;
            _popup?.QueueFree();
            _popup = null;
            _selectedRelic = null;
            _selectedPotion = null;
            _activeRows.Clear();
        }

        private static void MakePopupDraggable(Control popup, params Control[] handles)
        {
            foreach (var handle in handles)
            {
                handle.MouseFilter = Control.MouseFilterEnum.Stop;
                handle.GuiInput += (InputEvent e) =>
                {
                    if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
                    {
                        if (mb.Pressed)
                        {
                            _draggedControl = popup;
                            handle.AcceptEvent();
                        }
                        else if (_draggedControl == popup)
                        {
                            _draggedControl = null;
                            handle.AcceptEvent();
                        }
                    }
                    else if (e is InputEventMouseMotion motion && _draggedControl == popup)
                    {
                        if (!Input.IsMouseButtonPressed(MouseButton.Left))
                        {
                            _draggedControl = null;
                            return;
                        }

                        popup.Position += motion.Relative;
                        handle.AcceptEvent();
                    }
                };
            }
        }

        private static void AttachManualWheelScroll(ScrollContainer scroll, float maxScroll, params Control[] controls)
        {
            int max = Math.Max(0, (int)Math.Ceiling(maxScroll));
            if (max <= 0)
                return;

            foreach (var control in controls)
            {
                control.MouseFilter = Control.MouseFilterEnum.Stop;
                control.GuiInput += (InputEvent e) =>
                {
                    if (e is not InputEventMouseButton mb || !mb.Pressed)
                        return;

                    int direction = mb.ButtonIndex switch
                    {
                        MouseButton.WheelDown => 1,
                        MouseButton.WheelUp => -1,
                        _ => 0
                    };
                    if (direction == 0)
                        return;

                    int step = Math.Max(24, (int)(scroll.Size.Y * 0.22f));
                    scroll.ScrollVertical = Math.Clamp(scroll.ScrollVertical + direction * step, 0, max);
                    control.AcceptEvent();
                };
            }
        }

        // ═══════════════════════════════════════════
        //  按钮工厂
        // ═══════════════════════════════════════════

        private static (Control, Label) MakeButton(string text, Vector2 pos, Vector2 size, Action onClick)
        {
            var panel = new Panel { Position = pos, Size = size };

            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.18f, 0.18f, 0.18f, 0.85f);
            style.BorderColor = new Color(0.7f, 0.55f, 0.15f, 1f);
            style.BorderWidthLeft = style.BorderWidthRight = 1;
            style.BorderWidthTop = style.BorderWidthBottom = 1;
            style.CornerRadiusTopLeft = style.CornerRadiusTopRight = 4;
            style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 4;
            panel.AddThemeStyleboxOverride("panel", style);

            var lbl = new Label
            {
                Text = text,
                Size = size,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            panel.AddChild(lbl);

            panel.GuiInput += (InputEvent e) =>
            {
                if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
                    onClick();
            };

            return (panel, lbl);
        }

        // ═══════════════════════════════════════════
        //  按钮状态
        // ═══════════════════════════════════════════

        private static void UpdateButtonStates()
        {
            SetBtn(_sellCardBtn, _sellCardLbl, ShopSellManager.CardSoldThisShop, "卡牌(已售)", "出售卡牌");
            SetBtn(_sellRelicBtn, _sellRelicLbl, ShopSellManager.RelicSoldThisShop, "遗物(已售)", "出售遗物");
            SetBtn(_sellPotionBtn, _sellPotionLbl, ShopSellManager.PotionSoldThisShop, "药水(已售)", "出售药水");
            SetBtn(_transferBtn, _transferLbl, ShopSellManager.TransferUsedThisShop, "转账(已转)", "转账金币");
        }

        private static void SetBtn(Control? btn, Label? lbl, bool done, string t1, string t2)
        {
            if (btn == null) return;
            if (done) { btn.Visible = false; if (lbl != null) lbl.Text = t1; }
            else { btn.Visible = true; if (lbl != null) lbl.Text = t2; }
        }
    }
}
