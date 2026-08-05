# Third-Person Contact and Penetration Closure

Date: 2026-08-05

## Outcome

The recovered third-person rifle, shotgun and pistol poses now use bounded
two-joint hand contact solving after the authored holding pose. Long guns no
longer pass through the torso, and both hands visibly meet the weapon instead
of floating beside it. Knife and grenade remain intentional one-handed props.

## Runtime changes

- Rifle and Shotgun01 use resource-specific length, axis and socket offsets;
  the Shotgun01 forward axis was corrected instead of reusing the rifle
  rotation.
- The weapon socket is rebuilt from the posed right-hand frame before contact
  solving, so locomotion can continue to drive the character while the prop
  stays aligned with the upper body.
- Rifle, shotgun and pistol use explicit primary/support grip targets. A
  bounded CCD pass rotates upper arm and forearm joints toward those targets.
- Combat-action base rotations are captured only after holding/contact posing.
  Fire, equip and reload therefore animate from the valid weapon pose and
  restore to it when the action finishes.
- Death and respawn still restore the visual root and both upper arms exactly.

## Contact and penetration gate

The runtime audit instantiates the real `RemotePlayer` prefab and recovered
weapon resources, executes the driver, and rejects:

- primary-hand clearance above 4.0 cm;
- required support-hand clearance above 4.5 cm;
- rifle/shotgun torso clearance below 7.0 cm;
- inactive/missing runtime props.

Final measurements:

| Weapon | Primary hand | Support hand | Torso clearance |
|---|---:|---:|---:|
| Rifle | 0.0 cm | 1.91 cm | 22.68 cm |
| Shotgun | 1.25 cm | 0.0 cm | 21.55 cm |
| Pistol | 0.0 cm | 1.77 cm | 19.91 cm |
| Knife | 0.0 cm | one-handed | 30.72 cm |
| Grenade | 0.0 cm | one-handed | 24.90 cm |

The audit also rendered front and side previews for all five weapon classes.
Visual review confirms the long-gun stocks remain outside the torso and the
hand/forearm chains meet the weapon surfaces without the earlier floating or
body-crossing pose.

## Regression validation

- action changed during playback: passed
- action completion reset error (left/right): `0.0000° / 0.0000°`
- respawn reset error (position/rotation/left/right): all `0.0000`
- Unity EditMode: 39/39 passed
- Unity PlayMode: 4/4 passed

Evidence:

- `recovery/third-person-contact-penetration-render.log`
- `recovery/third-person-prop-preview/*-front.png`
- `recovery/third-person-prop-preview/*-side.png`
- `recovery/third-person-contact-penetration-editmode.xml`
- `recovery/third-person-contact-penetration-editmode.log`
- `recovery/third-person-contact-penetration-playmode.xml`
- `recovery/third-person-contact-penetration-playmode.log`

