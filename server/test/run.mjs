// Local tests for the Eorzea Milkers Network worker, using Node's built-in SQLite as a stand-in for D1.
//   node --no-warnings server/test/run.mjs
import { DatabaseSync } from "node:sqlite";
import { readFileSync } from "node:fs";
import { webcrypto, createHash } from "node:crypto";
import assert from "node:assert/strict";

globalThis.crypto ??= webcrypto;
const here = new URL(".", import.meta.url);
const worker = (await import(new URL("../src/index.js", here))).default;

// --- minimal D1 on top of node:sqlite
const sqlite = new DatabaseSync(":memory:");
sqlite.exec(readFileSync(new URL("../schema.sql", here), "utf8"));
class Stmt {
  constructor(sql, args = []) { this.sql = sql; this.args = args; }
  bind(...a) { return new Stmt(this.sql, a); }
  async first() { return sqlite.prepare(this.sql).get(...this.args) ?? null; }
  async all() { return { results: sqlite.prepare(this.sql).all(...this.args) }; }
  async run() { const r = sqlite.prepare(this.sql).run(...this.args); return { meta: { changes: r.changes } }; }
}
const env = {
  CONTEST: "off",   // the weekend contest only when a test turns it on
  DB: {
    prepare: sql => new Stmt(sql),
    batch: async stmts => {
      sqlite.exec("BEGIN");
      try { const out = []; for (const s of stmts) out.push(await s.run()); sqlite.exec("COMMIT"); return out; }
      catch (e) { sqlite.exec("ROLLBACK"); throw e; }
    },
  },
};

// --- helpers
const hex = n => [...webcrypto.getRandomValues(new Uint8Array(n))].map(b => b.toString(16).padStart(2, "0")).join("");
const sha = s => createHash("sha256").update(s).digest("hex");
const player = name => ({ id: hex(16), key: hex(32), name, hash: sha(`${name.toLowerCase()}@40`) });
async function call(p, method, path, body) {
  const headers = { "content-type": "application/json" };
  if (p) { headers["X-VD-Id"] = p.id; headers["X-VD-Key"] = p.key; }
  const res = await worker.fetch(new Request("https://x" + path, { method, headers, body: body ? JSON.stringify(body) : undefined }), env);
  return { status: res.status, data: await res.json() };
}
const beat = (p, extra = {}) => call(p, "POST", "/v1/heartbeat", { charHash: p.hash, version: "0.3.0.0", ...extra });
let passed = 0;
async function test(name, fn) {
  try { await fn(); passed++; console.log("ok  ", name); }
  catch (e) { console.log("FAIL", name, "\n   ", e.message, (e.stack || "").split("\n").find(l => l.includes("run.mjs:")) || ""); process.exitCode = 1; }
}

const alice = player("Alice Cow"), bob = player("Bob Hand"), carl = player("Carl Bull"), dana = player("Dana Visitor"), erin = player("Erin Owner");
const HOUSE = "1234567890123", HOUSE2 = "1234567890999", ELSEWHERE = "9999";
const dairyBody = (extra = {}) => ({ houseId: HOUSE, name: "Rose Hollow", isPublic: false, showOwner: true, ownerName: "Alice Cow",
  world: 40, worldName: "Gilgamesh", dcName: "Aether", district: "Mist", districtKind: 8, ward: 5, plot: 12, role: "cow", ...extra });
let dairy, orderId;

await test("rejects requests without an id", async () => {
  assert.equal((await call(null, "POST", "/v1/heartbeat", {})).status, 401);
});

await test("heartbeats count players online", async () => {
  for (const p of [alice, bob, carl, dana, erin]) await beat(p);
  assert.equal((await beat(alice)).data.online, 5);
});

await test("a stolen id with the wrong key is refused", async () => {
  assert.equal((await call({ id: alice.id, key: hex(32) }, "POST", "/v1/heartbeat", {})).status, 401);
});

await test("owner registers a dairy and joins it", async () => {
  const r = await call(alice, "PUT", "/v1/dairy", dairyBody());
  assert.equal(r.status, 200);
  dairy = r.data.id;
  const hb = await beat(alice);
  assert.deepEqual(hb.data.membership, { dairyId: dairy, dairy: "Rose Hollow", role: "cow", roles: ["cow"], isOwner: true, level: 1 });
});

await test("a dairy's data center comes from its own world, not where the owner is standing", async () => {
  const listed = async () => (await call(alice, "GET", "/v1/dairies")).data.dairies.find(d => d.mine);
  const before = await listed();
  // An older plugin saving the listing while visiting another data center: names are kept
  await call(alice, "PUT", "/v1/dairy", dairyBody({ worldName: "Hyperion", dcName: "Primal" }));
  assert.equal((await listed()).dcName, before.dcName);
  // Names for the dairy's own world are taken
  await call(alice, "PUT", "/v1/dairy", dairyBody({ worldName: "Hyperion", dcName: "Primal", namesFor: 40 }));
  assert.equal((await listed()).dcName, "Primal");
  await call(alice, "PUT", "/v1/dairy", dairyBody({ namesFor: 40 }));
  assert.equal((await listed()).dcName, "Aether");
  // A known world's data center always comes from the world list, whatever was sent or saved
  await call(alice, "PUT", "/v1/dairy", dairyBody({ worldName: "Lich", dcName: "Chaos", namesFor: 40 }));
  assert.equal((await listed()).dcName, "Light");
  await call(alice, "PUT", "/v1/dairy", dairyBody({ namesFor: 40 }));
  assert.equal((await listed()).dcName, "Aether");
});

await test("an address can only be registered once", async () => {
  const r = await call(erin, "PUT", "/v1/dairy", dairyBody({ name: "Thief Farm" }));
  assert.equal(r.status, 409);
});

await test("an owner can't register a second address", async () => {
  assert.equal((await call(alice, "PUT", "/v1/dairy", dairyBody({ houseId: HOUSE2 }))).status, 409);
});

await test("private dairy hides its address", async () => {
  const seen = (await call(dana, "GET", "/v1/dairies")).data.dairies.find(d => d.id === dairy);
  assert.equal(seen.isPublic, false);
  assert.equal(seen.ward, null);
  assert.equal(seen.houseId, null);
  assert.equal(seen.owner, "Alice Cow");
});

await test("cows can't invite", async () => {
  assert.equal((await call(dana, "POST", "/v1/invites", { toHash: bob.hash, toName: "Bob Hand", role: "cow" })).status, 403);
});

await test("owner invites a farmer; invite shows up for them; they accept", async () => {
  const r = await call(alice, "POST", "/v1/invites", { toHash: bob.hash, toName: "Bob Hand", role: "farmer" });
  assert.equal(r.status, 200);
  const hb = await beat(bob);
  assert.equal(hb.data.invites.length, 1);
  assert.equal(hb.data.invites[0].role, "farmer");
  assert.equal(hb.data.invites[0].dairy, "Rose Hollow");
  assert.equal((await call(dana, "POST", `/v1/invites/${r.data.id}/accept`, { charHash: dana.hash })).status, 404, "not for Dana");
  const a = await call(bob, "POST", `/v1/invites/${r.data.id}/accept`, { charHash: bob.hash, name: "Bob Hand", worldName: "Gilgamesh" });
  assert.equal(a.status, 200);
  assert.equal(a.data.role, "farmer");
  assert.equal((await beat(bob)).data.membership.role, "farmer");
});

await test("farmers can invite cows, bulls and farmers", async () => {
  const f = await call(bob, "POST", "/v1/invites", { toHash: dana.hash, toName: "Dana", role: "farmer" });
  assert.equal(f.status, 200);
  assert.equal((await call(bob, "DELETE", `/v1/invites/${f.data.id}`)).status, 200);
  const r = await call(bob, "POST", "/v1/invites", { toHash: carl.hash, toName: "Carl Bull", role: "bull" });
  assert.equal(r.status, 200);
  await call(carl, "POST", `/v1/invites/${r.data.id}/accept`, { charHash: carl.hash, name: "Carl Bull" });
  assert.equal((await beat(carl)).data.membership.role, "bull");
});

await test("can't invite someone already in the dairy", async () => {
  assert.equal((await call(alice, "POST", "/v1/invites", { toHash: carl.hash, toName: "Carl Bull", role: "cow" })).status, 409);
});

await test("members see the roster with roles and shared status", async () => {
  await beat(carl, { status: { milk: 3.5, cap: 8, grade: 0 } });
  const r = await call(bob, "GET", "/v1/dairy/mine");
  const names = r.data.members.map(m => `${m.name}:${m.role}:${m.isOwner}`);
  assert.deepEqual(names, ["Alice Cow:cow:true", "Bob Hand:farmer:false", "Carl Bull:bull:false"]);
  assert.equal(r.data.members[2].status.milk, 3.5);
  assert.equal(r.data.you.role, "farmer");
});

await test("notices: a cow ready to be bred again pops up for the rest of her dairy", async () => {
  await beat(alice, { houseId: HOUSE });
  const before = (await beat(bob, { houseId: HOUSE, noticeAfter: 0 })).data.notices.length;
  const sent = await call(alice, "POST", "/v1/dairy/notice", { kind: "breedReady" });
  assert.equal(sent.data.sent, true, JSON.stringify(sent.data));
  assert.equal((await call(alice, "POST", "/v1/dairy/notice", { kind: "breedReady" })).data.sent, false, "once every 15 minutes");
  assert.equal((await call(alice, "POST", "/v1/dairy/notice", { kind: "anything" })).status, 400);
  const n = (await beat(bob, { houseId: HOUSE, noticeAfter: 0 })).data.notices;
  assert.equal(n.length, before + 1);
  const mine = n[n.length - 1];
  assert.equal(mine.kind, "breedReady");
  assert.equal(mine.name, "Alice Cow");
  assert.equal((await beat(bob, { houseId: HOUSE, noticeAfter: mine.id })).data.notices.length, 0, "only new ones");
  assert.equal((await beat(bob, { noticeAfter: 0 })).data.notices.length, 0, "only to those standing in that dairy");
  assert.equal((await beat(alice, { houseId: HOUSE, noticeAfter: 0 })).data.notices.length, 0, "not your own");
  assert.equal((await beat(bob, { houseId: HOUSE })).data.notices.length, 0, "older plugins don't ask");
});

await test("breeding status: seed is kept within 0-100 and only real pregnancy stages pass", async () => {
  await beat(carl, { status: { milk: 3.5, cap: 8, seed: 250, preg: "<b>twins</b>" } });
  let st = (await call(bob, "GET", "/v1/dairy/mine")).data.members[2].status;
  assert.equal(st.seed, 100);
  assert.equal(st.preg, "");
  await beat(carl, { status: { milk: 3.5, cap: 8, seed: 60, preg: "second", pregFather: "Bram <b>Bull</b>" } });
  st = (await call(bob, "GET", "/v1/dairy/mine")).data.members[2].status;
  assert.equal(st.seed, 60);
  assert.equal(st.preg, "second");
  assert.equal(st.pregFather, "Bram bBullb", "the father's name, cleaned");
  await beat(carl, { status: { milk: 3.5, cap: 8, pregFather: "Bram Bull" } });
  assert.equal((await call(bob, "GET", "/v1/dairy/mine")).data.members[2].status.pregFather, "", "no father without a pregnancy");
});

