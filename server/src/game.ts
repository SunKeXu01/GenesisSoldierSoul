import { randomUUID } from "node:crypto";
import type {
  InputMessage,
  GrenadeMessage,
  PlayerSnapshot,
  ServerMessage,
  ShootMessage,
  Vector3,
  WeaponActionMessage,
} from "./protocol.js";

export const TICK_RATE = 20;
const MAX_PLAYERS = 12;
const MOVE_SPEED = 4;
const JUMP_SPEED = 7;
const GRAVITY = 20;
const PLAYER_RADIUS = 0.55;
const EYE_HEIGHT = 1.55;
const PISTOL_DAMAGE = 34;
const PISTOL_RANGE = 90;
const PISTOL_INTERVAL_MS = 250;
const RIFLE_DAMAGE = 30;
const RIFLE_RANGE = 100;
const RIFLE_INTERVAL_MS = 100;
const M16_DAMAGE = 27;
const M16_RANGE = 110;
const M16_INTERVAL_MS = 85;
const AK74M_DAMAGE = 33;
const AK74M_RANGE = 105;
const AK74M_INTERVAL_MS = 95;
const SHOTGUN_PELLET_COUNT = 8;
const SHOTGUN_PELLET_DAMAGE = 10;
const SHOTGUN_INNER_SPREAD_TANGENT = 0.015;
const SHOTGUN_OUTER_SPREAD_TANGENT = 0.04;
const SHOTGUN_RANGE = 55;
const SHOTGUN_INTERVAL_MS = 850;
const AWP_DAMAGE = 85;
const AWP_RANGE = 160;
const AWP_INTERVAL_MS = 1250;
const KNIFE_DAMAGE = 50;
const KNIFE_RANGE = 2.2;
const KNIFE_INTERVAL_MS = 500;
const GRENADE_FUSE_MS = 3000;
const GRENADE_FORCE = 10;
const GRENADE_UPWARD_FORCE = 2.5;
const GRENADE_RADIUS = 6;
const GRENADE_MAX_DAMAGE = 100;
const GRENADE_MIN_DAMAGE = 25;
const RESPAWN_DELAY_MS = 3000;
const SPAWN_PROTECTION_MS = 3000;
const ROUND_DURATION_MS = 3 * 60 * 1000;
const SCORE_LIMIT = 10;
const CLIENT_POSITION_GRACE = 0.08;
const CLIENT_POSITION_SPEED_TOLERANCE = 1.75;
const MAX_AIM_DEVIATION_DEGREES = 15;

type FirearmWeapon = "m4a1" | "m16" | "ak74m" | "shotgun01" | "awp" | "pistol";

type WeaponRule = {
  magazine: number;
  reserve: number;
  damage: number;
  range: number;
  intervalMs: number;
  reloadMs: number;
};

const WEAPON_RULES: Record<FirearmWeapon, WeaponRule> = {
  m4a1: { magazine: 30, reserve: 90, damage: 30, range: 100, intervalMs: 100, reloadMs: 2100 },
  m16: { magazine: 30, reserve: 90, damage: 27, range: 110, intervalMs: 85, reloadMs: 2100 },
  ak74m: { magazine: 30, reserve: 90, damage: 33, range: 105, intervalMs: 95, reloadMs: 2100 },
  shotgun01: { magazine: 8, reserve: 32, damage: 10, range: 55, intervalMs: 850, reloadMs: 2800 },
  awp: { magazine: 10, reserve: 30, damage: 85, range: 160, intervalMs: 1250, reloadMs: 2650 },
  pistol: { magazine: 12, reserve: 48, damage: 34, range: 90, intervalMs: 250, reloadMs: 1450 },
};

type Player = PlayerSnapshot & {
  lastActionSequence: number;
  moveX: number;
  moveZ: number;
  jumpRequested: boolean;
  verticalVelocity: number;
  groundY: number;
  lastShotAt: number;
  magazines: Record<FirearmWeapon, number>;
  reserves: Record<FirearmWeapon, number>;
  currentWeapon: FirearmWeapon | "knife" | "grenade";
  reloadingWeapon?: FirearmWeapon;
  reloadCompletesAt: number;
  grenades: number;
  respawnAt: number;
  protectedUntil: number;
  usesClientPosition: boolean;
  lastClientPositionAt: number;
};

