// Velvet Dairy Network: online count, dairy directory, roles and invites, shared stalls, buy orders and deliveries.
// Runs as a Cloudflare Worker with a D1 database (schema.sql). Every plugin install talks to it over HTTPS.
//
// Auth: each install makes a random id and secret key the first time. Requests send both
// (X-VD-Id / X-VD-Key); the server stores only a hash of the key and trusts it from then on.
//
// Roles, one dairy per player:
//   owner  registers their own estate (one dairy per address), invites farmers, cows and bulls, sets up stalls.
//          The owner is also a member with a play role (farmer, cow, bull or none).
//   farmer invites cows and bulls, hand milks.
//   cow    the milk game.   bull   joins; gameplay later.
//
// The server is the referee for anything between players: membership, who may invite whom, coins held for
// buy orders, in-person deliveries, and daily caps, so a modified plugin can't flood other players.

const ONLINE_WINDOW = 180;          // seconds since the last heartbeat that still counts as online
const HEARTBEAT_MIN = 20;           // ignore heartbeats faster than this (except when something changed)
const LISTING_MAX_AGE = 30 * 86400; // dairies not updated for 30 days drop out of the public directory
const ORDER_TTL = 7 * 86400;        // unfilled buy orders expire after a week (coins go back)
const INVITE_TTL = 7 * 86400;
const MAX_OPEN_ORDERS = 5;
const MAX_ORDER_OZ = 64;
const MIN_COINS_PER_OZ = 1;
const MAX_COINS_PER_OZ = 40;        // twice the best creamery price, so orders can't be used to hand out coins
const DAILY_POST_COINS = 3000;      // coins one install can put into buy orders per day
const DAILY_DELIVER_OZ = 400;       // more than anyone can make in a day at max level
const MAX_STALLS = 6;
const MAX_MEMBERS = 40;
const GRADES = 5;
const PLAY_ROLES = ["farmer", "cow", "bull", "none"];

// Dairy levels
const MAX_LEVEL = 20;
const MEMBER_DAILY_XP = 300;        // dairy XP one member can add per day from activity in the dairy
const XP_PER_BEAT = 60;             // most dairy XP one heartbeat can carry
const DELIVERY_XP_PER_OZ = 3;       // a filled buy order gives the dairy it was delivered to this much
const DELIVERER_XP_PER_OZ = 1;      // and the deliverer's own dairy this much
const MAX_DONATION = 100000;
const TIER_PRICE = [0, 0, 150, 400, 900];   // fund coins to raise a stall to this tier
const LEADERBOARD_SIZE = 20;
const INVITE_ROLES = ["farmer", "cow", "bull"];

export default {
  async fetch(request, env) {
    try {
      return await route(request, env);
    } catch (e) {
      if (e instanceof HttpError) return json({ error: e.message }, e.status);
      console.log("error", (e && e.stack) || e);
      return json({ error: "Server error." }, 500);
    }
  },
};

class HttpError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

const fail = (status, message) => { throw new HttpError(status, message); };
const now = () => Math.floor(Date.now() / 1000);
const today = () => new Date().toISOString().slice(0, 10);

function json(data, status = 200) {
  return new Response(JSON.stringify(data), { status, headers: { "content-type": "application/json" } });
}

async function body(request) {
  try {
    return await request.json();
  } catch {
    fail(400, "Bad request.");
  }
}

async function sha256(text) {
  const bytes = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(bytes)].map(b => b.toString(16).padStart(2, "0")).join("");
}

function randomId() {
  const b = new Uint8Array(12);
  crypto.getRandomValues(b);
  return [...b].map(x => x.toString(16).padStart(2, "0")).join("");
}

/** Plain text, no control characters, trimmed to a length. */
function clean(value, max) {
  return String(value ?? "").replace(/[\u0000-\u001f\u007f]/g, "").trim().slice(0, max);
}

function int(value, min, max, what) {
  const n = Number(value);
  if (!Number.isInteger(n) || n < min || n > max) fail(400, `Bad ${what}.`);
  return n;
}

function num(value, what) {
  const n = Number(value);
  if (!Number.isFinite(n) || Math.abs(n) > 100000) fail(400, `Bad ${what}.`);
  return n;
}

function houseId(value) {
  const s = String(value ?? "");
  if (!/^[0-9]{1,20}$/.test(s) || s === "0") fail(400, "Bad address.");
  return s;
}

/** Total XP needed to reach a level. Each level takes 250 more than the last (400, 650, 900...). */
function xpToReach(level) {
  let total = 0;
  for (let l = 1; l < level; l++) total += 400 + 250 * (l - 1);
  return total;
}
function levelFor(xp) {
  let l = 1;
  while (l < MAX_LEVEL && xp >= xpToReach(l + 1)) l++;
  return l;
}
/** Stall slots: 2 at level 1, one more at levels 3, 6, 9, 12, 15 and 18 (8 at most). */
const stallSlots = level => 2 + [3, 6, 9, 12, 15, 18].filter(l => level >= l).length;
/** Best stall tier allowed: 2 at first, 3 from level 5, 4 from level 12. */
const maxTier = level => level >= 12 ? 4 : level >= 5 ? 3 : 2;
/** Fund coins for the next stall: the first is free, then 200 more each. */
const stallPrice = count => 200 * count;
/** Monday (UTC) of this week, e.g. "2026-11-02". */
function weekKey(date = new Date()) {
  const d = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate()));
  d.setUTCDate(d.getUTCDate() - ((d.getUTCDay() + 6) % 7));
  return d.toISOString().slice(0, 10);
}