await test("scan: anyone can see a member's bonuses and breeding, unless they say no", async () => {
  await beat(carl, { name: "Carl Bull", worldName: "Gilgamesh" });
  const me = (await call(bob, "GET", "/v1/dairy/mine")).data.members.find(m => m.name === "Carl Bull");
  assert.equal(me.world, "Gilgamesh");
  await beat(carl, { status: { milk: 3, cap: 8, seed: 80, bonuses: [{ n: "Fed", p: 15 }, { n: "<b>x</b>", p: 99999 }], speed: 1.155, bredBy: "Bram", bredPct: 12, bredUntil: 9999999999 } });
  let r = await call(dana, "GET", `/v1/scan/${encodeURIComponent(me.world)}/${encodeURIComponent("carl bull")}`);
  assert.equal(r.status, 200, JSON.stringify(r.data) + " " + me.world);
  assert.equal(r.data.found, true);
  assert.equal(r.data.hidden, false, "anyone by default");
  assert.equal(r.data.dairy, "Rose Hollow");
  assert.deepEqual(r.data.status.bonuses, [{ n: "Fed", p: 15 }, { n: "<b>x</b>", p: 1000 }]);
  assert.equal(r.data.status.speed, 1.16);
  assert.equal(r.data.status.bredBy, "Bram");
  await beat(carl, { status: { milk: 3, cap: 8, scan: "dairy" } });
  r = await call(dana, "GET", `/v1/scan/${encodeURIComponent(me.world)}/Carl%20Bull`);
  assert.equal(r.data.hidden, true, "dairy only: an outsider can't");
  assert.equal(r.data.status, undefined);
  assert.equal((await call(bob, "GET", `/v1/scan/${encodeURIComponent(me.world)}/Carl%20Bull`)).data.hidden, false, "dairy only: a dairy-mate can");
  await beat(carl, { status: { milk: 3, cap: 8, scan: "nobody" } });
  assert.equal((await call(bob, "GET", `/v1/scan/${encodeURIComponent(me.world)}/Carl%20Bull`)).data.hidden, true, "nobody");
  assert.equal((await call(dana, "GET", "/v1/scan/Gilgamesh/Nobody%20Here")).data.found, false);
  await beat(carl, { status: { milk: 3.5, cap: 8, seed: 60 } });
});

await test("who's here: a public dairy lists the players there with roles and levels", async () => {
  const d = (await call(alice, "GET", "/v1/dairies")).data.dairies.find(x => x.name === "Rose Hollow");
  await beat(carl, { houseId: HOUSE, status: { milk: 3, cap: 8, level: 7, bullLevel: 3, farmerLevel: 1 } });
  let r = await call(dana, "GET", `/v1/dairies/${d.id}/here`);
  assert.equal(r.status, d.isPublic ? 200 : 403, JSON.stringify(r.data) + " " + d.id);
  r = await call(alice, "GET", `/v1/dairies/${d.id}/here`);
  assert.equal(r.status, 200, JSON.stringify(r.data));
  const c = r.data.people.find(p => p.name === "Carl Bull");
  assert.ok(c, JSON.stringify(r.data));
  assert.equal(c.member, true);
  assert.ok(c.bullLevel >= 1);
  await beat(carl, { houseId: HOUSE, status: { milk: 3, cap: 8, scan: "nobody" } });
  r = await call(alice, "GET", `/v1/dairies/${d.id}/here`);
  assert.equal(r.data.people.find(p => p.name === "Carl Bull"), undefined, "scan nobody: not named");
  assert.ok(r.data.hidden >= 1);
  await beat(carl, { houseId: HOUSE, status: { milk: 3, cap: 8, scan: "anyone" } });
});

await test("several roles at once: self-add cow/bull, farmer is given, only the owner takes away", async () => {
  let r = await call(carl, "POST", `/v1/members/${carl.id}/roles`, { add: "cow" });
  assert.deepEqual(r.data.roles, ["cow", "bull"], "anyone can add cow to themselves");
  assert.equal((await call(carl, "POST", `/v1/members/${carl.id}/roles`, { add: "farmer" })).status, 403, "can't make yourself a farmer");
  assert.equal((await call(carl, "POST", `/v1/members/${carl.id}/roles`, { remove: "farmer" })).status, 403, "only the owner takes farmer away");
  assert.deepEqual((await call(bob, "POST", `/v1/members/${carl.id}/roles`, { add: "farmer" })).data.roles, ["cow", "farmer", "bull"], "a farmer gives farmer");
  assert.deepEqual((await beat(carl)).data.membership.roles, ["cow", "farmer", "bull"]);
  assert.equal((await call(bob, "POST", `/v1/members/${carl.id}/roles`, { remove: "farmer" })).status, 403);
  r = await call(alice, "POST", `/v1/members/${carl.id}/roles`, { remove: "farmer" });
  assert.equal((await call(alice, "POST", `/v1/members/${carl.id}/roles`, { remove: "cow" })).status, 400, "cow is theirs: the owner can't take it");
  r = await call(carl, "POST", `/v1/members/${carl.id}/roles`, { remove: "cow" });
  assert.deepEqual(r.data.roles, ["bull"], "they drop their own cow");
  // The owner gives themselves any role and can take it back
  assert.deepEqual((await call(alice, "POST", `/v1/members/${alice.id}/roles`, { add: "farmer" })).data.roles, ["cow", "farmer"]);
  assert.deepEqual((await call(alice, "POST", `/v1/members/${alice.id}/roles`, { remove: "farmer" })).data.roles, ["cow"]);
});

await test("only the owner sets up stalls, everyone sees them", async () => {
  assert.equal((await call(bob, "POST", "/v1/stalls", { x: 1, y: 2, z: 3 })).status, 403);
  const r = await call(alice, "POST", "/v1/stalls", { name: "Stall 1", x: 1.5, y: 0, z: -3.25 });
  assert.equal(r.status, 200, "the first stall is free");
  assert.equal((await call(alice, "POST", "/v1/stalls", { x: 2, y: 0, z: 0 })).status, 400, "the second costs 500 from the fund");
  const stalls = (await call(carl, "GET", "/v1/dairy/mine")).data.stalls;
  assert.equal(stalls.length, 1);
  assert.equal(stalls[0].z, -3.25);
  assert.equal((await call(alice, "POST", `/v1/stalls/${r.data.id}/tier`, { tier: 2 })).status, 400, "the fund is empty");
  assert.equal((await call(carl, "POST", "/v1/dairy/fund", { coins: 1000 })).status, 200, "any member can donate");
  assert.equal((await call(alice, "POST", `/v1/stalls/${r.data.id}/tier`, { tier: 2 })).status, 200);
  assert.equal((await call(alice, "POST", `/v1/stalls/${r.data.id}/tier`, { tier: 3 })).status, 400, "tier 3 needs level 5");
  assert.equal((await call(alice, "POST", "/v1/stalls", { name: "Stall 2", x: 2, y: 0, z: 0 })).status, 200);
  assert.equal((await call(alice, "POST", "/v1/stalls", { x: 3, y: 0, z: 0 })).status, 400, "the third costs 2,000 (not level-limited any more)");
  // The owner moves a stall to a new seat; it keeps its tier
  assert.equal((await call(bob, "PUT", `/v1/stalls/${r.data.id}`, { x: 9, y: 0, z: 9 })).status, 403, "only the owner moves stalls");
  assert.equal((await call(alice, "PUT", `/v1/stalls/${r.data.id}`, { x: 9, y: 1, z: 9 })).status, 200);
  const moved = (await call(carl, "GET", "/v1/dairy/mine")).data.stalls.find(x => x.id === r.data.id);
  assert.equal(moved.x, 9);
  assert.equal(moved.tier, 2);
  const p = (await call(bob, "GET", "/v1/dairy/mine")).data.progress;
  assert.equal(p.fund, 1000 - 300 - 500);
  assert.equal(p.level, 1);
  assert.equal(p.stallSlots, 16, "no level limit, only the cap");
  assert.deepEqual(p.donors, [{ name: "Carl Bull", coins: 1000 }]);
});

