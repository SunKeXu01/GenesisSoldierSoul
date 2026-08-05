# Third-Person Eight-Direction Locomotion and Landing

Date: 2026-08-05

## Outcome

The recovered third-person character now has verified idle, continuous
eight-direction walking/running, jump and a distinct landing phase.

## Direction semantics

`GenesisDirectionalLocomotion.LocalBlend` is the canonical world-to-character
mapping used by both remote network players and training bots. It:

- removes vertical motion before direction calculation;
- transforms movement into character-local space;
- preserves cardinal and diagonal octants;
- normalises diagonals so they do not receive faster gait input;
- clamps speed blend to `[0, 1]` and returns a stable zero for idle.

The recovered freeform Cartesian 2D tree contains idle plus walk/run clips for
forward, backward, left and right. Diagonal directions continuously blend the
two adjacent authored directions. Runtime sampling proved all eight octants:

| Direction | Runtime contributing motions |
|---|---|
| back-left | backward walk/run + left walk/run |
| back | backward run |
| back-right | backward walk/run + right walk/run |
| left | left run |
| right | right run |
| forward-left | forward walk/run + left walk/run |
| forward | forward run |
| forward-right | forward walk/run + right walk/run |

Exact sampled weights are retained in
`recovery/recovered-character-animation-validation.json`.

## Jump and landing

The generated controller no longer transitions directly from `Jump` to
`Locomotion` when `Grounded` becomes true. It now uses:

`Locomotion → Jump → Land → Locomotion`

`Land` samples the recovered non-looping `Jump` clip from its authored final
phase (`cycleOffset=0.68`) at `1.35x`, then exits to locomotion after the short
contact/recovery segment. Runtime Animator sampling independently observed the
`Jump` and `Land` states.

## Validation

Character audit:

- recovered locomotion clips: 9/9
- directional 2D blend: passed
- runtime direction samples: 8/8
- Jump state and transition: passed
- Land state and transition: passed
- runtime Jump sample: passed
- runtime Land sample: passed
- required combat bones: 3/3
- weapon props: 5/5

EditMode tests:

- total: 39
- passed: 39
- failed: 0
- skipped: 0

The ten new direction tests cover all octants, rotated character space, idle
and overspeed clamping.

Evidence:

- `recovery/recovered-character-animation-validation.json`
- `recovery/third-person-eight-direction-land-generate.log`
- `recovery/third-person-eight-direction-land-runtime-audit.log`
- `recovery/third-person-eight-direction-land-editmode.xml`
- `recovery/third-person-eight-direction-land-editmode.log`