async function progressOf(env, dairyId) {
  const p = await env.DB.prepare("SELECT * FROM dairy_progress WHERE dairy_id = ?").bind(dairyId).first();
  const xp = p?.xp || 0;
  return { xp, fund: p?.fund || 0, weekXp: p && p.week === weekKey() ? p.week_xp : 0, level: levelFor(xp) };
}

function addXpStmt(env, dairyId, amount) {
  return env.DB.prepare(`
    INSERT INTO dairy_progress (dairy_id, xp, week, week_xp) VALUES (?, ?, ?, ?)
    ON CONFLICT(dairy_id) DO UPDATE SET
      xp = xp + excluded.xp,
      week_xp = CASE WHEN week = excluded.week THEN week_xp + excluded.week_xp ELSE excluded.week_xp END,
      week = excluded.week`).bind(dairyId, amount, weekKey(), amount);
}

/** Takes coins out of a dairy's fund; false when there aren't enough. */
async function spendFund(env, dairyId, coins) {
  if (coins <= 0) return true;
  const r = await env.DB.prepare("UPDATE dairy_progress SET fund = fund - ? WHERE dairy_id = ? AND fund >= ? RETURNING fund")
    .bind(coins, dairyId, coins).first();
  return !!r;
}

const isHash = v => /^[0-9a-f]{64}$/.test(String(v ?? ""));

// ---------------------------------------------------------------- auth

async function auth(request, env) {
  const id = request.headers.get("X-VD-Id") || "";
  const key = request.headers.get("X-VD-Key") || "";
  if (!/^[0-9a-f]{32}$/.test(id) || !/^[0-9a-f]{64}$/.test(key)) fail(401, "Missing id.");
  const hash = await sha256(key);
  let row = await env.DB.prepare("SELECT * FROM installs WHERE id = ?").bind(id).first();
  if (!row) {
    const t = now();
    await env.DB.prepare("INSERT INTO installs (id, key_hash, created, last_seen) VALUES (?, ?, ?, 0)").bind(id, hash, t).run();
    row = await env.DB.prepare("SELECT * FROM installs WHERE id = ?").bind(id).first();
  } else if (row.key_hash !== hash) {
    fail(401, "Wrong key.");
  }
  if (row.banned) fail(403, "This install is blocked from the network.");
  return row;
}

// ---------------------------------------------------------------- routes

async function route(request, env) {
  const url = new URL(request.url);
  const path = url.pathname.replace(/\/+$/, "");
  const method = request.method;

  if (method === "GET" && path === "/v1/stats") return json(await stats(env));

  const me = await auth(request, env);
  await expireOrders(env);

  const routes = [
    ["POST", /^\/v1\/heartbeat$/, () => heartbeat(env, me, request)],
    ["GET", /^\/v1\/dairies$/, () => listDairies(env, me)],
    ["GET", /^\/v1\/dairy\/mine$/, () => myDairy(env, me)],
    ["PUT", /^\/v1\/dairy$/, () => putDairy(env, me, request)],
    ["DELETE", /^\/v1\/dairy$/, () => deleteDairy(env, me)],
    ["POST", /^\/v1\/invites$/, () => invite(env, me, request)],
    ["POST", /^\/v1\/invites\/(\d+)\/accept$/, m => acceptInvite(env, me, Number(m[1]), request)],
    ["POST", /^\/v1\/invites\/(\d+)\/decline$/, m => declineInvite(env, me, Number(m[1]))],
    ["DELETE", /^\/v1\/invites\/(\d+)$/, m => cancelInvite(env, me, Number(m[1]))],
    ["POST", /^\/v1\/members\/leave$/, () => leave(env, me)],
    ["DELETE", /^\/v1\/members\/([0-9a-f]{32})$/, m => removeMember(env, me, m[1])],
    ["PUT", /^\/v1\/members\/([0-9a-f]{32})\/role$/, m => setRole(env, me, m[1], request)],
    ["POST", /^\/v1\/stalls$/, () => addStall(env, me, request)],
    ["DELETE", /^\/v1\/stalls\/([0-9a-f]{24})$/, m => removeStall(env, me, m[1])],
    ["POST", /^\/v1\/stalls\/([0-9a-f]{24})\/tier$/, m => upgradeStall(env, me, m[1], request)],
    ["GET", /^\/v1\/orders\/mine$/, () => myOrders(env, me)],
    ["POST", /^\/v1\/orders$/, () => postOrder(env, me, request)],
    ["GET", /^\/v1\/dairies\/([0-9a-f]{24})\/orders$/, m => dairyOrders(env, me, m[1])],
    ["DELETE", /^\/v1\/orders\/(\d+)$/, m => cancelOrder(env, me, Number(m[1]))],
    ["POST", /^\/v1\/orders\/(\d+)\/fill$/, m => fillOrder(env, me, Number(m[1]), request)],
    ["POST", /^\/v1\/dairy\/fund$/, () => donate(env, me, request)],
    ["GET", /^\/v1\/leaderboard$/, () => leaderboard(env, me)],
    ["GET", /^\/v1\/inbox$/, () => inbox(env, me)],
    ["POST", /^\/v1\/inbox\/claim$/, () => claim(env, me)],
  ];
  for (const [verb, re, fn] of routes) {
    const m = method === verb && path.match(re);
    if (m) return json(await fn(m));
  }
  fail(404, "Not found.");
}