type PendingGrenade = {
  id: string;
  throwerId: string;
  origin: Vector3;
  velocity: Vector3;
  groundY: number;
  thrownAt: number;
  explodesAt: number;
};

type MapConfig = {
  spawnPoints: Vector3[];
  minX: number;
  maxX: number;
  minZ: number;
  maxZ: number;
};

const MAP_CONFIGS: Record<string, MapConfig> = {
  scenegd: mapAround({ x: 990.28, y: 296.87, z: 1599.81 }, 120, 12),
  scenejd: mapAround({ x: 817.6, y: 578.7, z: 499.74 }, 120, 12),
  scenep: mapAround({ x: 206.66, y: 438.227, z: 110.617 }, 120, 12),
  // WebGL clients transmit positions relative to the recovered scene rig's
  // spawn origin and add that origin again when applying snapshots. Keep lobby
  // map coordinates local, like every other restored scene, so Pyramid is not
  // offset twice and remote players remain in the same world space.
  pyramid: mapAround({ x: 0, y: 0, z: 0 }, 40, 2),
  newconstructionsite: mapAround({ x: 0, y: 0, z: 0 }, 16, 2),
  classicconstructionsite: mapAround({ x: 0, y: 0, z: 0 }, 40, 2),
  steelfactory: mapAround({ x: 0, y: 0, z: 0 }, 40, 2),
  biochemicaltown: mapAround({ x: 0, y: 0, z: 0 }, 40, 2),
  radiationdistrict: mapAround({ x: 0, y: 0, z: 0 }, 40, 2),
  icefiremaze: mapAround({ x: 0, y: 0, z: 0 }, 40, 2),
  scene3: mapAround({ x: 0.464, y: 1.32, z: -62.5 }, 80, 10),
  ghost: mapAround({ x: 9.769, y: 2.18, z: 48.351 }, 70, 8),
};

const DEFAULT_MAP = mapAround({ x: 0, y: 0, z: 0 }, 60, 8);

export class GameRoom {
  readonly id: string;
  private tickNumber = 0;
  private spawnCursor = 0;
  private readonly players = new Map<string, Player>();
  private readonly pendingGrenades: PendingGrenade[] = [];
  private readonly map: MapConfig;
  private readonly roundEndsAt: number;
  private roundState: "playing" | "ended" = "playing";

  constructor(
    id: string,
    now = Date.now(),
    roundDurationMs = ROUND_DURATION_MS,
    mapId = id,
  ) {
    this.id = id;
    this.map = MAP_CONFIGS[mapId.toLowerCase()] ?? DEFAULT_MAP;
    this.roundEndsAt = now + roundDurationMs;
  }

  get size(): number {
    return this.players.size;
  }

  get full(): boolean {
    return this.size >= MAX_PLAYERS;
  }

  addPlayer(name: string, now = 0): PlayerSnapshot {
    if (this.full) throw new Error("room_full");
    const id = randomUUID();
    const player: Player = {
      id,
      name,
      position: this.nextSpawn(),
      yaw: 0,
      pitch: 0,
      health: 100,
      kills: 0,
      deaths: 0,
      alive: true,
      lastInputSequence: 0,
      lastActionSequence: 0,
      moveX: 0,
      moveZ: 0,
      jumpRequested: false,
      verticalVelocity: 0,
      groundY: 0,
      lastShotAt: 0,
      magazines: firearmValues((rule) => rule.magazine),
      reserves: firearmValues((rule) => rule.reserve),
      currentWeapon: "m4a1",
      reloadCompletesAt: 0,
      grenades: 1,
      respawnAt: 0,
      protectedUntil: now + SPAWN_PROTECTION_MS,
      usesClientPosition: false,
      lastClientPositionAt: 0,
    };
    player.groundY = player.position.y;
    this.players.set(id, player);
    return this.snapshot(player);
  }

  removePlayer(id: string): void {
    this.players.delete(id);
  }

