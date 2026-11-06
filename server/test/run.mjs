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
  assert.deepEqual(hb.data.membership, { dairyId: dairy, dairy: "Rose Hollow", role: "cow", isOwner: true });
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
  const r = await call(alice, "POST", "/v1/stalls", { name: "Stall 1", x: 1.5, y: 0, z: -3.25, limit: 1 });
  assert.equal(r.status, 200);
  assert.equal((await call(alice, "POST", "/v1/stalls", { x: 2, y: 0, z: 0, limit: 1 })).status, 400, "stall limit");
  const stalls = (await call(carl, "GET", "/v1/dairy/mine")).data.stalls;
  assert.equal(stalls.length, 1);
  assert.equal(stalls[0].z, -3.25);
  assert.equal((await call(alice, "POST", `/v1/stalls/${r.data.id}/tier`, { tier: 3 })).status, 409, "one tier at a time");
  assert.equal((await call(alice, "POST", `/v1/stalls/${r.data.id}/tier`, { tier: 2 })).status, 200);
});

await test("farmer removes a bull; can't remove the owner", async () => {
  assert.equal((await call(bob, "DELETE", `/v1/members/${alice.id}`)).status, 400);
  assert.equal((await call(bob, "DELETE", `/v1/members/${carl.id}`)).status, 200);
  assert.equal((await beat(carl)).data.membership, null);
  const items = (await call(carl, "POST", "/v1/inbox/claim")).data.items;
  assert.match(items[0].note, /removed from Rose Hollow/);
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
  const a = (await call(alice, "POST", "/v1/inbox/claim")).data.items.filter(i => i.kind === "milk");
  assert.equal(a[0].oz, 16);
  assert.equal(a[0].grade, 2);
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

await test("stats work without logging in", async () => {
  const r = await call(null, "GET", "/v1/stats");
  assert.equal(r.status, 200);
  assert.equal(r.data.online, 5);
});

console.log(`\n${passed} passed`);