// ---------------------------------------------------------------- helpers

async function membership(env, install) {
  return await env.DB.prepare(
    "SELECT m.*, d.owner = m.install AS is_owner, d.name AS dairy_name FROM members m JOIN dairies d ON d.id = m.dairy_id WHERE m.install = ?")
    .bind(install).first();
}

async function ownedDairy(env, me) {
  const d = await env.DB.prepare("SELECT * FROM dairies WHERE owner = ?").bind(me.id).first();
  if (!d) fail(403, "Only the dairy's owner can do that.");
  return d;
}

async function onlineCount(env) {
  const r = await env.DB.prepare("SELECT COUNT(*) AS n FROM installs WHERE last_seen >= ?").bind(now() - ONLINE_WINDOW).first();
  return r.n;
}

async function stats(env) {
  const d = await env.DB.prepare("SELECT COUNT(*) AS n, SUM(is_public) AS p FROM dairies WHERE updated >= ?")
    .bind(now() - LISTING_MAX_AGE).first();
  return { online: await onlineCount(env), dairies: d.n || 0, publicDairies: d.p || 0 };
}

async function dairyAt(env, hid) {
  if (!hid) return null;
  const s = String(hid);
  if (!/^[0-9]{1,20}$/.test(s)) return null;
  return await env.DB.prepare("SELECT * FROM dairies WHERE house_id = ?").bind(s).first();
}

function inboxStmt(env, install, kind, coins, oz, grade, note) {
  return env.DB.prepare("INSERT INTO inbox (install, kind, coins, oz, grade, note, created) VALUES (?, ?, ?, ?, ?, ?, ?)")
    .bind(install, kind, coins, oz, grade, note, now());
}

async function addInbox(env, install, kind, coins, oz, grade, note) {
  await inboxStmt(env, install, kind, coins, oz, grade, note).run();
}

const notice = (env, install, note) => addInbox(env, install, "notice", 0, 0, 0, note);

// ---------------------------------------------------------------- presence

async function heartbeat(env, me, request) {
  const b = await body(request);
  const t = now();
  const here = await dairyAt(env, b.houseId);
  const charHash = isHash(b.charHash) ? b.charHash : me.char_hash;
  if (t - me.last_seen >= HEARTBEAT_MIN || (here?.id ?? null) !== me.at_dairy || charHash !== me.char_hash) {
    await env.DB.prepare("UPDATE installs SET last_seen = ?, at_dairy = ?, version = ?, char_hash = ? WHERE id = ?")
      .bind(t, here ? here.id : null, clean(b.version, 20), charHash, me.id).run();
  }

  // Share a little status with your own dairy (milk, grade...) so the owner's HUD can show it
  const mem = await membership(env, me.id);
  if (mem && b.status && typeof b.status === "object") {
    const status = JSON.stringify(b.status).slice(0, 300);
    if (status !== mem.status) await env.DB.prepare("UPDATE members SET status = ? WHERE install = ?").bind(status, me.id).run();
  }

  // Dairy XP from what the member did since the last heartbeat. It only counts while the server sees them
  // in their own dairy (or delivering at someone else's public dairy), and only up to a daily amount.
  let dairyXp = 0;
  const asked = Math.max(0, Math.min(XP_PER_BEAT, Math.floor(Number(b.dairyXp) || 0)));
  const atHome = mem && here && here.id === mem.dairy_id;
  const visiting = mem && here && here.id !== mem.dairy_id && here.is_public;
  if (asked > 0 && (atHome || visiting)) {
    const day = today();
    const used = (await env.DB.prepare("SELECT xp FROM member_xp WHERE install = ? AND day = ?").bind(me.id, day).first())?.xp || 0;
    dairyXp = Math.min(asked, MEMBER_DAILY_XP - used);
    if (dairyXp > 0) {
      await env.DB.batch([
        env.DB.prepare(`INSERT INTO member_xp (install, day, xp) VALUES (?, ?, ?)
          ON CONFLICT(install, day) DO UPDATE SET xp = xp + excluded.xp`).bind(me.id, day, dairyXp),
        addXpStmt(env, mem.dairy_id, dairyXp),
      ]);
    } else dairyXp = 0;
  }

  const waiting = await env.DB.prepare("SELECT COUNT(*) AS n FROM inbox WHERE install = ? AND claimed IS NULL").bind(me.id).first();
  const invites = charHash ? (await env.DB.prepare(`
      SELECT i.id, i.role, d.name AS dairy, d.world_name AS world, d.is_public AS public
      FROM invites i JOIN dairies d ON d.id = i.dairy_id
      WHERE i.to_hash = ? AND i.status = 'pending' AND i.expires >= ?`).bind(charHash, t).all()).results : [];
  return {
    online: await onlineCount(env),
    here: here ? { id: here.id, name: here.name, isPublic: !!here.is_public, mine: !!mem && mem.dairy_id === here.id } : null,
    membership: mem ? { dairyId: mem.dairy_id, dairy: mem.dairy_name, role: mem.role, isOwner: !!mem.is_owner, level: (await progressOf(env, mem.dairy_id)).level } : null,
    dairyXp,
    invites: invites.map(i => ({ id: i.id, role: i.role, dairy: i.dairy, world: i.world })),
    inbox: waiting.n,
    time: t,
  };
}

// ---------------------------------------------------------------- directory

