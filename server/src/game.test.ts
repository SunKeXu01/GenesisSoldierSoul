import { describe, expect, it } from "vitest";
import { GameRoom } from "./game.js";

describe("GameRoom", () => {
  it("accepts twelve players and rejects the thirteenth", () => {
    const room = new GameRoom("capacity");
    for (let index = 0; index < 12; index += 1)
      expect(room.addPlayer(`玩家${index + 1}`)).toBeDefined();
    expect(room.size).toBe(12);
    expect(room.full).toBe(true);
    expect(() => room.addPlayer("第十三人")).toThrow("room_full");
  });

  it("uses scene-local coordinates for Pyramid lobby matches", () => {
    const room = new GameRoom("room:pyramid", 0, 180_000, "pyramid");
    const first = room.addPlayer("玩家一");
    const second = room.addPlayer("玩家二");

    expect(first.position).toEqual({ x: -2, y: 0, z: -2 });
    expect(second.position).toEqual({ x: 2, y: 0, z: 2 });
  });

  it("keeps all seven promoted maps' six spawn slots pairwise symmetric", () => {
    const mapIds = [
      "pyramid",
      "newconstructionsite",
      "biochemicaltown",
      "classicconstructionsite",
      "steelfactory",
      "icefiremaze",
      "radiationdistrict",
    ];
    for (const mapId of mapIds) {
      const room = new GameRoom(`fairness:${mapId}`, 0, 180_000, mapId);
      const positions = Array.from({ length: 6 }, (_, index) =>
        room.addPlayer(`公平性-${index + 1}`).position,
      );
      expect(new Set(positions.map((point) => `${point.x},${point.z}`)).size)
        .toBe(6);
      for (let index = 0; index < 6; index += 2) {
        expect(positions[index]!.x + positions[index + 1]!.x).toBeCloseTo(0);
        expect(positions[index]!.z + positions[index + 1]!.z).toBeCloseTo(0);
        expect(positions[index]!.y).toBe(positions[index + 1]!.y);
      }
      const meanX = positions.reduce((sum, point) => sum + point.x, 0) / 6;
      const meanZ = positions.reduce((sum, point) => sum + point.z, 0) / 6;
      expect(meanX).toBeCloseTo(0);
      expect(meanZ).toBeCloseTo(0);
    }
  });

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
          weapon: "pistol",
          direction: { x: 0, y: 0, z: 0 },
        },
        Date.now(),
      ),
    ).toEqual([]);
  });

  it("rejects shots that diverge from the latest authoritative aim", () => {
    const room = new GameRoom("aim-validation");
    const shooter = room.addPlayer("射线校验者");
    room.applyInput(shooter.id, {
      type: "input",
      sequence: 1,
      moveX: 0,
      moveZ: 0,
      jump: false,
      yaw: 0,
      pitch: 0,
    });

    expect(room.shoot(
      shooter.id,
      {
        type: "shoot",
        sequence: 2,
        weapon: "m4a1",
        direction: { x: 1, y: 0, z: 0 },
      },
      10_000,
    )).toEqual([]);
    expect(room.shoot(
      shooter.id,
      {
        type: "shoot",
        sequence: 2,
        weapon: "m4a1",
        direction: { x: 0, y: 0, z: 1 },
      },
      10_000,
    )).toEqual([expect.objectContaining({ type: "action", action: "fire" })]);
  });

  it("broadcasts validated combat presentation actions", () => {
    const room = new GameRoom("test");
    const player = room.addPlayer("动作玩家");
    expect(
      room.playerAction(player.id, {
        type: "action",
        sequence: 3,
        weapon: "pistol",
        action: "reload",
      }),
    ).toEqual([
      {
        type: "action",
        playerId: player.id,
        sequence: 3,
        weapon: "pistol",
        action: "reload",
      },
    ]);
    expect(
      room.playerAction(player.id, {
        type: "action",
        sequence: 4,
        weapon: "knife",
        action: "reload",
      }),
    ).toEqual([]);
    expect(
      room.playerAction(player.id, {
        type: "action",
        sequence: 2,
        weapon: "pistol",
        action: "equip",
      }),
    ).toEqual([]);
    expect(
      room.playerAction(player.id, {
        type: "action",
        sequence: 5,
        weapon: "shotgun01",
        action: "reload",
      }),
    ).toEqual([
      {
        type: "action",
        playerId: player.id,
        sequence: 5,
        weapon: "shotgun01",
        action: "reload",
      },
    ]);
  });

  it("broadcasts a fire action even when a valid shot misses", () => {
    const room = new GameRoom("test");
    const player = room.addPlayer("动作玩家");
    expect(
      room.shoot(
        player.id,
        {
          type: "shoot",
          sequence: 5,
          weapon: "pistol",
          direction: { x: 0, y: 1, z: 0 },
        },
        12_000,
      ),
    ).toEqual([
      {
        type: "action",
        playerId: player.id,
        sequence: 5,
        weapon: "pistol",
        action: "fire",
      },
    ]);
  });

  it("preserves shotgun presentation semantics when broadcasting fire", () => {
    const room = new GameRoom("test");
    const player = room.addPlayer("霰弹枪玩家");
    expect(
      room.shoot(
        player.id,
        {
          type: "shoot",
          sequence: 6,
          weapon: "shotgun01",
          direction: { x: 0, y: 1, z: 0 },
        },
        12_000,
      ),
    ).toEqual([
      {
        type: "action",
        playerId: player.id,
        sequence: 6,
        weapon: "shotgun01",
        action: "fire",
      },
    ]);
  });

  it("applies authoritative pistol damage, death, and respawn", () => {
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
      { type: "shoot", sequence: 1, weapon: "pistol", direction },
      start,
    );
    room.shoot(
      shooter.id,
      { type: "shoot", sequence: 2, weapon: "pistol", direction },
      start + 250,
    );
    const events = room.shoot(
      shooter.id,
      { type: "shoot", sequence: 3, weapon: "pistol", direction },
      start + 500,
    );

    expect(events.some((event) => event.type === "death")).toBe(true);
    const deadSnapshot = room.tick(start + 501);
    expect(deadSnapshot.type).toBe("snapshot");
    if (deadSnapshot.type !== "snapshot") return;
    expect(
      deadSnapshot.players.find((player) => player.id === target.id)?.alive,
    ).toBe(false);

    const respawnedSnapshot = room.tick(start + 3_501);
    expect(respawnedSnapshot.type).toBe("snapshot");
    if (respawnedSnapshot.type !== "snapshot") return;
    const respawned = respawnedSnapshot.players.find(
      (player) => player.id === target.id,
    );
    expect(respawned?.alive).toBe(true);
    expect(respawned?.health).toBe(100);
  });

  it("applies authoritative headshots to the head region", () => {
    const room = new GameRoom("pyramid");
    const shooter = room.addPlayer("爆头测试者");
    const target = room.addPlayer("目标");
    const direction = {
      x: target.position.x - shooter.position.x,
      y: target.position.y + 1.55 - (shooter.position.y + 1.55),
      z: target.position.z - shooter.position.z,
    };

    expect(room.shoot(
      shooter.id,
      { type: "shoot", sequence: 1, weapon: "m4a1", direction },
      20_000,
    )[0]).toMatchObject({
      type: "hit",
      damage: 60,
      targetHealth: 40,
      headshot: true,
    });
  });

  it("blocks damage during initial and respawn spawn protection", () => {
    const start = 25_000;
    const room = new GameRoom("pyramid", start);
    const shooter = room.addPlayer("攻击者", start);
    const target = room.addPlayer("受保护目标", start);
    const direction = {
      x: target.position.x - shooter.position.x,
      y: target.position.y + 1 - (shooter.position.y + 1.55),
      z: target.position.z - shooter.position.z,
    };
    expect(room.shoot(
      shooter.id,
      { type: "shoot", sequence: 1, weapon: "m4a1", direction },
      start + 2_999,
    )).toEqual([expect.objectContaining({ type: "action", action: "fire" })]);
    expect(room.shoot(
      shooter.id,
      { type: "shoot", sequence: 2, weapon: "m4a1", direction },
      start + 3_099,
    )[0]).toMatchObject({ type: "hit", targetId: target.id });
  });

  it("rejects empty magazines and enforces reload completion", () => {
    const room = new GameRoom("pyramid");
    const shooter = room.addPlayer("弹药测试者");
    const miss = { x: 0, y: 1, z: 0 };
    const start = 80_000;
    for (let shot = 0; shot < 12; shot += 1) {
      expect(room.shoot(
        shooter.id,
        { type: "shoot", sequence: shot + 1, weapon: "pistol", direction: miss },
        start + shot * 250,
      )).toEqual([expect.objectContaining({ type: "action", action: "fire" })]);
    }
    expect(room.shoot(
      shooter.id,
      { type: "shoot", sequence: 13, weapon: "pistol", direction: miss },
      start + 12 * 250,
    )).toEqual([]);

    expect(room.playerAction(
      shooter.id,
      { type: "action", sequence: 14, weapon: "pistol", action: "reload" },
      start + 12 * 250,
    )).toEqual([expect.objectContaining({ type: "action", action: "reload" })]);
    expect(room.shoot(
      shooter.id,
      { type: "shoot", sequence: 15, weapon: "pistol", direction: miss },
      start + 12 * 250 + 1_449,
    )).toEqual([]);
    expect(room.shoot(
      shooter.id,
      { type: "shoot", sequence: 16, weapon: "pistol", direction: miss },
      start + 12 * 250 + 1_450,
    )).toEqual([expect.objectContaining({ type: "action", action: "fire" })]);
  });

  it("applies recovered M4A1 rifle cadence and damage", () => {
    const room = new GameRoom("test");
    const shooter = room.addPlayer("玩家一");
    const target = room.addPlayer("玩家二");
    const direction = {
      x: target.position.x - shooter.position.x,
      y: target.position.y + 1 - (shooter.position.y + 1.55),
      z: target.position.z - shooter.position.z,
    };
    const start = 20_000;

    const first = room.shoot(
      shooter.id,
      { type: "shoot", sequence: 1, weapon: "rifle", direction },
      start,
    );
    const tooFast = room.shoot(
      shooter.id,
      { type: "shoot", sequence: 2, weapon: "rifle", direction },
      start + 50,
    );
    const second = room.shoot(
      shooter.id,
      { type: "shoot", sequence: 3, weapon: "rifle", direction },
      start + 100,
    );

    expect(first[0]).toMatchObject({ type: "hit", damage: 30 });
    expect(tooFast).toEqual([]);
    expect(second[0]).toMatchObject({ type: "hit", damage: 30 });
  });

  it("applies recovered M16 cadence and damage", () => {
    const room = new GameRoom("test");
    const shooter = room.addPlayer("M16玩家");
    const target = room.addPlayer("目标");
    const direction = {
      x: target.position.x - shooter.position.x,
      y: target.position.y + 1 - (shooter.position.y + 1.55),
      z: target.position.z - shooter.position.z,
    };
    const start = 30_000;

    const first = room.shoot(
      shooter.id,
      { type: "shoot", sequence: 1, weapon: "m16", direction },
      start,
    );
    const tooFast = room.shoot(
      shooter.id,
      { type: "shoot", sequence: 2, weapon: "m16", direction },
      start + 80,
    );
    const second = room.shoot(
      shooter.id,
      { type: "shoot", sequence: 3, weapon: "m16", direction },
      start + 85,
    );

    expect(first[0]).toMatchObject({ type: "hit", damage: 27 });
    expect(first.at(-1)).toMatchObject({
      type: "action",
      weapon: "m16",
      action: "fire",
    });
    expect(tooFast).toEqual([]);
    expect(second[0]).toMatchObject({ type: "hit", damage: 27 });
  });

  it("applies AK-74M cadence and authoritative damage", () => {
    const room = new GameRoom("test");
    const shooter = room.addPlayer("AK玩家");
    const target = room.addPlayer("目标");
    const direction = {
      x: target.position.x - shooter.position.x,
      y: target.position.y + 1 - (shooter.position.y + 1.55),
      z: target.position.z - shooter.position.z,
    };
    const start = 40_000;

    const first = room.shoot(
      shooter.id,
      { type: "shoot", sequence: 1, weapon: "ak74m", direction },
      start,
    );
    const tooFast = room.shoot(
      shooter.id,
      { type: "shoot", sequence: 2, weapon: "ak74m", direction },
      start + 90,
    );
    const second = room.shoot(
      shooter.id,
      { type: "shoot", sequence: 3, weapon: "ak74m", direction },
      start + 95,
    );

    expect(first[0]).toMatchObject({ type: "hit", damage: 33 });
    expect(tooFast).toEqual([]);
    expect(second[0]).toMatchObject({ type: "hit", damage: 33 });
  });

  it("applies AWP bolt cadence and authoritative damage", () => {
    const room = new GameRoom("test");
    const shooter = room.addPlayer("AWP玩家");
    const target = room.addPlayer("目标");
    const direction = {
      x: target.position.x - shooter.position.x,
      y: target.position.y + 1 - (shooter.position.y + 1.55),
      z: target.position.z - shooter.position.z,
    };
    const start = 50_000;

    const first = room.shoot(
      shooter.id,
      { type: "shoot", sequence: 1, weapon: "awp", direction },
      start,
    );
    const tooFast = room.shoot(
      shooter.id,
      { type: "shoot", sequence: 2, weapon: "awp", direction },
      start + 1_200,
    );
    const second = room.shoot(
      shooter.id,
      { type: "shoot", sequence: 3, weapon: "awp", direction },
      start + 1_250,
    );

    expect(first[0]).toMatchObject({ type: "hit", damage: 85 });
    expect(tooFast).toEqual([]);
    expect(second.some((event) => event.type === "death")).toBe(true);
  });

  it("aggregates deterministic Shotgun01 pellets and enforces cadence", () => {
    const room = new GameRoom("test");
    const shooter = room.addPlayer("霰弹枪玩家");
    const target = room.addPlayer("目标");
    const direction = {
      x: target.position.x - shooter.position.x,
      y: target.position.y + 1 - (shooter.position.y + 1.55),
      z: target.position.z - shooter.position.z,
    };
    const start = 60_000;

    const first = room.shoot(
      shooter.id,
      { type: "shoot", sequence: 1, weapon: "shotgun01", direction },
      start,
    );
    const tooFast = room.shoot(
      shooter.id,
      { type: "shoot", sequence: 2, weapon: "shotgun01", direction },
      start + 800,
    );
    const second = room.shoot(
      shooter.id,
      { type: "shoot", sequence: 3, weapon: "shotgun01", direction },
      start + 850,
    );

    expect(first[0]).toMatchObject({
      type: "hit",
      damage: 50,
      targetHealth: 50,
    });
    expect(tooFast).toEqual([]);
    expect(second.some((event) => event.type === "death")).toBe(true);
  });

  it("enforces the recovered knife range and authoritative damage", () => {
    const farRoom = new GameRoom("pyramid");
    const farShooter = farRoom.addPlayer("远距持刀者");
    const farTarget = farRoom.addPlayer("远距目标");
    const farDirection = {
      x: farTarget.position.x - farShooter.position.x,
      y: farTarget.position.y + 1 - (farShooter.position.y + 1.55),
      z: farTarget.position.z - farShooter.position.z,
    };
    expect(
      farRoom.shoot(
        farShooter.id,
        { type: "shoot", sequence: 1, weapon: "knife", direction: farDirection },
        65_000,
      ),
    ).toEqual([
      expect.objectContaining({ type: "action", weapon: "knife" }),
    ]);

    const closeRoom = new GameRoom("pyramid");
    const closeShooter = closeRoom.addPlayer("近距持刀者");
    const closeTarget = closeRoom.addPlayer("近距目标");
    const desiredPosition = {
      x: closeShooter.position.x + 1.4,
      y: closeShooter.position.y,
      z: closeShooter.position.z,
    };
    for (let sequence = 1; sequence <= 5; sequence += 1) {
      closeRoom.applyInput(
        closeTarget.id,
        {
          type: "input",
          sequence,
          moveX: 0,
          moveZ: 0,
          jump: false,
          yaw: 0,
          pitch: 0,
          position: desiredPosition,
        },
        65_000 + sequence * 250,
      );
    }
    const snapshot = closeRoom.tick(67_000);
    expect(snapshot.type).toBe("snapshot");
    if (snapshot.type !== "snapshot") return;
    const movedTarget = snapshot.players.find(
      (player) => player.id === closeTarget.id,
    )!;
    const closeDirection = {
      x: movedTarget.position.x - closeShooter.position.x,
      y: movedTarget.position.y + 1 - (closeShooter.position.y + 1.55),
      z: movedTarget.position.z - closeShooter.position.z,
    };
    expect(
      closeRoom.shoot(
        closeShooter.id,
        { type: "shoot", sequence: 1, weapon: "knife", direction: closeDirection },
        68_000,
      )[0],
    ).toMatchObject({ type: "hit", damage: 50, targetHealth: 50 });
  });

  it("allows one recovered grenade per life and applies delayed radial damage", () => {
    const room = new GameRoom("pyramid");
    const thrower = room.addPlayer("投掷者");
    const target = room.addPlayer("目标");
    const direction = {
      x: target.position.x - thrower.position.x,
      y: 0,
      z: target.position.z - thrower.position.z,
    };
    const start = 70_000;
    const thrown = room.throwGrenade(
      thrower.id,
      { type: "grenade", sequence: 1, direction },
      start,
    );
    const duplicate = room.throwGrenade(
      thrower.id,
      { type: "grenade", sequence: 2, direction },
      start + 100,
    );

    expect(thrown[0]).toMatchObject({
      type: "grenade",
      throwerId: thrower.id,
      explodesAt: start + 3_000,
    });
    expect(thrown[1]).toMatchObject({
      type: "action",
      playerId: thrower.id,
      sequence: 1,
      weapon: "grenade",
      action: "throw",
    });
    expect(duplicate).toEqual([]);
    expect(room.drainEvents(start + 2_999)).toEqual([]);

    const exploded = room.drainEvents(start + 3_000);
    expect(exploded[0]).toMatchObject({ type: "explosion", radius: 6 });
    expect(
      exploded.some(
        (event) =>
          event.type === "hit" &&
          event.targetId === target.id &&
          event.damage >= 25,
      ),
    ).toBe(true);
  });

  it("replenishes the recovered grenade after a respawn", () => {
    const room = new GameRoom("pyramid");
    const thrower = room.addPlayer("投掷者");
    const target = room.addPlayer("目标");
    const start = 80_000;

    expect(
      room.throwGrenade(
        thrower.id,
        {
          type: "grenade",
          sequence: 1,
          direction: { x: 1, y: 0, z: 0 },
        },
        start,
      ),
    ).toHaveLength(2);

    const direction = {
      x: thrower.position.x - target.position.x,
      y: thrower.position.y + 1 - (target.position.y + 1.55),
      z: thrower.position.z - target.position.z,
    };
    room.shoot(
      target.id,
      { type: "shoot", sequence: 1, weapon: "awp", direction },
      start + 1,
    );
    room.shoot(
      target.id,
      { type: "shoot", sequence: 2, weapon: "awp", direction },
      start + 1_251,
    );
    room.tick(start + 4_251);

    expect(
      room.throwGrenade(
        thrower.id,
        {
          type: "grenade",
          sequence: 2,
          direction: { x: 1, y: 0, z: 0 },
        },
        start + 4_252,
      ),
    ).toHaveLength(2);
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

  it("ends the round at the authoritative deadline and rejects combat", () => {
    const room = new GameRoom("pyramid", 1_000, 1_000);
    const shooter = room.addPlayer("回合测试者");
    expect(room.tick(1_999)).toMatchObject({
      type: "snapshot",
      roundState: "playing",
      roundEndsAt: 2_000,
    });
    expect(room.tick(2_000)).toMatchObject({
      type: "snapshot",
      roundState: "ended",
      roundEndsAt: 2_000,
    });
    expect(room.shoot(
      shooter.id,
      {
        type: "shoot",
        sequence: 1,
        weapon: "m4a1",
        direction: { x: 0, y: 0, z: 1 },
      },
      2_001,
    )).toEqual([]);
  });

  it("accepts collision-resolved client movement without simulating through walls", () => {
    const room = new GameRoom("pyramid");
    const player = room.addPlayer("玩家一");
    room.applyInput(
      player.id,
      {
        type: "input",
        sequence: 1,
        moveX: 0,
        moveZ: 1,
        jump: false,
        yaw: 0,
        pitch: 0,
        position: {
          x: player.position.x,
          y: player.position.y,
          z: player.position.z + 0.2,
        },
      },
      1_000,
    );

    const snapshot = room.tick(1_050);
    expect(snapshot.type).toBe("snapshot");
    if (snapshot.type !== "snapshot") return;
    const updated = snapshot.players.find((item) => item.id === player.id)!;
    expect(updated.position.z).toBeCloseTo(player.position.z + 0.2);
  });

  it("clamps impossible client position jumps", () => {
    const room = new GameRoom("pyramid");
    const player = room.addPlayer("玩家一");
    room.applyInput(
      player.id,
      {
        type: "input",
        sequence: 1,
        moveX: 0,
        moveZ: 0,
        jump: false,
        yaw: 0,
        pitch: 0,
        position: {
          x: player.position.x + 100,
          y: player.position.y,
          z: player.position.z,
        },
      },
      1_000,
    );

    const snapshot = room.tick(1_050);
    expect(snapshot.type).toBe("snapshot");
    if (snapshot.type !== "snapshot") return;
    const updated = snapshot.players.find((item) => item.id === player.id)!;
    expect(updated.position.x - player.position.x).toBeLessThan(0.5);
  });
});