  applyInput(id: string, input: InputMessage, now = Date.now()): void {
    const player = this.players.get(id);
    if (!player || !player.alive) return;
    if (input.sequence <= player.lastInputSequence) return;
    player.lastInputSequence = input.sequence;
    player.moveX = input.moveX;
    player.moveZ = input.moveZ;
    player.jumpRequested = input.jump;
    player.yaw = normalizeDegrees(input.yaw);
    player.pitch = input.pitch;
    if (input.position) {
      const elapsedMs =
        player.lastClientPositionAt > 0
          ? clamp(now - player.lastClientPositionAt, 1, 250)
          : 1000 / TICK_RATE;
      const maxDistance =
        MOVE_SPEED *
          (elapsedMs / 1000) *
          CLIENT_POSITION_SPEED_TOLERANCE +
        CLIENT_POSITION_GRACE;
      const deltaX = input.position.x - player.position.x;
      const deltaZ = input.position.z - player.position.z;
      const distance = Math.hypot(deltaX, deltaZ);
      const scale =
        distance > maxDistance && distance > 0
          ? maxDistance / distance
          : 1;
      player.position.x = clamp(
        player.position.x + deltaX * scale,
        this.map.minX,
        this.map.maxX,
      );
      player.position.z = clamp(
        player.position.z + deltaZ * scale,
        this.map.minZ,
        this.map.maxZ,
      );
      player.usesClientPosition = true;
      player.lastClientPositionAt = now;
    }
  }