function listing(row, me) {
  const mine = row.owner === me.id;
  const open = !!row.is_public || mine;
  return {
    id: row.id,
    name: row.name,
    description: row.description,
    isPublic: !!row.is_public,
    mine,
    owner: row.show_owner ? row.owner_name : "",
    worldName: row.world_name,
    dcName: row.dc_name,
    // The address only goes to other players when the owner made the dairy public
    houseId: open ? row.house_id : null,
    world: open ? row.world : null,
    district: open ? row.district : "",
    districtKind: open ? row.district_kind : 0,
    ward: open ? row.ward : null,
    plot: open ? row.plot : null,
    room: open ? row.room : null,
    members: row.members || 0,
    cowsHere: row.here || 0,
    openOrders: open ? row.orders || 0 : 0,
    level: levelFor(row.xp || 0),
    weekXp: row.week === weekKey() ? row.week_xp || 0 : 0,
    updated: row.updated,
  };
}

async function listDairies(env, me) {
  const t = now();
  const rows = await env.DB.prepare(`
    SELECT d.*, p.xp, p.week, p.week_xp,
      (SELECT COUNT(*) FROM installs i WHERE i.at_dairy = d.id AND i.last_seen >= ?) AS here,
      (SELECT COUNT(*) FROM members m WHERE m.dairy_id = d.id) AS members,
      (SELECT COUNT(*) FROM orders o WHERE o.dairy_id = d.id AND o.status = 'open') AS orders
    FROM dairies d LEFT JOIN dairy_progress p ON p.dairy_id = d.id
    WHERE d.updated >= ? OR d.owner = ?
    ORDER BY COALESCE(p.xp, 0) DESC, here DESC, d.is_public DESC, d.updated DESC
    LIMIT 500`).bind(t - ONLINE_WINDOW, t - LISTING_MAX_AGE, me.id).all();
  return { online: await onlineCount(env), dairies: rows.results.map(r => listing(r, me)) };
}

/** Everything about the dairy you belong to: members (with their shared status), stalls, pending invites. */
async function myDairy(env, me) {
  const mem = await membership(env, me.id);
  if (!mem) return { dairy: null };
  const d = await env.DB.prepare("SELECT * FROM dairies WHERE id = ?").bind(mem.dairy_id).first();
  const t = now();
  const members = (await env.DB.prepare(`
      SELECT m.install, m.role, m.name, m.world_name, m.status, i.last_seen
      FROM members m LEFT JOIN installs i ON i.id = m.install WHERE m.dairy_id = ? ORDER BY m.joined`).bind(d.id).all()).results;
  const stalls = (await env.DB.prepare("SELECT * FROM stalls WHERE dairy_id = ? ORDER BY created").bind(d.id).all()).results;
  const canInvite = mem.is_owner || mem.role === "farmer";
  const invites = canInvite ? (await env.DB.prepare(
    "SELECT id, to_name, role, expires FROM invites WHERE dairy_id = ? AND status = 'pending' AND expires >= ? ORDER BY created")
    .bind(d.id, t).all()).results : [];
  return {
    dairy: {
      id: d.id, name: d.name, description: d.description, isPublic: !!d.is_public, showOwner: !!d.show_owner,
      houseId: d.house_id, world: d.world, worldName: d.world_name, district: d.district, districtKind: d.district_kind,
      ward: d.ward, plot: d.plot, room: d.room,
    },
    you: { id: me.id, role: mem.role, isOwner: !!mem.is_owner },
    members: members.map(m => ({
      id: m.install, name: m.name, world: m.world_name, role: m.role, isOwner: m.install === d.owner,
      online: (m.last_seen || 0) >= t - ONLINE_WINDOW, status: safeParse(m.status),
    })),
    stalls: stalls.map(s => ({ id: s.id, name: s.name, x: s.x, y: s.y, z: s.z, tier: s.tier })),
    progress: await progressOut(env, d.id, stalls.length),
    invites: invites.map(i => ({ id: i.id, name: i.to_name, role: i.role, expires: i.expires })),
  };
}

async function progressOut(env, dairyId, stallCount) {
  const p = await progressOf(env, dairyId);
  const donors = (await env.DB.prepare("SELECT name, coins FROM donations WHERE dairy_id = ? AND coins > 0 ORDER BY coins DESC LIMIT 10")
    .bind(dairyId).all()).results;
  return {
    xp: p.xp, level: p.level, levelXp: xpToReach(p.level), nextXp: p.level >= MAX_LEVEL ? null : xpToReach(p.level + 1),
    fund: p.fund, weekXp: p.weekXp, week: weekKey(),
    stallSlots: stallSlots(p.level), maxTier: maxTier(p.level), nextStallPrice: stallPrice(stallCount), tierPrices: TIER_PRICE,
    donors: donors.map(x => ({ name: x.name, coins: x.coins })),
  };
}

function safeParse(s) {
  try {
    return s ? JSON.parse(s) : null;
  } catch {
    return null;
  }
}

/**
 * Registers or updates your dairy. The plugin only offers this inside your own estate; the server makes sure
 * an address is registered once and each owner has one dairy. Registering makes you a member (leaving any other dairy).
 */
