import { describe, expect, it } from "vitest";
import { GameRoom } from "./game.js";

describe("GameRoom", () => {
  it("ignores stale input sequences and moves from accepted input", () => {
    const room = new GameRoom("test");
    const player = room.addPlayer("玩家一");
    room.applyInput(player.id, {
      type: "input",
      sequence: 2,
      moveX: 0,
      moveZ: 1,
      jump: false,
      yaw: 0,
      pitch: 0,
    });
    room.applyInput(player.id, {
      type: "input",
      sequence: 1,
      moveX: 0,
      moveZ: -1,
      jump: false,
      yaw: 0,
      pitch: 0,
    });

    const snapshot = room.tick(Date.now());
    expect(snapshot.type).toBe("snapshot");
    if (snapshot.type !== "snapshot") return;
    const updated = snapshot.players.find((item) => item.id === player.id);
    expect(updated?.lastInputSequence).toBe(2);
    expect(updated!.position.z).toBeGreaterThan(player.position.z);
  });

  it("rejects impossible zero-length shots", () => {
    const room = new GameRoom("test");
    const player = room.addPlayer("玩家一");
    expect(
      room.shoot(
        player.id,
        {
          type: "shoot",
          sequence: 1,
          weapon: "rifle",
          direction: { x: 0, y: 0, z: 0 },
        },
        Date.now(),
      ),
    ).toEqual([]);
  });

  it("applies authoritative rifle damage, death, and respawn", () => {
    const room = new GameRoom("test");
    const shooter = room.addPlayer("玩家一");
    const target = room.addPlayer("玩家二");
    const direction = {
      x: target.position.x - shooter.position.x,
      y: target.position.y + 1 - (shooter.position.y + 1.55),
      z: target.position.z - shooter.position.z,
    };
    const start = 10_000;

    room.shoot(
      shooter.id,
      { type: "shoot", sequence: 1, weapon: "rifle", direction },
      start,
    );
    room.shoot(
      shooter.id,
      { type: "shoot", sequence: 2, weapon: "rifle", direction },
      start + 111,
    );
    const events = room.shoot(
      shooter.id,
      { type: "shoot", sequence: 3, weapon: "rifle", direction },
      start + 222,
    );

    expect(events.some((event) => event.type === "death")).toBe(true);
    const deadSnapshot = room.tick(start + 223);
    expect(deadSnapshot.type).toBe("snapshot");
    if (deadSnapshot.type !== "snapshot") return;
    expect(
      deadSnapshot.players.find((player) => player.id === target.id)?.alive,
    ).toBe(false);

    const respawnedSnapshot = room.tick(start + 3_223);
    expect(respawnedSnapshot.type).toBe("snapshot");
    if (respawnedSnapshot.type !== "snapshot") return;
    const respawned = respawnedSnapshot.players.find(
      (player) => player.id === target.id,
    );
    expect(respawned?.alive).toBe(true);
    expect(respawned?.health).toBe(100);
  });

  it("uses recovered scene coordinates and simulates jumping", () => {
    const room = new GameRoom("scenegd");
    const player = room.addPlayer("玩家一");
    expect(player.position.x).toBeGreaterThan(900);
    expect(player.position.y).toBeCloseTo(296.87);
    expect(player.position.z).toBeGreaterThan(1500);

    room.applyInput(player.id, {
      type: "input",
      sequence: 1,
      moveX: 0,
      moveZ: 0,
      jump: true,
      yaw: 0,
      pitch: 0,
    });
    const snapshot = room.tick(Date.now());
    expect(snapshot.type).toBe("snapshot");
    if (snapshot.type !== "snapshot") return;
    expect(
      snapshot.players.find((candidate) => candidate.id === player.id)!.position
        .y,
    ).toBeGreaterThan(player.position.y);
  });
});
