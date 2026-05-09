# Changelog

## v1.0.5

- Added an in-game auto-update prompt with three choices: automatic GitHub download, open GitHub manually, or skip.
- Delayed the update check until the startup UI is ready and switched package download to the GitHub asset API for better reliability.
- Reworked the update popup into a fixed, centered, draggable overlay with explicit progress and completion states.
- Restored multiplayer card selling through the game's native synchronized card-removal flow.
- Re-enabled multiplayer relic selling, potion selling, and teammate gold transfer through dedicated synchronized network messages.
- Improved teammate name resolution in the transfer popup and refreshed branch-aware update handling for `stable` and `public-beta`.
- Fixed a false-positive update prompt when the local mod was already on `v1.0.5`.
- Moved updater state/result markers away from `.json` filenames to avoid being scanned as mod manifests.

## v1.0.4

- Fixed public-beta v0.105.0 compatibility for native card sell price binding.
- Normalized resolved card models against the actual sellable deck list to avoid wrong 25/37/75 gold prices.
- Restricted native card price binding to the real deck selection holder.
- Added unsellable protection for newly added Neow relics.
- Rebuilt and revalidated the package against public-beta v0.105.0.

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
