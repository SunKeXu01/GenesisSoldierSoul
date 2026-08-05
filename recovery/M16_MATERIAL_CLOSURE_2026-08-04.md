# M16 Material Closure

Date: 2026-08-04

## Source finding

- The source FBX is preserved at `_解压资源/创世兵魂武器/武器/M16突击步枪/m16突击步枪.fbx` and copied byte-for-byte to `client-restored/Assets/RecoveredWeapons/M16/M16.fbx`.
- Both files have SHA-256 `3a089b6a2d0153cfa60c8caeb25bb183e320df56ca79e616c79d3259b48e8291`.
- The FBX contains complete geometry and material slots but no usable embedded or adjacent texture atlas.
- `_解压资源/创世兵魂素材/创世兵魂/m16.50280.png` is a 210×94 side-view UI/reference image. It is not a UV texture and was not misrepresented as one.

## Recovery closure

`GenesisRestoredBuild.CreateCombatResourcePrefabs` now deterministically creates three explicitly generated Texture2D assets and three Standard materials under:

`Assets/Resources/OriginalGame/Weapons/M16/GeneratedClosure/`

The gunmetal, polymer, and steel substitutes use stable procedural surface variation. Renderer-name rules select semantic parts when names survive; generic FBX mesh names use a stable sparse accent rule. The generated materials are applied to the source runtime prefab before the animated first-person composite is saved.

These are recovery substitutes, not claimed original textures. The source FBX and the reference image remain unchanged.

## Validation

- Renderers: 127
- Material slots: 239
- Missing materials: 0
- Distinct textures across the composite: 7 (3 generated M16 textures plus 4 archived arm textures)
- Textured material coverage: 239/239, 100%
- Animation clips: 7
- Required anchors missing: 0
- Automated candidate result: passed

The candidate remains outside the formal runtime list because the resource-closure policy has no approved rights record. Technical closure does not override the D-grade formal publication gate.