  shoot(id: string, message: ShootMessage, now: number): ServerMessage[] {
    const shooter = this.players.get(id);
    if (!shooter || !shooter.alive || this.roundState !== "playing") return [];
    if (message.sequence <= shooter.lastActionSequence) return [];
    completeReload(shooter, now);
    const firearm = normalizeFirearm(message.weapon);
    if (shooter.reloadingWeapon && shooter.reloadCompletesAt > now) return [];
    const interval =
      message.weapon === "knife"
        ? KNIFE_INTERVAL_MS
        : message.weapon === "m16"
          ? M16_INTERVAL_MS
          : message.weapon === "ak74m"
            ? AK74M_INTERVAL_MS
            : message.weapon === "shotgun01"
              ? SHOTGUN_INTERVAL_MS
            : message.weapon === "awp"
              ? AWP_INTERVAL_MS
          : message.weapon === "rifle" || message.weapon === "m4a1"
            ? RIFLE_INTERVAL_MS
            : PISTOL_INTERVAL_MS;
    if (now - shooter.lastShotAt < interval) return [];

    const direction = normalize(message.direction);
    if (!direction) return [];
    if (shooter.lastInputSequence > 0) {
      const yaw = shooter.yaw * Math.PI / 180;
      const pitch = shooter.pitch * Math.PI / 180;
      const cosine = Math.cos(pitch);
      const authoritativeAim = {
        x: Math.sin(yaw) * cosine,
        y: -Math.sin(pitch),
        z: Math.cos(yaw) * cosine,
      };
      const dot = direction.x * authoritativeAim.x +
        direction.y * authoritativeAim.y +
        direction.z * authoritativeAim.z;
      if (dot < Math.cos(MAX_AIM_DEVIATION_DEGREES * Math.PI / 180)) return [];
    }
    if (firearm && shooter.magazines[firearm] <= 0) return [];
    shooter.lastActionSequence = message.sequence;
    shooter.lastShotAt = now;
    shooter.currentWeapon = firearm ?? "knife";
    if (firearm) shooter.magazines[firearm] -= 1;
    const range =
      message.weapon === "knife"
        ? KNIFE_RANGE
        : message.weapon === "m16"
          ? M16_RANGE
          : message.weapon === "ak74m"
            ? AK74M_RANGE
            : message.weapon === "shotgun01"
              ? SHOTGUN_RANGE
            : message.weapon === "awp"
              ? AWP_RANGE
          : message.weapon === "rifle" || message.weapon === "m4a1"
            ? RIFLE_RANGE
            : PISTOL_RANGE;
    const damage =
      message.weapon === "knife"
        ? KNIFE_DAMAGE
        : message.weapon === "m16"
          ? M16_DAMAGE
          : message.weapon === "ak74m"
            ? AK74M_DAMAGE
            : message.weapon === "shotgun01"
              ? SHOTGUN_PELLET_DAMAGE
            : message.weapon === "awp"
              ? AWP_DAMAGE
          : message.weapon === "rifle" || message.weapon === "m4a1"
            ? RIFLE_DAMAGE
            : PISTOL_DAMAGE;

    const origin = {
      x: shooter.position.x,
      y: shooter.position.y + EYE_HEIGHT,
      z: shooter.position.z,
    };

    const damageByTarget = new Map<Player, number>();
    const headshotsByTarget = new Map<Player, boolean>();
    const shotDirections =
      message.weapon === "shotgun01"
        ? shotgunPelletDirections(direction)
        : [direction];
    for (const shotDirection of shotDirections) {
      let bestTarget: Player | undefined;
      let bestHeadshot = false;
      let bestDistance = range;
      for (const candidate of this.players.values()) {
        if (candidate.id === id || !candidate.alive || now < candidate.protectedUntil)
          continue;
        const bodyCenter = {
          x: candidate.position.x,
          y: candidate.position.y + 0.9,
          z: candidate.position.z,
        };
        const headCenter = {
          x: candidate.position.x,
          y: candidate.position.y + 1.55,
          z: candidate.position.z,
        };
        const bodyDistance = raySphereDistance(
          origin,
          shotDirection,
          bodyCenter,
          0.5,
        );
        const headDistance = message.weapon === "knife"
          ? undefined
          : raySphereDistance(origin, shotDirection, headCenter, 0.28);
        const headshot = headDistance !== undefined &&
          (bodyDistance === undefined || headDistance <= bodyDistance);
        const distance = headshot ? headDistance : bodyDistance;
        if (distance !== undefined && distance < bestDistance) {
          bestDistance = distance;
          bestTarget = candidate;
          bestHeadshot = headshot;
        }
      }
      if (bestTarget) {
        damageByTarget.set(
          bestTarget,
          (damageByTarget.get(bestTarget) ?? 0) +
            (bestHeadshot ? damage * 2 : damage),
        );
        headshotsByTarget.set(
          bestTarget,
          (headshotsByTarget.get(bestTarget) ?? false) || bestHeadshot,
        );
      }
    }

    const action: ServerMessage = {
      type: "action",
      playerId: shooter.id,
      sequence: message.sequence,
      // Preserve the authoritative weapon id so remote clients select the
      // matching model, muzzle behavior and recovered audio family.
      weapon: message.weapon,
      action: "fire",
    };
    if (damageByTarget.size === 0) return [action];
    const messages: ServerMessage[] = [];
    for (const [target, targetDamage] of damageByTarget) {
      target.health = Math.max(0, target.health - targetDamage);
      messages.push({
        type: "hit",
        shooterId: shooter.id,
        targetId: target.id,
        damage: targetDamage,
        targetHealth: target.health,
        weapon: message.weapon,
        headshot: headshotsByTarget.get(target) ?? false,
      });

      if (target.health === 0) {
        shooter.kills += 1;
        target.deaths += 1;
        target.alive = false;
        target.moveX = 0;
        target.moveZ = 0;
        target.respawnAt = now + RESPAWN_DELAY_MS;
        messages.push({
          type: "death",
          killerId: shooter.id,
          victimId: target.id,
          respawnAt: target.respawnAt,
        });
      }
    }
    messages.push(action);
    if (shooter.kills >= SCORE_LIMIT) this.roundState = "ended";

    return messages;
  }

  throwGrenade(
    id: string,
    message: GrenadeMessage,
    now: number,
  ): ServerMessage[] {
    const thrower = this.players.get(id);
    if (
      !thrower ||
      !thrower.alive ||
      thrower.grenades <= 0 ||
      this.roundState !== "playing"
    ) {
      return [];
    }
    const direction = normalize(message.direction);
    if (!direction) return [];

    thrower.grenades -= 1;
    const origin = {
      x: thrower.position.x,
      y: thrower.position.y + EYE_HEIGHT,
      z: thrower.position.z,
    };
    const velocity = {
      x: direction.x * GRENADE_FORCE,
      y: direction.y * GRENADE_FORCE + GRENADE_UPWARD_FORCE,
      z: direction.z * GRENADE_FORCE,
    };
    const grenade: PendingGrenade = {
      id: randomUUID(),
      throwerId: id,
      origin,
      velocity,
      groundY: thrower.groundY,
      thrownAt: now,
      explodesAt: now + GRENADE_FUSE_MS,
    };
    this.pendingGrenades.push(grenade);
    return [
      {
        type: "grenade",
        grenadeId: grenade.id,
        throwerId: grenade.throwerId,
        position: { ...grenade.origin },
        velocity: { ...grenade.velocity },
        explodesAt: grenade.explodesAt,
      },
      {
        type: "action",
        playerId: thrower.id,
        sequence: message.sequence,
        weapon: "grenade",
        action: "throw",
      },
    ];
  }

