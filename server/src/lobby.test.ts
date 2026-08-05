import { describe, expect, it } from "vitest";
import { LobbyRegistry, normalizeMap, sanitizeRoomId } from "./lobby.js";

describe("LobbyRegistry", () => {
  it("creates and lists recovered-map rooms with live occupancy", () => {
    const registry = new LobbyRegistry(() => "ROOM-A");
    const created = registry.create(
      "  新兵训练房  ",
      "BiochemicalTown",
      1_000,
    );
    expect(created).toMatchObject({
      id: "room-a",
      name: "新兵训练房",
      map: "biochemicaltown",
      maxPlayers: 12,
    });

    const counts = new Map([["room-a", 3]]);
    expect(registry.list(counts)[0]).toMatchObject({
      id: "room-a",
      players: 3,
    });
  });

  it("rejects invalid names and normalizes map and room identifiers", () => {
    const registry = new LobbyRegistry(() => "valid-room");
    expect(() => registry.create(" ", "pyramid")).toThrow(
      "invalid_room_name",
    );
    expect(normalizeMap("unknown")).toBe("pyramid");
    expect(sanitizeRoomId(" TEAM A! ")).toBe("teama");
  });

  it("accepts every promoted recovered map", () => {
    expect(normalizeMap("Steel Factory")).toBe("steelfactory");
    expect(normalizeMap("Ice-Fire Maze")).toBe("icefiremaze");
    expect(normalizeMap("Radiation_District")).toBe("radiationdistrict");
  });
});
