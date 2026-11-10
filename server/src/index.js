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
const DAILY_DELIVER_OZ = 1000;      // more than anyone can make in a day at max level
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
const GRADE_PRICE = [7, 10, 15, 21, 30];   // coins per oz the dairy pays a cow, by grade (C..S+)
const PAY_PER_LEVEL = 0.02;         // each dairy level adds 2% to what it pays
const FARMER_CUT = 0.25;            // a farmer gets this share of what the cow was paid for a hand milking
const FARMER_DAILY_COINS = 4500;
const COW_DAILY_HAND_OZ = 800;
const MAX_TAKE_OZ = 96;
const IN_PERSON_BONUS = 0.1;
// Baked treats members buy off their dairy's shelf: item -> default price and the owner's allowed range (coins each)
const SHELF_ITEMS = { cookies: { name: "Butter cookies", price: 40, min: 10, max: 150 } };
const SHELF_BAKER_SHARE = 0.8;      // the rest goes to the dairy fund
const SHELF_MAX_STOCK = 200;        // per item per dairy      // delivering a buy order in person at the buyer's public area pays 10% more
const CLEAN_SECONDS = 3600;         // a cleaned stall stays fresh this long
const CLEAN_AGAIN_SECONDS = 1800;   // and can be cleaned again once this much (or less) is left
const GROOM_COOLDOWN = 900;         // one groom per farmer per cow every 15 minutes
// Developer (same phrase hash as the plugin's dev mode)
const DEV_KEY_HASH = "7d4e0a8c4a26de8d2843e2a563f667d74beda3b81ffa447c22bd40d93b5af76f";
// Weekly contracts
const CONTRACT_BASE_OZ = 200, CONTRACT_OZ_PER_LEVEL = 40, CONTRACT_MAX_OZ = 1200;
const CONTRACT_XP_PER_OZ = 1.5, CONTRACT_FUND_PER_OZ = 4;             // most milk a farmer or owner can carry out of the tank at once
const PUMP_PER_BEAT = 120;          // most oz one heartbeat can add to the dairy tank
const MEMBER_DAILY_PUMP = 1500;     // most oz one member can add to a dairy tank per day
const INVITE_ROLES = ["farmer", "cow", "bull"];
const ROLE_ORDER = ["cow", "farmer", "bull"];

// A member can hold several roles at once; they're stored as "cow,farmer,bull" ("none" or "" for none).
const roleList = r => ROLE_ORDER.filter(x => String(r || "").split(",").includes(x));
const hasRole = (mem, r) => !!mem && roleList(mem.role).includes(r);
const roleText = list => list.length ? list.join(",") : "none";
const primaryRole = r => roleList(r)[0] || "none";
/** SQL test for "this member has role ?" on members.role */
const ROLE_SQL = "(',' || role || ',') LIKE '%,' || ? || ',%'";

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
/** Stalls aren't limited by level any more, only by the fund (and a sanity cap). */
const STALL_CAP = 16;
const stallSlots = () => STALL_CAP;
/** Best stall tier allowed: 2 at first, 3 from level 5, 4 from level 12. */
const maxTier = level => level >= 12 ? 4 : level >= 5 ? 3 : 2;
/** Fund coins for the next stall: the first is free, then they climb fast (250, 1,000, 2,250, 4,000, 6,250...). */
const stallPrice = count => 250 * count * count;
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
    ["POST", /^\/v1\/members\/([0-9a-f]{32})\/roles$/, m => changeRoles(env, me, m[1], request)],
    ["POST", /^\/v1\/stalls$/, () => addStall(env, me, request)],
    ["DELETE", /^\/v1\/stalls\/([0-9a-f]{24})$/, m => removeStall(env, me, m[1])],
    ["PUT", /^\/v1\/stalls\/([0-9a-f]{24})$/, m => moveStall(env, me, m[1], request)],
    ["POST", /^\/v1\/stalls\/([0-9a-f]{24})\/tier$/, m => upgradeStall(env, me, m[1], request)],
    ["POST", /^\/v1\/stalls\/([0-9a-f]{24})\/clean$/, m => cleanStall(env, me, m[1])],
    ["POST", /^\/v1\/groom$/, () => groom(env, me, request)],
    ["GET", /^\/v1\/shelf$/, () => shelfView(env, me)],
    ["POST", /^\/v1\/shelf\/stock$/, () => shelfStock(env, me, request)],
    ["POST", /^\/v1\/shelf\/buy$/, () => shelfBuy(env, me, request)],
    ["PUT", /^\/v1\/shelf\/price$/, () => shelfPrice(env, me, request)],
    ["PUT", /^\/v1\/dairy\/private$/, () => putPrivate(env, me, request)],
    ["DELETE", /^\/v1\/dairy\/private$/, () => deletePrivate(env, me)],
    ["GET", /^\/v1\/orders\/board$/, () => orderBoard(env, me)],
    ["GET", /^\/v1\/orders\/mine$/, () => myOrders(env, me)],
    ["POST", /^\/v1\/orders$/, () => postOrder(env, me, request)],
    ["GET", /^\/v1\/dairies\/([0-9a-f]{24})\/orders$/, m => dairyOrders(env, me, m[1])],
    ["DELETE", /^\/v1\/orders\/(\d+)$/, m => cancelOrder(env, me, Number(m[1]))],
    ["POST", /^\/v1\/orders\/(\d+)\/fill$/, m => fillOrder(env, me, Number(m[1]), request)],
    ["POST", /^\/v1\/dairy\/fund$/, () => donate(env, me, request)],
    ["POST", /^\/v1\/dairy\/tank\/take$/, () => takeFromTank(env, me, request)],
    ["POST", /^\/v1\/handpay$/, () => handPay(env, me, request)],
    ["POST", /^\/v1\/dairy\/contract\/ship$/, () => shipToContract(env, me, request)],
    ["POST", /^\/v1\/carry\/use$/, () => useCarried(env, me, request)],
    ["PUT", /^\/v1\/dev\/creamery$/, () => setCreamery(env, request)],
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

// ---------------------------------------------------------------- dairy areas (boxes)

const AREA_MIN = 4, AREA_MAX = 80, AREA_HEIGHT_MAX = 40;

/** The box's four corners on the ground (x, z), using the game's rotation (0 faces +z). */
function corners(a) {
  const fx = Math.sin(a.rot), fz = Math.cos(a.rot);    // forward
  const rx = Math.cos(a.rot), rz = -Math.sin(a.rot);   // right
  const hl = a.length / 2, hw = a.width / 2;
  return [[1, 1], [1, -1], [-1, -1], [-1, 1]].map(([f, r]) => [a.x + fx * hl * f + rx * hw * r, a.z + fz * hl * f + rz * hw * r]);
}

/** Is a point inside the box (and within its height)? */
function inArea(a, p) {
  if (Math.abs(p.y - a.y) > a.height) return false;
  const dx = p.x - a.x, dz = p.z - a.z;
  const along = dx * Math.sin(a.rot) + dz * Math.cos(a.rot);
  const across = dx * Math.cos(a.rot) - dz * Math.sin(a.rot);
  return Math.abs(along) <= a.length / 2 && Math.abs(across) <= a.width / 2;
}