  playerAction(
    id: string,
    message: WeaponActionMessage,
    now = Date.now(),
  ): ServerMessage[] {
    const player = this.players.get(id);
    if (!player || !player.alive || this.roundState !== "playing") return [];
    if (message.sequence <= player.lastActionSequence) return [];
    if (message.action === "reload" && message.weapon === "knife") return [];
    player.lastActionSequence = message.sequence;
    if (message.action === "equip") {
      player.reloadingWeapon = undefined;
      player.reloadCompletesAt = 0;
      player.currentWeapon = normalizeFirearm(message.weapon) ??
        (message.weapon === "grenade" ? "grenade" : "knife");
    } else {
      const weapon = normalizeFirearm(message.weapon) ??
        (player.currentWeapon === "grenade" || player.currentWeapon === "knife"
          ? undefined
          : player.currentWeapon);
      if (!weapon) return [];
      completeReload(player, now);
      const rule = WEAPON_RULES[weapon];
      if (player.magazines[weapon] < rule.magazine && player.reserves[weapon] > 0) {
        player.reloadingWeapon = weapon;
        player.reloadCompletesAt = now + rule.reloadMs;
      }
    }
    return [
      {
        type: "action",
        playerId: id,
        sequence: message.sequence,
        weapon: message.weapon,
        action: message.action,
      },
    ];
  }

  drainEvents(now: number): ServerMessage[] {
    const events: ServerMessage[] = [];
    for (let index = this.pendingGrenades.length - 1; index >= 0; index -= 1) {
      const grenade = this.pendingGrenades[index];
      if (grenade.explodesAt > now) continue;
      this.pendingGrenades.splice(index, 1);
      const position = grenadePosition(grenade, grenade.explodesAt);
      events.push({
        type: "explosion",
        grenadeId: grenade.id,
        throwerId: grenade.throwerId,
        position,
        radius: GRENADE_RADIUS,
      });
      const thrower = this.players.get(grenade.throwerId);
      for (const target of this.players.values()) {
        if (!target.alive || now < target.protectedUntil) continue;
        const distance = Math.hypot(
          target.position.x - position.x,
          target.position.y + 1 - position.y,
          target.position.z - position.z,
        );
        if (distance > GRENADE_RADIUS) continue;
        const falloff = 1 - distance / GRENADE_RADIUS;
        const damage = Math.round(
          GRENADE_MIN_DAMAGE +
            (GRENADE_MAX_DAMAGE - GRENADE_MIN_DAMAGE) * falloff,
        );
        target.health = Math.max(0, target.health - damage);
        events.push({
          type: "hit",
          shooterId: grenade.throwerId,
          targetId: target.id,
          damage,
          targetHealth: target.health,
          weapon: "grenade",
        });
        if (target.health !== 0) continue;
        if (thrower && target.id !== thrower.id) thrower.kills += 1;
        target.deaths += 1;
        target.alive = false;
        target.moveX = 0;
        target.moveZ = 0;
        target.respawnAt = now + RESPAWN_DELAY_MS;
        events.push({
          type: "death",
          killerId: grenade.throwerId,
          victimId: target.id,
          respawnAt: target.respawnAt,
        });
        if (thrower && thrower.kills >= SCORE_LIMIT)
          this.roundState = "ended";
      }
    }
    return events;
  }