async function putDairy(env, me, request) {
  const b = await body(request);
  const name = clean(b.name, 32);
  if (!name) fail(400, "Give your dairy a name.");
  const hid = houseId(b.houseId);
  const world = int(b.world, 1, 100000, "world");
  const ward = int(b.ward, 1, 60, "ward");
  const plot = int(b.plot, 1, 120, "plot");
  const room = int(b.room ?? 0, 0, 1000, "room");
  const districtKind = int(b.districtKind ?? 0, 0, 1000, "district");
  const role = PLAY_ROLES.includes(b.role) ? b.role : "cow";
  const t = now();

  const atAddress = await dairyAt(env, hid);
  if (atAddress && atAddress.owner !== me.id) fail(409, "This address is already registered as a dairy.");
  const existing = await env.DB.prepare("SELECT * FROM dairies WHERE owner = ?").bind(me.id).first();
  if (existing && existing.house_id !== hid) fail(409, `You already own ${existing.name}. Remove it before registering another.`);

  const id = existing ? existing.id : randomId();
  await env.DB.prepare(`
    INSERT INTO dairies (id, owner, house_id, name, description, is_public, show_owner, owner_name, world, world_name, dc_name,
                         district, district_kind, ward, plot, room, updated)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    ON CONFLICT(owner) DO UPDATE SET
      name = excluded.name, description = excluded.description, is_public = excluded.is_public,
      show_owner = excluded.show_owner, owner_name = excluded.owner_name, world_name = excluded.world_name,
      dc_name = excluded.dc_name, district = excluded.district, district_kind = excluded.district_kind, updated = excluded.updated`)
    .bind(id, me.id, hid, name, clean(b.description, 140), b.isPublic ? 1 : 0, b.showOwner ? 1 : 0,
      clean(b.ownerName, 40), world, clean(b.worldName, 30), clean(b.dcName, 30),
      clean(b.district, 30), districtKind, ward, plot, room, t).run();

  if (!existing) {
    // The owner joins their own dairy (leaving any other)
    const old = await membership(env, me.id);
    if (old && old.dairy_id !== id) await env.DB.prepare("DELETE FROM members WHERE install = ?").bind(me.id).run();
    await env.DB.prepare(
      "INSERT INTO members (install, dairy_id, role, name, world_name, joined) VALUES (?, ?, ?, ?, ?, ?) ON CONFLICT(install) DO UPDATE SET dairy_id = excluded.dairy_id, role = excluded.role")
      .bind(me.id, id, role, clean(b.ownerName, 40), clean(b.worldName, 30), t).run();
  }

  // Going private closes the shop: open buy orders are cancelled and refunded
  if (!b.isPublic) await refundOpenOrders(env, me.id, "Your dairy went private, so this buy order was cancelled.");
  return { id };
}

async function deleteDairy(env, me) {
  const d = await ownedDairy(env, me);
  await refundOpenOrders(env, me.id, "Your dairy was closed, so this buy order was cancelled.");
  const others = (await env.DB.prepare("SELECT install FROM members WHERE dairy_id = ? AND install != ?").bind(d.id, me.id).all()).results;
  for (const m of others) await notice(env, m.install, `${d.name} closed, so you're no longer a member.`);
  await env.DB.batch([
    env.DB.prepare("DELETE FROM members WHERE dairy_id = ?").bind(d.id),
    env.DB.prepare("DELETE FROM stalls WHERE dairy_id = ?").bind(d.id),
    env.DB.prepare("DELETE FROM dairy_progress WHERE dairy_id = ?").bind(d.id),
    env.DB.prepare("DELETE FROM donations WHERE dairy_id = ?").bind(d.id),
    env.DB.prepare("UPDATE invites SET status = 'cancelled' WHERE dairy_id = ? AND status = 'pending'").bind(d.id),
    env.DB.prepare("DELETE FROM dairies WHERE id = ?").bind(d.id),
  ]);
  return { ok: true };
}

// ---------------------------------------------------------------- invites and members

/** Owners invite farmers, cows and bulls; farmers invite cows and bulls. */
async function invite(env, me, request) {
  const b = await body(request);
  const mem = await membership(env, me.id);
  if (!mem) fail(403, "Join or register a dairy first.");
  const role = String(b.role ?? "");
  if (!INVITE_ROLES.includes(role)) fail(400, "Bad role.");
  if (!mem.is_owner && !(mem.role === "farmer" && (role === "cow" || role === "bull")))
    fail(403, mem.role === "farmer" ? "Farmers can invite cows and bulls, not farmers." : "Only the owner and farmers can invite.");
  if (!isHash(b.toHash)) fail(400, "Bad player.");
  const toName = clean(b.toName, 40);

  const count = await env.DB.prepare("SELECT COUNT(*) AS n FROM members WHERE dairy_id = ?").bind(mem.dairy_id).first();
  if (count.n >= MAX_MEMBERS) fail(400, `A dairy can have up to ${MAX_MEMBERS} members.`);
  const already = await env.DB.prepare(
    "SELECT 1 FROM members m JOIN installs i ON i.id = m.install WHERE m.dairy_id = ? AND i.char_hash = ?").bind(mem.dairy_id, b.toHash).first();
  if (already) fail(409, `${toName} is already in your dairy.`);
  const t = now();
  await env.DB.prepare("UPDATE invites SET status = 'cancelled' WHERE dairy_id = ? AND to_hash = ? AND status = 'pending'")
    .bind(mem.dairy_id, b.toHash).run();
  const r = await env.DB.prepare(
    "INSERT INTO invites (dairy_id, from_install, to_hash, to_name, role, created, expires) VALUES (?, ?, ?, ?, ?, ?, ?) RETURNING id")
    .bind(mem.dairy_id, me.id, b.toHash, toName, role, t, t + INVITE_TTL).first();
  return { id: r.id };
}

