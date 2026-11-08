# Velvet Dairy (proof of concept) — 18+

You play the cow. Keep yourself fed and comfortable inside your dairy, get milked in your stalls or by hand by a farmhand you registered, then sell what you make to the creamery and level up.

This is a fresh rebuild. It uses a new save section, so the old prototype's herd and settings are ignored.

## The rules
- **Your dairy is a house.** Register it from inside (yours, your FC's or a friend's). You can limit it to part of the house with the range slider.
- **Care only works inside your dairy.** Eating, drinking and stretching are locked anywhere else. The Barn tab and the HUD tell you why.
- **Milking only works inside your dairy too.**
  - **Stalls**: sit on a chair, bench or bed in your dairy and register it as a stall. Sitting in it milks you (automatically, or press Start).
  - **Hand milking**: only a **registered farmhand of your dairy** can do it. They stand within 3 y, target you and use one of the milking emotes (`/pet`, `/comfort`, `/hug` by default; you can change the list). Each emote draws 1 oz. Hand milking gives one grade higher and more xp.
- **Shopping, the creamery and goals work anywhere.**

## Your needs
Hunger, Thirst, Comfort and Mood go from 0 to 100, plus Feed quality.
- Hunger and thirst drop slowly all the time. Eat feed from your pantry or drink at the trough (free, every 20 s).
- Comfort rises while you sit on furniture in your dairy and drops when you don't. Stretch (free, every 5 min) helps.
- Mood follows how well you're kept. Treats and being milked lift it.
- Feed quality comes from rich feed (grain, salt lick, Velvet blend) and fades over time.

## Milk
- Milk builds up over time. Hover the "oz a minute" line to see what goes into your speed:
  - how well you're kept (needs)
  - +4% per level
  - **Contented** +25% when all four needs are 75 or more
  - **Treat boost** +30% for a while after clover (15 min), salt lick (10 min) or Velvet blend (30 min), up to an hour
  - +50% while resting on furniture in your dairy
  - the **Milk speed** setting
- **How much milk**: about five times a real person's yield, so roughly 125–150 oz a day with good care, more at high levels.
- **Your bottles**: you start with one Milk bottle (8 oz). Buy **Bigger bottle** (Cream jar 12, Dairy jug 20, Big jug 32, Milk churn 48) and **Extra bottle** (up to 5 bottles; the 5th unlocks at level 25). When one fills, the next starts. Each size has its own look (classic quart, wide-mouth jar, handled gallon jug, big milk jug with a carry handle, metal churn with a sight window). The big bottle shows the one filling; small ones underneath show the rest.
- When every bottle is full, nothing more comes and comfort and mood start dropping, so get milked.
- **Spoiling**: if every bottle stays full for **72 hours** without being milked, the milk spoils (you get warnings at 24 hours and 2 hours left, and the bottle shows a countdown). Spoiled milk turns sour yellow and can't be milked or sold. A **Dump milk** button appears on the HUD and under the bottle: it pours everything out for no xp or coins, and leaves an **off taste** that takes up to 40 points off your grade. Care tasks, milkings and creamery deliveries wear the off taste away. The 72 hours run on the real clock, logged in or not, so empty your bottles before you log out for a while.
- **Grade** (C, B, A, S, S+) comes from your needs and feed quality when the milking starts. Hand milking adds one grade. **Everyone starts at Grade C.** Your **milk quality** score sets the best grade you can make: B at 150, A at 800, S at 2,600, S+ at 6,000. You earn quality from every oz you're milked (+5, or +7.5 by hand), every creamery delivery (+10), and quality treats from the Shop: Cream biscuit +20 (30 coins), Golden alfalfa +50 (80), Star clover +100 (200). Quality treats share a **4-hour cooldown** (at most 6 a day). Milking all day for three days with treats reaches S; without treats it takes about four. Creamery orders only ask for grades you can reach.
- Sitting in a stall all day works: after each milking it waits until half a bottle has built up again, then milks you again.
- While you're logged out, no milk is made (so bottles you emptied stay safe). Needs drop gently (a quarter speed, never below 20).

