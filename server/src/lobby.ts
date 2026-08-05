import { randomBytes } from "node:crypto";

export const SUPPORTED_MAPS = [
  "pyramid",
  "newconstructionsite",
  "biochemicaltown",
  "classicconstructionsite",
  "steelfactory",
  "icefiremaze",
  "radiationdistrict",
] as const;

export type SupportedMap = (typeof SUPPORTED_MAPS)[number];

export type LobbyRoom = {
  id: string;
  name: string;
  map: SupportedMap;
  players: number;
  maxPlayers: number;
  createdAt: number;
};

type StoredLobbyRoom = Omit<LobbyRoom, "players">;

const MAP_SET = new Set<string>(SUPPORTED_MAPS);
const MAX_LISTED_ROOMS = 64;

export class LobbyRegistry {
  private readonly rooms = new Map<string, StoredLobbyRoom>();

  constructor(
    private readonly createId: () => string = () =>
      randomBytes(4).toString("hex"),
  ) {}

  create(
    name: string,
    requestedMap: string | undefined,
    now = Date.now(),
  ): StoredLobbyRoom {
    if (this.rooms.size >= MAX_LISTED_ROOMS) {
      throw new Error("room_limit");
    }
    const roomName = normalizeRoomName(name);
    if (!roomName) throw new Error("invalid_room_name");

    let id = "";
    for (let attempt = 0; attempt < 8; attempt += 1) {
      id = sanitizeRoomId(this.createId());
      if (id && !this.rooms.has(id)) break;
      id = "";
    }
    if (!id) throw new Error("room_id_unavailable");

    const room: StoredLobbyRoom = {
      id,
      name: roomName,
      map: normalizeMap(requestedMap),
      maxPlayers: 12,
      createdAt: now,
    };
    this.rooms.set(id, room);
    return room;
  }

  get(id: string): StoredLobbyRoom | undefined {
    return this.rooms.get(sanitizeRoomId(id));
  }

  list(playerCounts: ReadonlyMap<string, number>): LobbyRoom[] {
    return [...this.rooms.values()]
      .map((room) => ({
        ...room,
        players: playerCounts.get(room.id) ?? 0,
      }))
      .sort((left, right) => {
        const occupancy = right.players - left.players;
        return occupancy !== 0
          ? occupancy
          : left.createdAt - right.createdAt;
      });
  }
}

export function normalizeMap(value: string | undefined): SupportedMap {
  const candidate = (value ?? "").toLowerCase().replace(/[^a-z0-9]/g, "");
  return MAP_SET.has(candidate) ? (candidate as SupportedMap) : "pyramid";
}

export function sanitizeRoomId(value: string): string {
  return value.toLowerCase().replace(/[^a-z0-9_-]/g, "").slice(0, 24);
}

function normalizeRoomName(value: string): string | undefined {
  if (typeof value !== "string") return undefined;
  const name = value.trim().replace(/\s+/g, " ");
  if (
    name.length === 0 ||
    name.length > 24 ||
    /[\u0000-\u001f\u007f]/.test(name)
  ) {
    return undefined;
  }
  return name;
}
