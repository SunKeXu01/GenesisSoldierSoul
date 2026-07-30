import { createReadStream, statSync } from "node:fs";
import { createServer, type ServerResponse } from "node:http";
import { extname, resolve, sep } from "node:path";
import { WebSocket, WebSocketServer } from "ws";
import { GameRoom, TICK_RATE } from "./game.js";
import { parseClientMessage, type ServerMessage } from "./protocol.js";

const port = readPort(process.env.PORT, 8080);
const webRoot = process.env.WEB_ROOT
  ? resolve(process.env.WEB_ROOT)
  : undefined;
const allowedOrigins = new Set(
  (process.env.ALLOWED_ORIGINS ?? "")
    .split(",")
    .map((origin) => origin.trim())
    .filter(Boolean),
);
const rooms = new Map<string, GameRoom>();
const sessions = new Map<
  WebSocket,
  { room: GameRoom; playerId: string }
>();

const httpServer = createServer((request, response) => {
  if (request.url === "/health") {
    response.writeHead(200, { "content-type": "application/json" });
    response.end(
      JSON.stringify({
        ok: true,
        rooms: rooms.size,
        players: sessions.size,
        uptime: process.uptime(),
      }),
    );
    return;
  }

  if (webRoot && request.url && serveStatic(webRoot, request.url, response)) {
    return;
  }

  response.writeHead(404, { "content-type": "application/json" });
  response.end(JSON.stringify({ error: "not_found" }));
});

const websocketServer = new WebSocketServer({
  server: httpServer,
  path: "/game",
  maxPayload: 16 * 1024,
  verifyClient: ({ origin }, accept) => {
    accept(allowedOrigins.size === 0 || allowedOrigins.has(origin));
  },
});

websocketServer.on("connection", (socket) => {
  let joined = false;
  let messageCount = 0;
  let messageWindowStartedAt = Date.now();
  const joinTimeout = setTimeout(() => socket.close(4001, "join_timeout"), 5000);

  socket.on("message", (payload, isBinary) => {
    const now = Date.now();
    if (now - messageWindowStartedAt >= 1000) {
      messageWindowStartedAt = now;
      messageCount = 0;
    }
    messageCount += 1;
    if (messageCount > 120) {
      socket.close(4004, "rate_limited");
      return;
    }

    const text = rawDataToString(payload);
    if (isBinary || Buffer.byteLength(text) > 16 * 1024) {
      socket.close(4002, "invalid_payload");
      return;
    }

    const message = parseClientMessage(text);
    if (!message) {
      send(socket, {
        type: "error",
        code: "invalid_message",
        message: "消息格式无效",
      });
      return;
    }

    if (!joined) {
      if (message.type !== "join") {
        send(socket, {
          type: "error",
          code: "join_required",
          message: "请先加入房间",
        });
        return;
      }

      const roomId = sanitizeRoom(message.room ?? "public");
      const room = rooms.get(roomId) ?? new GameRoom(roomId);
      rooms.set(roomId, room);
      if (room.full) {
        socket.close(4003, "room_full");
        return;
      }

      const player = room.addPlayer(message.name);
      sessions.set(socket, { room, playerId: player.id });
      joined = true;
      clearTimeout(joinTimeout);
      send(socket, {
        type: "welcome",
        id: player.id,
        room: room.id,
        tickRate: TICK_RATE,
        serverTime: Date.now(),
      });
      return;
    }

    const session = sessions.get(socket);
    if (!session || message.type === "join") return;
    if (message.type === "input") {
      session.room.applyInput(session.playerId, message, now);
      return;
    }
    for (const event of session.room.shoot(
      session.playerId,
      message,
      Date.now(),
    )) {
      broadcastRoom(session.room, event);
    }
  });

  socket.on("close", () => {
    clearTimeout(joinTimeout);
    const session = sessions.get(socket);
    if (!session) return;
    session.room.removePlayer(session.playerId);
    sessions.delete(socket);
    if (session.room.size === 0) rooms.delete(session.room.id);
  });
});

