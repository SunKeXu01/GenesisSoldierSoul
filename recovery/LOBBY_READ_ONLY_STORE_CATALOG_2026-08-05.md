# Recovered Read-Only Store Catalog

Date: 2026-08-05

## Outcome

The original Zhu lobby `商城` navigation entry is functional again. Its
recovered `Button 7`, which previously played only a click sound, now opens an
evidence-backed resource catalog. The same catalog is also reachable from the
functional warehouse as a convenience link.

The catalog is deliberately read-only. No authoritative price table, account
balance, ownership purchase state or transaction protocol survives in the
current closure, so none has been invented.

## Recovered visual evidence

The Resources prefab `GenesisStoreBackdrop` retains direct dependencies on two
bitmaps extracted from the archived `storeUI.110482.swf`:

- `bitmap-00004.png`: recovered steel store panel;
- `bitmap-00293.png`: recovered weapon-wall artwork.

The generated runtime layout is rendered inside the original lobby frame and
explicitly labels its evidence boundary:

`NO RECOVERED PRICE / BALANCE / PURCHASE DATA`

## Catalog authority

`GenesisStoreCatalog` is a pure read-only model containing eight unique entries
whose Resources prefabs all resolve at runtime:

| Entry | Status | Runtime meaning |
|---|---|---|
| M4A1 | equipped/available | verified primary loadout |
| M16 | available | verified primary loadout |
| Shotgun01 | available | verified primary loadout |
| Pistol01 | standard issue | existing secondary slot |
| Knife01 | standard issue | existing melee slot |
| Grenade01 | standard issue | existing utility slot |
| AK-74M | recovery candidate/locked | prefab closed, formal slot disabled |
| AWP | recovery candidate/locked | prefab closed, formal slot disabled |

Availability is recomputed from the current primary selection and the existing
experimental-weapon gate. Candidate items cannot be equipped through the
catalog.

## Non-transactional invariant

The runtime catalog contains eight non-interactive resource cards and exactly
one Button: `CloseRecoveredCatalog`. There are no Buy, Recharge, Price,
Currency or Confirm Purchase controls, and the catalog assembly exposes no
transaction API.

This preserves the archived store's browse/navigation role without presenting
fictional economy behavior as recovered functionality.

## Validation

- catalog entries: 8/8 unique;
- Resources prefab resolution: 8/8;
- recovery candidates: 2;
- standard-issue entries: 3;
- recovered store textures: 2/2 direct prefab dependencies;
- runtime cards: 8;
- runtime catalog buttons: 1 (`BACK`) only;
- Unity EditMode: 65/65 passed;
- Unity PlayMode: 6/6 passed;
- PlayMode clicked the original Zhu `商城` Button 7 and observed the catalog;
- `git diff --check`: passed.

Evidence:

- `recovery/store-catalog-preview.png`
- `recovery/store-catalog-build.log`
- `recovery/store-catalog-resource-audit.log`
- `recovery/store-catalog-runtime-audit.log`
- `recovery/store-catalog-editmode.xml`
- `recovery/store-catalog-editmode.log`
- `recovery/store-catalog-playmode.xml`
- `recovery/store-catalog-playmode.log`

## Other archival lobby panels

VIP, tasks, activities, team rewards and exchange panels retain recoverable
artwork, but their authoritative entitlement, reward or account services are
absent. Under the checklist's “完整资源与逻辑依据” condition, they remain archival
presentation rather than receiving fabricated business behavior.

