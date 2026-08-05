# Next Weapon Candidate Audit

Date: 2026-08-04

Formal grade for every item below is D until an approved rights record exists. The technical status is recorded separately so a usable mesh is not confused with a publishable weapon.

| Candidate | Source closure | Standard conversion | First-person closure | Decision |
|---|---|---|---|---|
| AN94 | `1.3ds` + `an94.bmp` | Verified GLB + PNG | No hands, skeleton, anchors, or actions | Candidate only; best next rifle after rights review |
| M249 | `m249_saw.3ds` + `m249.jpg` + Maya source | Verified GLB + PNG | No hands, skeleton, anchors, belt/feed actions, or reload sequence | Candidate only; requires weapon-specific animation work |
| FAMAS | `FAMAS.max` only | Not converted; native 3ds Max source is unsupported by the safe toolchain | None | Format-blocked reference |
| Gatling | `加特林机枪2.max` only | Not converted; native 3ds Max source is unsupported by the safe toolchain | None | Format- and animation-blocked reference |
| Hand axe | `cf.3ds` + `401e6fb1.bmp` + reference screenshot | Verified GLB + PNG | No hand rig, strike clips, hit locator, or equip pose | Candidate only; viable after a dedicated melee rig |
| Kukri | two `.max` files, one explicitly uncoloured | Not converted; native 3ds Max source is unsupported by the safe toolchain | None | Format-, material-, and animation-blocked reference |

The verified conversions are recorded with input/output hashes and Blender 5.2.0 LTS parameters in `recovered-media-conversions.json`. Native `.max` files remain unchanged; the project does not execute bundled converters or invent a successful conversion.

## AK-74M and AWP progress

Both root models now have isolated first-person candidate prefabs built on the archived AssaultRifle01 two-hand skeleton. Each carries the original seven fire/idle/reload clips, `WeaponMainLocator`, `Muzzle`, and `RightHand` anchors, with 100% non-null textured material coverage. Preview images are:

- `recovered-ak74m-viewmodel-candidate-preview.png`
- `recovered-awp-viewmodel-candidate-preview.png`

The technical first-person closure was completed on 2026-08-05. Runtime selection now resolves directly to each audited dedicated prefab instead of rebuilding and overlaying another model on `AssaultRifle01`; the AWP long axis and per-weapon framing profiles were corrected. Both candidates pass three-aspect runtime framing, anchor/material/clip audit and the 29-test EditMode suite. They remain disabled by `GenesisWeaponLoadout.ExperimentalRecoveredWeaponsEnabled = false` because their shared fallback material is provisional and their rights grade remains D; technical completion does not bypass the formal-resource policy.
