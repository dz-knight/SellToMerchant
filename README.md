# SellToMerchant

Sell cards, relics, and potions at the merchant in Slay the Spire 2.

## Features

- Sell removable cards for gold at the merchant.
- Sell eligible relics and potions for gold.
- Show card sell price only while hovering in the native deck-removal selector.
- Support draggable relic and potion sell windows with wheel scrolling.
- Support branch-aware auto update for `stable` and `public-beta`.

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

Validated against Slay the Spire 2 `public-beta v0.105.0`.
Future game updates may require another rebuild if UI or model structures change.
