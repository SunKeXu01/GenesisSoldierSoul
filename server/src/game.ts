import { randomUUID } from "node:crypto";
import type {
  InputMessage,
  PlayerSnapshot,
  ServerMessage,
  ShootMessage,
  Vector3,
} from "./protocol.js";

export const TICK_RATE = 20;
const MAX_PLAYERS = 16;
const MOVE_SPEED = 4;
const JUMP_SPEED = 7;
const GRAVITY = 20;
const PLAYER_RADIUS = 0.55;
const EYE_HEIGHT = 1.55;
const PISTOL_DAMAGE = 34;
const PISTOL_RANGE = 90;
const PISTOL_INTERVAL_MS = 250;
const KNIFE_DAMAGE = 50;
const KNIFE_RANGE = 2.2;
const KNIFE_INTERVAL_MS = 500;
const RESPAWN_DELAY_MS = 3000;
const ROUND_DURATION_MS = 3 * 60 * 1000;
const SCORE_LIMIT = 10;

type Player = PlayerSnapshot & {
  moveX: number;
  moveZ: number;
  jumpRequested: boolean;
  verticalVelocity: number;
  groundY: number;
  lastShotAt: number;
  respawnAt: number;
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
  pyramid: mapAround({ x: 0, y: 0, z: 0 }, 40, 2),
  scene3: mapAround({ x: 0.464, y: 1.32, z: -62.5 }, 80, 10),
  ghost: mapAround({ x: 9.769, y: 2.18, z: 48.351 }, 70, 8),
};

const DEFAULT_MAP = mapAround({ x: 0, y: 0, z: 0 }, 60, 8);

export class GameRoom {
  readonly id: string;
  private tickNumber = 0;
  private spawnCursor = 0;
  private readonly players = new Map<string, Player>();
  private readonly map: MapConfig;
  private readonly roundEndsAt: number;
  private roundState: "playing" | "ended" = "playing";

  constructor(id: string, now = Date.now(), roundDurationMs = ROUND_DURATION_MS) {
    this.id = id;
    this.map = MAP_CONFIGS[id.toLowerCase()] ?? DEFAULT_MAP;
    this.roundEndsAt = now + roundDurationMs;
  }

  get size(): number {
    return this.players.size;
  }

  get full(): boolean {
    return this.size >= MAX_PLAYERS;
  }

  addPlayer(name: string): PlayerSnapshot {
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
      moveX: 0,
      moveZ: 0,
      jumpRequested: false,
      verticalVelocity: 0,
      groundY: 0,
      lastShotAt: 0,
      respawnAt: 0,
    };
    player.groundY = player.position.y;
    this.players.set(id, player);
    return this.snapshot(player);
  }

  removePlayer(id: string): void {
    this.players.delete(id);
  }

  applyInput(id: string, input: InputMessage): void {
    const player = this.players.get(id);
    if (!player || !player.alive) return;
    if (input.sequence <= player.lastInputSequence) return;
    player.lastInputSequence = input.sequence;
    player.moveX = input.moveX;
    player.moveZ = input.moveZ;
    player.jumpRequested = input.jump;
    player.yaw = normalizeDegrees(input.yaw);
    player.pitch = input.pitch;
  }

  shoot(id: string, message: ShootMessage, now: number): ServerMessage[] {
    const shooter = this.players.get(id);
    if (!shooter || !shooter.alive || this.roundState !== "playing") return [];
    const interval =
      message.weapon === "knife" ? KNIFE_INTERVAL_MS : PISTOL_INTERVAL_MS;
    if (now - shooter.lastShotAt < interval) return [];

    const direction = normalize(message.direction);
    if (!direction) return [];
    shooter.lastShotAt = now;
    const range = message.weapon === "knife" ? KNIFE_RANGE : PISTOL_RANGE;
    const damage = message.weapon === "knife" ? KNIFE_DAMAGE : PISTOL_DAMAGE;

    const origin = {
      x: shooter.position.x,
      y: shooter.position.y + EYE_HEIGHT,
      z: shooter.position.z,
    };

    let bestTarget: Player | undefined;
    let bestDistance = range;
    for (const candidate of this.players.values()) {
      if (candidate.id === id || !candidate.alive) continue;
      const center = {
        x: candidate.position.x,
        y: candidate.position.y + 1,
        z: candidate.position.z,
      };
      const distance = raySphereDistance(
        origin,
        direction,
        center,
        PLAYER_RADIUS,
      );
      if (distance !== undefined && distance < bestDistance) {
        bestDistance = distance;
        bestTarget = candidate;
      }
    }

    if (!bestTarget) return [];
    bestTarget.health = Math.max(0, bestTarget.health - damage);
    const messages: ServerMessage[] = [
      {
        type: "hit",
        shooterId: shooter.id,
        targetId: bestTarget.id,
        damage,
        targetHealth: bestTarget.health,
      },
    ];

    if (bestTarget.health === 0) {
      shooter.kills += 1;
      bestTarget.deaths += 1;
      bestTarget.alive = false;
      bestTarget.moveX = 0;
      bestTarget.moveZ = 0;
      bestTarget.respawnAt = now + RESPAWN_DELAY_MS;
      messages.push({
        type: "death",
        killerId: shooter.id,
        victimId: bestTarget.id,
        respawnAt: bestTarget.respawnAt,
      });
      if (shooter.kills >= SCORE_LIMIT) this.roundState = "ended";
    }

    return messages;
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
        if (player.respawnAt <= now) this.respawn(player);
        continue;
      }

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
      lastInputSequence: player.lastInputSequence,
    };
  }

  private respawn(player: Player): void {
    player.position = this.nextSpawn();
    player.health = 100;
    player.alive = true;
    player.respawnAt = 0;
    player.groundY = player.position.y;
    player.verticalVelocity = 0;
  }

  private nextSpawn(): Vector3 {
    const point =
      this.map.spawnPoints[this.spawnCursor % this.map.spawnPoints.length];
    this.spawnCursor += 1;
    return { ...point };
  }
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
