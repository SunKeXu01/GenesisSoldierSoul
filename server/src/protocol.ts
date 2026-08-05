export type Vector3 = {
  x: number;
  y: number;
  z: number;
};

export type JoinMessage = {
  type: "join";
  name: string;
  room?: string;
  map?: string;
};

export type InputMessage = {
  type: "input";
  sequence: number;
  moveX: number;
  moveZ: number;
  jump: boolean;
  yaw: number;
  pitch: number;
  position?: Vector3;
};

export type ShootMessage = {
  type: "shoot";
  sequence: number;
  direction: Vector3;
  weapon:
    | "rifle"
    | "m4a1"
    | "m16"
    | "ak74m"
    | "shotgun01"
    | "awp"
    | "pistol"
    | "knife";
};

export type GrenadeMessage = {
  type: "grenade";
  sequence: number;
  direction: Vector3;
};

export type WeaponActionMessage = {
  type: "action";
  sequence: number;
  weapon:
    | "rifle"
    | "m4a1"
    | "m16"
    | "ak74m"
    | "shotgun01"
    | "awp"
    | "pistol"
    | "knife"
    | "grenade";
  action: "equip" | "reload";
};

export type ClientMessage =
  | JoinMessage
  | InputMessage
  | ShootMessage
  | GrenadeMessage
  | WeaponActionMessage;

export type PlayerSnapshot = {
  id: string;
  name: string;
  position: Vector3;
  yaw: number;
  pitch: number;
  health: number;
  kills: number;
  deaths: number;
  alive: boolean;
  protectedUntil: number;
  lastInputSequence: number;
};

export type ServerMessage =
  | {
      type: "welcome";
      id: string;
      room: string;
      tickRate: number;
      serverTime: number;
    }
  | {
      type: "snapshot";
      tick: number;
      serverTime: number;
      players: PlayerSnapshot[];
      roundState: "playing" | "ended";
      roundEndsAt: number;
    }
  | {
      type: "hit";
      shooterId: string;
      targetId: string;
      damage: number;
      targetHealth: number;
      weapon: ShootMessage["weapon"] | "grenade";
      headshot?: boolean;
    }
  | {
      type: "death";
      killerId: string;
      victimId: string;
      respawnAt: number;
    }
  | {
      type: "grenade";
      grenadeId: string;
      throwerId: string;
      position: Vector3;
      velocity: Vector3;
      explodesAt: number;
    }
  | {
      type: "explosion";
      grenadeId: string;
      throwerId: string;
      position: Vector3;
      radius: number;
    }
  | {
      type: "action";
      playerId: string;
      sequence: number;
      weapon: WeaponActionMessage["weapon"];
      action: "equip" | "reload" | "fire" | "throw";
    }
  | {
      type: "error";
      code: string;
      message: string;
    };

const finite = (value: unknown): value is number =>
  typeof value === "number" && Number.isFinite(value);

const boundedString = (value: unknown, maxLength: number): value is string =>
  typeof value === "string" &&
  value.trim().length > 0 &&
  value.trim().length <= maxLength &&
  !/[\u0000-\u001f\u007f]/.test(value);

const vector3 = (value: unknown): value is Vector3 => {
  if (!value || typeof value !== "object") return false;
  const candidate = value as Record<string, unknown>;
  return finite(candidate.x) && finite(candidate.y) && finite(candidate.z);
};

export function parseClientMessage(raw: string): ClientMessage | undefined {
  let value: unknown;
  try {
    value = JSON.parse(raw);
  } catch {
    return undefined;
  }

  if (!value || typeof value !== "object") return undefined;
  const candidate = value as Record<string, unknown>;

  if (candidate.type === "join" && boundedString(candidate.name, 20)) {
    if (
      candidate.room !== undefined &&
      !boundedString(candidate.room, 24)
    ) {
      return undefined;
    }
    if (candidate.map !== undefined && !boundedString(candidate.map, 32)) {
      return undefined;
    }
    return {
      type: "join",
      name: candidate.name.trim(),
      room:
        typeof candidate.room === "string"
          ? candidate.room.trim().toLowerCase()
          : undefined,
      map:
        typeof candidate.map === "string"
          ? candidate.map.trim().toLowerCase()
          : undefined,
    };
  }

  if (
    candidate.type === "input" &&
    Number.isInteger(candidate.sequence) &&
    finite(candidate.moveX) &&
    finite(candidate.moveZ) &&
    (candidate.jump === undefined || typeof candidate.jump === "boolean") &&
    finite(candidate.yaw) &&
    finite(candidate.pitch) &&
    (candidate.position === undefined || vector3(candidate.position))
  ) {
    return {
      type: "input",
      sequence: candidate.sequence as number,
      moveX: Math.max(-1, Math.min(1, candidate.moveX)),
      moveZ: Math.max(-1, Math.min(1, candidate.moveZ)),
      jump: candidate.jump === true,
      yaw: candidate.yaw,
      pitch: Math.max(-89, Math.min(89, candidate.pitch)),
      position:
        candidate.position === undefined
          ? undefined
          : candidate.position,
    };
  }

  if (
    candidate.type === "shoot" &&
    Number.isInteger(candidate.sequence) &&
    (candidate.weapon === "rifle" ||
      candidate.weapon === "m4a1" ||
      candidate.weapon === "m16" ||
      candidate.weapon === "ak74m" ||
      candidate.weapon === "shotgun01" ||
      candidate.weapon === "awp" ||
      candidate.weapon === "pistol" ||
      candidate.weapon === "knife") &&
    vector3(candidate.direction)
  ) {
    return {
      type: "shoot",
      sequence: candidate.sequence as number,
      weapon: candidate.weapon,
      direction: candidate.direction,
    };
  }

  if (
    candidate.type === "grenade" &&
    Number.isInteger(candidate.sequence) &&
    vector3(candidate.direction)
  ) {
    return {
      type: "grenade",
      sequence: candidate.sequence as number,
      direction: candidate.direction,
    };
  }

  if (
    candidate.type === "action" &&
    Number.isInteger(candidate.sequence) &&
    (candidate.weapon === "rifle" ||
      candidate.weapon === "m4a1" ||
      candidate.weapon === "m16" ||
      candidate.weapon === "ak74m" ||
      candidate.weapon === "shotgun01" ||
      candidate.weapon === "awp" ||
      candidate.weapon === "pistol" ||
      candidate.weapon === "knife" ||
      candidate.weapon === "grenade") &&
    (candidate.action === "equip" || candidate.action === "reload")
  ) {
    return {
      type: "action",
      sequence: candidate.sequence as number,
      weapon: candidate.weapon,
      action: candidate.action,
    };
  }

  return undefined;
}
