-- Velvet Dairy Network database (Cloudflare D1 / SQLite)

-- One row per plugin install.
CREATE TABLE IF NOT EXISTS installs (
  id            TEXT PRIMARY KEY,          -- random id the plugin made
  key_hash      TEXT NOT NULL,             -- SHA-256 of the install's secret key
  char_hash     TEXT,                      -- SHA-256 of "name@worldId" for the character logged in (to match invites)
  created       INTEGER NOT NULL,
  last_seen     INTEGER NOT NULL,
  at_dairy      TEXT,                      -- dairy the player is standing in right now, if any
  version       TEXT,
  post_day      TEXT, post_coins  INTEGER NOT NULL DEFAULT 0,   -- coins put into buy orders today (cap)
  deliver_day   TEXT, deliver_oz  REAL    NOT NULL DEFAULT 0,   -- oz delivered to other dairies today (cap)
  banned        INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS installs_seen ON installs(last_seen);
CREATE INDEX IF NOT EXISTS installs_at ON installs(at_dairy, last_seen);
CREATE INDEX IF NOT EXISTS installs_char ON installs(char_hash);

-- Dairies. One per address (house_id), one per owner. Address columns go to other players only when public.
CREATE TABLE IF NOT EXISTS dairies (
  id            TEXT PRIMARY KEY,
  owner         TEXT NOT NULL UNIQUE,      -- installs.id
  house_id      TEXT NOT NULL UNIQUE,      -- the game's id for the estate (world, district, ward, plot, room)
  name          TEXT NOT NULL,
  description   TEXT NOT NULL DEFAULT '',
  is_public     INTEGER NOT NULL DEFAULT 0,
  show_owner    INTEGER NOT NULL DEFAULT 0,
  owner_name    TEXT NOT NULL DEFAULT '',
  world         INTEGER NOT NULL,
  world_name    TEXT NOT NULL DEFAULT '',
  dc_name       TEXT NOT NULL DEFAULT '',
  district      TEXT NOT NULL DEFAULT '',  -- "Mist", "The Lavender Beds", ...
  district_kind INTEGER NOT NULL DEFAULT 0,-- Lifestream residential aetheryte id (Mist 8, Lavender Beds 2, Goblet 9, Shirogane 111, Empyreum 70)
  ward          INTEGER NOT NULL,          -- 1-based, as players say it
  plot          INTEGER NOT NULL,          -- 1-based
  room          INTEGER NOT NULL DEFAULT 0,-- apartment or chamber number, 0 for a house
  updated       INTEGER NOT NULL
);

-- Who belongs to which dairy. One dairy per player. The owner is also a member with a play role.
CREATE TABLE IF NOT EXISTS members (
  install    TEXT PRIMARY KEY,
  dairy_id   TEXT NOT NULL,
  role       TEXT NOT NULL,               -- farmer | cow | bull | none (owner only)
  name       TEXT NOT NULL DEFAULT '',    -- character name, shown to the dairy's members
  world_name TEXT NOT NULL DEFAULT '',
  status     TEXT NOT NULL DEFAULT '',    -- small JSON the member's plugin shares with their dairy (milk, grade...)
  joined     INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS members_dairy ON members(dairy_id);

CREATE TABLE IF NOT EXISTS invites (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,
  dairy_id   TEXT NOT NULL,
  from_install TEXT NOT NULL,
  to_hash    TEXT NOT NULL,               -- SHA-256 of "name@worldId" of the invited character
  to_name    TEXT NOT NULL,
  role       TEXT NOT NULL,               -- farmer | cow | bull
  created    INTEGER NOT NULL,
  expires    INTEGER NOT NULL,
  status     TEXT NOT NULL DEFAULT 'pending'  -- pending | accepted | declined | cancelled
);
CREATE INDEX IF NOT EXISTS invites_to ON invites(to_hash, status);
CREATE INDEX IF NOT EXISTS invites_dairy ON invites(dairy_id, status);

-- Stalls belong to the dairy: the owner sets them up and upgrades them, every cow there uses them.
CREATE TABLE IF NOT EXISTS stalls (
  id        TEXT PRIMARY KEY,
  dairy_id  TEXT NOT NULL,
  name      TEXT NOT NULL,
  x REAL NOT NULL, y REAL NOT NULL, z REAL NOT NULL,
  tier      INTEGER NOT NULL DEFAULT 1,
  created   INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS stalls_dairy ON stalls(dairy_id);

-- Buy orders a dairy owner posts on their own creamery board. The coins are held here until filled or cancelled.
CREATE TABLE IF NOT EXISTS orders (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,
  dairy_id   TEXT NOT NULL,
  owner      TEXT NOT NULL,
  oz         INTEGER NOT NULL,
  min_grade  INTEGER NOT NULL,
  coins      INTEGER NOT NULL,
  created    INTEGER NOT NULL,
  expires    INTEGER NOT NULL,
  status     TEXT NOT NULL DEFAULT 'open', -- open | filled | cancelled | expired
  filled_by  TEXT,
  filled_at  INTEGER,
  grade      INTEGER
);
CREATE INDEX IF NOT EXISTS orders_dairy ON orders(dairy_id, status);
CREATE INDEX IF NOT EXISTS orders_open ON orders(status, expires);

-- Things waiting for a player: coins for a delivery, milk for an owner, refunds, notices.
CREATE TABLE IF NOT EXISTS inbox (
  id       INTEGER PRIMARY KEY AUTOINCREMENT,
  install  TEXT NOT NULL,
  kind     TEXT NOT NULL,               -- payout | milk | refund | notice
  coins    INTEGER NOT NULL DEFAULT 0,
  oz       REAL NOT NULL DEFAULT 0,
  grade    INTEGER NOT NULL DEFAULT 0,
  note     TEXT NOT NULL DEFAULT '',
  created  INTEGER NOT NULL,
  claimed  INTEGER
);
CREATE INDEX IF NOT EXISTS inbox_open ON inbox(install, claimed);