/** Do two boxes overlap on the ground? (separating axis test) A whole-house place overlaps anything in that house. */
function overlaps(a, b) {
  if (a.whole || b.whole) return true;   // callers only compare boxes in the same zone, world and house
  const ca = corners(a), cb = corners(b);
  for (const poly of [ca, cb]) {
    for (let i = 0; i < 4; i++) {
      const [x1, z1] = poly[i], [x2, z2] = poly[(i + 1) % 4];
      const nx = z2 - z1, nz = x1 - x2;
      const proj = pts => pts.map(([x, z]) => x * nx + z * nz);
      const pa = proj(ca), pb = proj(cb);
      if (Math.max(...pa) < Math.min(...pb) || Math.max(...pb) < Math.min(...pa)) return false;
    }
  }
  return Math.abs(a.y - b.y) <= a.height + b.height;
}

function readArea(v) {
  if (!v || typeof v !== "object") return null;
  const num = (n, lo, hi, what) => {
    const x = Number(n);
    if (!Number.isFinite(x) || x < lo || x > hi) fail(400, `Bad ${what}.`);
    return x;
  };
  const house = v.house ? String(v.house) : "";
  if (house && !/^[0-9]{1,20}$/.test(house)) fail(400, "Bad house.");
  return {
    territory: int(v.territory, 1, 100000, "zone"), world: int(v.world, 1, 100000, "world"), house,
    x: num(v.x, -5000, 5000, "position"), y: num(v.y, -5000, 5000, "position"), z: num(v.z, -5000, 5000, "position"),
    width: num(v.width, AREA_MIN, AREA_MAX, "width"), length: num(v.length, AREA_MIN, AREA_MAX, "length"),
    height: num(v.height ?? 6, 1, AREA_HEIGHT_MAX, "height"), rot: num(v.rot ?? 0, -10, 10, "facing"),
    mapId: int(v.mapId ?? 0, 0, 100000, "map"), zone: clean(v.zone, 60),
    whole: v.wholeHouse === true ? 1 : 0,
  };
}

const areaRow = r => r && ({
  territory: r.territory, world: r.world, house: r.house, x: r.x, y: r.y, z: r.z,
  width: r.width, length: r.length, height: r.height, rot: r.rot, mapId: r.map_id, zone: r.zone, wholeHouse: !!r.whole,
});

/**
 * The dairy whose box contains this position, if any. With `memberOf` (a dairy id), that dairy's private place
 * counts too: only its own members ever find themselves "in" a private place.
 */
async function dairyInArea(env, pos, memberOf = null) {
  if (!pos || typeof pos !== "object") return null;
  const territory = Number(pos.territory), world = Number(pos.world), house = pos.house ? String(pos.house) : "";
  const p = { x: Number(pos.x), y: Number(pos.y), z: Number(pos.z) };
  if (!Number.isFinite(territory) || !Number.isFinite(world) || !Number.isFinite(p.x)) return null;
  const rows = (await env.DB.prepare("SELECT * FROM dairy_area WHERE territory = ? AND world = ? AND house = ?").bind(territory, world, house).all()).results;
  const hit = rows.find(r => inArea(r, p));
  if (hit) return await env.DB.prepare("SELECT * FROM dairies WHERE id = ?").bind(hit.dairy_id).first();
  if (!memberOf) return null;
  const priv = await env.DB.prepare("SELECT * FROM dairy_private WHERE dairy_id = ? AND territory = ? AND world = ? AND house = ?")
    .bind(memberOf, territory, world, house).first();
  if (!priv || !(priv.whole || inArea(priv, p))) return null;
  const d = await env.DB.prepare("SELECT * FROM dairies WHERE id = ?").bind(memberOf).first();
  return d ? { ...d, in_private: 1 } : null;
}

