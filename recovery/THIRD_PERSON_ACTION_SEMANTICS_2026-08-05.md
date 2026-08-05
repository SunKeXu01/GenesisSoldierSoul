# Third-Person Shared Action Semantics

Date: 2026-08-05

## Outcome

The local first-person/network mirror, remote player replicas and offline
training bots now construct the same canonical combat-action command before a
presentation action is played or transmitted. Weapon aliases, presentation
families, action states and durations are no longer independently hard-coded
in the three paths.

## Canonical command

`GenesisCombatActionSemantics` is the shared client authority. Each command
contains:

- canonical wire weapon;
- third-person presentation weapon family;
- canonical wire action;
- semantic kind and local action state;
- recovered third-person presentation duration.

Weapon mapping:

| Input weapon | Canonical wire weapon | Presentation family |
|---|---|---|
| `rifle` | `rifle` | rifle |
| `m4a1`, `m16`, `ak74m`, `awp` | unchanged | rifle |
| `shotgun`, `shotgun01` | `shotgun01` | shotgun |
| `pistol` | `pistol` | pistol |
| `knife` | `knife` | knife |
| `grenade` | `grenade` | grenade |

Action mapping:

| Wire action | Local state | Presentation behavior |
|---|---|---|
| `equip` | Deploying | equip |
| `fire` | Firing | firearm fire |
| `fire` on knife | Melee | knife strike |
| `reload` | Reloading | firearm reload |
| `throw` on grenade | Throwing | grenade throw |

Invalid combinations such as knife reload, rifle throw and mismatched
Firing/Melee states are rejected without mutating presentation state.

## Integration

- Local outgoing `shoot` and `action` messages pass through the canonical
  command before serialization.
- Remote action events are validated and canonicalized before consuming their
  sequence number or reaching `GenesisThirdPersonActionDriver`.
- Training Bot attacks create the same command and call the same driver
  overload as remote replicas.
- The string-based driver entry remains as a compatibility adapter but now
  delegates to the canonical command.

## Fixed divergences

The server previously broadcast every non-pistol/non-knife shot as `rifle`.
Consequently, a remote player holding Shotgun01 switched to the rifle prop when
firing. Shotgun fire broadcasts now preserve `shotgun01`, which the shared
client semantics maps to the shotgun presentation family.

Grenade `equip` also no longer falls through to the grenade `throw` pose. The
two commands remain distinct through semantic kind, state and runtime driver
behavior.

## Validation

Runtime prefab audit:

- local/remote/Bot semantic parity: passed;
- Shotgun01 fire resolved to `(shotgun, fire)`;
- grenade equip resolved to `equip`;
- grenade throw resolved to `throw`;
- action completion reset error: `0.0000° / 0.0000°`;
- respawn position/rotation/arm reset errors: all `0.0000`;
- all five weapon props and contact gates: passed.

Automated regression:

- Unity EditMode: 56/56 passed;
- Unity PlayMode: 4/4 passed;
- server Vitest: 29/29 passed;
- server TypeScript build: passed.

Evidence:

- `recovery/action-semantics-runtime-audit.log`
- `recovery/action-semantics-editmode.xml`
- `recovery/action-semantics-editmode.log`
- `recovery/action-semantics-playmode.xml`
- `recovery/action-semantics-playmode.log`