await test("dairy XP: only in the dairy, capped per member per day", async () => {
  assert.equal((await beat(bob, { houseId: ELSEWHERE, dairyXp: 50, awake: true })).data.dairyXp, 0, "away from the dairy");
  assert.equal((await beat(bob, { houseId: HOUSE, dairyXp: 50, awake: false })).data.dairyXp, 0, "asleep earns nothing");
  let total = 0;
  for (let i = 0; i < 8; i++) total += (await beat(bob, { houseId: HOUSE, dairyXp: 999, awake: true })).data.dairyXp;
  assert.equal(total, 300, "daily cap");
  await beat(alice, { houseId: HOUSE, dairyXp: 60, awake: true });
  const p = (await call(alice, "GET", "/v1/dairy/mine")).data.progress;
  assert.equal(p.xp, 360);
  assert.equal(p.weekXp, 360);
  assert.equal(p.nextXp, 400);
  await beat(alice, { houseId: HOUSE, dairyXp: 60, awake: true });
  assert.equal((await beat(alice)).data.membership.level, 2);
  // Pumped milk fills the dairy tank, only while awake in the dairy
  assert.equal((await beat(bob, { houseId: HOUSE, pumped: [10, 0, 2.5, 0, 0], awake: true })).data.pumped, 12.5);
  assert.equal((await beat(bob, { houseId: ELSEWHERE, pumped: [10], awake: true })).data.pumped, 0, "away");
  assert.equal((await beat(bob, { houseId: HOUSE, pumped: [10], awake: false })).data.pumped, 0, "asleep");
  const tank = (await call(alice, "GET", "/v1/dairy/mine")).data.progress.tank;
  assert.deepEqual(tank.byGrade, [12.5, 0, 0, 0, 0], "plain milk: grades don't matter any more");
  assert.equal(tank.total, 12.5);
  assert.equal(tank.pumpers[0].oz, 12.5);
  // Farmers and owners fill jugs from the tank; others can't
  assert.equal((await call(bob, "POST", "/v1/dairy/tank/take", { grade: 0, oz: 4 })).data.left, 8.5);
  assert.equal((await call(bob, "POST", "/v1/dairy/tank/take", { grade: 0, oz: 50 })).status, 400, "not that much");
  assert.equal((await call(carl, "POST", "/v1/dairy/tank/take", { grade: 0, oz: 1 })).status, 403, "bulls can't");
  // "Fill up" asks for what the tank shows, rounded: 4.96 oz shows as 5.0, and 5 can be taken (the tank goes to 0)
  const tankBefore = sqlite.prepare("SELECT oz FROM dairy_tank WHERE grade = 0").get().oz;
  sqlite.prepare("UPDATE dairy_tank SET oz = 4.96 WHERE grade = 0").run();
  const up = await call(bob, "POST", "/v1/dairy/tank/take", { grade: 0, oz: 5 });
  assert.equal(up.status, 200);
  assert.equal(up.data.left, 0);
  assert.equal((await call(bob, "POST", "/v1/dairy/tank/take", { grade: 0, oz: 1 })).status, 400, "empty now");
  sqlite.prepare("UPDATE dairy_tank SET oz = ? WHERE grade = 0").run(tankBefore);
  sqlite.prepare("UPDATE carried SET oz = oz - 5 WHERE install = ?").run(bob.id);
  // A big farmer tank fills up in one go (up to 400 oz)
  sqlite.prepare("UPDATE dairy_tank SET oz = 500 WHERE grade = 0").run();
  assert.equal((await call(bob, "POST", "/v1/dairy/tank/take", { grade: 0, oz: 144 })).status, 200, "a 144 oz tank");
  assert.equal((await call(bob, "POST", "/v1/dairy/tank/take", { grade: 0, oz: 401 })).status, 400, "more than the biggest tank");
  sqlite.prepare("UPDATE dairy_tank SET oz = ? WHERE grade = 0").run(tankBefore);
  sqlite.prepare("UPDATE carried SET oz = oz - 144 WHERE install = ?").run(bob.id);
  // A cow's hand milking pays the farmer a share
  await beat(alice, { houseId: HOUSE, awake: true });
  const hp = await call(alice, "POST", "/v1/handpay", { farmer: "Bob Hand", world: "Gilgamesh", oz: 10, grade: 1 });
  assert.equal(hp.data.coins, Math.round(10 * 8 * 1.02 * 0.25));
  assert.equal((await call(alice, "POST", "/v1/handpay", { farmer: "Nobody", world: "Gilgamesh", oz: 10, grade: 1 })).status, 400);
  assert.equal((await call(bob, "POST", "/v1/handpay", { farmer: "Bob Hand", world: "Gilgamesh", oz: 10, grade: 1 })).status, 400, "farmers don't report");
  const pay = (await call(bob, "POST", "/v1/inbox/claim")).data.items.find(i => i.kind === "payout");
  assert.equal(pay.coins, hp.data.coins);
  assert.equal(pay.grade, 1, "hand milking shares are marked as game-made (they taper in the plugin)");
  await beat(alice);   // back out of the dairy
  // A bull's seed goes into its own seed tank, not the milk tank; only bulls add seed
  assert.equal((await beat(carl, { houseId: HOUSE, seedPumped: 1.25, awake: true })).data.seedPumped, 1.25);
  assert.equal((await beat(carl, { houseId: HOUSE, seedPumped: 999, awake: true })).data.seedPumped, 24, "per heartbeat");
  assert.equal((await beat(bob, { houseId: HOUSE, seedPumped: 5, awake: true })).data.seedPumped, 0, "farmers make no seed");
  const st2 = (await call(alice, "GET", "/v1/dairy/mine")).data.progress.tank;
  assert.equal(st2.seed, 25.25);
  assert.equal(st2.total, 8.5, "the milk tank doesn't change");
  assert.equal(st2.seedPumpers[0].name, "Carl Bull");
  assert.ok(st2.pumpers.every(x => x.name !== "Carl Bull"));
  // A bull's hand milking pays the farmer a share at five times the price per oz
  const sp = await call(carl, "POST", "/v1/handpay", { farmer: "Bob Hand", world: "Gilgamesh", oz: 1, grade: 0, seed: true });
  assert.equal(sp.data.coins, Math.round(1 * 6 * 5 * 1.02 * 0.25));
  assert.equal((await call(alice, "POST", "/v1/handpay", { farmer: "Bob Hand", world: "Gilgamesh", oz: 1, grade: 0, seed: true })).status, 400, "cows don't report seed");
  await call(bob, "POST", "/v1/inbox/claim");
  await beat(carl);

  // Weekly contract: sized by level; shipped from carried jugs (at the Grand Creamery once it's set); pays once
  let c = (await call(alice, "GET", "/v1/dairy/mine")).data.progress.contract;
  assert.equal(c.ozNeed, 600, "sized when first seen (level 1)");
  assert.equal(c.rewardFund, 2400);
  // The rest of this test ships a small contract to the end
  sqlite.exec("UPDATE contracts SET oz_need = 200, reward_xp = 300, reward_fund = 800");
  assert.equal(c.minGrade, 0);
  assert.equal((await call(carl, "POST", "/v1/dairy/contract/ship", { grade: 0, oz: 5 })).status, 403, "bulls can't ship");
  await beat(bob, { houseId: HOUSE, awake: true });
  let r = await call(bob, "POST", "/v1/dairy/contract/ship", { grade: 0, oz: 4 });
  assert.equal(r.data.shipped, 4, "the 4 oz bob carried out earlier");
  assert.equal((await call(bob, "POST", "/v1/dairy/contract/ship", { grade: 0, oz: 5 })).status, 400, "nothing left in his jugs");
  await beat(bob, { houseId: HOUSE, awake: true, pumped: [0, 120, 0, 0, 0] });
  await beat(bob, { houseId: HOUSE, awake: true, pumped: [0, 120, 0, 0, 0] });
  for (const oz of [96, 96, 8]) assert.equal((await call(bob, "POST", "/v1/dairy/tank/take", { grade: 1, oz })).status, 200);
  assert.equal((await beat(bob, { houseId: HOUSE, awake: true })).data.carried, 200);
  // Once the Grand Creamery is set, shipping happens there
  assert.equal((await call(alice, "PUT", "/v1/dev/creamery", { territory: 129, x: 1, y: 2, z: 3 })).status, 403, "developer only");
  sqlite.exec(`INSERT INTO settings (key, value) VALUES ('creamery', '{"name":"Test Creamery","territory":129,"x":0,"y":0,"z":0,"radius":8}')`);
  assert.equal((await beat(bob, { houseId: HOUSE })).data.creamery.name, "Test Creamery");
  assert.equal((await beat(bob, { houseId: HOUSE })).data.creameries.length, 1, "the old single creamery counts as the first");
  // Several Grand Creameries (one per Grand Company): all listed, the first also as "creamery" for older plugins
  sqlite.exec(`INSERT INTO settings (key, value) VALUES ('creameries', '[{"name":"Limsa Creamery","territory":128,"x":0,"y":0,"z":0,"radius":8},null,{"name":"Uldah Creamery","territory":130,"x":0,"y":0,"z":0,"radius":8}]')`);
  const many = (await beat(bob, { houseId: HOUSE })).data;
  assert.deepEqual(many.creameries.map(c => [c.name, c.slot]), [["Limsa Creamery", 0], ["Uldah Creamery", 2]]);
  assert.equal(many.creamery.name, "Limsa Creamery");
  assert.match((await call(bob, "POST", "/v1/dairy/contract/ship", { grade: 1, oz: 10 })).data.error, /a Grand Creamery \(Limsa Creamery, Uldah Creamery\)/);
  assert.equal((await call(alice, "DELETE", "/v1/dev/creamery/1")).status, 403, "developer only");
  assert.equal((await call(bob, "POST", "/v1/dairy/contract/ship", { grade: 1, oz: 10 })).status, 400, "not at the creamery");
  await beat(alice, { atCreamery: true, creamerySlot: 2, name: "Alice Owner", worldName: "Lich" });
  const atC = (await beat(bob, { atCreamery: true, creamerySlot: 2, name: "Bob Hand", worldName: "Gilgamesh" })).data.creameryHere;
  assert.equal(atC.slot, 2);
  assert.deepEqual(atC.here, [{ name: "Alice Owner", world: "Lich" }], "everyone else at this creamery, any world");
  assert.equal(atC.todayOz, 0);
  const fundBefore = (await call(alice, "GET", "/v1/dairy/mine")).data.progress.fund;
  r = await call(bob, "POST", "/v1/dairy/contract/ship", { grade: 1, oz: 500 });
  assert.equal(r.data.shipped, 196, "only what's left is taken");
  assert.equal(r.data.finished, true);
  assert.equal((await call(bob, "POST", "/v1/dairy/contract/ship", { grade: 1, oz: 1 })).status, 409, "done");
  const after = (await beat(alice, { atCreamery: true, creamerySlot: 2, name: "Alice Owner", worldName: "Lich" })).data.creameryHere;
  assert.equal(after.todayOz, 196, "delivered here today");
  assert.equal(after.todayDairies, 1);
  assert.equal((await beat(alice, { atCreamery: true, creamerySlot: 0 })).data.creameryHere.todayOz, 0, "per creamery");
  c = (await call(alice, "GET", "/v1/dairy/mine")).data.progress;
  assert.equal(c.contract.completed, true);
  assert.equal(c.fund, fundBefore + 800);
  assert.equal(c.tank.byGrade[0], 48.5, "all one milk: the 8.5 left plus 240 pumped less 200 taken");
  assert.equal((await call(bob, "POST", "/v1/carry/use", { oz: 1 })).data.carried, 3);
  sqlite.exec("DELETE FROM settings");
  await beat(bob);
  const board = (await call(dana, "GET", "/v1/leaderboard")).data;
  assert.equal(board.dairies[0].name, "Rose Hollow");
  assert.equal(board.dairies[0].weekXp, 720);
});

await test("farmer chores: clean a stall (fresh an hour, again at half), groom a cow (15 min each)", async () => {
  const stall = (await call(alice, "GET", "/v1/dairy/mine")).data.stalls[0];
  assert.equal(stall.cleanUntil, 0, "not cleaned yet");
  await beat(bob, { houseId: ELSEWHERE, awake: true });
  assert.equal((await call(bob, "POST", `/v1/stalls/${stall.id}/clean`)).status, 400, "only inside the dairy");
  await beat(bob, { houseId: HOUSE, awake: true });
  await beat(carl, { houseId: HOUSE, awake: true });
  assert.equal((await call(carl, "POST", `/v1/stalls/${stall.id}/clean`)).status, 403, "bulls don't clean");
  assert.equal((await call(bob, "POST", `/v1/stalls/${stall.id}/clean`)).status, 200);
  assert.equal((await call(bob, "POST", `/v1/stalls/${stall.id}/clean`)).status, 400, "still clean");
  assert.ok((await call(carl, "GET", "/v1/dairy/mine")).data.stalls.find(x => x.id === stall.id).cleanUntil > 0, "everyone sees it's fresh");

  assert.equal((await call(carl, "POST", "/v1/groom", { cow: alice.id })).status, 403, "only farmers groom");
  assert.equal((await call(bob, "POST", "/v1/groom", { cow: carl.id })).status, 200, "bulls get groomed too");
  assert.equal((await call(bob, "POST", "/v1/groom", { cow: bob.id })).status, 400, "not yourself");
  assert.equal((await call(bob, "POST", "/v1/groom", { cow: alice.id })).status, 200);
  assert.equal((await call(bob, "POST", "/v1/groom", { cow: alice.id })).status, 400, "15 minutes per cow");
  assert.match((await call(bob, "POST", "/v1/groom", { cow: alice.id })).data.error ?? "", /again in \d+ min/, "says how long, so the plugin can wait that long");
  const items = (await call(alice, "POST", "/v1/inbox/claim")).data.items;
  assert.ok(items.some(i => i.kind === "groom"), "the cow hears about it");
  await beat(bob, { houseId: ELSEWHERE, awake: true });
  await beat(carl, { houseId: ELSEWHERE, awake: true });
});

