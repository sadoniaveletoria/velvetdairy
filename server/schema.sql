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

-- Dairy levels: XP the members earn together, the shared fund, and this week's XP for the leaderboard.
-- Kept by dairy, so XP stays when a member leaves.
CREATE TABLE IF NOT EXISTS dairy_progress (
  dairy_id  TEXT PRIMARY KEY,
  xp        INTEGER NOT NULL DEFAULT 0,
  fund      INTEGER NOT NULL DEFAULT 0,
  week      TEXT NOT NULL DEFAULT '',     -- Monday (UTC) of the week week_xp belongs to
  week_xp   INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS progress_week ON dairy_progress(week, week_xp);

-- How much dairy XP one install has added today (daily cap).
CREATE TABLE IF NOT EXISTS member_xp (
  install  TEXT NOT NULL,
  day      TEXT NOT NULL,
  xp       INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (install, day)
);

-- Coins each member has put into their dairy's fund.
CREATE TABLE IF NOT EXISTS donations (
  dairy_id TEXT NOT NULL,
  install  TEXT NOT NULL,
  name     TEXT NOT NULL DEFAULT '',
  coins    INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (dairy_id, install)
);

-- The dairy's milk tank: milk members pump at its stalls is recorded here by grade (a copy; members keep theirs).
CREATE TABLE IF NOT EXISTS dairy_tank (
  dairy_id TEXT NOT NULL,
  grade    INTEGER NOT NULL,             -- 0 C, 1 B, 2 A, 3 S, 4 S+
  oz       REAL NOT NULL DEFAULT 0,
  PRIMARY KEY (dairy_id, grade)
);

-- Who pumped how much into a dairy's tank (stays with the dairy), and today's amount per install (cap).
CREATE TABLE IF NOT EXISTS tank_contrib (
  dairy_id TEXT NOT NULL,
  install  TEXT NOT NULL,
  name     TEXT NOT NULL DEFAULT '',
  oz       REAL NOT NULL DEFAULT 0,
  PRIMARY KEY (dairy_id, install)
);
CREATE TABLE IF NOT EXISTS member_pump (
  install TEXT NOT NULL,
  day     TEXT NOT NULL,
  oz      REAL NOT NULL DEFAULT 0,
  PRIMARY KEY (install, day)
);

-- Weekly creamery contract per dairy: ship milk from the dairy tank; finishing pays dairy XP and fund coins.
CREATE TABLE IF NOT EXISTS contracts (
  dairy_id   TEXT NOT NULL,
  week       TEXT NOT NULL,              -- Monday (UTC)
  oz_need    INTEGER NOT NULL,
  min_grade  INTEGER NOT NULL,
  oz_done    REAL NOT NULL DEFAULT 0,
  reward_xp  INTEGER NOT NULL,
  reward_fund INTEGER NOT NULL,
  completed  INTEGER,                    -- time it was finished
  PRIMARY KEY (dairy_id, week)
);
CREATE TABLE IF NOT EXISTS contract_contrib (
  dairy_id TEXT NOT NULL,
  week     TEXT NOT NULL,
  install  TEXT NOT NULL,
  name     TEXT NOT NULL DEFAULT '',
  oz       REAL NOT NULL DEFAULT 0,
  PRIMARY KEY (dairy_id, week, install)
);

-- Game-wide settings the developer sets (the Grand Creamery's location).
CREATE TABLE IF NOT EXISTS settings (
  key   TEXT PRIMARY KEY,
  value TEXT NOT NULL
);

-- Milk each install has taken from its dairy tank and not yet used (contracts and buy orders draw on it).
CREATE TABLE IF NOT EXISTS carried (
  install TEXT PRIMARY KEY,
  oz      REAL NOT NULL DEFAULT 0
);

-- Last time each install's plugin said it was standing at the Grand Creamery.
CREATE TABLE IF NOT EXISTS creamery_presence (
  install TEXT PRIMARY KEY,
  at      INTEGER NOT NULL
);

-- Where a dairy is: a box anywhere in the world (open world, city, or inside a specific house).
CREATE TABLE IF NOT EXISTS dairy_area (
  dairy_id  TEXT PRIMARY KEY,
  territory INTEGER NOT NULL,
  world     INTEGER NOT NULL,
  house     TEXT NOT NULL DEFAULT '',     -- the house's id when the box is inside a house, '' outdoors
  x REAL NOT NULL, y REAL NOT NULL, z REAL NOT NULL,   -- center (y = floor height)
  width     REAL NOT NULL,                -- side to side, yalms
  length    REAL NOT NULL,                -- front to back, yalms
  height    REAL NOT NULL,                -- how far above and below the floor still counts
  rot       REAL NOT NULL DEFAULT 0,      -- which way it faces (radians, like the game's rotation)
  map_id    INTEGER NOT NULL DEFAULT 0,
  zone      TEXT NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS area_where ON dairy_area(territory, world, house);

-- A farmer cleaned this stall: cows milked here get a bonus until `until`. Stalls never get worse than their tier.
CREATE TABLE IF NOT EXISTS stall_clean (
  stall_id  TEXT PRIMARY KEY,
  dairy_id  TEXT NOT NULL,
  until     INTEGER NOT NULL,
  by_name   TEXT NOT NULL DEFAULT ''
);

-- Ingredients a dairy's farmers baked (butter), shared by all of them.
CREATE TABLE IF NOT EXISTS dairy_pantry (
  dairy_id  TEXT NOT NULL,
  item      TEXT NOT NULL,
  qty       INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (dairy_id, item)
);

-- When a farmer last fed a cow or bull from the dairy shelf (once per farmer per member every 15 minutes).
CREATE TABLE IF NOT EXISTS feeds (
  farmer  TEXT NOT NULL,
  member  TEXT NOT NULL,
  at      INTEGER NOT NULL,
  PRIMARY KEY (farmer, member)
);

-- When a farmer last groomed a cow (one groom per farmer per cow every 15 minutes).
CREATE TABLE IF NOT EXISTS grooms (
  farmer  TEXT NOT NULL,
  cow     TEXT NOT NULL,
  at      INTEGER NOT NULL,
  PRIMARY KEY (farmer, cow)
);

-- A dairy's private place (a home or apartment): works like the dairy for its members, but visitors, the directory
-- and deliveries never see it. Same shape as dairy_area.
CREATE TABLE IF NOT EXISTS dairy_private (
  dairy_id  TEXT PRIMARY KEY,
  territory INTEGER NOT NULL,
  world     INTEGER NOT NULL,
  house     TEXT NOT NULL DEFAULT '',
  x REAL NOT NULL, y REAL NOT NULL, z REAL NOT NULL,
  width     REAL NOT NULL,
  length    REAL NOT NULL,
  height    REAL NOT NULL,
  rot       REAL NOT NULL DEFAULT 0,
  map_id    INTEGER NOT NULL DEFAULT 0,
  zone      TEXT NOT NULL DEFAULT '',
  whole     INTEGER NOT NULL DEFAULT 0      -- 1 = the whole house or apartment counts (no box)
);
CREATE INDEX IF NOT EXISTS private_where ON dairy_private(territory, world, house);

-- The dairy shelf: treats farmers baked, waiting for members to buy them. One row per baked batch (bought oldest first),
-- so each baker gets paid for their own.
CREATE TABLE IF NOT EXISTS shelf (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,
  dairy_id   TEXT NOT NULL,
  item       TEXT NOT NULL,
  baker      TEXT NOT NULL,
  baker_name TEXT NOT NULL DEFAULT '',
  qty        INTEGER NOT NULL,
  created    INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS shelf_dairy ON shelf(dairy_id, item, created);

-- The owner's price for each shelf item (coins each); missing = the default.
CREATE TABLE IF NOT EXISTS shelf_price (
  dairy_id TEXT NOT NULL,
  item     TEXT NOT NULL,
  price    INTEGER NOT NULL,
  PRIMARY KEY (dairy_id, item)
);

-- Applications: a player without a dairy asks to join one with the roles they want. The owner (and farmers, if the
-- owner allows it) accept or deny. Accepting adds them; their other pending applications are withdrawn.
CREATE TABLE IF NOT EXISTS applications (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,
  dairy_id   TEXT NOT NULL,
  install    TEXT NOT NULL,
  name       TEXT NOT NULL DEFAULT '',
  world_name TEXT NOT NULL DEFAULT '',
  roles      TEXT NOT NULL,               -- "cow,farmer"
  note       TEXT NOT NULL DEFAULT '',
  levels     TEXT NOT NULL DEFAULT '',    -- "Cow 23 · Farmer 12", as the applicant's plugin reports it
  status     TEXT NOT NULL DEFAULT 'pending',  -- pending | accepted | denied | withdrawn
  created    INTEGER NOT NULL,
  answered   INTEGER NOT NULL DEFAULT 0,
  answered_by TEXT NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS applications_dairy ON applications(dairy_id, status);
CREATE INDEX IF NOT EXISTS applications_install ON applications(install, status);

-- What a dairy is looking for, and whether its farmers may answer applications (off unless the owner turns it on).
CREATE TABLE IF NOT EXISTS dairy_applying (
  dairy_id       TEXT PRIMARY KEY,
  looking_for    TEXT NOT NULL DEFAULT '',   -- "cow,farmer"; empty = not taking applications
  farmers_answer INTEGER NOT NULL DEFAULT 0
);
