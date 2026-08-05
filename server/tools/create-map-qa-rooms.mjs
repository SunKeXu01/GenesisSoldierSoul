import { writeFile } from "node:fs/promises";

const baseUrl = (process.argv[2] ?? "http://127.0.0.1:8080").replace(/\/$/, "");
const outputPath = process.argv[3];

const maps = [
  ["pyramid", "MapQA Pyramid"],
  ["newconstructionsite", "MapQA NewConstruction"],
  ["biochemicaltown", "MapQA Biochemical"],
  ["classicconstructionsite", "MapQA Classic"],
  ["steelfactory", "MapQA Steel"],
  ["icefiremaze", "MapQA IceFire"],
  ["radiationdistrict", "MapQA Radiation"],
];

const listResponse = await fetch(`${baseUrl}/api/rooms`);
if (!listResponse.ok) throw new Error(`room list failed: ${listResponse.status}`);
const list = await listResponse.json();
const rooms = Array.isArray(list.rooms) ? list.rooms : [];
const byMap = new Map(rooms.map((room) => [room.map, room]));

for (const [map, name] of maps) {
  if (byMap.has(map)) continue;
  const response = await fetch(`${baseUrl}/api/rooms`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ name, map }),
  });
  if (!response.ok) throw new Error(`create ${map} failed: ${response.status}`);
  const payload = await response.json();
  byMap.set(map, payload.room);
}

const report = JSON.stringify({
  rooms: maps.map(([map]) => byMap.get(map)),
}, null, 2);
if (outputPath) await writeFile(outputPath, report + "\n", "utf8");
console.log(report);