  tick(now: number): ServerMessage {
    this.tickNumber += 1;
    const dt = 1 / TICK_RATE;
    if (now >= this.roundEndsAt) this.roundState = "ended";

    for (const player of this.players.values()) {
      if (this.roundState === "ended") {
        player.moveX = 0;
        player.moveZ = 0;
        continue;
      }
      if (!player.alive) {
        if (player.respawnAt <= now) this.respawn(player, now);
        continue;
      }

      completeReload(player, now);

      if (!player.usesClientPosition) {
        const length = Math.hypot(player.moveX, player.moveZ);
        const scale = length > 1 ? 1 / length : 1;
        const yaw = (player.yaw * Math.PI) / 180;
        const localX = player.moveX * scale;
        const localZ = player.moveZ * scale;
        const worldX = localX * Math.cos(yaw) + localZ * Math.sin(yaw);
        const worldZ = -localX * Math.sin(yaw) + localZ * Math.cos(yaw);
        player.position.x = clamp(
          player.position.x + worldX * MOVE_SPEED * dt,
          this.map.minX,
          this.map.maxX,
        );
        player.position.z = clamp(
          player.position.z + worldZ * MOVE_SPEED * dt,
          this.map.minZ,
          this.map.maxZ,
        );
      }
      if (
        player.jumpRequested &&
        player.position.y <= player.groundY + 0.01
      ) {
        player.verticalVelocity = JUMP_SPEED;
      }
      player.jumpRequested = false;
      player.verticalVelocity -= GRAVITY * dt;
      player.position.y += player.verticalVelocity * dt;
      if (player.position.y < player.groundY) {
        player.position.y = player.groundY;
        player.verticalVelocity = 0;
      }
    }

    return {
      type: "snapshot",
      tick: this.tickNumber,
      serverTime: now,
      players: [...this.players.values()].map((player) =>
        this.snapshot(player),
      ),
      roundState: this.roundState,
      roundEndsAt: this.roundEndsAt,
    };
  }

  private snapshot(player: Player): PlayerSnapshot {
    return {
      id: player.id,
      name: player.name,
      position: { ...player.position },
      yaw: player.yaw,
      pitch: player.pitch,
      health: player.health,
      kills: player.kills,
      deaths: player.deaths,
      alive: player.alive,
      protectedUntil: player.protectedUntil,
      lastInputSequence: player.lastInputSequence,
    };
  }

  private respawn(player: Player, now: number): void {
    player.position = this.nextSpawn();
    player.health = 100;
    player.alive = true;
    player.respawnAt = 0;
    player.protectedUntil = now + SPAWN_PROTECTION_MS;
    player.groundY = player.position.y;
    player.verticalVelocity = 0;
    player.grenades = 1;
    player.magazines = firearmValues((rule) => rule.magazine);
    player.reloadingWeapon = undefined;
    player.reloadCompletesAt = 0;
  }

  private nextSpawn(): Vector3 {
    const point =
      this.map.spawnPoints[this.spawnCursor % this.map.spawnPoints.length];
    this.spawnCursor += 1;
    return { ...point };
  }
}

function normalizeFirearm(weapon: string): FirearmWeapon | undefined {
  if (weapon === "rifle") return "m4a1";
  return Object.prototype.hasOwnProperty.call(WEAPON_RULES, weapon)
    ? weapon as FirearmWeapon
    : undefined;
}

function firearmValues(
  value: (rule: WeaponRule) => number,
): Record<FirearmWeapon, number> {
  return Object.fromEntries(
    Object.entries(WEAPON_RULES).map(([weapon, rule]) => [weapon, value(rule)]),
  ) as Record<FirearmWeapon, number>;
}

function completeReload(player: Player, now: number): void {
  const weapon = player.reloadingWeapon;
  if (!weapon || player.reloadCompletesAt > now) return;
  const rule = WEAPON_RULES[weapon];
  const loaded = Math.min(
    rule.magazine - player.magazines[weapon],
    player.reserves[weapon],
  );
  player.magazines[weapon] += loaded;
  player.reserves[weapon] -= loaded;
  player.reloadingWeapon = undefined;
  player.reloadCompletesAt = 0;
}

