# Recovered Lobby Settings

Date: 2026-08-05

## Outcome

The archived Zhu lobby settings entry is functional without replacing its
original artwork. The runtime controller attaches to the recovered hierarchy:

`Button 11 → RawImage 2 → Assets/Texture2D/设置 1.png`

The original footer buttons now retain their recovered presentation while
providing real Restore Defaults, Save and Cancel behavior.

## Settings restored

The functional overlay exposes six settings supported by current runtime
logic:

- master volume: 0–100%;
- music volume: 0–100%;
- effects volume: 0–100%;
- mouse sensitivity: 25–250;
- Unity quality tier: every tier defined by the project;
- target frame rate: 30, 45 or 60 FPS.

All settings are sanitized before use, stored under dedicated
`Genesis.Settings.*` PlayerPrefs keys and applied again after every scene load.
Preview changes are live; Cancel restores the last saved snapshot, Save commits
the draft, and Restore Defaults previews the known-safe defaults before save.

## Runtime consumers

- `AudioListener.volume` receives the master level.
- Recovered looping/play-on-awake sources use the music multiplier; other
  recovered sources use the effects multiplier without repeatedly multiplying
  their authored base volume.
- Legacy `MouseLook` instances receive the saved sensitivity.
- The WebGL browser mouse-delta path uses the same sensitivity ratio instead of
  its former fixed constant.
- `QualitySettings` and `Application.targetFrameRate` receive validated values.

## Original-scene audit

The audit opens the actual `Zhu` scene and verifies:

- Button 11 targets the original hidden settings panel;
- the panel uses `Assets/Texture2D/设置 1.png`;
- all three recovered footer buttons remain present;
- 12 functional minus/plus controls and 25 text elements are generated;
- the functional overlay is fully contained by the recovered panel;
- the default snapshot is valid without correction.

The rendered 1280×720 evidence image confirms the overlay remains inside the
original frame and does not cover Restore Defaults, Save or Cancel.

## Validation

- Unity EditMode: 62/62 passed;
- Unity PlayMode: 5/5 passed;
- PlayMode loaded `Zhu`, clicked the actual recovered Button 11 and observed the
  functional panel at runtime;
- lobby button audit: 187 buttons, 0 invalid persistent events;
- `git diff --check`: passed.

Evidence:

- `recovery/lobby-settings-preview.png`
- `recovery/lobby-settings-runtime-audit.log`
- `recovery/lobby-settings-editmode.xml`
- `recovery/lobby-settings-editmode.log`
- `recovery/lobby-settings-playmode.xml`
- `recovery/lobby-settings-playmode.log`
- `recovery/lobby-button-audit.csv`
- `recovery/lobby-peripheral-discovery.log`

## Subsequent store closure

The broader checklist item was subsequently closed with an evidence-backed
read-only store catalog. It connects the original `商城` button and recovered
store artwork to eight verified resource entries while intentionally excluding
prices, balances and purchase transactions. See
`recovery/LOBBY_READ_ONLY_STORE_CATALOG_2026-08-05.md`.