await test("private place: members count as in the dairy there, nobody else sees it", async () => {
  const priv = { territory: 340, world: 40, house: "5555", x: 0, y: 0, z: 0, width: 10, length: 10, height: 6, rot: 0, mapId: 0, zone: "Mist" };
  const at = { territory: 340, world: 40, house: "5555", x: 1, y: 0, z: 1 };
  assert.equal((await call(bob, "PUT", "/v1/dairy/private", { area: priv })).status, 403, "only the owner sets it");
  assert.equal((await call(alice, "PUT", "/v1/dairy/private", { area: priv })).status, 200);
  assert.ok((await call(bob, "GET", "/v1/dairy/mine")).data.dairy.privateArea, "members get the private place");
  const hb = (await beat(bob, { pos: at, awake: true })).data;
  assert.ok(hb.here && hb.here.mine && hb.here.private, "a member there is in their dairy");
  assert.equal(hb.here.isPublic, false);
  assert.equal((await beat(dana, { pos: at })).data.here, null, "a visitor there isn't anywhere");
  // The whole house: anywhere inside it counts, no box
  assert.equal((await call(alice, "PUT", "/v1/dairy/private", { area: { ...priv, house: "", wholeHouse: true } })).status, 400, "only inside a house");
  assert.equal((await call(alice, "PUT", "/v1/dairy/private", { area: { ...priv, wholeHouse: true } })).status, 200);
  assert.ok((await beat(bob, { pos: { ...at, x: 900, z: -900 }, awake: true })).data.here?.private, "far corner of the house still counts");
  assert.equal((await call(alice, "DELETE", "/v1/dairy/private")).status, 200);
  assert.equal((await beat(bob, { pos: at, awake: true })).data.here, null, "gone once removed");
  await beat(bob, { houseId: ELSEWHERE, awake: true });
});

await test("dairy shelf: farmers stock it, members buy, baker and fund are paid, owner sets the price", async () => {
  await beat(bob, { houseId: HOUSE, awake: true });
  await beat(carl, { houseId: HOUSE, awake: true });
  assert.equal((await call(carl, "POST", "/v1/shelf/stock", { item: "cookies", qty: 3 })).status, 403, "bulls don't bake");
  assert.equal((await call(bob, "POST", "/v1/shelf/stock", { item: "rocks", qty: 3 })).status, 400, "only shelf items");
  assert.equal((await call(bob, "POST", "/v1/shelf/stock", { item: "cookies", qty: 3 })).status, 200);
  let v = (await call(carl, "GET", "/v1/shelf")).data.items.find(i => i.item === "cookies");
  assert.equal(v.qty, 3);
  assert.equal(v.price, 40, "default price");
  assert.equal((await call(bob, "PUT", "/v1/shelf/price", { item: "cookies", price: 50 })).status, 403, "only the owner prices");
  assert.equal((await call(alice, "PUT", "/v1/shelf/price", { item: "cookies", price: 999 })).status, 400, "within range");
  assert.equal((await call(alice, "PUT", "/v1/shelf/price", { item: "cookies", price: 50 })).status, 200);
  const before = (await call(alice, "GET", "/v1/dairy/mine")).data.progress.fund;
  await call(bob, "POST", "/v1/inbox/claim");
  const r = await call(carl, "POST", "/v1/shelf/buy", { item: "cookies" });
  assert.equal(r.status, 200);
  assert.equal(r.data.price, 50);
  assert.equal(r.data.bakerCut, 40);
  assert.equal((await call(alice, "GET", "/v1/dairy/mine")).data.progress.fund, before + 10, "fund gets 20%");
  assert.ok((await call(bob, "POST", "/v1/inbox/claim")).data.items.some(i => i.kind === "payout" && i.coins === 40), "the baker is paid");
  assert.equal((await call(carl, "GET", "/v1/shelf")).data.items.find(i => i.item === "cookies").qty, 2);
  // The dairy pantry: ingredients farmers make are shared
  assert.equal((await call(carl, "POST", "/v1/pantry/add", { item: "butter", qty: 2 })).status, 403, "only farmers and the owner");
  assert.equal((await call(bob, "POST", "/v1/pantry/add", { item: "cookies", qty: 1 })).status, 400, "only ingredients");
  assert.equal((await call(bob, "POST", "/v1/pantry/add", { item: "butter", qty: 3 })).data.qty, 3);
  assert.equal((await call(alice, "GET", "/v1/pantry")).data.items.butter, 3, "the owner sees the farmer's butter");
  assert.equal((await call(bob, "POST", "/v1/pantry/take", { item: "butter", qty: 4 })).status, 409, "not more than there is");
  assert.equal((await call(bob, "POST", "/v1/pantry/take", { item: "butter", qty: 2 })).data.qty, 1);
    // Farmers feed cows and bulls from the shelf for free, but never themselves
  await call(carl, "POST", "/v1/inbox/claim");
  assert.equal((await call(bob, "POST", "/v1/shelf/feed", { item: "cookies", to: bob.id })).status, 400, "not yourself");
  assert.equal((await call(carl, "POST", "/v1/shelf/feed", { item: "cookies", to: alice.id })).status, 403, "only farmers");
  const coinsBefore = (await call(bob, "POST", "/v1/inbox/claim")).data.items.length;
  assert.equal((await call(bob, "POST", "/v1/shelf/feed", { item: "cookies", to: carl.id })).status, 200);
  const fed = (await call(carl, "POST", "/v1/inbox/claim")).data.items.find(i => i.kind === "fed");
  assert.ok(fed && fed.grade === 0, "the bull gets the cookie");
  assert.equal((await call(bob, "POST", "/v1/inbox/claim")).data.items.length, coinsBefore, "nobody is paid for it");
  assert.equal((await call(carl, "GET", "/v1/shelf")).data.items.find(i => i.item === "cookies").qty, 1);
  assert.equal((await call(bob, "POST", "/v1/shelf/feed", { item: "cookies", to: carl.id })).status, 400, "once every 15 minutes each");
  await beat(carl, { houseId: ELSEWHERE, awake: true });
  assert.equal((await call(carl, "POST", "/v1/shelf/buy", { item: "cookies" })).status, 400, "only in the dairy");
  await beat(bob, { houseId: ELSEWHERE, awake: true });
});

await test("weekend contest: pumping fills the contract too and ranks pumpers; the feeding bell rings once an hour", async () => {
  // The bell: farmers and the owner, from inside their dairy, once an hour
  await beat(alice, { houseId: HOUSE, awake: true });
  const rung = await call(alice, "POST", "/v1/dairy/notice", { kind: "feedingBell" });
  assert.equal(rung.status, 200, JSON.stringify(rung.data));
  assert.equal((await call(alice, "POST", "/v1/dairy/notice", { kind: "feedingBell" })).status, 429, "once an hour");
  // The dairy's progress says when it can ring again (the button stays grey until then)
  const bell = (await call(alice, "GET", "/v1/dairy/mine")).data.progress.bell;
  assert.ok(bell.by.length > 0);
  assert.equal(bell.readyAt - bell.at, 3600);
  const heard = (await beat(carl, { houseId: HOUSE, noticeAfter: 0 })).data.notices;
  assert.ok(heard.some(n => n.kind === "feedingBell"), "everyone in the dairy hears it");
  // The contest
  env.CONTEST = "on";
  const before = (await call(alice, "GET", "/v1/dairy/mine")).data.progress.contract;
  await beat(alice, { houseId: HOUSE, awake: true, pumped: [5] });
  const after = (await call(alice, "GET", "/v1/dairy/mine")).data.progress.contract;
  assert.ok(after.contest.on);
  assert.ok(after.completed || after.ozDone > before.ozDone, "pumping fills the contract");
  assert.ok(after.contest.top.length > 0, "and ranks the pumper");
  env.CONTEST = "off";
});

await test("farmer removes a bull; can't remove the owner", async () => {
  assert.equal((await call(bob, "DELETE", `/v1/members/${alice.id}`)).status, 400);
  assert.equal((await call(bob, "DELETE", `/v1/members/${carl.id}`)).status, 200);
  assert.equal((await beat(carl)).data.membership, null);
  const items = (await call(carl, "POST", "/v1/inbox/claim")).data.items;
  assert.ok(items.some(i => /removed from Rose Hollow/.test(i.note)));
});

await test("owner changes a member's role; owner can't leave their own dairy", async () => {
  assert.equal((await call(alice, "PUT", `/v1/members/${bob.id}/role`, { role: "cow" })).status, 200);
  assert.equal((await beat(bob)).data.membership.role, "cow");
  assert.equal((await call(alice, "POST", "/v1/members/leave")).status, 400);
});

await test("several dairies: joining another keeps the first, owners can join others, standing in one plays in it", async () => {
  await call(erin, "PUT", "/v1/dairy", dairyBody({ houseId: HOUSE2, name: "Erin Acres", ownerName: "Erin", role: "farmer" }));
  const inv = await call(erin, "POST", "/v1/invites", { toHash: bob.hash, toName: "Bob Hand", role: "cow" });
  const a = await call(bob, "POST", `/v1/invites/${inv.data.id}/accept`, { charHash: bob.hash, name: "Bob Hand" });
  assert.equal(a.data.left, null, "nobody leaves anything");
  let hb = (await beat(bob)).data;
  assert.equal(hb.membership.dairy, "Erin Acres", "plays in the new one");
  assert.deepEqual(hb.dairies.map(d => d.name).sort(), ["Erin Acres", "Rose Hollow"]);
  assert.equal((await call(bob, "POST", "/v1/dairy/active", { id: dairy })).status, 200);
  assert.equal((await beat(bob)).data.membership.dairy, "Rose Hollow", "switched back");
  assert.equal((await beat(bob, { houseId: HOUSE2 })).data.membership.dairy, "Erin Acres", "standing in a dairy of yours plays in it");
  assert.equal((await call(bob, "POST", "/v1/dairy/active", { id: "ffffffffffffffffffffffff" })).status, 404);
  // Cow and bull follow you: Bob became a bull in Erin Acres, so he's one in Rose Hollow too; farmer stays per dairy
  await call(bob, "POST", `/v1/members/${bob.id}/roles`, { add: "bull" });
  const roseBob = (await call(alice, "GET", "/v1/dairy/mine")).data.members.find(m => m.name === "Bob Hand");
  assert.ok(roseBob.roles.includes("bull"), "bull everywhere");
  // Both still list Bob
  const rose = (await call(alice, "GET", "/v1/dairy/mine")).data.members.map(m => m.name);
  assert.ok(rose.includes("Bob Hand"));
  // An owner joins someone else's dairy too
  const inv2 = await call(erin, "POST", "/v1/invites", { toHash: alice.hash, toName: "Alice Cow", role: "cow" });
  assert.equal((await call(alice, "POST", `/v1/invites/${inv2.data.id}/accept`, { charHash: alice.hash })).status, 200);
  hb = (await beat(alice)).data;
  assert.equal(hb.membership.dairy, "Erin Acres");
  assert.equal(hb.membership.isOwner, false);
  // Leaving only leaves the one you're playing in; your own dairy stays
  assert.equal((await call(alice, "POST", "/v1/members/leave")).status, 200);
  hb = (await beat(alice)).data;
  assert.equal(hb.membership.dairy, "Rose Hollow");
  assert.equal(hb.membership.isOwner, true);
  // Back to how the later tests expect it: Bob only in Erin Acres
  await call(bob, "POST", "/v1/dairy/active", { id: dairy });
  assert.equal((await call(bob, "POST", "/v1/members/leave")).status, 200);
  assert.equal((await beat(bob)).data.membership.dairy, "Erin Acres");
});