async function pendingInviteFor(env, me, id, charHash) {
  const hash = isHash(charHash) ? charHash : me.char_hash;
  const i = await env.DB.prepare("SELECT * FROM invites WHERE id = ? AND status = 'pending' AND expires >= ?").bind(id, now()).first();
  if (!i || !hash || i.to_hash !== hash) fail(404, "That invite isn't for you, or it expired.");
  return i;
}

async function acceptInvite(env, me, id, request) {
  const b = await body(request);
  const i = await pendingInviteFor(env, me, id, b.charHash);
  const owned = await env.DB.prepare("SELECT name FROM dairies WHERE owner = ?").bind(me.id).first();
  if (owned) fail(409, `You own ${owned.name}. Close it before joining another dairy.`);
  const d = await env.DB.prepare("SELECT * FROM dairies WHERE id = ?").bind(i.dairy_id).first();
  if (!d) fail(404, "That dairy closed.");
  const old = await membership(env, me.id);
  await env.DB.batch([
    env.DB.prepare("DELETE FROM members WHERE install = ?").bind(me.id),
    env.DB.prepare("INSERT INTO members (install, dairy_id, role, name, world_name, joined) VALUES (?, ?, ?, ?, ?, ?)")
      .bind(me.id, d.id, i.role, clean(b.name, 40) || i.to_name, clean(b.worldName, 30), now()),
    env.DB.prepare("UPDATE invites SET status = 'accepted' WHERE id = ?").bind(id),
  ]);
  await notice(env, i.from_install, `${i.to_name} joined ${d.name} as a ${i.role}.`);
  return { ok: true, dairy: d.name, role: i.role, left: old ? old.dairy_name : null };
}

async function declineInvite(env, me, id) {
  const i = await pendingInviteFor(env, me, id, null);
  await env.DB.prepare("UPDATE invites SET status = 'declined' WHERE id = ?").bind(id).run();
  await notice(env, i.from_install, `${i.to_name} declined the invite.`);
  return { ok: true };
}

async function cancelInvite(env, me, id) {
  const i = await env.DB.prepare("SELECT * FROM invites WHERE id = ? AND status = 'pending'").bind(id).first();
  if (!i) fail(404, "That invite is already gone.");
  const mem = await membership(env, me.id);
  if (i.from_install !== me.id && !(mem && mem.is_owner && mem.dairy_id === i.dairy_id)) fail(403, "Not your invite.");
  await env.DB.prepare("UPDATE invites SET status = 'cancelled' WHERE id = ?").bind(id).run();
  return { ok: true };
}

async function leave(env, me) {
  const mem = await membership(env, me.id);
  if (!mem) fail(404, "You aren't in a dairy.");
  if (mem.is_owner) fail(400, "You own this dairy. Close it instead (Dairy tab).");
  await env.DB.prepare("DELETE FROM members WHERE install = ?").bind(me.id).run();
  return { ok: true };
}

/** Owners remove anyone but themselves; farmers remove cows and bulls. */
async function removeMember(env, me, target) {
  const mem = await membership(env, me.id);
  const them = await membership(env, target);
  if (!mem || !them || them.dairy_id !== mem.dairy_id) fail(404, "They aren't in your dairy.");
  if (them.is_owner) fail(400, "The owner can't be removed.");
  const allowed = mem.is_owner || (mem.role === "farmer" && (them.role === "cow" || them.role === "bull"));
  if (!allowed) fail(403, "You can't remove them.");
  await env.DB.prepare("DELETE FROM members WHERE install = ?").bind(target).run();
  await notice(env, target, `You were removed from ${mem.dairy_name}.`);
  return { ok: true };
}

async function setRole(env, me, target, request) {
  const b = await body(request);
  const mem = await membership(env, me.id);
  if (!mem || !mem.is_owner) fail(403, "Only the owner can change roles.");
  const them = await membership(env, target);
  if (!them || them.dairy_id !== mem.dairy_id) fail(404, "They aren't in your dairy.");
  const allowed = them.is_owner ? PLAY_ROLES : INVITE_ROLES;
  if (!allowed.includes(b.role)) fail(400, "Bad role.");
  await env.DB.prepare("UPDATE members SET role = ? WHERE install = ?").bind(b.role, target).run();
  if (target !== me.id) await notice(env, target, `You're now a ${b.role} at ${mem.dairy_name}.`);
  return { ok: true };
}

// ---------------------------------------------------------------- stalls

async function addStall(env, me, request) {
  const b = await body(request);
  const d = await ownedDairy(env, me);
  const count = await env.DB.prepare("SELECT COUNT(*) AS n FROM stalls WHERE dairy_id = ?").bind(d.id).first();
  const p = await progressOf(env, d.id);
  const limit = stallSlots(p.level);
  if (count.n >= limit) fail(400, `Your dairy has room for ${limit} stall(s) at level ${p.level}. Level up for more.`);
  const price = stallPrice(count.n);
  if (!(await spendFund(env, d.id, price))) fail(400, `A new stall costs ${price} coins from the dairy fund.`);
  const id = randomId();
  await env.DB.prepare("INSERT INTO stalls (id, dairy_id, name, x, y, z, tier, created) VALUES (?, ?, ?, ?, ?, ?, 1, ?)")
    .bind(id, d.id, clean(b.name, 24) || `Stall ${count.n + 1}`, num(b.x, "x"), num(b.y, "y"), num(b.z, "z"), now()).run();
  return { id };
}

