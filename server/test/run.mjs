// Local tests for the Velvet Dairy Network worker, using Node's built-in SQLite as a stand-in for D1.
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
  catch (e) { console.log("FAIL", name, "\n   ", e.message); process.exitCode = 1; }
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
  assert.deepEqual(hb.data.membership, { dairyId: dairy, dairy: "Rose Hollow", role: "cow", isOwner: true, level: 1 });
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

await test("farmers can invite cows and bulls but not farmers", async () => {
  assert.equal((await call(bob, "POST", "/v1/invites", { toHash: dana.hash, toName: "Dana", role: "farmer" })).status, 403);
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

await test("only the owner sets up stalls, everyone sees them", async () => {
  assert.equal((await call(bob, "POST", "/v1/stalls", { x: 1, y: 2, z: 3 })).status, 403);
  const r = await call(alice, "POST", "/v1/stalls", { name: "Stall 1", x: 1.5, y: 0, z: -3.25 });
  assert.equal(r.status, 200, "the first stall is free");
  assert.equal((await call(alice, "POST", "/v1/stalls", { x: 2, y: 0, z: 0 })).status, 400, "the second costs 200 from the fund");
  const stalls = (await call(carl, "GET", "/v1/dairy/mine")).data.stalls;
  assert.equal(stalls.length, 1);
  assert.equal(stalls[0].z, -3.25);
  assert.equal((await call(alice, "POST", `/v1/stalls/${r.data.id}/tier`, { tier: 2 })).status, 400, "the fund is empty");
  assert.equal((await call(carl, "POST", "/v1/dairy/fund", { coins: 500 })).status, 200, "any member can donate");
  assert.equal((await call(alice, "POST", `/v1/stalls/${r.data.id}/tier`, { tier: 2 })).status, 200);
  assert.equal((await call(alice, "POST", `/v1/stalls/${r.data.id}/tier`, { tier: 3 })).status, 400, "tier 3 needs level 5");
  assert.equal((await call(alice, "POST", "/v1/stalls", { name: "Stall 2", x: 2, y: 0, z: 0 })).status, 200);
  assert.equal((await call(alice, "POST", "/v1/stalls", { x: 3, y: 0, z: 0 })).status, 400, "level 1 has 2 slots");
  const p = (await call(bob, "GET", "/v1/dairy/mine")).data.progress;
  assert.equal(p.fund, 500 - 150 - 200);
  assert.equal(p.level, 1);
  assert.equal(p.stallSlots, 2);
  assert.deepEqual(p.donors, [{ name: "Carl Bull", coins: 500 }]);
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
  assert.deepEqual(tank.byGrade, [10, 0, 2.5, 0, 0]);
  assert.equal(tank.total, 12.5);
  assert.equal(tank.pumpers[0].oz, 12.5);
  // Farmers and owners fill jugs from the tank; others can't
  assert.equal((await call(bob, "POST", "/v1/dairy/tank/take", { grade: 0, oz: 4 })).data.left, 6);
  assert.equal((await call(bob, "POST", "/v1/dairy/tank/take", { grade: 0, oz: 50 })).status, 400, "not that much");
  assert.equal((await call(carl, "POST", "/v1/dairy/tank/take", { grade: 0, oz: 1 })).status, 403, "bulls can't");
  // A cow's hand milking pays the farmer a share
  await beat(alice, { houseId: HOUSE, awake: true });
  const hp = await call(alice, "POST", "/v1/handpay", { farmer: "Bob Hand", world: "Gilgamesh", oz: 10, grade: 1 });
  assert.equal(hp.data.coins, Math.round(10 * 7 * 1.02 * 0.25));
  assert.equal((await call(alice, "POST", "/v1/handpay", { farmer: "Nobody", world: "Gilgamesh", oz: 10, grade: 1 })).status, 400);
  assert.equal((await call(bob, "POST", "/v1/handpay", { farmer: "Bob Hand", world: "Gilgamesh", oz: 10, grade: 1 })).status, 400, "farmers don't report");
  const pay = (await call(bob, "POST", "/v1/inbox/claim")).data.items.find(i => i.kind === "payout");
  assert.equal(pay.coins, hp.data.coins);
  await beat(alice);   // back out of the dairy

  // Weekly contract: sized by level, shipped from the tank by farmers/owners, pays once
  let c = (await call(alice, "GET", "/v1/dairy/mine")).data.progress.contract;
  assert.equal(c.ozNeed, 200, "sized when first seen (level 1)");
  assert.equal(c.minGrade, 0);
  assert.equal((await call(carl, "POST", "/v1/dairy/contract/ship", { grade: 0, oz: 5 })).status, 403, "bulls can't ship");
  await beat(bob, { houseId: HOUSE, awake: true });
  let r = await call(bob, "POST", "/v1/dairy/contract/ship", { grade: 0, oz: 5 });
  assert.equal(r.data.shipped, 5);
  assert.equal((await call(bob, "POST", "/v1/dairy/contract/ship", { grade: 0, oz: 5 })).status, 400, "tank only had 6 oz of C");
  // Fill the tank and finish it
  await beat(bob, { houseId: HOUSE, awake: true, pumped: [0, 120, 0, 0, 0] });
  await beat(bob, { houseId: HOUSE, awake: true, pumped: [0, 120, 0, 0, 0] });
  const fundBefore = (await call(alice, "GET", "/v1/dairy/mine")).data.progress.fund;
  r = await call(bob, "POST", "/v1/dairy/contract/ship", { grade: 1, oz: 500 });
  assert.equal(r.data.shipped, 195, "only what's left is taken");
  assert.equal(r.data.finished, true);
  assert.equal((await call(bob, "POST", "/v1/dairy/contract/ship", { grade: 1, oz: 1 })).status, 409, "done");
  c = (await call(alice, "GET", "/v1/dairy/mine")).data.progress;
  assert.equal(c.contract.completed, true);
  assert.equal(c.fund, fundBefore + 800);
  assert.equal(c.tank.byGrade[1], 45);
  await beat(bob);
  const board = (await call(dana, "GET", "/v1/leaderboard")).data;
  assert.equal(board.dairies[0].name, "Rose Hollow");
  assert.equal(board.dairies[0].weekXp, 720);
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

await test("joining a new dairy leaves the old one; owners can't join others", async () => {
  await call(erin, "PUT", "/v1/dairy", dairyBody({ houseId: HOUSE2, name: "Erin Acres", ownerName: "Erin", role: "farmer" }));
  const inv = await call(erin, "POST", "/v1/invites", { toHash: bob.hash, toName: "Bob Hand", role: "cow" });
  const a = await call(bob, "POST", `/v1/invites/${inv.data.id}/accept`, { charHash: bob.hash, name: "Bob Hand" });
  assert.equal(a.data.left, "Rose Hollow");
  assert.equal((await beat(bob)).data.membership.dairy, "Erin Acres");
  const inv2 = await call(erin, "POST", "/v1/invites", { toHash: alice.hash, toName: "Alice Cow", role: "cow" });
  assert.equal((await call(alice, "POST", `/v1/invites/${inv2.data.id}/accept`, { charHash: alice.hash })).status, 409);
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
});

await test("delivery must be in person and meet the grade", async () => {
  assert.equal((await call(dana, "POST", `/v1/orders/${orderId}/fill`, { grade: 2, houseId: ELSEWHERE })).status, 400);
  assert.equal((await call(dana, "POST", `/v1/orders/${orderId}/fill`, { grade: 0, houseId: HOUSE })).status, 400);
  assert.equal((await call(alice, "POST", `/v1/orders/${orderId}/fill`, { grade: 2, houseId: HOUSE })).status, 400, "own order");
});

await test("delivery pays the seller and gives the owner the milk, once", async () => {
  assert.equal((await call(dana, "POST", `/v1/orders/${orderId}/fill`, { grade: 2, houseId: HOUSE })).status, 200);
  assert.equal((await call(carl, "POST", `/v1/orders/${orderId}/fill`, { grade: 2, houseId: HOUSE })).status, 409);
  const d = (await call(dana, "POST", "/v1/inbox/claim")).data.items;
  assert.equal(d[0].kind, "payout");
  assert.equal(d[0].coins, 200);
  assert.equal((await call(dana, "POST", "/v1/inbox/claim")).data.items.length, 0);
  const a = (await call(alice, "POST", "/v1/inbox/claim")).data.items;
  assert.ok(a.some(i => i.kind === "notice"), "owner is told");
  const tank = (await call(alice, "GET", "/v1/dairy/mine")).data.progress.tank;
  assert.ok(tank.byGrade[2] >= 16, "the milk went into the buying dairy's tank");
});

await test("going private cancels and refunds open orders", async () => {
  await call(alice, "POST", "/v1/orders", { oz: 8, minGrade: 0, coins: 40 });
  await call(alice, "PUT", "/v1/dairy", dairyBody({ isPublic: false }));
  const items = (await call(alice, "POST", "/v1/inbox/claim")).data.items;
  assert.equal(items.filter(i => i.kind === "refund")[0].coins, 40);
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

await test("stats work without logging in", async () => {
  const r = await call(null, "GET", "/v1/stats");
  assert.equal(r.status, 200);
  assert.equal(r.data.online, 5);
});

console.log(`\n${passed} passed`);