await test("public dairy shows its address and who's there", async () => {
  await call(alice, "PUT", "/v1/dairy", dairyBody({ isPublic: true, showOwner: false }));
  await beat(dana, { houseId: HOUSE });
  const seen = (await call(dana, "GET", "/v1/dairies")).data.dairies.find(d => d.id === dairy);
  assert.equal(seen.ward, 5);
  assert.equal(seen.plot, 12);
  assert.equal(seen.districtKind, 8);
  assert.equal(seen.owner, "");
  assert.equal(seen.cowsHere, 1);
  assert.equal((await beat(dana, { houseId: HOUSE })).data.here.mine, false);
});

await test("buy orders: owner only, price limits", async () => {
  assert.equal((await call(dana, "POST", "/v1/orders", { oz: 16, minGrade: 1, coins: 200 })).status, 403);
  assert.equal((await call(alice, "POST", "/v1/orders", { oz: 16, minGrade: 1, coins: 9999 })).status, 400);
  const r = await call(alice, "POST", "/v1/orders", { oz: 16, minGrade: 1, coins: 200 });
  assert.equal(r.status, 200);
  orderId = r.data.id;
  assert.equal((await call(dana, "GET", `/v1/dairies/${dairy}/orders`)).data.orders.length, 1);
  const board = (await call(dana, "GET", "/v1/orders/board")).data.orders;
  assert.equal(board.length, 1, "other dairies' orders are on the board");
  assert.equal(board[0].dairyName.length > 0, true);
  assert.equal((await call(alice, "GET", "/v1/orders/board")).data.orders.length, 0, "not your own dairy's");
});

await test("delivery: from your own dairy or in person, and meet the grade", async () => {
  assert.equal((await call(dana, "POST", `/v1/orders/${orderId}/fill`, { grade: 2, houseId: ELSEWHERE })).status, 400);
  assert.equal((await call(dana, "POST", `/v1/orders/${orderId}/fill`, { grade: 0, houseId: ELSEWHERE })).status, 400, "not from just anywhere");
  assert.equal((await call(alice, "POST", `/v1/orders/${orderId}/fill`, { grade: 2, houseId: HOUSE })).status, 400, "own order");
});

await test("delivery pays the seller and gives the owner the milk, once", async () => {
  assert.equal((await call(dana, "POST", `/v1/orders/${orderId}/fill`, { grade: 2, houseId: HOUSE })).status, 400, "no carried milk");
  sqlite.exec(`INSERT INTO carried (install, oz) VALUES ('${dana.id}', 16)`);
  assert.equal((await call(dana, "POST", `/v1/orders/${orderId}/fill`, { grade: 2, houseId: HOUSE })).status, 200);
  assert.equal((await call(carl, "POST", `/v1/orders/${orderId}/fill`, { grade: 2, houseId: HOUSE })).status, 409);
  const d = (await call(dana, "POST", "/v1/inbox/claim")).data.items;
  assert.equal(d[0].kind, "payout");
  assert.equal(d[0].coins, 200, "in person pays the same as from your own dairy");
  assert.equal((await call(dana, "POST", "/v1/inbox/claim")).data.items.length, 0);
  const a = (await call(alice, "POST", "/v1/inbox/claim")).data.items;
  assert.ok(a.some(i => i.kind === "notice"), "owner is told");
  const tank = (await call(alice, "GET", "/v1/dairy/mine")).data.progress.tank;
  assert.ok(tank.byGrade[0] >= 16, "the milk went into the buying dairy's tank");
});

await test("private dairies keep their orders; farmers deliver from their own dairy (no bonus)", async () => {
  const o = (await call(alice, "POST", "/v1/orders", { oz: 8, minGrade: 0, coins: 40 })).data.id;
  await call(alice, "PUT", "/v1/dairy", dairyBody({ isPublic: false }));
  assert.equal((await call(alice, "POST", "/v1/inbox/claim")).data.items.filter(i => i.kind === "refund").length, 0, "nothing refunded");
  assert.ok((await call(erin, "GET", "/v1/orders/board")).data.orders.some(x => x.id === o && !x.dairyPublic), "on the board, marked private");
  sqlite.exec(`INSERT INTO carried (install, oz) VALUES ('${erin.id}', 8) ON CONFLICT(install) DO UPDATE SET oz = 8`);
  await beat(erin, { houseId: ELSEWHERE, awake: true });
  assert.equal((await call(erin, "POST", `/v1/orders/${o}/fill`, { grade: 1, houseId: ELSEWHERE })).status, 400, "not from just anywhere");
  await beat(erin, { houseId: HOUSE2, awake: true });
  const r = await call(erin, "POST", `/v1/orders/${o}/fill`, { grade: 1, houseId: HOUSE2 });
  assert.equal(r.status, 200, "from inside Erin's own dairy");
  assert.equal(r.data.inPerson, false);
  assert.equal(r.data.coins, 40, "no in-person bonus");
});

await test("closing a dairy removes members and tells them", async () => {
  const inv = await call(alice, "POST", "/v1/invites", { toHash: dana.hash, toName: "Dana", role: "cow" });
  await call(dana, "POST", `/v1/invites/${inv.data.id}/accept`, { charHash: dana.hash });
  assert.equal((await call(dana, "DELETE", "/v1/dairy")).status, 403, "only the owner");
  assert.equal((await call(alice, "DELETE", "/v1/dairy")).status, 200);
  assert.equal((await beat(dana)).data.membership, null);
  assert.match((await call(dana, "POST", "/v1/inbox/claim")).data.items[0].note, /closed/);
  assert.equal((await call(erin, "PUT", "/v1/dairy", dairyBody({ name: "x" }))).status, 409, "Erin still owns Erin Acres");
});

await test("names are cleaned and trimmed", async () => {
  const r = await call(dana, "PUT", "/v1/dairy", dairyBody({ houseId: "777", name: "  Moo\u0007 Moo Farm with a very very long name indeed  " }));
  const d = (await call(dana, "GET", "/v1/dairies")).data.dairies.find(x => x.id === r.data.id);
  assert.ok(d.name.length <= 32);
  assert.ok(!d.name.includes("\u0007"));
});

await test("higher level dairies list first; delivery gives dairy XP", async () => {
  const list = (await call(dana, "GET", "/v1/dairies")).data.dairies;
  assert.ok(list[0].level >= list[list.length - 1].level);
});

await test("a dairy can be a box anywhere; boxes can't overlap; standing inside counts", async () => {
  const fay = player("Fay Field"), gus = player("Gus Field");
  await beat(fay); await beat(gus);
  const area = { territory: 134, world: 40, house: "", x: 100, y: 20, z: -50, width: 20, length: 12, height: 6, rot: 0.5, mapId: 15, zone: "Middle La Noscea" };
  const body = (extra) => ({ name: "Meadow Stall", isPublic: true, showOwner: false, ownerName: "Fay Field", worldName: "Gilgamesh", dcName: "Aether", role: "farmer", area, ...extra });
  const r = await call(fay, "PUT", "/v1/dairy", body());
  assert.equal(r.status, 200);
  // Gus can't put a box on top of it, but can next door
  assert.equal((await call(gus, "PUT", "/v1/dairy", body({ name: "Copycat", area: { ...area, x: 108 } }))).status, 409);
  assert.equal((await call(gus, "PUT", "/v1/dairy", body({ name: "Next Door", area: { ...area, x: 140 } }))).status, 200);
  assert.equal((await call(gus, "PUT", "/v1/dairy", body({ name: "Next Door", area: { ...area, x: 140, world: 41 } }))).status, 200, "another world is fine");
  // Standing inside the box counts as being there
  let hb = await beat(gus, { pos: { territory: 134, world: 40, house: "", x: 103, y: 21, z: -48 } });
  assert.equal(hb.data.here.name, "Meadow Stall");
  hb = await beat(gus, { pos: { territory: 134, world: 40, house: "", x: 130, y: 21, z: -48 } });
  assert.equal(hb.data.here, null, "outside the box");
  hb = await beat(gus, { pos: { territory: 134, world: 40, house: "", x: 103, y: 40, z: -48 } });
  assert.equal(hb.data.here, null, "too far above");
  // Listed with its area for visitors; the owner's dairy has its box
  const listed = (await call(gus, "GET", "/v1/dairies")).data.dairies.find(d => d.name === "Meadow Stall");
  assert.equal(listed.area.zone, "Middle La Noscea");
  assert.equal(listed.houseId, null);
  assert.equal((await call(fay, "GET", "/v1/dairy/mine")).data.dairy.area.width, 20);
  // Renaming keeps the box
  assert.equal((await call(fay, "PUT", "/v1/dairy", { name: "Meadow Barn", isPublic: true, role: "farmer" })).status, 200);
  assert.equal((await call(fay, "GET", "/v1/dairy/mine")).data.dairy.area.x, 100);
});

await test("whoami tells a save which character it last played as", async () => {
  assert.equal((await call(alice, "GET", "/v1/whoami")).data.charHash, alice.hash);
  const nobody = player("Never Beat");
  assert.equal((await call(nobody, "GET", "/v1/whoami")).data.charHash, null);
});

await test("a renamed member shows the new name to their dairy", async () => {
  await beat(bob, { name: "Bobby Hand", worldName: "Sargatanas" });
  const r = await call(bob, "GET", "/v1/dairy/mine");
  const me = r.data.members.find(m => m.name === "Bobby Hand");
  assert.ok(me, "new name on the roster");
  assert.equal(me.worldName ?? me.world, "Sargatanas");
  await beat(bob, { name: "Bob Hand", worldName: "Gilgamesh" });
});

await test("stats work without logging in", async () => {
  const r = await call(null, "GET", "/v1/stats");
  assert.equal(r.status, 200);
  assert.equal(r.data.online, 7, "five players plus Fay and Gus");
});

await test("island dairies: boxes may overlap other islands, and members count as in their own", async () => {
  const isle = { territory: 1055, world: 40, house: "", x: 10, y: 0, z: 10, width: 20, length: 20 };
  const a = player("Isla One"), b = player("Isla Two"), friend = player("Isla Friend");
  await beat(a); await beat(b); await beat(friend);
  assert.equal((await call(a, "PUT", "/v1/dairy", { name: "Isle Farm One", isPublic: true, role: "cow", area: isle })).status, 200);
  // Same spot on another player's island: no overlap
  assert.equal((await call(b, "PUT", "/v1/dairy", { name: "Isle Farm Two", isPublic: true, role: "cow", area: { ...isle, house: "123456789" } })).status, 200);
  // The owner standing there counts as in their dairy; so does a member visiting that island
  const r = await beat(a, { pos: { territory: 1055, world: 40, house: "", x: 12, y: 0, z: 11 } });
  assert.equal(r.data.here?.mine, true);
  const other = await beat(friend, { pos: { territory: 1055, world: 40, house: "", x: 12, y: 0, z: 11 } });
  assert.equal(other.data.here, null);   // not a member of either: can't tell whose island
});



