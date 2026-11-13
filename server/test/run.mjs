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

await test("several roles at once: self-add cow/bull, farmer is given, only the owner takes away", async () => {
  let r = await call(carl, "POST", `/v1/members/${carl.id}/roles`, { add: "cow" });
  assert.deepEqual(r.data.roles, ["cow", "bull"], "anyone can add cow to themselves");
  assert.equal((await call(carl, "POST", `/v1/members/${carl.id}/roles`, { add: "farmer" })).status, 403, "can't make yourself a farmer");
  assert.equal((await call(carl, "POST", `/v1/members/${carl.id}/roles`, { remove: "cow" })).status, 403, "only the owner takes roles away");
  assert.deepEqual((await call(bob, "POST", `/v1/members/${carl.id}/roles`, { add: "farmer" })).data.roles, ["cow", "farmer", "bull"], "a farmer gives farmer");
  assert.deepEqual((await beat(carl)).data.membership.roles, ["cow", "farmer", "bull"]);
  assert.equal((await call(bob, "POST", `/v1/members/${carl.id}/roles`, { remove: "farmer" })).status, 403);
  r = await call(alice, "POST", `/v1/members/${carl.id}/roles`, { remove: "farmer" });
  r = await call(alice, "POST", `/v1/members/${carl.id}/roles`, { remove: "cow" });
  assert.deepEqual(r.data.roles, ["bull"]);
  // The owner gives themselves any role and can take it back
  assert.deepEqual((await call(alice, "POST", `/v1/members/${alice.id}/roles`, { add: "farmer" })).data.roles, ["cow", "farmer"]);
  assert.deepEqual((await call(alice, "POST", `/v1/members/${alice.id}/roles`, { remove: "farmer" })).data.roles, ["cow"]);
});

await test("only the owner sets up stalls, everyone sees them", async () => {
  assert.equal((await call(bob, "POST", "/v1/stalls", { x: 1, y: 2, z: 3 })).status, 403);
  const r = await call(alice, "POST", "/v1/stalls", { name: "Stall 1", x: 1.5, y: 0, z: -3.25 });
  assert.equal(r.status, 200, "the first stall is free");
  assert.equal((await call(alice, "POST", "/v1/stalls", { x: 2, y: 0, z: 0 })).status, 400, "the second costs 250 from the fund");
  const stalls = (await call(carl, "GET", "/v1/dairy/mine")).data.stalls;
  assert.equal(stalls.length, 1);
  assert.equal(stalls[0].z, -3.25);
  assert.equal((await call(alice, "POST", `/v1/stalls/${r.data.id}/tier`, { tier: 2 })).status, 400, "the fund is empty");
  assert.equal((await call(carl, "POST", "/v1/dairy/fund", { coins: 500 })).status, 200, "any member can donate");
  assert.equal((await call(alice, "POST", `/v1/stalls/${r.data.id}/tier`, { tier: 2 })).status, 200);
  assert.equal((await call(alice, "POST", `/v1/stalls/${r.data.id}/tier`, { tier: 3 })).status, 400, "tier 3 needs level 5");
  assert.equal((await call(alice, "POST", "/v1/stalls", { name: "Stall 2", x: 2, y: 0, z: 0 })).status, 200);
  assert.equal((await call(alice, "POST", "/v1/stalls", { x: 3, y: 0, z: 0 })).status, 400, "the third costs 1,000 (not level-limited any more)");
  // The owner moves a stall to a new seat; it keeps its tier
  assert.equal((await call(bob, "PUT", `/v1/stalls/${r.data.id}`, { x: 9, y: 0, z: 9 })).status, 403, "only the owner moves stalls");
  assert.equal((await call(alice, "PUT", `/v1/stalls/${r.data.id}`, { x: 9, y: 1, z: 9 })).status, 200);
  const moved = (await call(carl, "GET", "/v1/dairy/mine")).data.stalls.find(x => x.id === r.data.id);
  assert.equal(moved.x, 9);
  assert.equal(moved.tier, 2);
  const p = (await call(bob, "GET", "/v1/dairy/mine")).data.progress;
  assert.equal(p.fund, 500 - 150 - 250);
  assert.equal(p.level, 1);
  assert.equal(p.stallSlots, 16, "no level limit, only the cap");
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
  assert.deepEqual(tank.byGrade, [12.5, 0, 0, 0, 0], "plain milk: grades don't matter any more");
  assert.equal(tank.total, 12.5);
  assert.equal(tank.pumpers[0].oz, 12.5);
  // Farmers and owners fill jugs from the tank; others can't
  assert.equal((await call(bob, "POST", "/v1/dairy/tank/take", { grade: 0, oz: 4 })).data.left, 8.5);
  assert.equal((await call(bob, "POST", "/v1/dairy/tank/take", { grade: 0, oz: 50 })).status, 400, "not that much");
  assert.equal((await call(carl, "POST", "/v1/dairy/tank/take", { grade: 0, oz: 1 })).status, 403, "bulls can't");
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

  // Weekly contract: sized by level; shipped from carried jugs (at the Grand Creamery once it's set); pays once
  let c = (await call(alice, "GET", "/v1/dairy/mine")).data.progress.contract;
  assert.equal(c.ozNeed, 500, "sized when first seen (level 1)");
  assert.equal(c.rewardFund, 2000);
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
  await beat(bob, { atCreamery: true });
  const fundBefore = (await call(alice, "GET", "/v1/dairy/mine")).data.progress.fund;
  r = await call(bob, "POST", "/v1/dairy/contract/ship", { grade: 1, oz: 500 });
  assert.equal(r.data.shipped, 196, "only what's left is taken");
  assert.equal(r.data.finished, true);
  assert.equal((await call(bob, "POST", "/v1/dairy/contract/ship", { grade: 1, oz: 1 })).status, 409, "done");
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
  assert.equal((await call(bob, "POST", "/v1/groom", { cow: carl.id })).status, 400, "only cows get groomed");
  assert.equal((await call(bob, "POST", "/v1/groom", { cow: alice.id })).status, 200);
  assert.equal((await call(bob, "POST", "/v1/groom", { cow: alice.id })).status, 400, "15 minutes per cow");
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
  assert.equal(d[0].coins, 220, "in person pays 10% more");
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

await test("applications: accepting one withdraws the others; withdraw your own", async () => {
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
  assert.equal((await call(o2, "GET", "/v1/dairy/applications")).data.applications.length, 0);   // withdrawn
  assert.equal((await call(o2, "POST", `/v1/dairy/applications/${a3.data.id}/accept`, {})).status, 404);
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

console.log(`\n${passed} passed`);