## Coins
- **Getting paid**: cows don't sell milk. The dairy pays you per oz the moment you're milked (stall or hand), by grade: C 5, B 7, A 10, S 14, S+ 20 coins, plus 2% per dairy level and your looks bonus. The milk goes into the dairy's tank. When a farmer hand milks you, they also get a share (25% of your pay, not taken from you).
- **Creamery** tab: everyone sees **Your pay**. Farmers and owners also get **Jugs** (fill up to 192 oz from the dairy tank), **Customer orders** (a new one every 20 minutes) and **Deliveries** (other dairies' buy orders and visitor orders), and keep the coins they earn delivering. Buy-order milk goes into the buying dairy's tank.
- **Shop** tab: feed, a second and third stall, plush bedding, bigger and extra bottles, a clean trough, stall upgrades (Wooden, Padded, Velvet, Gilded: faster and more milk) and things to wear (bell collar, show ribbon, gold-cap bottles).
- **Daily goals** (Progress tab): give 10 oz, fill an order, do 3 care tasks, get hand milked. Claim each for coins and xp, plus a bonus for all four.
- **Awards**: 14 of them, 25 coins each.

## Levels (1–30)
New Cow → Dairy Cow (3) → Good Producer (6) → Prize Cow (10) → Blue Ribbon (15) → Champion (21) → Velvet Legend (28).
XP ticks in while you make milk: +1 for every 0.1 oz (none while asleep or full). Emptying at a stall adds 2 per oz times the stall's tier bonus (x1 to x1.5); hand milking adds 6 per oz + 10. All milk XP is multiplied by your grade (C x1, B x1.2, A x1.4, S x1.7, S+ x2). Plus orders, goals and care. Each level needs 30 more XP than the last. Levels unlock feed, upgrades, stall tiers, better-paying orders and more capacity.

## Sounds
The stall's milking machine is a real recording: "Milk Pump Step02" by **Erdie** on Freesound (https://freesound.org/s/49877/), cut into a start, a seamless loop and a stop. The farmhand squirt is generated.

While a stall milks you, you hear the milking machine: the motor starts, the pump pulses, and air hisses out when it stops. Each farmhand stroke squirts into the pail. Only you hear them (they play on your computer, not in the game). Turn them off or change the volume in Settings.

**Use your own recordings**: Settings → **Open my sounds folder**, then drop in `.wav` or `.mp3` files named `pump_start`, `pump_loop`, `pump_stop` and/or `squirt`. Any you add replace the built-in ones (the others stay built-in). The folder lives with your settings, so plugin updates don't wipe it. `pump_loop` repeats for as long as a stall milks you, so a few seconds of steady pumping that starts and ends at the same point loops best.
If you ship your own sounds with the plugin, put them in `VelvetDairy/Assets/sounds` with the same names (check the recording's license allows that).

## The HUD
A small card with your milk bottle, grade, needs and status. Drag it where you like, lock it in Settings, and double-click or right-click it to open the main window. `/vdairy hud` shows or hides it.

## Farmhands
- Register one: right-click them > **Velvet Dairy** > **Register as farmhand**, or target them and press **Register my target** on the Dairy tab. You have to be inside your dairy to do it.
- They **don't need the plugin**. Your game sees their emotes, so the plugin watches for them.
- The Dairy tab shows what the plugin sees for each farmhand: distance, whether they're targeting you and which emote they're doing. If an emote you want doesn't count, have them do it and press **Use their emote**.
- Right-click a farmhand > Velvet Dairy > **Let them milk me** starts it from the world.

## Staying awake

Milk is for people who are actually playing:

- After **15 minutes** with no activity (moving or turning your character, targeting, emotes, sending a chat message, or clicking in Velvet Dairy's windows; moving the mouse or pressing keys alone doesn't count) you start **dozing**: milk comes in at half speed and stalls don't start a new milking.
- After **30 minutes** you're **asleep**: milking stops, and you make no milk, XP or dairy XP until you do anything at all.
- After **12 hours** of making milk in one day you're **tired**: half speed until the next day.
- Stalls only milk you while you're **sitting on the stall's own seat**, not near it.

## The Velvet Dairy Network

When the network is up, dairies are shared between players:

- **Owners** mark out their dairy anywhere: stand in the middle of the spot (a field, a corner of a city, or a room in any house), then size and turn a box that's drawn on the ground while you set it up. Boxes can't overlap another dairy in the same zone and world, and each player can own or belong to one dairy at a time. Owners can move or resize it later (Dairy > Listing).
- Owners invite **farmers, cows and bulls**, set up and upgrade the dairy's stalls, and choose a play role for themselves (any role, or none). Owners get an extra **owner HUD** showing who's online, who's in the dairy and how full each cow is.
- **Farmers** hand milk the dairy's cows and can invite cows and bulls (not other farmers).
- **Cows** make milk and get milked in the dairy's stalls or by its farmers. **Bulls** are a role only for now.
- Invite with right-click > Velvet Dairy, or target a player and use the Members panel on the Dairy tab.
- The **Dairies** tab shows how many people are online and lists every dairy. Public ones show their address: Visit takes you there with Lifestream (or copies the address).
- **Weekly creamery contract**: each dairy gets one a week (resets Monday 00:00 UTC): about 200 oz at level 1, +40 oz a level, Grade B+ from level 5, A+ from 10, S+ from 15. Farmers and the owner fill jugs from the dairy tank (Creamery > Jugs), take them to the **Grand Creamery** (a spot in the world the developer picks; Dairy > Contract shows where, with Show on map) and ship them there. Finishing it gives the dairy about 1.5 xp and 4 fund coins per oz.
- **Dairy milk tank**: milk from milkings at a dairy (stalls or hand milking) is also stored in that dairy's own tank, by grade (a copy; the cow keeps hers). The Dairy tab shows the tank and its top pumpers. What it's used for comes later.
- **Deliveries**: owners of public dairies post buy orders (coins are held until filled). Cows go there in person and deliver from their tank. Each public dairy also has two creamery visitor orders a day.

Without the network (or with it turned off in Settings) everything works locally as before.

## Privacy and safety
- Everything stays in the window and your own chat log. Nothing is ever said in public chat.
- The plugin never moves your character or does emotes for you.
- Only people you registered yourself can hand milk you. Remove them any time.
- **Pause** in Settings freezes everything. Locked until you confirm you're 18+. Not available for Lalafell characters.

## Save protection
Saves are signed (an HMAC of coins, xp, quality, milk, tank, pantry, upgrades, stats, awards and orders), and a signed backup of the last good save is kept as `save-backup.json` in the plugin's config folder. If someone edits their save by hand, the plugin notices on load and puts back the backup; with no good backup, progress starts over (the dairy, stalls, farmhands and settings are kept). The Progress tab counts edits that were undone.
The signing key is in `VelvetDairy/Game/IntegrityKey.cs`, which `.gitignore` keeps out of Git. Don't change it after release: every existing save would look edited.
This stops save editing, not someone changing the plugin's code. Only a server can stop that (needed anyway for shared leaderboards or trading).

## Developer mode
Players always get the normal game: milk speed 1x, no pause, sitting in a stall always starts milking, fixed stall and farmhand ranges, and no test farmhand.
Developer mode adds a **Developer** panel in Settings (shortcuts, the test farmhand, and those rules). It turns on:
- automatically when the plugin is loaded from Dev Plugin Locations, or
- on a live install with `/vdairy dev <phrase>` (`/vdairy dev off` turns it off). Only a SHA-256 hash of the phrase is in the code (`DevKeyHash` in Plugin.cs); the comment there shows how to make a new one.

## Commands
- `/vdairy`: open or close
- `/vdairy hud`: show or hide the HUD

## Install (players and testers)
1. `/xlsettings` → **Experimental** → **Custom Plugin Repositories**.
2. Add `https://raw.githubusercontent.com/sadoniaveletoria/velvetdairy/main/repo.json`, tick it, **Save**.
3. `/xlplugins` → search **Velvet Dairy** → Install. Updates arrive like any other plugin.

## Network server (for me)

The server is in `server/` (Cloudflare Worker + D1). Add the repo secrets `CLOUDFLARE_API_TOKEN` (Workers Scripts edit + D1 edit) and `CLOUDFLARE_ACCOUNT_ID`; the "Network server" workflow then creates the database, deploys, and writes `network.json`, which the plugin reads to find the server. Tests: `node --no-warnings server/test/run.mjs`.

## Releasing (for me)
Bump `<Version>` in `VelvetDairy/VelvetDairy.csproj` **and** `AssemblyVersion` in `repo.json`, then push to `main`.
GitHub Actions builds it, tags `v<Version>` and publishes `latest.zip`; Dalamud picks it up on its next refresh.
The save-signing key comes from the `VELVET_INTEGRITY_KEY` repository secret; locally it lives in `VelvetDairy/Game/IntegrityKey.cs`, which Git ignores.

## Build and load (local dev)
1. `dotnet build VelvetDairy\VelvetDairy.csproj -c Release`
2. `/xlsettings` → Experimental → Dev Plugin Locations → add `VelvetDairy\bin\x64\Release\VelvetDairy.dll` → Save (if you still have the old one there, replace it)
3. `/xlplugins` → Dev Tools → Installed Dev Plugins → enable **Velvet Dairy**

## Testing on your own
1. Settings: tick 18+ and open the barn.
2. Go into a house. **Dairy** tab → name it → **Register here**. The header chip turns green.
3. Sit on a chair or bed → **Add the seat I'm on**. Stand up, sit again: milking starts by itself. Stand up to stop.
4. Barn tab: try **Drink**, **Stretch** and **Eat**. Leave the house: they lock and say why.
5. Settings → Developer → **Add test farmhand**. Barn tab → **Test**. It milks you every 2.5 s.
6. Creamery tab: sell a bottle and deliver an order. Shop: buy hay.
7. Settings → Developer (dev builds only): **Fill me up**, **+500 coins** and **Reset needs** speed things up. **Spoil my milk now** and **Spoils in 1 minute** test spoiling. **What the plugin sees** shows the house, seat, stall and your emote.

With a friend: register them as a farmhand, have them stand close and target you, start **Let them milk me** and have them `/pet` you. Watch their line on the Dairy tab.

## Credits
- Milking machine sound: "Milk Pump Step02" by Erdie, https://freesound.org/s/49877/
- Young Serif font (art only): SIL Open Font License

## Art
`tools/make_dairy_art.py` generates everything in `VelvetDairy/Assets` (boutique creamery look). `tools/preview.py` renders a rough mock of the Barn tab.
`python3 tools/make_dairy_art.py VelvetDairy/Assets`