await test("applications: apply, answer, cooldown, and farmers only when allowed", async () => {
  const owner = player("Opal Owner"), farmer = player("Fern Farmer"), ann = player("Ann Applicant"), ben = player("Ben Applicant");
  for (const p of [owner, farmer, ann, ben]) await beat(p);
  const reg = await call(owner, "PUT", "/v1/dairy", dairyBody({ houseId: "5550001112223", name: "Apple Acres", ownerName: "Opal Owner", isPublic: true }));
  assert.equal(reg.status, 200);
  const acres = reg.data.id ?? (await call(owner, "GET", "/v1/dairy/mine")).data.dairy.id;
  // Not taking applications until the owner says what they're looking for
  assert.equal((await call(ann, "POST", "/v1/applications", { dairyId: acres, roles: ["cow"], name: "Ann Applicant" })).status, 409);
  assert.equal((await call(farmer, "PUT", "/v1/dairy/applying", { lookingFor: ["cow"] })).status, 403);
  assert.equal((await call(owner, "PUT", "/v1/dairy/applying", { lookingFor: ["cow", "farmer"] })).status, 200);
  const listed = (await call(ann, "GET", "/v1/dairies")).data.dairies.find(d => d.id === acres);
  assert.deepEqual(listed.lookingFor, ["cow", "farmer"]);
  // Only roles they're looking for
  assert.equal((await call(ann, "POST", "/v1/applications", { dairyId: acres, roles: ["bull"], name: "Ann Applicant" })).status, 409);
  const sent = await call(ann, "POST", "/v1/applications", { dairyId: acres, roles: ["cow", "farmer"], name: "Ann Applicant", worldName: "Gilgamesh", note: "Hi!", levels: "Cow 3" });
  assert.equal(sent.status, 200);
  assert.equal((await call(ann, "POST", "/v1/applications", { dairyId: acres, roles: ["cow"], name: "Ann Applicant" })).status, 409);   // twice
  assert.equal((await call(ann, "GET", "/v1/dairies")).data.applications[0].status, "pending");
  // The owner sees it (and the heartbeat count); a farmer member doesn't until allowed
  assert.equal((await beat(owner)).data.applications, 1);
  const bens = await call(ben, "POST", "/v1/applications", { dairyId: acres, roles: ["farmer"], name: "Ben Applicant" });
  assert.equal((await call(owner, "POST", `/v1/dairy/applications/${bens.data.id}/accept`, { roles: ["farmer"] })).status, 200);
  assert.equal((await call(ben, "GET", "/v1/dairy/applications")).status, 403);   // ben is now a farmer, but farmers can't answer yet
  assert.equal((await call(owner, "PUT", "/v1/dairy/applying", { lookingFor: ["cow", "farmer"], farmersAnswer: true })).status, 200);
  const seen = await call(ben, "GET", "/v1/dairy/applications");
  assert.equal(seen.status, 200);
  assert.equal(seen.data.applications[0].name, "Ann Applicant");
  // Accept only one of the two roles; members can't apply elsewhere
  const ok = await call(ben, "POST", `/v1/dairy/applications/${sent.data.id}/accept`, { roles: ["cow"] });
  assert.equal(ok.status, 200);
  const roster = (await call(owner, "GET", "/v1/dairy/mine")).data.members.find(m => m.name === "Ann Applicant");
  assert.deepEqual(roster.roles, ["cow"]);
  assert.equal((await call(ann, "POST", "/v1/applications", { dairyId: acres, roles: ["cow"], name: "Ann Applicant" })).status, 409);
  // Deny, then the 3 day wait
  const cal = player("Cal Applicant");
  await beat(cal);
  const c1 = await call(cal, "POST", "/v1/applications", { dairyId: acres, roles: ["cow"], name: "Cal Applicant" });
  assert.equal((await call(owner, "POST", `/v1/dairy/applications/${c1.data.id}/deny`)).status, 200);
  const again = await call(cal, "POST", "/v1/applications", { dairyId: acres, roles: ["cow"], name: "Cal Applicant" });
  assert.equal(again.status, 409);
  assert.match(again.data.error, /again in 3 days/);
  assert.ok((await call(cal, "GET", "/v1/dairies")).data.applications.some(a => a.status === "denied" && a.retryAt > 0));
});

await test("applications: accepting one keeps the others open (you can join several); withdraw your own", async () => {
  const o1 = player("Olive One"), o2 = player("Otto Two"), dee = player("Dee Applicant");
  for (const p of [o1, o2, dee]) await beat(p);
  await call(o1, "PUT", "/v1/dairy", dairyBody({ houseId: "5550001112224", name: "Dairy One", ownerName: "Olive One" }));
  await call(o2, "PUT", "/v1/dairy", dairyBody({ houseId: "5550001112225", name: "Dairy Two", ownerName: "Otto Two" }));
  await call(o1, "PUT", "/v1/dairy/applying", { lookingFor: ["cow"] });
  await call(o2, "PUT", "/v1/dairy/applying", { lookingFor: ["cow"] });
  const id1 = (await call(o1, "GET", "/v1/dairy/mine")).data.dairy.id, id2 = (await call(o2, "GET", "/v1/dairy/mine")).data.dairy.id;
  const a1 = await call(dee, "POST", "/v1/applications", { dairyId: id1, roles: ["cow"], name: "Dee Applicant" });
  const a2 = await call(dee, "POST", "/v1/applications", { dairyId: id2, roles: ["cow"], name: "Dee Applicant" });
  assert.equal((await call(dee, "DELETE", `/v1/applications/${a2.data.id}`)).status, 200);
  const a3 = await call(dee, "POST", "/v1/applications", { dairyId: id2, roles: ["cow"], name: "Dee Applicant" });
  assert.equal((await call(o1, "POST", `/v1/dairy/applications/${a1.data.id}/accept`, {})).status, 200);
  assert.equal((await call(o2, "GET", "/v1/dairy/applications")).data.applications.length, 1, "still open");
  assert.equal((await call(o2, "POST", `/v1/dairy/applications/${a3.data.id}/accept`, {})).status, 200);
  assert.deepEqual((await beat(dee)).data.dairies.map(d => d.name).sort(), ["Dairy One", "Dairy Two"]);
  assert.equal((await call(dee, "POST", "/v1/applications", { dairyId: id1, roles: ["cow"], name: "Dee Applicant" })).status, 409, "already in it");
});

await test("weekly contract: an unfinished week still pays for the share delivered", async () => {
  const o = player("Pia Partial");
  await beat(o);
  await call(o, "PUT", "/v1/dairy", dairyBody({ houseId: "5550001119999", name: "Partial Pasture", ownerName: "Pia Partial" }));
  const mine = (await call(o, "GET", "/v1/dairy/mine")).data;
  const id = mine.dairy.id, before = mine.progress;
  // Last week: 500 of 2,000 oz delivered, never finished; this week's contract isn't made yet
  sqlite.exec(`DELETE FROM contracts WHERE dairy_id = '${id}'`);
  sqlite.exec(`INSERT INTO contracts (dairy_id, week, oz_need, min_grade, oz_done, reward_xp, reward_fund) VALUES ('${id}', '2000-01-03', 2000, 0, 500, 3000, 8000)`);
  await call(o, "GET", "/v1/dairy/mine");   // makes this week's contract, which settles last week's
  const after = (await call(o, "GET", "/v1/dairy/mine")).data.progress;
  assert.equal(after.fund, before.fund + 2000, "a quarter of the fund reward");
  assert.equal(after.xp, before.xp + 750, "a quarter of the xp");
  assert.equal(after.contract.ozDone, 0, "a fresh contract for this week");
  // Paid once
  const again = (await call(o, "GET", "/v1/dairy/mine")).data.progress;
  assert.equal(again.fund, after.fund);
});

await test("guests: milked in someone else's public dairy, their milk goes into that dairy's tank", async () => {
  const hana = player("Hana Host"), gus = player("Gus Guest");
  const H3 = "1234567890333", H4 = "1234567890444";
  await beat(hana, { name: "Hana Host", worldName: "Gilgamesh" });
  await beat(gus, { name: "Gus Guest", worldName: "Gilgamesh" });
  assert.equal((await call(hana, "PUT", "/v1/dairy", dairyBody({ houseId: H3, name: "Open Barn", isPublic: true, ownerName: "Hana Host", role: "farmer" }))).status, 200);
  assert.equal((await call(gus, "PUT", "/v1/dairy", dairyBody({ houseId: H4, name: "Gus Farm", isPublic: false, ownerName: "Gus Guest", role: "cow" }))).status, 200);
  await call(hana, "POST", "/v1/stalls", { name: "Seat", x: 1, y: 2, z: 3 });

  // Gus visits Hana's public dairy: he's a guest, with its stalls and online farmers, and his milk fills Hana's tank
  await beat(hana, { houseId: H3, status: { milking: "Gus Guest" } });
  const r = (await beat(gus, { houseId: H3, pumped: [6], awake: true })).data;
  assert.equal(r.here.guest, true);
  assert.equal(r.here.stalls.length, 1);
  assert.equal(r.here.farmers[0].name, "Hana Host");
  assert.equal(r.here.farmers[0].milking, "Gus Guest");
  assert.equal(r.pumped, 6);
  assert.equal((await call(hana, "GET", "/v1/dairy/mine")).data.progress.tank.total, 6);
  assert.equal((await call(gus, "GET", "/v1/dairy/mine")).data.progress.tank.total, 0, "not his own dairy's tank");
  assert.ok((await call(hana, "GET", "/v1/dairy/mine")).data.guests.some(g => g.name === "Gus Guest"), "the host's farmers see the guest");

  // Hana's farmers can hand milk the guest: the farmer's share is paid as usual
  const pay = await call(gus, "POST", "/v1/handpay", { farmer: "Hana Host", world: "Gilgamesh", oz: 10, grade: 0 });
  assert.equal(pay.status, 200, JSON.stringify(pay.data));
  assert.ok(pay.data.coins > 0);

  // Hana (a farmer) can groom the guest too
  assert.equal((await call(hana, "POST", "/v1/groom", { cow: gus.id })).status, 200);

  // A private dairy has no guests
  await call(hana, "PUT", "/v1/dairy", dairyBody({ houseId: H3, name: "Open Barn", isPublic: false, ownerName: "Hana Host", role: "farmer" }));
  const r2 = (await beat(gus, { houseId: H3, pumped: [6], awake: true })).data;
  assert.ok(!r2.here?.guest);
  assert.equal(r2.pumped, 0);

  // Looking a player up: their roles, and whom they allowed to breed them
  await beat(gus, { status: { milk: 1, cap: 8, breedOk: "Bram Bull" } });
  const look = (await call(hana, "GET", "/v1/player?name=Gus%20Guest&world=Gilgamesh")).data;
  assert.equal(look.found, true);
  assert.deepEqual(look.roles, ["cow"]);
  assert.equal(look.breedOk, "Bram Bull");
  assert.equal((await call(hana, "GET", "/v1/player?name=Nobody%20Here&world=Gilgamesh")).data.found, false);
});