/** Every box (public areas and private places) in this zone, world and house, except the given dairy's own. */
async function boxesAt(env, a, exceptDairy) {
  const q = "WHERE territory = ? AND world = ? AND house = ?";
  const pub = (await env.DB.prepare(`SELECT * FROM dairy_area ${q}`).bind(a.territory, a.world, a.house).all()).results;
  const priv = (await env.DB.prepare(`SELECT * FROM dairy_private ${q}`).bind(a.territory, a.world, a.house).all()).results;
  return [...pub, ...priv].filter(r => r.dairy_id !== exceptDairy);
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
  const home = await membership(env, me.id);
  const here = (await dairyInArea(env, b.pos, home ? home.dairy_id : null)) || (await dairyAt(env, b.houseId));
  const charHash = isHash(b.charHash) ? b.charHash : me.char_hash;
  if (t - me.last_seen >= HEARTBEAT_MIN || (here?.id ?? null) !== me.at_dairy || charHash !== me.char_hash) {
    await env.DB.prepare("UPDATE installs SET last_seen = ?, at_dairy = ?, version = ?, char_hash = ? WHERE id = ?")
      .bind(t, here ? here.id : null, clean(b.version, 20), charHash, me.id).run();
  }

  // Share a little status with your own dairy (milk, grade...) so the owner's HUD can show it
  const mem = await membership(env, me.id);
  if (mem && b.status && typeof b.status === "object") {
    // Only known fields, kept small: what a member shares with their own dairy
    const st = b.status || {};
    const num = v => Number.isFinite(Number(v)) ? Math.round(Number(v) * 10) / 10 : 0;
    const status = JSON.stringify({
      milk: num(st.milk), cap: num(st.cap), grade: Math.max(0, Math.min(4, Math.floor(num(st.grade)))), spoiled: !!st.spoiled,
      inDairy: !!st.inDairy, level: Math.floor(num(st.level)),
      farmerLevel: Math.floor(num(st.farmerLevel)), bullLevel: Math.floor(num(st.bullLevel)),
      quality: Math.floor(num(st.quality)), totalOz: Math.floor(num(st.totalOz)),
      emotes: Array.isArray(st.emotes) ? st.emotes.slice(0, 8).map(e => clean(e, 24)).filter(Boolean) : [],
      handBy: clean(st.handBy, 40),
      milking: clean(st.milking, 40),   // a farmer: the cow they pressed Milk on (hand milking only counts then)
      // A cow on a stall, so everyone's stall highlight shows it: which stall, awake/dozing/asleep, and the milking drain
      stall: clean(st.stall, 40), wake: Math.max(0, Math.min(2, Math.floor(num(st.wake)))),
      drainLeft: Math.max(0, Math.min(1, num(st.drainLeft))), drainRate: Math.max(0, Math.min(1, num(st.drainRate))), drainAt: Math.floor(num(st.drainAt)),
      anyEmote: st.anyEmote === undefined ? true : !!st.anyEmote,   // older plugins don't say; any looping emote is their default
    });
    if (status !== mem.status) await env.DB.prepare("UPDATE members SET status = ? WHERE install = ?").bind(status, me.id).run();
  }

  // Dairy XP from what the member did since the last heartbeat. It only counts while the server sees them
  // in their own dairy (or delivering at someone else's public dairy), and only up to a daily amount.
  let dairyXp = 0;
  const asked = Math.max(0, Math.min(XP_PER_BEAT, Math.floor(Number(b.dairyXp) || 0)));
  const atHome = mem && here && here.id === mem.dairy_id;
  const visiting = mem && here && here.id !== mem.dairy_id && here.is_public;
  if (asked > 0 && b.awake === true && (atHome || visiting)) {
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

  // Milk from milkings in the dairy (stalls or hand) goes into the dairy's tank too (only while awake in your own dairy)
  let pumped = 0;
  const pump = Array.isArray(b.pumped) ? b.pumped.slice(0, 5).map(v => Math.max(0, Math.min(PUMP_PER_BEAT, Number(v) || 0))) : [];
  const asking = pump.reduce((a, v) => a + v, 0);
  if (asking > 0 && b.awake === true && atHome) {
    const day = today();
    const used = (await env.DB.prepare("SELECT oz FROM member_pump WHERE install = ? AND day = ?").bind(me.id, day).first())?.oz || 0;
    const room = Math.max(0, Math.min(PUMP_PER_BEAT, MEMBER_DAILY_PUMP - used));
    const scale = asking > room ? room / asking : 1;
    const stmts = [];
    pump.forEach((oz, grade) => {
      const add = Math.round(oz * scale * 100) / 100;
      if (add <= 0) return;
      pumped += add;
      stmts.push(env.DB.prepare(`INSERT INTO dairy_tank (dairy_id, grade, oz) VALUES (?, ?, ?)
        ON CONFLICT(dairy_id, grade) DO UPDATE SET oz = oz + excluded.oz`).bind(mem.dairy_id, grade, add));
    });
    if (pumped > 0) {
      stmts.push(env.DB.prepare(`INSERT INTO member_pump (install, day, oz) VALUES (?, ?, ?)
        ON CONFLICT(install, day) DO UPDATE SET oz = oz + excluded.oz`).bind(me.id, day, pumped));
      stmts.push(env.DB.prepare(`INSERT INTO tank_contrib (dairy_id, install, name, oz) VALUES (?, ?, ?, ?)
        ON CONFLICT(dairy_id, install) DO UPDATE SET oz = oz + excluded.oz, name = excluded.name`).bind(mem.dairy_id, me.id, mem.name || "", pumped));
      await env.DB.batch(stmts);
    }
  }

  if (b.atCreamery === true)
    await env.DB.prepare(`INSERT INTO creamery_presence (install, at) VALUES (?, ?)
      ON CONFLICT(install) DO UPDATE SET at = excluded.at`).bind(me.id, t).run();

  const waiting = await env.DB.prepare("SELECT COUNT(*) AS n FROM inbox WHERE install = ? AND claimed IS NULL").bind(me.id).first();
  const invites = charHash ? (await env.DB.prepare(`
      SELECT i.id, i.role, d.name AS dairy, d.world_name AS world, d.is_public AS public
      FROM invites i JOIN dairies d ON d.id = i.dairy_id
      WHERE i.to_hash = ? AND i.status = 'pending' AND i.expires >= ?`).bind(charHash, t).all()).results : [];
  return {
    online: await onlineCount(env),
    here: here ? { id: here.id, name: here.name, isPublic: !!here.is_public && !here.in_private, mine: !!mem && mem.dairy_id === here.id, private: !!here.in_private } : null,
    membership: mem ? { dairyId: mem.dairy_id, dairy: mem.dairy_name, role: primaryRole(mem.role), roles: roleList(mem.role), isOwner: !!mem.is_owner, level: (await progressOf(env, mem.dairy_id)).level } : null,
    dairyXp,
    pumped,
    creamery: await creameryOf(env),
    carried: await carriedOf(env, me.id),
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
    houseId: open && /^[0-9]+$/.test(row.house_id) ? row.house_id : null,
    area: open && row.a_territory ? {
      territory: row.a_territory, world: row.a_world, house: row.a_house, x: row.a_x, y: row.a_y, z: row.a_z,
      mapId: row.a_map, zone: row.a_zone,
    } : null,
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
      a.territory AS a_territory, a.world AS a_world, a.house AS a_house, a.x AS a_x, a.y AS a_y, a.z AS a_z, a.map_id AS a_map, a.zone AS a_zone,
      (SELECT COUNT(*) FROM installs i WHERE i.at_dairy = d.id AND i.last_seen >= ?) AS here,
      (SELECT COUNT(*) FROM members m WHERE m.dairy_id = d.id) AS members,
      (SELECT COUNT(*) FROM orders o WHERE o.dairy_id = d.id AND o.status = 'open') AS orders
    FROM dairies d LEFT JOIN dairy_progress p ON p.dairy_id = d.id LEFT JOIN dairy_area a ON a.dairy_id = d.id
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
      SELECT m.install, m.role, m.name, m.world_name, m.status, i.last_seen, i.version
      FROM members m LEFT JOIN installs i ON i.id = m.install WHERE m.dairy_id = ? ORDER BY m.joined`).bind(d.id).all()).results;
  const stalls = (await env.DB.prepare(
    "SELECT s.*, COALESCE(c.until, 0) AS clean_until FROM stalls s LEFT JOIN stall_clean c ON c.stall_id = s.id WHERE s.dairy_id = ? ORDER BY s.created")
    .bind(d.id).all()).results;
  const canInvite = mem.is_owner || hasRole(mem, "farmer");
  const invites = canInvite ? (await env.DB.prepare(
    "SELECT id, to_name, role, expires FROM invites WHERE dairy_id = ? AND status = 'pending' AND expires >= ? ORDER BY created")
    .bind(d.id, t).all()).results : [];
  return {
    dairy: {
      id: d.id, name: d.name, description: d.description, isPublic: !!d.is_public, showOwner: !!d.show_owner,
      houseId: d.house_id, world: d.world, worldName: d.world_name, district: d.district, districtKind: d.district_kind,
      ward: d.ward, plot: d.plot, room: d.room,
      area: areaRow(await env.DB.prepare("SELECT * FROM dairy_area WHERE dairy_id = ?").bind(d.id).first()),
      privateArea: areaRow(await env.DB.prepare("SELECT * FROM dairy_private WHERE dairy_id = ?").bind(d.id).first()),
    },
    you: { id: me.id, role: primaryRole(mem.role), roles: roleList(mem.role), isOwner: !!mem.is_owner },
    members: members.map(m => ({
      id: m.install, name: m.name, world: m.world_name, role: primaryRole(m.role), roles: roleList(m.role), isOwner: m.install === d.owner,
      online: (m.last_seen || 0) >= t - ONLINE_WINDOW, lastSeen: m.last_seen || 0, version: m.version || "", status: safeParse(m.status),
    })),
    stalls: stalls.map(s => ({ id: s.id, name: s.name, x: s.x, y: s.y, z: s.z, tier: s.tier, cleanUntil: s.clean_until > t ? s.clean_until : 0 })),
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
    tank: await tankOf(env, dairyId),
    payBonus: payBonus(p.level),
    contract: await contractOut(env, dairyId, p.level),
  };
}

/** The dairy's milk tank by grade, and its top pumpers. */
async function tankOf(env, dairyId) {
  const rows = (await env.DB.prepare("SELECT grade, oz FROM dairy_tank WHERE dairy_id = ?").bind(dairyId).all()).results;
  const byGrade = [0, 0, 0, 0, 0];
  for (const r of rows) if (r.grade >= 0 && r.grade < 5) byGrade[r.grade] = Math.round(r.oz * 10) / 10;
  const top = (await env.DB.prepare("SELECT name, oz FROM tank_contrib WHERE dairy_id = ? ORDER BY oz DESC LIMIT 10").bind(dairyId).all()).results;
  return {
    byGrade,
    total: Math.round(byGrade.reduce((a, v) => a + v, 0) * 10) / 10,
    pumpers: top.map(x => ({ name: x.name, oz: Math.round(x.oz * 10) / 10 })),
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
  const area = readArea(b.area);
  if (area) area.whole = 0;   // whole-house is for private places only
  const existing = await env.DB.prepare("SELECT * FROM dairies WHERE owner = ?").bind(me.id).first();
  const ownerRoles = Array.isArray(b.roles) ? roleList(b.roles.join(",")) : PLAY_ROLES.includes(b.role) ? roleList(b.role) : ["cow"];
  const role = roleText(ownerRoles);
  const t = now();
  let hid, world, ward, plot, room, districtKind;
  if (area) {
    // A box anywhere: it can't overlap another dairy's box in the same zone, world and house
    const others = await boxesAt(env, area, existing ? existing.id : null);
    if (others.some(r => overlaps(r, area))) fail(409, "That overlaps another dairy. Move or shrink your box.");
    hid = `area:${me.id}`;
    world = area.world; ward = 0; plot = 0; room = 0; districtKind = 0;
  } else if (existing) {
    if (b.houseId != null && String(b.houseId) !== existing.house_id) fail(409, `You already own ${existing.name}. Remove it before registering another.`);
    // Just updating the name, listing or role
    hid = existing.house_id; world = existing.world; ward = existing.ward; plot = existing.plot; room = existing.room; districtKind = existing.district_kind;
  } else {
    hid = houseId(b.houseId);
    world = int(b.world, 1, 100000, "world");
    ward = int(b.ward, 1, 60, "ward");
    plot = int(b.plot, 1, 120, "plot");
    room = int(b.room ?? 0, 0, 1000, "room");
    districtKind = int(b.districtKind ?? 0, 0, 1000, "district");
    const atAddress = await dairyAt(env, hid);
    if (atAddress && atAddress.owner !== me.id) fail(409, "This address is already registered as a dairy.");
  }

  const id = existing ? existing.id : randomId();
  await env.DB.prepare(`
    INSERT INTO dairies (id, owner, house_id, name, description, is_public, show_owner, owner_name, world, world_name, dc_name,
                         district, district_kind, ward, plot, room, updated)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    ON CONFLICT(owner) DO UPDATE SET
      name = excluded.name, description = excluded.description, is_public = excluded.is_public,
      show_owner = excluded.show_owner, owner_name = excluded.owner_name, world_name = excluded.world_name,
      dc_name = excluded.dc_name, district = excluded.district, district_kind = excluded.district_kind, updated = excluded.updated,
      house_id = excluded.house_id, world = excluded.world, ward = excluded.ward, plot = excluded.plot, room = excluded.room`)
    .bind(id, me.id, hid, name, clean(b.description, 140), b.isPublic ? 1 : 0, b.showOwner ? 1 : 0,
      clean(b.ownerName, 40), world, clean(b.worldName, 30), clean(b.dcName, 30),
      clean(area ? area.zone : b.district, 30), districtKind, ward, plot, room, t).run();

  if (area) {
    await env.DB.prepare(`INSERT INTO dairy_area (dairy_id, territory, world, house, x, y, z, width, length, height, rot, map_id, zone)
      VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT(dairy_id) DO UPDATE SET territory = excluded.territory, world = excluded.world, house = excluded.house,
        x = excluded.x, y = excluded.y, z = excluded.z, width = excluded.width, length = excluded.length, height = excluded.height,
        rot = excluded.rot, map_id = excluded.map_id, zone = excluded.zone`)
      .bind(id, area.territory, area.world, area.house, area.x, area.y, area.z, area.width, area.length, area.height, area.rot, area.mapId, area.zone).run();
  }

  if (!existing) {
    // The owner joins their own dairy (leaving any other)
    const old = await membership(env, me.id);
    if (old && old.dairy_id !== id) await env.DB.prepare("DELETE FROM members WHERE install = ?").bind(me.id).run();
    await env.DB.prepare(
      "INSERT INTO members (install, dairy_id, role, name, world_name, joined) VALUES (?, ?, ?, ?, ?, ?) ON CONFLICT(install) DO UPDATE SET dairy_id = excluded.dairy_id, role = excluded.role")
      .bind(me.id, id, role, clean(b.ownerName, 40), clean(b.worldName, 30), t).run();
  }

  // Private dairies keep their buy orders: farmers deliver to them from their own dairies
  return { id };
}

/** The owner sets their dairy's private place (a home or apartment): only members ever count as "in" it. */
async function putPrivate(env, me, request) {
  const d = await ownedDairy(env, me);
  const area = readArea((await body(request)).area);
  if (!area) fail(400, "Mark out the private place first.");
  if (area.whole && !area.house) fail(400, "Go inside the house or apartment to use the whole of it.");
  if ((await boxesAt(env, area, d.id)).some(r => overlaps(r, area))) fail(409, "That overlaps another dairy. Move or shrink the box.");
  await env.DB.prepare(`INSERT INTO dairy_private (dairy_id, territory, world, house, x, y, z, width, length, height, rot, map_id, zone, whole)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    ON CONFLICT(dairy_id) DO UPDATE SET territory = excluded.territory, world = excluded.world, house = excluded.house,
      x = excluded.x, y = excluded.y, z = excluded.z, width = excluded.width, length = excluded.length, height = excluded.height,
      rot = excluded.rot, map_id = excluded.map_id, zone = excluded.zone, whole = excluded.whole`)
    .bind(d.id, area.territory, area.world, area.house, area.x, area.y, area.z, area.width, area.length, area.height, area.rot, area.mapId, area.zone, area.whole).run();
  return { ok: true };
}

async function deletePrivate(env, me) {
  const d = await ownedDairy(env, me);
  await env.DB.prepare("DELETE FROM dairy_private WHERE dairy_id = ?").bind(d.id).run();
  return { ok: true };
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
    env.DB.prepare("DELETE FROM dairy_tank WHERE dairy_id = ?").bind(d.id),
    env.DB.prepare("DELETE FROM tank_contrib WHERE dairy_id = ?").bind(d.id),
    env.DB.prepare("DELETE FROM contracts WHERE dairy_id = ?").bind(d.id),
    env.DB.prepare("DELETE FROM dairy_area WHERE dairy_id = ?").bind(d.id),
    env.DB.prepare("DELETE FROM dairy_private WHERE dairy_id = ?").bind(d.id),
    env.DB.prepare("DELETE FROM shelf WHERE dairy_id = ?").bind(d.id),
    env.DB.prepare("DELETE FROM shelf_price WHERE dairy_id = ?").bind(d.id),
    env.DB.prepare("DELETE FROM contract_contrib WHERE dairy_id = ?").bind(d.id),
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
  if (!mem.is_owner && !hasRole(mem, "farmer")) fail(403, "Only the owner and farmers can invite.");
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
  if (old && old.dairy_id === d.id) {
    // Already in this dairy: the invite adds the role
    await env.DB.batch([
      env.DB.prepare("UPDATE members SET role = ? WHERE install = ?").bind(roleText(roleList(old.role + "," + i.role)), me.id),
      env.DB.prepare("UPDATE invites SET status = 'accepted' WHERE id = ?").bind(id),
    ]);
    await notice(env, i.from_install, `${i.to_name} is now also a ${i.role} at ${d.name}.`);
    return { ok: true, dairy: d.name, role: i.role, left: null };
  }
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
  const allowed = mem.is_owner || (hasRole(mem, "farmer") && !hasRole(them, "farmer"));
  if (!allowed) fail(403, "You can't remove them.");
  await env.DB.prepare("DELETE FROM members WHERE install = ?").bind(target).run();
  await notice(env, target, `You were removed from ${mem.dairy_name}.`);
  return { ok: true };
}

/**
 * Add or remove one role. Anyone can add cow or bull to themselves; farmer is given by a farmer or the owner
 * (owners can give themselves anything); only the owner takes roles away.
 */
async function changeRoles(env, me, target, request) {
  const b = await body(request);
  const mem = await membership(env, me.id);
  if (!mem) fail(400, "Join a dairy first.");
  const them = target === me.id ? mem : await membership(env, target);
  if (!them || them.dairy_id !== mem.dairy_id) fail(404, "They aren't in your dairy.");
  const add = b.add ? String(b.add) : null, remove = b.remove ? String(b.remove) : null;
  const self = target === me.id;
  let list = roleList(them.role);
  if (add) {
    if (!INVITE_ROLES.includes(add)) fail(400, "Bad role.");
    const ok = mem.is_owner
      || (add === "farmer" ? hasRole(mem, "farmer") && !self : self || hasRole(mem, "farmer"));
    if (!ok) fail(403, add === "farmer" ? "Farmer is given by a farmer or the owner." : "Only the owner and farmers can give others a role.");
    if (!list.includes(add)) list = roleList(list.concat(add).join(","));
  }
  if (remove) {
    if (!mem.is_owner) fail(403, "Only the owner can take a role away.");
    list = list.filter(r => r !== remove);
  }
  await env.DB.prepare("UPDATE members SET role = ? WHERE install = ?").bind(roleText(list), target).run();
  if (!self) await notice(env, target, add ? `You're now also a ${add} at ${mem.dairy_name}.` : `You're no longer a ${remove} at ${mem.dairy_name}.`);
  return { ok: true, roles: list };
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
  if (count.n >= STALL_CAP) fail(400, `A dairy can have up to ${STALL_CAP} stalls.`);
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

/** The owner moves a stall to a new seat; it keeps its name and tier. */
async function moveStall(env, me, id, request) {
  const b = await body(request);
  const d = await ownedDairy(env, me);
  const num = v => {
    const x = Number(v);
    if (!Number.isFinite(x) || Math.abs(x) > 5000) fail(400, "Bad position.");
    return x;
  };
  const name = b.name != null ? clean(b.name, 24) : null;
  const r = await env.DB.prepare("UPDATE stalls SET x = ?, y = ?, z = ?, name = COALESCE(?, name) WHERE id = ? AND dairy_id = ? RETURNING id")
    .bind(num(b.x), num(b.y), num(b.z), name || null, id, d.id).first();
  if (!r) fail(404, "That stall is gone.");
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

// ---------------------------------------------------------------- farmer chores

/** A farmer (or the owner) cleans a stall in their dairy: fresh for an hour, again once half an hour is left. */
async function cleanStall(env, me, id) {
  const mem = await membership(env, me.id);
  if (!mem) fail(400, "Join a dairy first.");
  if (!mem.is_owner && !hasRole(mem, "farmer")) fail(403, "Only farmers and the owner clean stalls.");
  if (!inOwnDairy(me, mem)) fail(400, "Go to your dairy to clean its stalls.");
  const s = await env.DB.prepare("SELECT id FROM stalls WHERE id = ? AND dairy_id = ?").bind(id, mem.dairy_id).first();
  if (!s) fail(404, "That stall is gone.");
  const t = now();
  const c = await env.DB.prepare("SELECT until FROM stall_clean WHERE stall_id = ?").bind(id).first();
  if (c && c.until - t > CLEAN_AGAIN_SECONDS) fail(400, `That stall is still clean. It can be cleaned again in ${Math.ceil((c.until - t - CLEAN_AGAIN_SECONDS) / 60)} min.`);
  const until = t + CLEAN_SECONDS;
  await env.DB.prepare(`INSERT INTO stall_clean (stall_id, dairy_id, until, by_name) VALUES (?, ?, ?, ?)
    ON CONFLICT(stall_id) DO UPDATE SET until = excluded.until, by_name = excluded.by_name`).bind(id, mem.dairy_id, until, mem.name || "").run();
  return { ok: true, until };
}

/** A farmer grooms a cow of their dairy: the cow's plugin gets a Happy top-up through their inbox. */
async function groom(env, me, request) {
  const b = await body(request);
  const mem = await membership(env, me.id);
  if (!mem || !hasRole(mem, "farmer")) fail(403, "Only farmers groom cows.");
  if (!inOwnDairy(me, mem)) fail(400, "Go to your dairy to groom its cows.");
  const cowId = String(b.cow || "");
  if (cowId === me.id) fail(400, "You can't groom yourself.");
  const cow = await env.DB.prepare(`SELECT install, name FROM members WHERE install = ? AND dairy_id = ? AND ${ROLE_SQL}`)
    .bind(cowId, mem.dairy_id, "cow").first();
  if (!cow) fail(400, "That cow isn't in your dairy.");
  const t = now();
  const last = await env.DB.prepare("SELECT at FROM grooms WHERE farmer = ? AND cow = ?").bind(me.id, cow.install).first();
  if (last && t - last.at < GROOM_COOLDOWN) fail(400, `You can groom ${cow.name} again in ${Math.ceil((GROOM_COOLDOWN - (t - last.at)) / 60)} min.`);
  await env.DB.batch([
    env.DB.prepare(`INSERT INTO grooms (farmer, cow, at) VALUES (?, ?, ?) ON CONFLICT(farmer, cow) DO UPDATE SET at = excluded.at`).bind(me.id, cow.install, t),
    inboxStmt(env, cow.install, "groom", 0, 0, 0, `${mem.name || "A farmer"} groomed you. Happy +30 min.`),
  ]);
  return { ok: true, next: t + GROOM_COOLDOWN };
}

// ---------------------------------------------------------------- the dairy shelf

async function shelfPriceOf(env, dairyId, item) {
  const row = await env.DB.prepare("SELECT price FROM shelf_price WHERE dairy_id = ? AND item = ?").bind(dairyId, item).first();
  return row ? row.price : SHELF_ITEMS[item].price;
}

function shelfItem(b) {
  const item = String(b.item || "");
  if (!SHELF_ITEMS[item]) fail(400, "That can't go on the shelf.");
  return item;
}

/** What's on your dairy's shelf: each item, how many, and its price. */
async function shelfView(env, me) {
  const mem = await membership(env, me.id);
  if (!mem) return { items: [] };
  const rows = (await env.DB.prepare("SELECT item, SUM(qty) AS qty FROM shelf WHERE dairy_id = ? GROUP BY item").bind(mem.dairy_id).all()).results;
  const items = [];
  for (const [item, def] of Object.entries(SHELF_ITEMS)) {
    const r = rows.find(x => x.item === item);
    items.push({ item, name: def.name, qty: r ? r.qty : 0, price: await shelfPriceOf(env, mem.dairy_id, item), min: def.min, max: def.max });
  }
  return { items };
}

/** A farmer (or the owner) puts what they baked on the shelf. */
async function shelfStock(env, me, request) {
  const b = await body(request);
  const item = shelfItem(b);
  const qty = int(b.qty, 1, 50, "qty");
  const mem = await membership(env, me.id);
  if (!mem || !(mem.is_owner || hasRole(mem, "farmer"))) fail(403, "Only farmers and the owner stock the shelf.");
  const have = await env.DB.prepare("SELECT COALESCE(SUM(qty), 0) AS n FROM shelf WHERE dairy_id = ? AND item = ?").bind(mem.dairy_id, item).first();
  if (have.n + qty > SHELF_MAX_STOCK) fail(400, `The shelf holds up to ${SHELF_MAX_STOCK} ${SHELF_ITEMS[item].name.toLowerCase()}.`);
  await env.DB.prepare("INSERT INTO shelf (dairy_id, item, baker, baker_name, qty, created) VALUES (?, ?, ?, ?, ?, ?)")
    .bind(mem.dairy_id, item, me.id, mem.name || "", qty, now()).run();
  return { ok: true, qty: have.n + qty };
}

/** A member buys one off the shelf: the baker gets their share (inbox), the fund gets the rest. Coins come from the buyer's plugin. */
async function shelfBuy(env, me, request) {
  const b = await body(request);
  const item = shelfItem(b);
  const mem = await membership(env, me.id);
  if (!mem) fail(400, "Join a dairy first.");
  if (!inOwnDairy(me, mem)) fail(400, "Go to your dairy to buy from its shelf.");
  const price = await shelfPriceOf(env, mem.dairy_id, item);
  // Oldest batch first; only one buyer can take each one
  for (let tries = 0; tries < 3; tries++) {
    const row = await env.DB.prepare("SELECT * FROM shelf WHERE dairy_id = ? AND item = ? AND qty > 0 ORDER BY created, id LIMIT 1")
      .bind(mem.dairy_id, item).first();
    if (!row) fail(409, `The shelf is out of ${SHELF_ITEMS[item].name.toLowerCase()}.`);
    const took = await env.DB.prepare("UPDATE shelf SET qty = qty - 1 WHERE id = ? AND qty > 0 RETURNING qty").bind(row.id).first();
    if (!took) continue;
    const bakerCut = row.baker === me.id ? 0 : Math.floor(price * SHELF_BAKER_SHARE);
    const fundCut = price - bakerCut;
    const stmts = [
      env.DB.prepare("DELETE FROM shelf WHERE id = ? AND qty <= 0").bind(row.id),
      env.DB.prepare(`INSERT INTO dairy_progress (dairy_id, fund, week) VALUES (?, ?, ?)
        ON CONFLICT(dairy_id) DO UPDATE SET fund = fund + excluded.fund`).bind(mem.dairy_id, fundCut, weekKey()),
    ];
    if (bakerCut > 0) stmts.push(inboxStmt(env, row.baker, "payout", bakerCut, 0, 0, `${mem.name || "Someone"} bought your ${SHELF_ITEMS[item].name.toLowerCase()}: +${bakerCut} coins.`));
    await env.DB.batch(stmts);
    return { ok: true, price, baker: row.baker_name, bakerCut, fundCut };
  }
  fail(409, "Someone else just bought that. Try again.");
}

/** The owner sets a shelf item's price, within its range. */
async function shelfPrice(env, me, request) {
  const b = await body(request);
  const item = shelfItem(b);
  const d = await ownedDairy(env, me);
  const def = SHELF_ITEMS[item];
  const price = int(b.price, def.min, def.max, "price");
  await env.DB.prepare(`INSERT INTO shelf_price (dairy_id, item, price) VALUES (?, ?, ?)
    ON CONFLICT(dairy_id, item) DO UPDATE SET price = excluded.price`).bind(d.id, item, price).run();
  return { ok: true, price };
}

// ---------------------------------------------------------------- pay, tank jugs

const payBonus = level => Math.round((1 + PAY_PER_LEVEL * (level - 1)) * 100) / 100;

/** Is this install standing in its own dairy right now (as of its last heartbeat)? */
function inOwnDairy(me, mem) {
  return !!mem && me.at_dairy === mem.dairy_id && me.last_seen >= now() - ONLINE_WINDOW;
}

/** Farmers and owners fill jugs from the dairy's tank to deliver to orders. */
async function takeFromTank(env, me, request) {
  const b = await body(request);
  const mem = await membership(env, me.id);
  if (!mem) fail(400, "Join a dairy first.");
  if (!mem.is_owner && !hasRole(mem, "farmer")) fail(403, "Only farmers and the owner fill jugs from the dairy tank.");
  if (!inOwnDairy(me, mem)) fail(400, "Go to your dairy to fill jugs from its tank.");
  const grade = int(b.grade, 0, GRADES - 1, "grade");
  const oz = int(b.oz, 1, MAX_TAKE_OZ, "oz");
  const r = await env.DB.prepare("UPDATE dairy_tank SET oz = oz - ? WHERE dairy_id = ? AND grade = ? AND oz >= ? RETURNING oz")
    .bind(oz, mem.dairy_id, grade, oz).first();
  if (!r) fail(400, `The tank doesn't have ${oz} oz of Grade ${gradeName(grade)}.`);
  await env.DB.prepare(`INSERT INTO carried (install, oz) VALUES (?, ?) ON CONFLICT(install) DO UPDATE SET oz = oz + excluded.oz`).bind(me.id, oz).run();
  return { ok: true, oz, grade, left: Math.round(r.oz * 10) / 10 };
}

/** A cow's plugin reports a hand milking; the farmer who did it gets a share, in their inbox. */
async function handPay(env, me, request) {
  const b = await body(request);
  const mem = await membership(env, me.id);
  if (!mem || !hasRole(mem, "cow")) fail(400, "Only cows report hand milkings.");
  if (!inOwnDairy(me, mem)) fail(400, "Hand milkings count inside your dairy.");
  const grade = int(b.grade, 0, GRADES - 1, "grade");
  const oz = Math.max(0, Math.min(200, Number(b.oz) || 0));
  if (oz <= 0) fail(400, "No milk.");
  const name = clean(b.farmer, 40), world = clean(b.world, 30);
  const farmer = await env.DB.prepare(
    `SELECT install, name FROM members WHERE dairy_id = ? AND ${ROLE_SQL} AND name = ? AND world_name = ?`).bind(mem.dairy_id, "farmer", name, world).first();
  if (!farmer || farmer.install === me.id) fail(400, "That farmer isn't in your dairy.");

  const day = today();
  const cowKey = `hand:${me.id}`, farmKey = `farm:${farmer.install}`;
  const cowUsed = (await env.DB.prepare("SELECT oz FROM member_pump WHERE install = ? AND day = ?").bind(cowKey, day).first())?.oz || 0;
  const farmUsed = (await env.DB.prepare("SELECT oz FROM member_pump WHERE install = ? AND day = ?").bind(farmKey, day).first())?.oz || 0;
  const counted = Math.min(oz, COW_DAILY_HAND_OZ - cowUsed);
  if (counted <= 0) return { ok: true, coins: 0 };
  const p = await progressOf(env, mem.dairy_id);
  const coins = Math.min(Math.round(counted * GRADE_PRICE[grade] * payBonus(p.level) * FARMER_CUT), FARMER_DAILY_COINS - farmUsed);
  if (coins <= 0) return { ok: true, coins: 0 };
  await env.DB.batch([
    env.DB.prepare(`INSERT INTO member_pump (install, day, oz) VALUES (?, ?, ?)
      ON CONFLICT(install, day) DO UPDATE SET oz = oz + excluded.oz`).bind(cowKey, day, counted),
    env.DB.prepare(`INSERT INTO member_pump (install, day, oz) VALUES (?, ?, ?)
      ON CONFLICT(install, day) DO UPDATE SET oz = oz + excluded.oz`).bind(farmKey, day, coins),
    inboxStmt(env, farmer.install, "payout", coins, 0, 0, `${mem.name || "A cow"} paid you ${coins} coins for hand milking ${Math.round(counted * 10) / 10} oz.`),
  ]);
  return { ok: true, coins };
}

// ---------------------------------------------------------------- weekly contracts

const contractMinGrade = level => level >= 15 ? 3 : level >= 10 ? 2 : level >= 5 ? 1 : 0;

/** This week's contract for a dairy, made the first time anyone looks (sized by the dairy's level then). */
async function contractFor(env, dairyId, level) {
  const week = weekKey();
  let c = await env.DB.prepare("SELECT * FROM contracts WHERE dairy_id = ? AND week = ?").bind(dairyId, week).first();
  if (c) return c;
  const need = Math.min(CONTRACT_MAX_OZ, CONTRACT_BASE_OZ + CONTRACT_OZ_PER_LEVEL * (level - 1));
  await env.DB.prepare(`INSERT OR IGNORE INTO contracts (dairy_id, week, oz_need, min_grade, reward_xp, reward_fund) VALUES (?, ?, ?, ?, ?, ?)`)
    .bind(dairyId, week, need, contractMinGrade(level), Math.round(need * CONTRACT_XP_PER_OZ), Math.round(need * CONTRACT_FUND_PER_OZ)).run();
  return await env.DB.prepare("SELECT * FROM contracts WHERE dairy_id = ? AND week = ?").bind(dairyId, week).first();
}

async function contractOut(env, dairyId, level) {
  const c = await contractFor(env, dairyId, level);
  const top = (await env.DB.prepare("SELECT name, oz FROM contract_contrib WHERE dairy_id = ? AND week = ? ORDER BY oz DESC LIMIT 5")
    .bind(dairyId, c.week).all()).results;
  return {
    week: c.week, ozNeed: c.oz_need, ozDone: Math.round(c.oz_done * 10) / 10, minGrade: c.min_grade,
    rewardXp: c.reward_xp, rewardFund: c.reward_fund, completed: !!c.completed,
    shippers: top.map(x => ({ name: x.name, oz: Math.round(x.oz * 10) / 10 })),
  };
}

/** Farmers and the owner bring jugs to the Grand Creamery and ship them to their dairy's contract. Finishing it pays out once. */
async function shipToContract(env, me, request) {
  const b = await body(request);
  const mem = await membership(env, me.id);
  if (!mem) fail(400, "Join a dairy first.");
  if (!mem.is_owner && !hasRole(mem, "farmer")) fail(403, "Only farmers and the owner ship milk to the contract.");
  const creamery = await creameryOf(env);
  if (creamery) {
    const seen = await env.DB.prepare("SELECT at FROM creamery_presence WHERE install = ?").bind(me.id).first();
    if (!seen || seen.at < now() - ONLINE_WINDOW) fail(400, `Bring your jugs to ${creamery.name} to ship them.`);
  } else if (!inOwnDairy(me, mem)) fail(400, "Go to your dairy to ship milk.");
  const grade = int(b.grade, 0, GRADES - 1, "grade");
  const p = await progressOf(env, mem.dairy_id);
  const c = await contractFor(env, mem.dairy_id, p.level);
  if (c.completed) fail(409, "This week's contract is already done.");
  if (grade < c.min_grade) fail(400, `This contract needs Grade ${gradeName(c.min_grade)} or better.`);
  const want = Math.min(int(b.oz, 1, CONTRACT_MAX_OZ, "oz"), Math.ceil(c.oz_need - c.oz_done));
  if (want <= 0) fail(409, "This week's contract is already full.");
  if (!(await useCarriedOz(env, me.id, want))) fail(400, "You can only ship milk you've carried out of your dairy's tank.");

  const done = await env.DB.prepare(
    "UPDATE contracts SET oz_done = oz_done + ? WHERE dairy_id = ? AND week = ? AND completed IS NULL RETURNING oz_done, oz_need")
    .bind(want, mem.dairy_id, c.week).first();
  if (!done) {
    await env.DB.prepare("UPDATE carried SET oz = oz + ? WHERE install = ?").bind(want, me.id).run();
    fail(409, "This week's contract was just finished.");
  }

  await env.DB.prepare(`INSERT INTO contract_contrib (dairy_id, week, install, name, oz) VALUES (?, ?, ?, ?, ?)
    ON CONFLICT(dairy_id, week, install) DO UPDATE SET oz = oz + excluded.oz, name = excluded.name`)
    .bind(mem.dairy_id, c.week, me.id, mem.name || "", want).run();

  let finished = false;
  if (done.oz_done >= done.oz_need) {
    // Only one shipment can mark it finished, so the reward is paid once
    const won = await env.DB.prepare("UPDATE contracts SET completed = ? WHERE dairy_id = ? AND week = ? AND completed IS NULL RETURNING reward_xp, reward_fund")
      .bind(now(), mem.dairy_id, c.week).first();
    if (won) {
      finished = true;
      const members = (await env.DB.prepare("SELECT install FROM members WHERE dairy_id = ?").bind(mem.dairy_id).all()).results;
      await env.DB.batch([
        addXpStmt(env, mem.dairy_id, won.reward_xp),
        env.DB.prepare(`INSERT INTO dairy_progress (dairy_id, fund, week) VALUES (?, ?, ?)
          ON CONFLICT(dairy_id) DO UPDATE SET fund = fund + excluded.fund`).bind(mem.dairy_id, won.reward_fund, weekKey()),
        ...members.map(m => inboxStmt(env, m.install, "notice", 0, 0, 0,
          `${mem.dairy_name} finished this week's creamery contract! +${won.reward_xp} dairy xp and ${won.reward_fund} coins for the fund.`)),
      ]);
    }
  }

  return { ok: true, shipped: want, ozDone: Math.round(done.oz_done * 10) / 10, ozNeed: done.oz_need, finished };
}

// ---------------------------------------------------------------- carried milk and the Grand Creamery

async function carriedOf(env, install) {
  const r = await env.DB.prepare("SELECT oz FROM carried WHERE install = ?").bind(install).first();
  return Math.round((r?.oz || 0) * 10) / 10;
}

/** Takes oz from what an install carries; false if they don't have that much. */
async function useCarriedOz(env, install, oz) {
  const r = await env.DB.prepare("UPDATE carried SET oz = oz - ? WHERE install = ? AND oz >= ? - 0.001 RETURNING oz").bind(oz, install, oz).first();
  if (r && r.oz < 0) await env.DB.prepare("UPDATE carried SET oz = 0 WHERE install = ?").bind(install).run();
  return !!r;
}

/** The plugin reports milk used on the creamery's own customer and visitor orders. */
async function useCarried(env, me, request) {
  const b = await body(request);
  const oz = Math.max(0, Math.min(1000, Number(b.oz) || 0));
  await env.DB.prepare("UPDATE carried SET oz = MAX(0, oz - ?) WHERE install = ?").bind(oz, me.id).run();
  return { ok: true, carried: await carriedOf(env, me.id) };
}

async function creameryOf(env) {
  const r = await env.DB.prepare("SELECT value FROM settings WHERE key = 'creamery'").first();
  return r ? safeParse(r.value) : null;
}

/** Developer only: puts the Grand Creamery where the developer is standing. */
async function setCreamery(env, request) {
  const phrase = request.headers.get("X-VD-Dev") || "";
  if (!phrase || (await sha256(phrase)) !== DEV_KEY_HASH) fail(403, "Developer only.");
  const b = await body(request);
  const value = {
    name: clean(b.name, 40) || "The Grand Creamery",
    territory: int(b.territory, 1, 100000, "territory"),
    mapId: int(b.mapId ?? 0, 0, 100000, "map"),
    zone: clean(b.zone, 60),
    x: Number(b.x) || 0, y: Number(b.y) || 0, z: Number(b.z) || 0,
    radius: Math.max(2, Math.min(50, Number(b.radius) || 8)),
  };
  await env.DB.prepare(`INSERT INTO settings (key, value) VALUES ('creamery', ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value`)
    .bind(JSON.stringify(value)).run();
  return { ok: true, creamery: value };
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

/** Every open buy order from other dairies, best paying first: farmers fill these from their own dairy. */
async function orderBoard(env, me) {
  const home = await membership(env, me.id);
  const rows = (await env.DB.prepare(`
    SELECT o.*, d.name AS dairy_name, d.is_public AS dairy_public
    FROM orders o JOIN dairies d ON d.id = o.dairy_id
    WHERE o.status = 'open' AND o.expires >= ? AND o.owner != ? AND o.dairy_id != ?
    ORDER BY o.coins * 1.0 / o.oz DESC LIMIT 60`).bind(now(), me.id, home ? home.dairy_id : "").all()).results;
  return { orders: rows.map(o => ({ ...orderOut(o), dairyName: o.dairy_name, dairyPublic: !!o.dairy_public })) };
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
  if (!d) fail(409, "That dairy isn't taking deliveries.");
  // Delivered from inside your own dairy (the milk goes over by cart), or in person at their public area for a bonus.
  // Private places never count: only the buyer's own members can be "in" those.
  const here = (await dairyInArea(env, b.pos)) || (await dairyAt(env, b.houseId));
  const inPerson = !!here && here.id === d.id && !!d.is_public;
  const home = await membership(env, me.id);
  if (!inPerson && !(home && inOwnDairy(me, home)))
    fail(400, d.is_public ? `Deliver from inside your own dairy, or in person at ${d.name} for a bonus.` : "Deliver from inside your own dairy.");
  const bonus = inPerson ? Math.round(o.coins * IN_PERSON_BONUS) : 0;

  const day = today();
  const delivered = me.deliver_day === day ? me.deliver_oz : 0;
  if (delivered + o.oz > DAILY_DELIVER_OZ) fail(400, `You can deliver up to ${DAILY_DELIVER_OZ} oz a day.`);

  if (!(await useCarriedOz(env, me.id, o.oz))) fail(400, "Fill jugs from your dairy's tank first: you can only deliver milk you've carried out.");

  // Only one deliverer can win the order
  const won = await env.DB.prepare(
    "UPDATE orders SET status = 'filled', filled_by = ?, filled_at = ?, grade = ? WHERE id = ? AND status = 'open' RETURNING id")
    .bind(me.id, now(), grade, id).first();
  if (!won) {
    await env.DB.prepare("UPDATE carried SET oz = oz + ? WHERE install = ?").bind(o.oz, me.id).run();
    fail(409, "Someone already filled that order.");
  }

  await env.DB.batch([
    env.DB.prepare("UPDATE installs SET deliver_day = ?, deliver_oz = ? WHERE id = ?").bind(day, delivered + o.oz, me.id),
    inboxStmt(env, me.id, "payout", o.coins + bonus, 0, 0,
      `${d.name} paid ${o.coins} coins for your ${o.oz} oz${bonus ? ` (+${bonus} for delivering in person)` : ""}.`),
    // The milk goes into the buying dairy's tank
    env.DB.prepare(`INSERT INTO dairy_tank (dairy_id, grade, oz) VALUES (?, ?, ?)
      ON CONFLICT(dairy_id, grade) DO UPDATE SET oz = oz + excluded.oz`).bind(d.id, grade, o.oz),
    inboxStmt(env, o.owner, "notice", 0, 0, 0, `A delivery filled your buy order: ${o.oz} oz of Grade ${gradeName(grade)} went into your dairy's tank.`),
    addXpStmt(env, d.id, o.oz * DELIVERY_XP_PER_OZ),
  ]);
  if (home && home.dairy_id !== d.id) await addXpStmt(env, home.dairy_id, o.oz * DELIVERER_XP_PER_OZ).run();
  return { ok: true, coins: o.coins + bonus, bonus, inPerson, oz: o.oz, dairy: d.name };
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
