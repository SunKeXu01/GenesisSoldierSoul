import readline from "node:readline";
import WebSocket from "ws";

const room = (process.argv[2] ?? "public").trim().toLowerCase();
const url = process.argv[3] ?? "ws://127.0.0.1:8080/game";
const socket = new WebSocket(url);
let sequence = 0;
let actorId = "";
let actorPosition;
let referencePosition;
let desiredPosition;
let players = [];
let actorYaw = 0;
let referenceIndex = 0;
let referenceYaw = 0;
let jumpTraceUntil = 0;
let referenceTraceUntil = 0;

const send = (message) => {
  if (socket.readyState !== WebSocket.OPEN) return;
  socket.send(JSON.stringify(message));
};

const input = ({ jump = false, moveX = 0, moveZ = 0 } = {}) => {
  if (!actorPosition) return;
  send({
    type: "input",
    sequence: ++sequence,
    moveX,
    moveZ,
    jump,
    yaw: actorYaw,
    pitch: 0,
    position: desiredPosition ?? actorPosition,
  });
};

const action = (weapon, name = "equip") => {
  send({ type: "action", sequence: ++sequence, weapon, action: name });
};

const shoot = (weapon) => {
  send({
    type: "shoot",
    sequence: ++sequence,
    weapon,
    direction: { x: 0, y: 0, z: -1 },
  });
};

const shootReference = (weapon) => {
  if (!actorPosition || !referencePosition) return;
  const origin = {
    x: actorPosition.x,
    y: actorPosition.y + 1.55,
    z: actorPosition.z,
  };
  send({
    type: "shoot",
    sequence: ++sequence,
    weapon,
    direction: {
      x: referencePosition.x - origin.x,
      y: referencePosition.y + 1 - origin.y,
      z: referencePosition.z - origin.z,
    },
  });
};

const jump = () => {
  // Stop pinning the actor to its staging coordinate while the authoritative
  // server integrates the jump. Re-pin only after the airborne window so the
  // observer receives genuine vertical snapshots instead of an immediate y=0
  // correction on every tick.
  desiredPosition = undefined;
  jumpTraceUntil = Date.now() + 1250;
  send({
    type: "input",
    sequence: ++sequence,
    moveX: 0,
    moveZ: 0,
    jump: true,
    yaw: actorYaw,
    pitch: 0,
  });
  setTimeout(() => {
    if (!actorPosition) return;
    desiredPosition = {
      x: actorPosition.x,
      y: actorPosition.y,
      z: actorPosition.z,
    };
    input();
  }, 1100);
};

