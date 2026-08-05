# Aim Alignment Gate

Date: 2026-08-05

## Result

The centred crosshair, local training hit test, network shot request and visual
muzzle presentation now derive from the same normalised camera-authored shot
direction.

- `GenesisAimAlignment.ShotRay` owns the gameplay ray origin and direction.
- Training hit detection consumes that exact ray.
- The server request consumes the exact same `shotDirection` value.
- Rifle and pistol muzzle effects converge from their physical muzzle position
  onto the gameplay ray at the canonical weapon range, removing model-axis and
  close-viewmodel parallax from the visual presentation.
- Invalid zero-length directions fail safely to world forward without NaN.

The firearm mesh itself remains presentation-only: moving a viewmodel cannot
move the authoritative hit ray away from the screen-centre crosshair.

## Automated verification

Unity `2022.3.62f3c1` EditMode test result:

- total: 26
- passed: 26
- failed: 0
- skipped: 0

Evidence:

- `recovery/aim-alignment-editmode.xml`
- `recovery/aim-alignment-editmode.log`

The three dedicated tests cover exact direction normalisation, muzzle-to-ray
convergence with viewmodel parallax, and the zero-direction safety fallback.