const tickTimer = setInterval(() => {
  const now = Date.now();
  for (const room of rooms.values()) {
    broadcastRoom(room, room.tick(now));
  }
}, 1000 / TICK_RATE);

tickTimer.unref();

const heartbeatTimer = setInterval(() => {
  for (const socket of websocketServer.clients) {
    if (socket.readyState === WebSocket.OPEN) socket.ping();
  }
}, 30_000);
heartbeatTimer.unref();

httpServer.listen(port, "0.0.0.0", () => {
  console.log(`Genesis Soldier Soul server listening on 0.0.0.0:${port}`);
});

function send(socket: WebSocket, message: ServerMessage): void {
  if (socket.readyState === WebSocket.OPEN) {
    socket.send(JSON.stringify(message));
  }
}

function broadcastRoom(room: GameRoom, message: ServerMessage): void {
  const encoded = JSON.stringify(message);
  for (const [socket, session] of sessions) {
    if (session.room === room && socket.readyState === WebSocket.OPEN) {
      socket.send(encoded);
    }
  }
}

function sanitizeRoom(value: string): string {
  const room = value.replace(/[^a-z0-9_-]/g, "").slice(0, 24);
  return room || "public";
}

function readPort(value: string | undefined, fallback: number): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed > 0 && parsed <= 65535
    ? parsed
    : fallback;
}

function rawDataToString(
  payload: Buffer | ArrayBuffer | Buffer[],
): string {
  if (Buffer.isBuffer(payload)) return payload.toString("utf8");
  if (Array.isArray(payload)) return Buffer.concat(payload).toString("utf8");
  return Buffer.from(payload).toString("utf8");
}

function serveStatic(
  root: string,
  requestUrl: string,
  response: ServerResponse,
): boolean {
  let pathname: string;
  try {
    pathname = decodeURIComponent(new URL(requestUrl, "http://localhost").pathname);
  } catch {
    return false;
  }

  const relativePath = pathname === "/" ? "index.html" : `.${pathname}`;
  const filePath = resolve(root, relativePath);
  if (filePath !== root && !filePath.startsWith(`${root}${sep}`)) {
    return false;
  }

  let stat;
  try {
    stat = statSync(filePath);
  } catch {
    return false;
  }
  if (!stat.isFile()) return false;

  response.writeHead(200, staticHeaders(filePath));
  createReadStream(filePath).pipe(response);
  return true;
}

function staticHeaders(filePath: string): Record<string, string> {
  const headers: Record<string, string> = {
    // Unity emits stable filenames (WebGL.data/WebGL.wasm) for every rebuild.
    // Revalidation is required during restoration so browsers do not keep an
    // older broken scene package under the same URL.
    "cache-control": "no-cache",
  };

  if (filePath.endsWith(".wasm.unityweb")) {
    headers["content-type"] = "application/wasm";
    headers["content-encoding"] = "br";
    return headers;
  }
  if (filePath.endsWith(".js.unityweb")) {
    headers["content-type"] = "application/javascript; charset=utf-8";
    headers["content-encoding"] = "br";
    return headers;
  }
  if (filePath.endsWith(".data.unityweb")) {
    headers["content-type"] = "application/octet-stream";
    headers["content-encoding"] = "br";
    return headers;
  }

  const mimeTypes: Record<string, string> = {
    ".html": "text/html; charset=utf-8",
    ".js": "application/javascript; charset=utf-8",
    ".wasm": "application/wasm",
    ".data": "application/octet-stream",
    ".css": "text/css; charset=utf-8",
    ".png": "image/png",
    ".ico": "image/x-icon",
  };
  headers["content-type"] =
    mimeTypes[extname(filePath).toLowerCase()] ?? "application/octet-stream";
  return headers;
}
