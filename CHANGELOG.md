# Changelog

## v1.0.3

- Fixed wrong card sell prices when many sellable cards were present in the merchant card selector.
- Fixed `TryResolveModel` type matching so derived or wrapped card models resolve to the correct rarity and sell price.
- Adjusted price hover and drag behavior to reduce UI interference while selecting cards.
- Removed more obsolete code paths left from the old tooltip implementation.
- Synced repository documentation for the new release.

## v1.0.2

- Fixed card sell price display to use a compact hover panel instead of a cloned keyword tooltip.
- Removed the obsolete right-side sell-price hover path to reduce maintenance risk and tooltip conflicts.
- Optimized hover hit testing by preferring direct control bounds before recursive fallback aggregation.
- Kept relic and potion sell popups draggable with scroll support for long lists.
- Updated mod metadata and auto-update configuration for `dz-knight/SellToMerchant`.
