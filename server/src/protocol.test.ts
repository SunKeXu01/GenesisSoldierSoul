import { describe, expect, it } from "vitest";
import { parseClientMessage } from "./protocol.js";

describe("parseClientMessage", () => {
  it("normalizes room names and clamps movement input", () => {
    expect(
      parseClientMessage(
        JSON.stringify({
          type: "join",
          name: "  玩家一  ",
          room: "  TEAM-A  ",
        }),
      ),
    ).toEqual({ type: "join", name: "玩家一", room: "team-a" });

    expect(
      parseClientMessage(
        JSON.stringify({
          type: "input",
          sequence: 2,
          moveX: 5,
          moveZ: -7,
          yaw: 90,
          pitch: 200,
        }),
      ),
    ).toMatchObject({ moveX: 1, moveZ: -1, pitch: 89 });

    expect(
      parseClientMessage(
        JSON.stringify({
          type: "shoot",
          sequence: 4,
          weapon: "rifle",
          direction: { x: 1, y: 0, z: 0 },
        }),
      ),
    ).toMatchObject({ weapon: "rifle" });
    expect(
      parseClientMessage(
        JSON.stringify({
          type: "shoot",
          sequence: 5,
          weapon: "m16",
          direction: { x: 1, y: 0, z: 0 },
        }),
      ),
    ).toMatchObject({ weapon: "m16" });
    expect(
      parseClientMessage(
        JSON.stringify({
          type: "shoot",
          sequence: 6,
          weapon: "ak74m",
          direction: { x: 1, y: 0, z: 0 },
        }),
      ),
    ).toMatchObject({ weapon: "ak74m" });
    expect(
      parseClientMessage(
        JSON.stringify({
          type: "shoot",
          sequence: 7,
          weapon: "awp",
          direction: { x: 1, y: 0, z: 0 },
        }),
      ),
    ).toMatchObject({ weapon: "awp" });
    expect(
      parseClientMessage(
        JSON.stringify({
          type: "shoot",
          sequence: 8,
          weapon: "shotgun01",
          direction: { x: 1, y: 0, z: 0 },
        }),
      ),
    ).toMatchObject({ weapon: "shotgun01" });
    expect(
      parseClientMessage(
        JSON.stringify({
          type: "grenade",
          sequence: 9,
          direction: { x: 0, y: 0, z: 1 },
        }),
      ),
    ).toMatchObject({ type: "grenade", sequence: 9 });
    expect(
      parseClientMessage(
        JSON.stringify({
          type: "action",
          sequence: 10,
          weapon: "pistol",
          action: "reload",
        }),
      ),
    ).toMatchObject({ type: "action", weapon: "pistol", action: "reload" });
    expect(
      parseClientMessage(
        JSON.stringify({
          type: "action",
          sequence: 11,
          weapon: "shotgun01",
          action: "reload",
        }),
      ),
    ).toMatchObject({ type: "action", weapon: "shotgun01", action: "reload" });

    expect(
      parseClientMessage(
        JSON.stringify({
          type: "input",
          sequence: 3,
          moveX: 0,
          moveZ: 1,
          jump: false,
          yaw: 0,
          pitch: 0,
          position: { x: 1, y: 2, z: 3 },
        }),
      ),
    ).toMatchObject({ position: { x: 1, y: 2, z: 3 } });
  });

  it("rejects malformed and oversized fields", () => {
    expect(parseClientMessage("{")).toBeUndefined();
    expect(
      parseClientMessage(
        JSON.stringify({
          type: "join",
          name: "x".repeat(21),
        }),
      ),
    ).toBeUndefined();
    expect(
      parseClientMessage(
        JSON.stringify({
          type: "shoot",
          sequence: 1,
          weapon: "rocket",
          direction: { x: 1, y: 0, z: 0 },
        }),
      ),
    ).toBeUndefined();
  });
});