const runCommand = (raw) => {
  const command = raw.trim().toLowerCase();
  if (!command) return;
  if (["rifle", "m4", "m4a1"].includes(command)) action("rifle");
  else if (["pistol", "m9"].includes(command)) action("pistol");
  else if (command === "knife") action("knife");
  else if (["grenade", "nade"].includes(command)) action("grenade");
  else if (command === "reload") action("rifle", "reload");
  else if (command === "reload-pistol") action("pistol", "reload");
  else if (command === "fire") shoot("m4a1");
  else if (command === "burst") {
    for (let index = 0; index < 10; index += 1)
      setTimeout(() => shoot("m4a1"), index * 110);
  }
  else if (command === "fire-pistol") shoot("pistol");
  else if (command === "fire-knife") shoot("knife");
  else if (command === "hit-ref") shootReference("pistol");
  else if (command === "kill-ref") {
    // Three authoritative pistol hits are slower than a synthetic one-shot but
    // exercise the production cadence/damage path and proved more reliable for
    // browser screenshot timing than delayed AWP shots.
    shootReference("pistol");
    setTimeout(() => shootReference("pistol"), 260);
    setTimeout(() => shootReference("pistol"), 520);
  }
  else if (command === "throw") {
    action("grenade");
    send({
      type: "grenade",
      sequence: ++sequence,
      direction: { x: 0, y: 0.25, z: -1 },
    });
  } else if (command === "jump") jump();
  else if (command === "jump-delayed") setTimeout(jump, 800);
  else if (command === "jump-delayed-long") setTimeout(jump, 2500);
  else if (["front", "front-mid", "front-far", "front-high", "front-right"].includes(command)) {
    if (!referencePosition || !actorPosition) return;
    const distance = command === "front"
      ? 2.2
      : command === "front-mid" || command === "front-right"
        ? 3.5
        : 7;
    const radians = referenceYaw * Math.PI / 180;
    desiredPosition = {
      x: referencePosition.x + Math.sin(radians) * distance
        + (command === "front-right" ? Math.cos(radians) * 2 : 0),
      y: referencePosition.y + (command === "front-high" ? 2 : 0),
      z: referencePosition.z + Math.cos(radians) * distance
        - (command === "front-right" ? Math.sin(radians) * 2 : 0),
    };
    actorYaw = (referenceYaw + 180) % 360;
    input();
  }
  else if (["rear", "rear-far", "rear-high"].includes(command)) {
    if (!referencePosition || !actorPosition) return;
    const distance = command === "rear" ? 2.2 : 7;
    const radians = referenceYaw * Math.PI / 180;
    desiredPosition = {
      x: referencePosition.x - Math.sin(radians) * distance,
      y: referencePosition.y + (command === "rear-high" ? 2 : 0),
      z: referencePosition.z - Math.cos(radians) * distance,
    };
    actorYaw = referenceYaw;
    input();
  }
  else if (command === "first" || command === "second") {
    referenceIndex = command === "second" ? 1 : 0;
    console.log(`reference player index=${referenceIndex}`);
  }
  else if (command === "left") input({ moveX: -1 });
  else if (command === "right") input({ moveX: 1 });
  else if (command === "forward") input({ moveZ: 1 });
  else if (command === "back") input({ moveZ: -1 });
  else if (command === "face-away") {
    actorYaw = 0;
    input();
  } else if (command === "face-camera") {
    actorYaw = 180;
    input();
  } else if (command === "face-side") {
    actorYaw = (referenceYaw + 90) % 360;
    input();
  } else if (command === "where") {
    console.log(JSON.stringify({ actorPosition, referencePosition, desiredPosition, players }));
  } else if (command === "trace-ref") {
    referenceTraceUntil = Date.now() + 1600;
  } else if (command === "quit") socket.close();
  else console.log(`unknown command: ${command}`);
};

socket.on("open", () => {
  send({ type: "join", name: "QA-ACTOR", room });
});

socket.on("message", (payload) => {
  const message = JSON.parse(payload.toString());
  if (message.type === "welcome") {
    actorId = message.id;
    console.log(`joined room=${room} id=${actorId}`);
    return;
  }
  if (message.type !== "snapshot") return;
  players = message.players.map((player) => ({
    id: player.id,
    name: player.name,
    position: player.position,
    yaw: player.yaw,
    alive: player.alive,
  }));
  const actor = message.players.find((player) => player.id === actorId);
  if (!actor) return;
  const references = message.players.filter((player) => player.id !== actorId);
  const reference = references[Math.min(referenceIndex, references.length - 1)];
  if (reference) {
    referencePosition = reference.position;
    referenceYaw = reference.yaw;
    if (Date.now() < referenceTraceUntil)
      console.log(`reference-y=${referencePosition.y.toFixed(3)}`);
  }
  actorPosition = actor.position;
  if (Date.now() < jumpTraceUntil)
    console.log(`jump-y=${actorPosition.y.toFixed(3)}`);
  if (sequence === 0 || desiredPosition) input();
  if (actor.alive === false)
    console.log(`actor dead; respawn pending health=${actor.health}`);
});

socket.on("close", (code, reason) => {
  console.log(`closed code=${code} reason=${reason.toString()}`);
  process.exit(0);
});

socket.on("error", (error) => {
  console.error(error.message);
  process.exitCode = 1;
});

const terminal = readline.createInterface({
  input: process.stdin,
  output: process.stdout,
  terminal: false,
});
terminal.on("line", runCommand);
