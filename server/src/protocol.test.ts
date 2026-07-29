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