function grenadePosition(grenade: PendingGrenade, now: number): Vector3 {
  const elapsed = clamp(
    (now - grenade.thrownAt) / 1000,
    0,
    GRENADE_FUSE_MS / 1000,
  );
  const height = Math.max(0, grenade.origin.y - (grenade.groundY + 0.15));
  const groundTime =
    (grenade.velocity.y +
      Math.sqrt(
        grenade.velocity.y * grenade.velocity.y + 2 * GRAVITY * height,
      )) /
    GRAVITY;
  const travelTime = Math.min(elapsed, groundTime);
  return {
    x: grenade.origin.x + grenade.velocity.x * travelTime,
    y: Math.max(
      grenade.groundY + 0.15,
      grenade.origin.y +
        grenade.velocity.y * travelTime -
        (GRAVITY * travelTime * travelTime) / 2,
    ),
    z: grenade.origin.z + grenade.velocity.z * travelTime,
  };
}

function mapAround(
  center: Vector3,
  halfExtent: number,
  spawnOffset: number,
): MapConfig {
  return {
    spawnPoints: [
      { x: center.x - spawnOffset, y: center.y, z: center.z - spawnOffset },
      { x: center.x + spawnOffset, y: center.y, z: center.z + spawnOffset },
      { x: center.x - spawnOffset, y: center.y, z: center.z + spawnOffset },
      { x: center.x + spawnOffset, y: center.y, z: center.z - spawnOffset },
      { x: center.x, y: center.y, z: center.z - spawnOffset * 1.4 },
      { x: center.x, y: center.y, z: center.z + spawnOffset * 1.4 },
    ],
    minX: center.x - halfExtent,
    maxX: center.x + halfExtent,
    minZ: center.z - halfExtent,
    maxZ: center.z + halfExtent,
  };
}

function normalizeDegrees(value: number): number {
  return ((value % 360) + 360) % 360;
}

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
}

function normalize(value: Vector3): Vector3 | undefined {
  const length = Math.hypot(value.x, value.y, value.z);
  if (length < 0.001 || length > 1000) return undefined;
  return {
    x: value.x / length,
    y: value.y / length,
    z: value.z / length,
  };
}

function shotgunPelletDirections(forward: Vector3): Vector3[] {
  const reference = Math.abs(forward.y) > 0.92
    ? { x: 1, y: 0, z: 0 }
    : { x: 0, y: 1, z: 0 };
  const right = normalize({
    x: reference.y * forward.z - reference.z * forward.y,
    y: reference.z * forward.x - reference.x * forward.z,
    z: reference.x * forward.y - reference.y * forward.x,
  });
  if (!right) return [forward];
  const up = normalize({
    x: forward.y * right.z - forward.z * right.y,
    y: forward.z * right.x - forward.x * right.z,
    z: forward.x * right.y - forward.y * right.x,
  });
  if (!up) return [forward];

  const directions = [forward];
  for (let index = 0; index < SHOTGUN_PELLET_COUNT - 1; index += 1) {
    const inner = index < 4;
    const ringIndex = inner ? index : index - 4;
    const ringCount = inner ? 4 : 3;
    const angle = (ringIndex / ringCount) * Math.PI * 2 +
      (inner ? 0 : Math.PI / 4);
    const spread = inner
      ? SHOTGUN_INNER_SPREAD_TANGENT
      : SHOTGUN_OUTER_SPREAD_TANGENT;
    const pellet = normalize({
      x: forward.x +
        (right.x * Math.cos(angle) + up.x * Math.sin(angle)) *
          spread,
      y: forward.y +
        (right.y * Math.cos(angle) + up.y * Math.sin(angle)) *
          spread,
      z: forward.z +
        (right.z * Math.cos(angle) + up.z * Math.sin(angle)) *
          spread,
    });
    if (pellet) directions.push(pellet);
  }
  return directions;
}

function raySphereDistance(
  origin: Vector3,
  direction: Vector3,
  center: Vector3,
  radius: number,
): number | undefined {
  const ox = origin.x - center.x;
  const oy = origin.y - center.y;
  const oz = origin.z - center.z;
  const halfB = ox * direction.x + oy * direction.y + oz * direction.z;
  const c = ox * ox + oy * oy + oz * oz - radius * radius;
  const discriminant = halfB * halfB - c;
  if (discriminant < 0) return undefined;
  const distance = -halfB - Math.sqrt(discriminant);
  return distance >= 0 ? distance : undefined;
}