async function removeStall(env, me, id) {
  const d = await ownedDairy(env, me);
  const r = await env.DB.prepare("DELETE FROM stalls WHERE id = ? AND dairy_id = ? RETURNING id").bind(id, d.id).first();
  if (!r) fail(404, "That stall is already gone.");
  return { ok: true };
}

/** One tier at a time, up to 4. The owner's plugin takes the coins. */
async function upgradeStall(env, me, id, request) {
  const b = await body(request);
  const d = await ownedDairy(env, me);
  const tier = int(b.tier, 2, 4, "tier");
  const p = await progressOf(env, d.id);
  if (tier > maxTier(p.level)) fail(400, `Your dairy needs level ${tier >= 4 ? 12 : 5} for that.`);
  const s = await env.DB.prepare("SELECT tier FROM stalls WHERE id = ? AND dairy_id = ?").bind(id, d.id).first();
  if (!s || s.tier !== tier - 1) fail(409, "That stall changed; refresh and try again.");
  if (!(await spendFund(env, d.id, TIER_PRICE[tier]))) fail(400, `That upgrade costs ${TIER_PRICE[tier]} coins from the dairy fund.`);
  const r = await env.DB.prepare("UPDATE stalls SET tier = ? WHERE id = ? AND dairy_id = ? AND tier = ? RETURNING id")
    .bind(tier, id, d.id, tier - 1).first();
  if (!r) {
    await env.DB.prepare("UPDATE dairy_progress SET fund = fund + ? WHERE dairy_id = ?").bind(TIER_PRICE[tier], d.id).run();
    fail(409, "That stall changed; refresh and try again.");
  }
  return { ok: true };
}

// ---------------------------------------------------------------- dairy fund and leaderboard

/** Any member can put coins into their dairy's fund. Their plugin takes the coins first. */
async function donate(env, me, request) {
  const b = await body(request);
  const mem = await membership(env, me.id);
  if (!mem) fail(400, "Join a dairy first.");
  const coins = int(b.coins, 1, MAX_DONATION, "coins");
  await env.DB.batch([
    env.DB.prepare(`INSERT INTO dairy_progress (dairy_id, fund, week) VALUES (?, ?, ?)
      ON CONFLICT(dairy_id) DO UPDATE SET fund = fund + excluded.fund`).bind(mem.dairy_id, coins, weekKey()),
    env.DB.prepare(`INSERT INTO donations (dairy_id, install, name, coins) VALUES (?, ?, ?, ?)
      ON CONFLICT(dairy_id, install) DO UPDATE SET coins = coins + excluded.coins, name = excluded.name`)
      .bind(mem.dairy_id, me.id, mem.name || "", coins),
  ]);
  const p = await progressOf(env, mem.dairy_id);
  return { ok: true, fund: p.fund };
}

/** This week's most active dairies (week starts Monday 00:00 UTC). */
async function leaderboard(env, me) {
  const week = weekKey();
  const mem = await membership(env, me.id);
  const rows = (await env.DB.prepare(`
    SELECT d.id, d.name, d.is_public, d.world_name, p.xp, p.week_xp
    FROM dairy_progress p JOIN dairies d ON d.id = p.dairy_id
    WHERE p.week = ? AND p.week_xp > 0
    ORDER BY p.week_xp DESC LIMIT ?`).bind(week, LEADERBOARD_SIZE).all()).results;
  return {
    week,
    dairies: rows.map((r, i) => ({
      rank: i + 1, id: r.id, name: r.name, isPublic: !!r.is_public, worldName: r.world_name,
      level: levelFor(r.xp), weekXp: r.week_xp, mine: !!mem && mem.dairy_id === r.id,
    })),
  };
}

// ---------------------------------------------------------------- buy orders

function orderOut(o) {
  return { id: o.id, dairyId: o.dairy_id, oz: o.oz, minGrade: o.min_grade, coins: o.coins, expires: o.expires, status: o.status };
}

async function expireOrders(env) {
  const rows = await env.DB.prepare(
    "UPDATE orders SET status = 'expired' WHERE status = 'open' AND expires < ? RETURNING id, owner, coins, oz").bind(now()).all();
  for (const o of rows.results) {
    await addInbox(env, o.owner, "refund", o.coins, 0, 0, `Your buy order for ${o.oz} oz expired; ${o.coins} coins came back.`);
  }
}

async function refundOpenOrders(env, owner, note) {
  const rows = await env.DB.prepare(
    "UPDATE orders SET status = 'cancelled' WHERE owner = ? AND status = 'open' RETURNING id, coins").bind(owner).all();
  for (const o of rows.results) await addInbox(env, owner, "refund", o.coins, 0, 0, note);
}

async function myOrders(env, me) {
  const rows = await env.DB.prepare("SELECT * FROM orders WHERE owner = ? AND status = 'open' ORDER BY created DESC").bind(me.id).all();
  return { orders: rows.results.map(orderOut) };
}

async function dairyOrders(env, me, dairyId) {
  const d = await env.DB.prepare("SELECT * FROM dairies WHERE id = ?").bind(dairyId).first();
  if (!d || (!d.is_public && d.owner !== me.id)) fail(404, "That dairy isn't public.");
  const rows = await env.DB.prepare(
    "SELECT * FROM orders WHERE dairy_id = ? AND status = 'open' ORDER BY coins * 1.0 / oz DESC LIMIT 20").bind(dairyId).all();
  return { orders: rows.results.map(orderOut) };
}