await test("cloud backup: a rebuilt PC gets its save and dairy back with the Lodestone", async () => {
  // Faye owns a dairy and backs up her save; the key and the developer phrase never leave her PC
  const alice = player("Faye Restore");
  await beat(alice);
  const reg = await call(alice, "PUT", "/v1/dairy", dairyBody({ houseId: "7770001112229", name: "Faye Farm", ownerName: "Faye Restore", ward: 23, plot: 41, world: 77, worldName: "Lamia", district: "The Goblet", districtKind: 9 }));
  assert.equal(reg.status, 200, JSON.stringify(reg.data));
  const before = (await beat(alice)).data.membership;
  assert.equal(before?.dairy, "Faye Farm");
  const save = JSON.stringify({ Xp: 5000, BullXp: 10, FarmerXp: 20, Coins: 900, NetKey: alice.key, DevKey: "secret", Seal: "AB" });
  assert.equal((await call(alice, "POST", "/v1/backup", { charHash: alice.hash, save })).data.ok, true);
  const mine = (await call(alice, "GET", "/v1/backup")).data;
  assert.equal(mine.found, true);
  assert.equal(JSON.parse(mine.save).Coins, 900);
  assert.ok(!mine.save.includes(alice.key) && !mine.save.includes("secret"));
  // A second upload right away is skipped (one every two minutes)
  assert.equal((await call(alice, "POST", "/v1/backup", { charHash: alice.hash, save })).data.skipped, true);
  // A reset save (less XP) never replaces the backup
  sqlite.prepare("UPDATE cloud_saves SET saved = saved - 1000").run();
  const reset = JSON.stringify({ Xp: 2, BullXp: 0, FarmerXp: 0, Coins: 60, Seal: "CD" });
  assert.equal((await call(alice, "POST", "/v1/backup", { charHash: alice.hash, save: reset })).data.kept, true);
  assert.equal(JSON.parse((await call(alice, "GET", "/v1/backup")).data.save).Coins, 900);

  // Her PC is rebuilt: a brand new install on the same character
  const fresh = { ...player("Faye Restore"), hash: alice.hash };
  await beat(fresh);
  assert.equal((await call(fresh, "GET", "/v1/restore/available")).data.available, true);
  const { code } = (await call(fresh, "POST", "/v1/restore/start", { name: "Faye Restore", world: "Gilgamesh" })).data;
  assert.match(code, /^MILK-[A-Z0-9]{8}$/);
  // Asking again gives the same code
  assert.equal((await call(fresh, "POST", "/v1/restore/start", { name: "Faye Restore", world: "Gilgamesh" })).data.code, code);

  // Someone else can't use her code, even claiming to be her
  const thief = { ...player("Faye Restore"), hash: alice.hash };
  await beat(thief);
  let profile = "";
  env.LODESTONE_FETCH = async u => u.includes("?q=")
    ? `<a href="/lodestone/character/4242/" class="entry__link"><p class="entry__name">Faye Restore</p></a>`
    : u.endsWith("/4242/") ? `<div class="character__selfintroduction">${profile}</div>` : "";
  const steal = await call(thief, "POST", "/v1/restore/check", { name: "Faye Restore", world: "Gilgamesh", worldId: 40, code });
  assert.equal(steal.status, 400);

  // Not in the profile yet
  const early = await call(fresh, "POST", "/v1/restore/check", { name: "Faye Restore", world: "Gilgamesh", worldId: 40, code });
  assert.equal(early.data.ok, false);
  assert.match(early.data.why, /isn't in/);
  // Checking again straight away is too soon
  assert.equal((await call(fresh, "POST", "/v1/restore/check", { name: "Faye Restore", world: "Gilgamesh", worldId: 40, code })).status, 429);

  sqlite.prepare("UPDATE restore_codes SET checked = 0").run();
  profile = `Hi! ${code.toLowerCase()} cows forever`;
  const ok = (await call(fresh, "POST", "/v1/restore/check", { name: "Faye Restore", world: "Gilgamesh", worldId: 40, code })).data;
  assert.equal(ok.ok, true);
  assert.equal(ok.netId, alice.id);
  assert.equal(JSON.parse(ok.save).Coins, 900);
  delete env.LODESTONE_FETCH;

  // The new install now signs in as her old one with its own key: still the owner of her dairy
  const back = { id: alice.id, key: fresh.key, name: "Faye Restore", hash: alice.hash };
  const hb = await beat(back);
  assert.equal(hb.status, 200);
  assert.deepEqual(hb.data.membership, before);
  // ...and the old key doesn't work any more
  assert.equal((await beat(alice)).status, 401);
});

await test("Discord walkthrough: verify on the Lodestone, then paste the restore key in the plugin", async () => {
  env.BOT_KEY = "bot-key-for-tests-0123456789";
  const bot = async (path, key, b) => {
    const r = await worker.fetch(new Request("https://x" + path, { method: "POST",
      headers: { "content-type": "application/json", "X-Bot-Key": key }, body: JSON.stringify(b) }), env);
    return { status: r.status, data: await r.json() };
  };
  const gail = player("Gail Discord");
  await beat(gail);
  await call(gail, "POST", "/v1/backup", { charHash: gail.hash, save: JSON.stringify({ Xp: 777, Coins: 321 }) });

  assert.equal((await bot("/v1/bot/restore/start", "wrong-key-wrong-key-wrong", { name: "Gail Discord", world: "Gilgamesh", discordUser: "123456789" })).status, 401);
  assert.equal((await bot("/v1/bot/restore/start", env.BOT_KEY, { name: "Gail", world: "Gilgamesh", discordUser: "123456789" })).status, 400);
  const { code } = (await bot("/v1/bot/restore/start", env.BOT_KEY, { name: "Gail Discord", world: "Gilgamesh", discordUser: "123456789" })).data;
  assert.match(code, /^MILK-/);

  let profile = "";
  env.LODESTONE_FETCH = async u => u.includes("?q=")
    ? `<a href="/lodestone/character/777/" class="entry__link"><p class="entry__name">Gail Discord</p></a>`
    : u.endsWith("/777/") ? `<div class="character__selfintroduction">${profile}</div>` : "";
  // Another Discord user can't verify it, and it isn't in the profile yet
  assert.equal((await bot("/v1/bot/restore/verify", env.BOT_KEY, { discordUser: "999999999", code })).data.ok, false);
  sqlite.prepare("UPDATE restore_codes SET checked = 0").run();
  assert.match((await bot("/v1/bot/restore/verify", env.BOT_KEY, { discordUser: "123456789", code })).data.why, /isn't in/);
  sqlite.prepare("UPDATE restore_codes SET checked = 0").run();
  profile = code;
  const v = (await bot("/v1/bot/restore/verify", env.BOT_KEY, { discordUser: "123456789", code })).data;
  assert.equal(v.ok, true);
  assert.match(v.key, /^MILKR-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}$/);
  delete env.LODESTONE_FETCH;

  const fresh = { ...player("Gail Discord"), hash: gail.hash };
  await beat(fresh);
  // The key only works for that character
  assert.equal((await call(fresh, "POST", "/v1/restore/redeem", { key: v.key, name: "Other Person", world: "Gilgamesh", worldId: 40 })).status, 400);
  const r = (await call(fresh, "POST", "/v1/restore/redeem", { key: v.key.toLowerCase().replaceAll("-", " "), name: "Gail Discord", world: "Gilgamesh", worldId: 40 })).data;
  assert.equal(r.ok, true);
  assert.equal(r.netId, gail.id);
  assert.equal(JSON.parse(r.save).Coins, 321);
  // Used once only
  assert.equal((await call(fresh, "POST", "/v1/restore/redeem", { key: v.key, name: "Gail Discord", world: "Gilgamesh", worldId: 40 })).status, 404);
  assert.equal((await beat({ id: gail.id, key: fresh.key, hash: gail.hash })).status, 200);
  delete env.BOT_KEY;

  // Restoring onto the install that already is the character's says so
  const self = { id: gail.id, key: fresh.key, hash: gail.hash };
  const { code: own } = (await call(self, "POST", "/v1/restore/start", { name: "Gail Discord", world: "Gilgamesh" })).data;
  const why = (await call(self, "POST", "/v1/restore/check", { name: "Gail Discord", world: "Gilgamesh", worldId: 40, code: own })).data.why;
  assert.match(why, /already signed in/);

  // A new recovery code: the old key stops working
  const newKey = "ab".repeat(32);
  assert.equal((await call(self, "POST", "/v1/rekey", { newKey })).data.ok, true);
  assert.equal((await beat(self)).status, 401);
  assert.equal((await beat({ ...self, key: newKey })).status, 200);
  assert.equal((await call({ ...self, key: newKey }, "POST", "/v1/rekey", { newKey: "nope" })).status, 400);
});

await test("breeding stalls use the seed tank, and dairies sell spare seed to each other", async () => {
  const hana = player("Hana Breeder"), ivo = player("Ivo Buyer");
  await beat(hana);
  await beat(ivo);
  const H1 = "4440001112221", H2 = "4440001112222";
  assert.equal((await call(hana, "PUT", "/v1/dairy", dairyBody({ houseId: H1, name: "Hana Hollow", ownerName: "Hana Breeder", ward: 21, plot: 7 }))).status, 200);
  assert.equal((await call(ivo, "PUT", "/v1/dairy", dairyBody({ houseId: H2, name: "Ivo Acres", ownerName: "Ivo Buyer", ward: 22, plot: 8 }))).status, 200);
  const dh = (await beat(hana)).data.membership.dairyId, di = (await beat(ivo)).data.membership.dairyId;
  const stall = (await call(hana, "POST", "/v1/stalls", { name: "Breeding pen", x: 1, y: 0, z: 1 })).data.id;
  assert.ok(stall);

  // Only the owner switches it; then it shows as a breeding stall
  assert.equal((await call(ivo, "POST", `/v1/stalls/${stall}/breeding`, { on: true })).status, 404);
  assert.equal((await call(hana, "POST", `/v1/stalls/${stall}/breeding`, { on: true })).data.breed, true);
  const listed = (await call(hana, "GET", "/v1/dairy/mine")).data.stalls.find(x => x.id === stall);
  assert.equal(listed.breed, true);

  // Sitting in it: needs to be there, and seed in the tank
  assert.equal((await call(hana, "POST", `/v1/stalls/${stall}/breed`)).status, 400, "not in the dairy");
  await beat(hana, { houseId: H1 });
  // Not loaded: a cow can't use it
  const empty = await call(hana, "POST", `/v1/stalls/${stall}/breed`);
  assert.equal(empty.status, 400);
  assert.match(empty.data.error, /no breedings loaded/);
  // Loading takes 3 oz from the seed tank each
  const noSeed = await call(hana, "POST", `/v1/stalls/${stall}/load`);
  assert.match(noSeed.data.error, /seed tank/);
  sqlite.prepare("INSERT INTO dairy_tank (dairy_id, grade, oz) VALUES (?, -1, 13) ON CONFLICT(dairy_id, grade) DO UPDATE SET oz = 13").run(dh);
  let l = (await call(hana, "POST", `/v1/stalls/${stall}/load`)).data;
  assert.equal(l.loaded, 1);
  assert.equal(l.tank, 10);
  l = (await call(hana, "POST", `/v1/stalls/${stall}/load`)).data;
  assert.equal(l.loaded, 2);
  assert.equal(l.tank, 7);
  assert.equal((await call(ivo, "POST", `/v1/stalls/${stall}/load`)).status, 403, "only that dairy's farmers and owner");
  assert.equal((await call(hana, "GET", "/v1/dairy/mine")).data.stalls.find(x => x.id === stall).loaded, 2);

  const bred = (await call(hana, "POST", `/v1/stalls/${stall}/breed`)).data;
  assert.equal(bred.ok, true);
  assert.equal(bred.loaded, 1);
  assert.equal((await call(hana, "POST", `/v1/stalls/${stall}/breed`)).status, 429, "20 minutes between breedings");
  // Unloading puts the rest back in the tank
  assert.equal((await call(hana, "POST", `/v1/stalls/${stall}/unload`)).data.tank, 10);
  // Five at most
  for (let i = 0; i < 3; i++) await call(hana, "POST", `/v1/stalls/${stall}/load`);
  sqlite.prepare("UPDATE dairy_tank SET oz = 30 WHERE dairy_id = ? AND grade = -1").run(dh);
  await call(hana, "POST", `/v1/stalls/${stall}/load`);
  await call(hana, "POST", `/v1/stalls/${stall}/load`);
  assert.match((await call(hana, "POST", `/v1/stalls/${stall}/load`)).data.error, /full/);
  // Back to milking: its loaded seed goes back too, and it can't breed
  assert.equal((await call(hana, "POST", `/v1/stalls/${stall}/breeding`, { on: false })).data.breed, false);
  assert.equal((await call(hana, "GET", "/v1/seed/market")).data.tank, 39);
  sqlite.prepare("UPDATE dairy_tank SET oz = 7 WHERE dairy_id = ? AND grade = -1").run(dh);
  assert.equal((await call(hana, "POST", `/v1/stalls/${stall}/breed`)).status, 404);

  // Selling spare seed: it leaves the tank while it's listed
  assert.equal((await call(hana, "POST", "/v1/seed/sell", { oz: 50, price: 40 })).status, 400, "not that much in the tank");
  const sale = (await call(hana, "POST", "/v1/seed/sell", { oz: 5, price: 40 })).data.id;
  assert.equal((await call(hana, "GET", "/v1/seed/market")).data.tank, 2);
  const market = (await call(ivo, "GET", "/v1/seed/market")).data;
  const row = market.sales.find(x => x.id === sale);
  assert.equal(row.dairy, "Hana Hollow");
  assert.equal(row.price, 40);
  assert.equal((await call(hana, "GET", "/v1/seed/market")).data.sales.some(x => x.id === sale), false, "not your own in the list");

  // Buying from the dairy fund, into the seller's fund
  assert.equal((await call(ivo, "POST", `/v1/seed/sales/${sale}/buy`, { oz: 2 })).status, 400, "no coins in the fund");
  sqlite.prepare("INSERT INTO dairy_progress (dairy_id, fund, week) VALUES (?, 500, 'w') ON CONFLICT(dairy_id) DO UPDATE SET fund = 500").run(di);
  const fundBefore = sqlite.prepare("SELECT fund FROM dairy_progress WHERE dairy_id = ?").get(dh)?.fund || 0;
  const bought = (await call(ivo, "POST", `/v1/seed/sales/${sale}/buy`, { oz: 2 })).data;
  assert.equal(bought.cost, 80);
  assert.equal(bought.tank, 2);
  assert.equal(sqlite.prepare("SELECT fund FROM dairy_progress WHERE dairy_id = ?").get(di).fund, 420);
  assert.equal(sqlite.prepare("SELECT fund FROM dairy_progress WHERE dairy_id = ?").get(dh).fund, fundBefore + 80);
  assert.equal((await call(hana, "POST", `/v1/seed/sales/${sale}/buy`, { oz: 1 })).status, 400, "not your own");
  assert.equal((await call(ivo, "POST", `/v1/seed/sales/${sale}/buy`, { oz: 4 })).status, 400, "only 3 left");

  // Taking it down: the rest goes back into the seed tank
  assert.equal((await call(ivo, "POST", `/v1/seed/sales/${sale}/close`)).status, 404, "not theirs");
  assert.equal((await call(hana, "POST", `/v1/seed/sales/${sale}/close`)).data.returned, 3);
  assert.equal((await call(hana, "GET", "/v1/seed/market")).data.tank, 5);
});

await test("quotas, the feed bin, the Velvet pump and order fund pay", async () => {
  const alice = player("Olive Owner"), bob = player("Fran Farmer"), carl = player("Bert Bull");
  const H = "5550001112221";
  for (const x of [alice, bob, carl]) await beat(x);
  const made = await call(alice, "PUT", "/v1/dairy", dairyBody({ houseId: H, name: "Quota Farm", ownerName: "Olive Owner", ward: 23, plot: 9 }));
  assert.equal(made.status, 200, JSON.stringify(made.data));
  for (const [who, role, name] of [[bob, "farmer", "Fran Farmer"], [carl, "bull", "Bert Bull"]]) {
    const inv = await call(alice, "POST", "/v1/invites", { toHash: who.hash, toName: name, role });
    assert.equal(inv.status, 200, JSON.stringify(inv.data));
    const acc = await call(who, "POST", `/v1/invites/${inv.data.id}/accept`, { charHash: who.hash, name, worldName: "Gilgamesh" });
    assert.equal(acc.status, 200, JSON.stringify(acc.data));
  }
  for (const x of [alice, bob, carl]) await beat(x, { houseId: H, awake: true });
  const mine = (await call(alice, "GET", "/v1/dairy/mine")).data;
  const did = mine.dairy.id;
  assert.equal(mine.members.find(m => m.id === carl.id).quota, 1, "Steady by default");
  // Quotas: farmers set cows' and bulls', the owner anyone's
  assert.equal((await call(carl, "PUT", `/v1/members/${alice.id}/quota`, { size: 2 })).status, 403, "bulls can't set quotas");
  assert.equal((await call(bob, "PUT", `/v1/members/${carl.id}/quota`, { size: 3 })).status, 200);
  assert.equal((await call(bob, "PUT", `/v1/members/${bob.id}/quota`, { size: 3 })).status, 403, "not your own as a farmer");
  assert.equal((await call(alice, "PUT", `/v1/members/${bob.id}/quota`, { size: 0 })).status, 200, "the owner sets a farmer's");
  assert.equal((await call(alice, "PUT", `/v1/members/${bob.id}/quota`, { size: 9 })).status, 400);
  const q = (await call(carl, "GET", "/v1/dairy/mine")).data.members;
  assert.equal(q.find(m => m.id === carl.id).quota, 3);
  assert.equal(q.find(m => m.id === bob.id).quota, 0);
  // Feed bin: wheat from the start; better feed needs dairy level and the fund
  let p = (await call(alice, "GET", "/v1/dairy/mine")).data.progress;
  assert.deepEqual(p.feedBin, ["hay"]);
  assert.equal((await call(bob, "POST", "/v1/dairy/feedbin/grain")).status, 403, "only the owner unlocks feed");
  assert.equal((await call(alice, "POST", "/v1/dairy/feedbin/grain")).status, 400, "needs dairy level 3");
  sqlite.prepare("INSERT INTO dairy_progress (dairy_id, xp, fund, week) VALUES (?, 20000, 6000, 'w') ON CONFLICT(dairy_id) DO UPDATE SET xp = 20000, fund = 6000").run(did);
  assert.equal((await call(alice, "POST", "/v1/dairy/feedbin/grain")).status, 200);
  assert.equal((await call(alice, "POST", "/v1/dairy/feedbin/grain")).status, 400, "already there");
  assert.equal((await call(alice, "GET", "/v1/dairy/mine")).data.progress.fund, 4000);
  await call(carl, "POST", "/v1/inbox/claim");
  assert.equal((await call(carl, "POST", "/v1/feedbin/feed", { item: "grain", to: alice.id })).status, 403, "only farmers (and the owner) feed");
  assert.equal((await call(bob, "POST", "/v1/feedbin/feed", { item: "goldenmash", to: carl.id })).status, 400, "not unlocked");
  assert.equal((await call(bob, "POST", "/v1/feedbin/feed", { item: "grain", to: carl.id })).status, 200);
  assert.equal((await call(bob, "POST", "/v1/feedbin/feed", { item: "hay", to: carl.id })).status, 400, "once every 15 minutes");
  const fed = (await call(carl, "POST", "/v1/inbox/claim")).data.items.find(i => i.kind === "binfed");
  assert.equal(fed.grade, 1, "which feed it was");
  // The Velvet pump
  assert.equal((await call(bob, "POST", "/v1/dairy/pump")).status, 403, "the owner runs it");
  assert.equal((await call(alice, "POST", "/v1/dairy/pump")).status, 200);
  p = (await call(alice, "GET", "/v1/dairy/mine")).data.progress;
  assert.equal(p.fund, 1500);
  assert.ok(p.pumpUntil > Date.now() / 1000 + 7000);
  assert.equal((await call(alice, "POST", "/v1/dairy/pump")).status, 400, "not enough in the fund");
  // An order delivered pays the fund 2 coins an oz (only milk actually carried)
  sqlite.prepare("INSERT INTO carried (install, oz) VALUES (?, 20) ON CONFLICT(install) DO UPDATE SET oz = 20").run(bob.id);
  const used = (await call(bob, "POST", "/v1/carry/use", { oz: 30, order: true })).data;
  assert.equal(used.fund, 40, "20 oz carried, 2 coins an oz");
  assert.equal((await call(alice, "GET", "/v1/dairy/mine")).data.progress.fund, 1540);
});

await test("dev server test bots: an owner adds a cow, a farmer and a bull, and milks the bots", async () => {
  const owner = player("Tess Tester");
  await beat(owner);
  const H = "7770004445556";
  const made = await call(owner, "PUT", "/v1/dairy", dairyBody({ houseId: H, name: "Bot Barn", ownerName: "Tess Tester", ward: 27, plot: 13, district: "Goblet", districtKind: 9, roles: ["farmer"] }));
  assert.equal(made.status, 200, JSON.stringify(made.data));
  await beat(owner, { houseId: H, awake: true });
  assert.equal((await call(owner, "POST", "/v1/dev/bots", { on: true })).status, 404, "not on the live server");
  env.BOTS = "on";
  try {
    assert.equal((await call(owner, "POST", "/v1/dev/bots", { on: true })).data.bots, 3);
    let mine = (await call(owner, "GET", "/v1/dairy/mine")).data;
    const bots = mine.members.filter(m => m.status?.bot);
    assert.deepEqual(bots.map(b => b.name).sort(), ["Brutus Bot", "Daisy Bot", "Hank Bot"]);
    assert.ok(bots.every(b => b.online && b.here), "online and in the dairy");
    const daisy = bots.find(b => b.name === "Daisy Bot");
    assert.ok(daisy.status.milk > 0);
    const r = await call(owner, "POST", `/v1/dev/bots/${daisy.id}/milk`);
    assert.equal(r.status, 200, JSON.stringify(r.data));
    assert.ok(r.data.oz > 0 && r.data.coins > 0);
    assert.equal((await call(owner, "POST", `/v1/dev/bots/${daisy.id}/milk`)).status, 400, "empty now");
    assert.ok((await call(owner, "GET", "/v1/dairy/mine")).data.progress.tank.total > 0, "her milk is in the tank");
    // Removing them takes them out of the dairy
    assert.equal((await call(owner, "POST", "/v1/dev/bots", { on: false })).data.bots, 0);
    mine = (await call(owner, "GET", "/v1/dairy/mine")).data;
    assert.equal(mine.members.filter(m => m.status?.bot).length, 0);
  } finally { delete env.BOTS; }
});

console.log(`\n${passed} passed`);
