export type Vector3 = {
  x: number;
  y: number;
  z: number;
};

export type JoinMessage = {
  type: "join";
  name: string;
  room?: string;
};

export type InputMessage = {
  type: "input";
  sequence: number;
  moveX: number;
  moveZ: number;
  jump: boolean;
  yaw: number;
  pitch: number;
};

export type ShootMessage = {
  type: "shoot";
  sequence: number;
  direction: Vector3;
  weapon: "rifle";
};

export type ClientMessage = JoinMessage | InputMessage | ShootMessage;

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
    }
  | {
      type: "hit";
      shooterId: string;
      targetId: string;
      damage: number;
      targetHealth: number;
    }
  | {
      type: "death";
      killerId: string;
      victimId: string;
      respawnAt: number;
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
    return {
      type: "join",
      name: candidate.name.trim(),
      room:
        typeof candidate.room === "string"
          ? candidate.room.trim().toLowerCase()
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
    finite(candidate.pitch)
  ) {
    return {
      type: "input",
      sequence: candidate.sequence as number,
      moveX: Math.max(-1, Math.min(1, candidate.moveX)),
      moveZ: Math.max(-1, Math.min(1, candidate.moveZ)),
      jump: candidate.jump === true,
      yaw: candidate.yaw,
      pitch: Math.max(-89, Math.min(89, candidate.pitch)),
    };
  }

  if (
    candidate.type === "shoot" &&
    Number.isInteger(candidate.sequence) &&
    candidate.weapon === "rifle" &&
    vector3(candidate.direction)
  ) {
    return {
      type: "shoot",
      sequence: candidate.sequence as number,
      weapon: "rifle",
      direction: candidate.direction,
    };
  }

  return undefined;
}