async function postOrder(env, me, request) {
  const b = await body(request);
  const d = await ownedDairy(env, me);
  if (!d.is_public) fail(400, "Make your dairy public to post buy orders.");
  const oz = int(b.oz, 1, MAX_ORDER_OZ, "amount");
  const minGrade = int(b.minGrade, 0, GRADES - 1, "grade");
  const coins = int(b.coins, oz * MIN_COINS_PER_OZ, oz * MAX_COINS_PER_OZ, "price");
  const open = await env.DB.prepare("SELECT COUNT(*) AS n FROM orders WHERE owner = ? AND status = 'open'").bind(me.id).first();
  if (open.n >= MAX_OPEN_ORDERS) fail(400, `You can have ${MAX_OPEN_ORDERS} buy orders open at a time.`);

  const day = today();
  const posted = me.post_day === day ? me.post_coins : 0;
  if (posted + coins > DAILY_POST_COINS) fail(400, `You can put up to ${DAILY_POST_COINS} coins into buy orders a day.`);

  const t = now();
  const r = await env.DB.prepare(
    "INSERT INTO orders (dairy_id, owner, oz, min_grade, coins, created, expires) VALUES (?, ?, ?, ?, ?, ?, ?) RETURNING id")
    .bind(d.id, me.id, oz, minGrade, coins, t, t + ORDER_TTL).first();
  await env.DB.prepare("UPDATE installs SET post_day = ?, post_coins = ? WHERE id = ?").bind(day, posted + coins, me.id).run();
  return { id: r.id };
}

async function cancelOrder(env, me, id) {
  const o = await env.DB.prepare(
    "UPDATE orders SET status = 'cancelled' WHERE id = ? AND owner = ? AND status = 'open' RETURNING coins, oz").bind(id, me.id).first();
  if (!o) fail(404, "That order is already gone.");
  await addInbox(env, me.id, "refund", o.coins, 0, 0, `You cancelled your buy order for ${o.oz} oz; ${o.coins} coins came back.`);
  return { ok: true };
}

/**
 * Hands milk to another dairy's buy order. The deliverer must be standing in that dairy, and can deliver at most
 * DAILY_DELIVER_OZ a day. The seller is paid and the owner gets the milk through their inboxes.
 */
async function fillOrder(env, me, id, request) {
  const b = await body(request);
  const grade = int(b.grade, 0, GRADES - 1, "grade");
  const o = await env.DB.prepare("SELECT * FROM orders WHERE id = ?").bind(id).first();
  if (!o || o.status !== "open") fail(409, "Someone already filled that order.");
  if (o.owner === me.id) fail(400, "You can't fill your own order.");
  if (grade < o.min_grade) fail(400, "Your milk's grade is too low for this order.");

  const d = await env.DB.prepare("SELECT * FROM dairies WHERE id = ?").bind(o.dairy_id).first();
  if (!d || !d.is_public) fail(409, "That dairy isn't taking deliveries.");
  const here = await dairyAt(env, b.houseId);
  if (!here || here.id !== d.id) fail(400, `Deliveries are made in person: go to ${d.name} first.`);

  const day = today();
  const delivered = me.deliver_day === day ? me.deliver_oz : 0;
  if (delivered + o.oz > DAILY_DELIVER_OZ) fail(400, `You can deliver up to ${DAILY_DELIVER_OZ} oz a day.`);

  // Only one deliverer can win the order
  const won = await env.DB.prepare(
    "UPDATE orders SET status = 'filled', filled_by = ?, filled_at = ?, grade = ? WHERE id = ? AND status = 'open' RETURNING id")
    .bind(me.id, now(), grade, id).first();
  if (!won) fail(409, "Someone already filled that order.");

  await env.DB.batch([
    env.DB.prepare("UPDATE installs SET deliver_day = ?, deliver_oz = ? WHERE id = ?").bind(day, delivered + o.oz, me.id),
    inboxStmt(env, me.id, "payout", o.coins, 0, 0, `${d.name} paid ${o.coins} coins for your ${o.oz} oz.`),
    inboxStmt(env, o.owner, "milk", 0, o.oz, grade, `A delivery filled your buy order: ${o.oz} oz of Grade ${gradeName(grade)}.`),
    addXpStmt(env, d.id, o.oz * DELIVERY_XP_PER_OZ),
  ]);
  const home = await membership(env, me.id);
  if (home && home.dairy_id !== d.id) await addXpStmt(env, home.dairy_id, o.oz * DELIVERER_XP_PER_OZ).run();
  return { ok: true, coins: o.coins, oz: o.oz, dairy: d.name };
}

function gradeName(g) {
  return ["C", "B", "A", "S", "S+"][g] || "?";
}

// ---------------------------------------------------------------- inbox

async function inbox(env, me) {
  const rows = await env.DB.prepare("SELECT * FROM inbox WHERE install = ? AND claimed IS NULL ORDER BY id").bind(me.id).all();
  return { items: rows.results.map(itemOut) };
}

function itemOut(r) {
  return { id: r.id, kind: r.kind, coins: r.coins, oz: r.oz, grade: r.grade, note: r.note };
}

/** Marks everything waiting as claimed and returns it in the same step, so nothing is applied twice. */
async function claim(env, me) {
  const rows = await env.DB.prepare("UPDATE inbox SET claimed = ? WHERE install = ? AND claimed IS NULL RETURNING *")
    .bind(now(), me.id).all();
  return { items: rows.results.map(itemOut) };
}
