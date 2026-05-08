# Changelog

## v1.0.3

- Fixed card sell price tooltip showing wrong price when many cards are in the same grid — display control key collision caused only the last-scanned card's price to be stored.
- Fixed `TryResolveModel<T>` to also match interface-typed properties (e.g. `ICardModel`), improving card model resolution on individual card controls.
- The sell side panel is now draggable by its background, border, or title.
- Removed unused custom card sell popup (`ShowCardSellPopup`, `BuildCardRow`), disabled hover-tip clone code, and dead helper methods.
- Removed unused `NetworkSync` class.
- Unified `MakePanelDraggable` / `MakePopupDraggable` into a single drag helper.

## v1.0.2

- Fixed card sell price display to use a compact hover panel instead of a cloned keyword tooltip.
- Removed the obsolete right-side sell-price hover path to reduce maintenance risk and tooltip conflicts.
- Optimized hover hit testing by preferring direct control bounds before recursive fallback aggregation.
- Kept relic and potion sell popups draggable with scroll support for long lists.
- Updated mod metadata and auto-update configuration for `dz-knight/SellToMerchant`.
