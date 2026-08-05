# AK-74M and AWP First-Person Closure

Date: 2026-08-05

## Outcome

AK-74M and AWP now have technically complete, isolated first-person candidate
closures. This does not promote either weapon into the formal loadout: both
remain rights grade D and `ExperimentalRecoveredWeaponsEnabled` remains false.

## Runtime correction

- `GenesisWeaponLoadout.FirstPersonResourcePath` maps AK-74M and AWP directly
  to their audited dedicated prefabs.
- The old runtime model-overlay path was removed. It previously ignored the
  candidate prefab, instantiated a second model and disabled mesh renderers,
  so prefab audit success did not prove the runtime used the audited closure.
- AK-74M and AWP now use independent `GenesisViewmodelKind` framing profiles.
- AWP's imported long axis was corrected from a sideways screen-spanning pose
  to the authored forward viewmodel axis.

## Closure audit

| Candidate | Renderers | Materials | Textured coverage | Clips | Required anchors |
|---|---:|---:|---:|---:|---|
| AK74MViewmodelCandidate | 13 | 13 | 100% | 7 | complete |
| AWPViewmodelCandidate | 9 | 9 | 100% | 7 | complete |

Both include `Fire`, `Idle01`, `Idle02`, `Idle03`, `Idle04`, `Reload` and
`ReloadEmpty`, the archived two-hand animated skeleton, `WeaponMainLocator`,
`Muzzle` and `RightHand`.

The full validation result is intentionally reported as 9/10 because the
separate M16 candidate still fails its own candidate criterion. All six formal
runtime-required weapons passed; the AK-74M and AWP rows both passed.

## Viewport evidence

The runtime framing gate rendered 1280x720, 1280x800 and 1200x900 for both
weapons. In every view, the firearm remained within the horizontal and upper
viewport limits and the muzzle anchor projected inside the visible viewport.

- `recovery/recovered-ak74m-runtime-1280x720.png`
- `recovery/recovered-ak74m-runtime-1280x800.png`
- `recovery/recovered-ak74m-runtime-1200x900.png`
- `recovery/recovered-awp-runtime-1280x720.png`
- `recovery/recovered-awp-runtime-1280x800.png`
- `recovery/recovered-awp-runtime-1200x900.png`
- `recovery/ak74m-runtime-framing.log`
- `recovery/awp-runtime-framing.log`

## Automated verification

- Unity: `2022.3.62f3c1`
- EditMode: 29 total, 29 passed, 0 failed, 0 skipped
- Dedicated resource-path tests prove both IDs resolve to their audited prefab
  rather than the generic assault-rifle fallback.
- Per-weapon framing tests prove AK-74M and AWP no longer share the generic
  rifle pose.

Evidence:

- `recovery/ak-awp-dedicated-editmode.xml`
- `recovery/ak-awp-dedicated-editmode.log`
- `recovery/ak-awp-dedicated-viewmodel-audit.log`
- `recovery/recovered-weapon-validation.json`
