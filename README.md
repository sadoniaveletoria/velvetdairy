# Eorzea Milkers (18+)

A dairy life game for FFXIV, played with friends. Be a **cow** who makes milk, a **farmer** who milks and delivers it, or the **owner** who runs the dairy, or any mix of these. Everyone levels up, earns coins and grows their dairy together.

> **Work in progress.** Expect changes and new features in updates.

**Join the community Discord (18+):** <https://discord.gg/yacq26rw4u>. Ask questions, find a dairy, and get update news and testing builds first.

---

## Getting started

**Install**
1. Type `/xlsettings`, open **Experimental**, find **Custom Plugin Repositories**.
2. Add `https://raw.githubusercontent.com/sadoniaveletoria/velvetdairy/main/repo.json`, tick it and **Save**.
3. Type `/xlplugins`, search **Eorzea Milkers** and **Install**. Updates arrive like any other plugin.

**First time**
- Confirm you're 18+ and a short walkthrough starts. Pick **Join a dairy** or **Start my own**.
- The full guide is the **?** in the main window, or `/milkers guide`. `/milkers tutorial` runs the walkthrough again. A complete written guide to every role is in [GUIDE.md](GUIDE.md).

---

## Roles

You can hold several roles at once, and each role levels up on its own. Your progress is never lost if a role is added or removed.

| Role | What you do | How you get it |
|---|---|---|
| **Cow** | Make milk, get milked, earn pay for every ounce | Add it to yourself, or be invited |
| **Farmer** | Hand milk cows, groom them, clean stalls, carry and deliver milk | Given by the owner or another farmer |
| **Owner** | Run the dairy: place stalls, invite people, post buy orders | Start your own dairy |
| **Bull** | Make seed and get milked (like milk, but much less), breed your dairy's cows with the [Corruption](https://github.com/RaylaPetal/corruption) plugin. | Add it to yourself |

Cow and bull are yours: you add or drop them, and they hold in every dairy you're in. Farmer is given by each dairy, and only that dairy's owner can take it away.

---

## The HUD

- The small HUD shows your bottle (and seed vials for bulls), your grade and what's happening now. Double-click or right-click it to open the main window.
- Its buttons: **Herd Care** (farmers), and while you target another player close by, **Scan**, plus **Breed** if you're both cow and bull and so are they (members of your dairy). The Kitchen is on **Roles → Farmer**.
- **It only shows while you're in a dairy** (yours, or one you're visiting). Elsewhere it hides to keep your screen clear, even when you're full or being milked by hand. Want it everywhere? Untick **Only while I'm in a dairy** in Settings. `/milkers hud` (or the Settings box) hides it completely.

---

## Your dairy

- **The public area** is a box you mark out anywhere: a field, a corner of a city, or a room in a house. Visitors can find it and deliver to it in person.
- **The feeding bell:** farmers and the owner can ring it from **Herd Care**, inside the dairy and while active (not idle), once an hour. Until it can ring again the button is greyed out and shows "Bell ready in …"; hover it to see who rang it last. Everyone in the dairy hears it chime; each cow and bull gets a pop-up to pick one bar (Fed, Watered, Comfy or Happy) to fill right up, plus a 20 minute boost. The ringer gets 10 farmer xp.
- **Weekly contract:** 600 oz at dairy level 1, growing 150 oz a level up to 3,500 oz.
- **Weekend milk contest** (all of Saturday and Sunday, UTC): milk pumped into your dairy's tank also fills the weekly contract, so it counts twice (now, and again when it's shipped). The Contract card shows the weekend's top pumpers and last weekend's champion.
- **Details:** on the **Dairies** tab, each public dairy (and your own) has a **Details** button: where it is, its owner, what it's looking for and its description, how much milk (and seed) is in its tank, how many stalls it has, and **who's there right now** with their roles and levels (cow, bull, farmer), member or visiting, and whether each cow can be bred. Handy for finding a dairy with a bull. Players whose scan setting is "My dairy only" or "Nobody" are counted but not named.
- **Finding a dairy:** the **Dairies** tab shows each one's address (for a box outdoors in a residential district, the district, ward and the plot whose yard it's in). **Visit** uses the [Lifestream](https://github.com/NightmareXIV/Lifestream) plugin to take you there: to the house, or to that ward and plot with the spot flagged on your map. Other outdoor dairies are flagged on your map. Without Lifestream, Visit copies the address.
- **The private place** (optional) is a second spot just for members, like your home or apartment. Use the whole house, or draw borders around part of it. Everything works there, but visitors never see it.
- **Stalls** are seats (a chair, bench or bed) that milk whoever sits on them. Put them in either place. The first is free; each one after costs more from the dairy fund (500, 2,000, 4,500, 8,000…). Raising a stall a tier costs 300 (Padded), 1,000 (Velvet) and 2,500 (Gilded). Upgrade them to Wooden, Padded, Velvet and Gilded for faster milking.
- **Stall markers** (Settings, on by default): a small green dot above each stall, only on your screen, so you can see which seats milk you.
- **The dairy fund** is shared money, filled by members' donations, its farmers' drop-offs at other dairies (2 coins an oz) and the weekly contract. The owner spends it on stalls, stall upgrades, the **feed bin** and the **Velvet pump** (Dairy → Feed & pump).
- **The feed bin:** farmers feed cows and bulls from it for free (Herd Care → Feed), each one every 15 minutes; it fills their Fed bar. Wheat grain is there from the start; the owner unlocks Sweet grain (dairy level 3, 2,000), Honey oats (6, 6,000), Velvet blend (10, 15,000) and Golden mash (15, 35,000) from the fund.
- **The Velvet pump** (dairy level 5): 2,500 from the fund runs it for 2 hours; everyone milked in the dairy makes milk and seed 15% faster.
- **Home pasture:** being milked in your own dairy gives +10% milk and seed.
- **The dairy shelf** (Pantry tab) sells treats your farmers baked, like Butter cookies (Happy and a milk boost). Any member can buy them inside the dairy; the baker gets 80% and the fund 20%. The owner sets the price.
- **The milk tank** holds the dairy's milk. Every milking adds to it, and farmers fill their jugs from it.
- **Dairy levels** come from members playing together and unlock better stall tiers and higher pay. The **Dairies** tab ranks every dairy, with the highest level at the top.

---

## Playing as a cow

**Making milk**
- Milk builds up while you play. You start with one 8 oz bottle. Buy bigger bottles (up to a 64 oz Velvet churn) and extra bottles (up to **4 bottles** in all) in the Shop.
- When every bottle is full, no more milk comes, so get milked.
- No milk is made while you're logged out.

**Getting milked** (only inside your dairy)
- **At a stall:** sit on its seat and it starts by itself. You don't need to get up between milkings: stay seated and it starts again once half a bottle has refilled, and sitting there builds Comfy like any seat in your dairy. (After 30 minutes without touching the keyboard, mouse or controller you start dozing and it won't start a new milking until you move, emote or click something.)
- **By hand:** a farmer of your dairy stands close, targets you and presses **Milk**. Hand milking is faster, pays more and makes up a lost grade.
- **You're paid for your daily quota**, not per ounce. A farmer or the owner sets each member's size (members list → ...): Light (6 oz at level 1, 30 coins), Steady (12 oz, 60), Heavy (18 oz, 100) or Devoted (32 oz, 170); the oz grow 2% a level up to 50. Bulls' quota is in seed. Farmers have a separate farmer quota for milk delivered (drop-offs, buy orders, contract), which the owner sets. Quotas belong to each dairy you're in. Reach it, then claim it in **Goals**. Your grade (C x1 up to S+ x1.6), your wearables and the dairy's level raise it. Daily goals pay 20 coins each and 40 more for all four.
- **No daily limit on pay:** everything you earn is paid in full, however long you play. The only brake is getting **tired**: after 12 hours of making milk in a day, milk (and seed) comes in at half speed, so pay halves too, until the daily reset at midnight Central time. Quotas, daily goals and breeding output reset then too.
- **Ask first (optional):** turn off "Farmers can start milking me without asking" on Roles → Cow and you get an Allow / Not now pop-up each time a farmer presses Milk on you.

**Your grade**
- Your grade is about you, not your milk. Higher grades raise your quota pay and milk you faster: B +10%, A +20%, S +35%, S+ +50% speed. (Grade no longer adds to the amount collected: the stall's tier does.) Milk itself is just milk: any of it fills any order or delivery.
- Everyone starts at **Grade C**. Being milked, deliveries and quality treats build your **milk quality**, which unlocks B (150), A (800), S (2,600) and S+ (6,000).
- **Keep getting milked:** after 3 days without a milking your grade slips one step. One milking brings it back. Going away? A **Dry-off tonic** holds your grade for 7 days.

**Bonuses** (the Barn tab)
- **Fed** (a farmer or the owner feeds you from the dairy's feed bin; alone off the network you eat feed yourself), **Watered** (drink), **Comfy** (rest) and **Happy** (treats; a farmer's baked ones are better than the ones you buy) each make milk come faster. They're for cows and bulls only: a farmer who isn't also a cow or bull doesn't have them (no Getting comfy when sitting, no food or drink in their Shop or Care, no need bars on the HUD).
- Each use adds 30 minutes to that bar, up to an hour, and each has a 15-minute cooldown. Better food and treats give bigger bonuses. **In your dairy the bars drain half as fast, so the same top-up shows as 60 minutes there** (and the game's messages say +60 min in the dairy, +30 elsewhere).
- **Over an hour is full enough:** while a bar has more than an hour left (at the rate it drains right now), you can't top it up: eating, drinking, a farmer's grooming, the feeding bell and sitting for Comfy all wait until it's under an hour. Food and treats that fill those bars are greyed out too, even ones that also do something else (a boost, quality), so nothing is used up for less than it says. The exception: a treat with a **bigger bonus** than the one going can still be eaten. Its bonus takes over (for example Fresh clover's +4% becomes Cream clover's +5%), but it adds no time; its row warns "bonus only, no time" first. A bar that's already full to the top counts as full as well. (In a dairy bars drain half as fast, so a full bar shows up to 2 hours.)
- **In a dairy your bars last longer:** while you're in a dairy (yours, or a public one you're visiting), Fed, Watered, Comfy and Happy drain **half as fast**. Out of a dairy they drain at the normal rate.
- **Comfy:** sit on furniture in your dairy (a stall seat counts); there's no button. You get no Comfy until you've stayed seated **10 minutes**, then it adds 30 minutes to the bar. Keep sitting and every further 10 minutes adds another 30, up to a full hour. Get up sooner and that sit gives nothing. The Comfy bar doesn't drain while you sit, and the bar and HUD show how long is left. Sitting itself doesn't speed up milk; Comfy does.
- **All four at once** makes you **Contented**, a bit faster still.
- Bars only drain while you're awake and playing. Running out never hurts you; you're just back to normal speed.

**Spoiling:** if your bottles stay full for **72 hours**, the milk spoils and has to be dumped, which lowers your grade for a while. Empty your bottles before a long break.

---

## Playing as a farmer

- **Hand milking:** press **Herd Care** on the HUD (or Roles → Farmer), stand near a cow, press **Target**, then **Milk**. Herd Care lists only the cows and bulls who are in your dairy right now (members, and guests visiting a public dairy), and only while you're there too; someone online in a different dairy or out in the world isn't listed. Your character does the milking emote (/beesknees by default, or one you choose) until the cow is empty, then stops by itself. You earn 25% of what the cow is paid, on top of their pay (10% more for a cow wearing the Champion's ribbon).
- **Your own animation:** if you use Penumbra, pick an animation mod and its options with **Animation…** in the Herd Care window. It switches on only while you milk.
- **Chores:** **Groom** a cow or bull (they get 30 minutes of Happy), **Feed** a cow or bull from the feed bin (their Fed bar) or a treat from the dairy shelf (their Happy bar; press Feed and pick which; free), and **Clean** an empty stall (cows milked there get a bonus for an hour). All earn farmer XP.
- **The Kitchen** (the Kitchen button on **Roles → Farmer**): bake with milk from your jugs. Recipes unlock with your farmer level:
  - **Butter** (level 1): 16 oz of milk → 1 butter.
  - **Butter cookies** (level 3): 1 butter + 8 oz of milk → 3 cookies, for the dairy shelf.
  - **Flour** (level 6): 2 wheat grain (from the Shop) → 1 flour.
  - **Cream buns** (level 10): 1 flour + 1 butter + 12 oz of milk → 2 buns, for the dairy shelf.
  
  Pick how many with − and +; each one in a batch takes a little longer than the last. A progress bar shows how it's going; it pauses if you leave the dairy, and **Stop** keeps what's finished. **Ingredients are shared:** butter and flour go into the dairy pantry, and every farmer can bake with them. Treats go on the **dairy shelf**, where members buy them (you get 80%) or farmers feed them to cows and bulls.
- **Jugs:** fill them from the dairy tank at the top of the **Kitchen** window. Bigger farmer tanks unlock as your farmer level grows.
- **Deliveries:**
  - **Drop-offs:** walk into any other dairy and leave milk from your jugs in their tank (Creamery → Deliveries), no order needed. The Grand Creamery pays you 1.5 coins an oz, your own dairy fund gets 2 coins an oz, and you get farmer XP.
  - **Other dairies' buy orders**, public or private. Fill them from inside your own dairy, or in person at their dairy (same pay).
  - **Daily limit:** you can deliver 30 oz a day to other dairies at farmer level 1, plus 5 oz a level (525 at level 100). Drop-offs and buy orders share it. You can make **10 deliveries a day**, and drop off at each dairy **once a day**.
  - **The weekly contract** (600 oz at dairy level 1, plus 150 a level, up to 3,500), delivered in person at any **Grand Creamery**. **Dairy → Contract** shows where they are. Fill it for the full reward; if it isn't full by Monday, your dairy still gets the share it delivered.

---

## Bulls and breeding

Breeding needs the Corruption plugin for both of you; balance may still change as we see how it plays.
- **Bulls** make seed and get milked, the way cows make milk, with the same four bonuses. Seed fills small vials; a bull's daily quota is in seed. Add the Bull role to yourself on the Roles tab.
- **Breeding needs** the [Corruption](https://github.com/RaylaPetal/corruption) plugin (0.3.5 or newer) and Cummy, for both of you. The bull's Corruption needs **Let other plugins control Corruption** turned on (Settings → Other plugins). The cow's Cummy blocks always apply.
- **When a bull of your dairy finishes in a Corruption scene with a cow of your dairy, she's bred:** +5% milk for 30 minutes (less if the bull's seed output is low that day; 45 minutes with the Champion's ribbon). Once per scene, and a cow can be bred again 20 minutes after the last time. The bull gets up to 40 bull xp; after 20 breedings in a day his seed output (and xp) drops, resetting at midnight Central time.
- **Who breeds whom** in Eorzea Milkers follows your roles in the dairy: a bull can breed a cow. Whether it can become a **pregnancy** is Corruption's: its Pregnancy settings (can carry, can breed) must allow it. If they don't, the scene note says so; the bred bonus and bull xp still count.
- **Both cow and bull?** With a partner who's also both (the **Breed** button only shows then), whoever is doing the breeding targets the other and presses **Breed** on the HUD (or right-click them → Eorzea Milkers → Breed). Only that way counts, and finishing doesn't breed anyone until one of you has pressed it. Press it again to cancel. It's used up once you've bred them, so press it again next time. If you both press it, neither counts until one of you cancels.
- **Bred bonus over:** when a cow's bred bonus runs out, everyone with the HUD in the dairy she's standing in gets a small pop-up with a soft bell ("Rayla's bred bonus has run out"), and so does she. (She can be bred again 20 minutes after the last time, so by then she's ready.)
- **Breeding stalls:** the owner can switch any stall to a breeding stall (**Dairy > Stalls > Make it breed**; **Make it milk** switches it back). It no longer milks anyone. **Farmers and the owner load it** (**Load 1** on its row, while in the dairy): each load moves **3 oz from the dairy's seed tank** into the stall as one breeding, up to **5 loaded**, and gives +8 farmer xp. The owner can **Unload** it to put the seed back (switching it back to milking does too). A cow who sits in it for **5 minutes** uses one loaded breeding and is bred, with the same bred bonus as a scene (+5% milk for 30 minutes, 45 with Prized cow), but **no pregnancy**: pregnancy only comes from a Corruption scene. An empty stall tells her to ask a farmer. The HUD shows a bar filling while she sits; getting up early cancels it. The usual 20 minutes between breedings applies. The dairy's bulls fill the seed tank when they're milked. Members and guests of public dairies can use it. Stall markers are **green** for milking stalls and **orange** for breeding stalls.
- **The seed market** (**Creamery > Seed market**): dairies with spare seed sell it to dairies that need it. The owner puts seed from the seed tank up for sale (amount and coins per ounce; up to 3 listings). It leaves the tank while listed, and the coins go to the dairy fund as it sells (the owner gets a notice). **Take down** returns what's left. Owners buy other dairies' seed from the dairy fund, straight into their seed tank. Other members can see the market, but only the owner buys and sells.
- **Bull Shop items** (unlocked by bull level, only for bulls; a cow who's also a bull gets both sets): Nose ring (5, +3% seed pay), Stud book (10, 25 breedings a day at full output), Brass horn caps (20, +5% seed pay), Prize stud (25, +25% bull xp from breeding), Breeding pen (35, each breeding gives your dairy xp), Herd sire (50, seed fills 15% faster), Champion bull sash (75, +10% seed pay and breeding output never below 60%), Legendary stud (100, 30 breedings a day at full output).
- **Bull foods** (Shop, by bull level): Iron oats (8: a soaked-oat drink that fills Watered), Bull's salt lick (12: Happy and +25% bull xp for 30 min), Seed tea (15: your next seed milking gives 25% more), Stud draught (20: your next breeding gives the cow 50% more bred bonus, 15 min longer).
- **Cow-and-bull foods** (for those who are both; unlocked by the lower of the two levels): Twin blend (10: Happy and a 30 min boost to milk and seed), Velvet & brass bonbon (15: Happy and +20% cow and bull xp for 30 min), and for everyone, the Second wind tonic (level 10: you tire 2 hours later today; once a day).
- **Pregnancy** comes from Corruption (a bull always finishes inside, so it rolls for conception). Once it's confirmed, a pregnant cow gives +5%, +10% and +15% milk by trimester until the birth. Roles → Cow shows the trimester, due date, Sire and past births.
- During a scene the HUD shows who you're with and your arousal (from Corruption); the main window says who breeds whom and why. When a breeding counts is up to Corruption: whenever the breeder climaxes.

---

## Joining a dairy

- Get **invited** by the owner or a farmer, or **apply**: on the **Dairies** tab, dairies that are looking for members show what they want (Cows, Farmers, Bulls) and an **Apply** button. Pick your roles, add a short note and send it.
- **You can belong to several dairies at once**, and own one of your own as well. Joining another keeps the ones you're in; applications to other dairies stay open.
- **One dairy at a time is the one you're playing in:** milking, stalls, the contract, the tank and dairy XP all count for it. Walking into another of your dairies switches to it, or press **Play here** under **Your dairies** on the Dairy tab.
- **Your levels are yours:** cow, bull and farmer levels and your progress go with you to every dairy. Cow and bull hold everywhere; farmer only where a dairy made you one.
- In someone else's dairy and want your own? Press **Start my own dairy** on the Dairy tab. **Leave dairy** only leaves the one you're playing in.
- **Roles across dairies:** cow and bull are **yours**: you add or drop them yourself (Roles tab), and they hold in every dairy you're in. An owner can't take them away (they can remove you from their dairy instead). **Farmer** is a job each dairy gives you, so you're only a farmer where you've been made one.

---

## Visiting public dairies (guests)

- In someone else's **public** dairy you're a **guest**. You need to be a cow or bull (in any dairy of yours).
- You can be **milked on its stalls** and cared for there (drink, Comfy, treats) like at home, with your own grade, quota and XP. Your milk (or seed) goes into **that dairy's tank**.
- Its farmers can hand milk, groom and feed you. **Hand milking always asks you to Allow first.**
- **Breeding with a bull from another dairy:** when one is in a Corruption scene with you, you get a pop-up. If you **Allow**, their finish counts as a breeding; if not, it doesn't.
- If a dairy says no, you can apply to it again after 3 days.

---

## Scanning other players

Target someone with Eorzea Milkers and press **Scan** on the HUD (or right-click them → Eorzea Milkers → Scan) to see their milk, bonuses, bred bonus, pregnancy and when they can be bred again. Anyone can scan; each player chooses in Settings who can scan them (anyone, their dairy only, or nobody).

---

## Running a dairy (owners)

- Invite people with **right-click → Eorzea Milkers**, or on **Dairy → Overview**.
- Take **applications**: in **Members → Applications**, tick what you're looking for, then accept or deny. You can let your farmers answer applications too (off unless you turn it on).
- **Manage members** shows everyone's levels, milk, quality, how much they've given and when they were last online.
- Post **buy orders** to bring milk into your tank from other dairies. This works even if your dairy is private. Type the amount you want (1 to 400 oz); a farmer delivers it in one go, so a big order needs a farmer with a big tank.
- Fill the **weekly contract** together for dairy XP and fund coins.

---

## Levels, coins and goals

- **Levels go to 100.** Each takes longer than the last: the first ten come quickly, the last ones take days. Reaching 100 is about six months for a dedicated player or a year of casual play.
- **Cow levels** make milk faster (up to level 50) and unlock feed and upgrades. **Farmer levels** unlock bigger tanks, new recipes and a bigger daily delivery limit.
- **Daily goals** (Progress tab): four for cows and four for farmers, with a bonus for finishing a set.
- **Awards:** collect them for coins.
- **Stats:** your lifetime totals (milk given, best grade, milkings, deliveries, coins). They include **breed counters**: as a cow, how many times a bull has bred you; as a bull, how many cows you've bred (scene breedings, counted from version 0.9.93 on; breeding stalls don't count).
- **The Shop** sells treats, tonics (Second wind, Pump drops, Udder balm, Dry-off), quality treats and, for farmers, Churn oil, wheat grain for flour and bigger farmer tanks. Everything else that used to be an upgrade (bottles, bedding, troughs, bells, wearables, bull perks) is now a **free level reward**: Shop → Level rewards lists them and the level each comes at. Wearables raise your quota pay.
- **Comfort items:** Plush bedding (Comfy +7% instead of +4%, Comfy bar drains half as fast), Lavender bedding (Comfy +3% more, Happy bar drains half as fast), Silk cushion (Comfy bar drains 15% slower on top), Bell collar (Happy drains a little slower), Clean trough (Watered lasts half again as long), Endless spring (Watered drains 20% slower).
- **Champion's ribbon** (cow level 28): farmers who hand milk you earn 10% more; being bred lasts 45 minutes; pregnancy gives 5% more milk.
- The main window shows your Eorzea Milkers version at the bottom of the left side (handy when asking for help).

---

## Staying awake

Milk is for people who are actually playing.

- **15 minutes** with no activity (moving, emotes, targeting, chatting, or clicking in Eorzea Milkers) and you start **dozing**: half-speed milk.
- **30 minutes** and you're **asleep**: no milk, no XP.
- **Sitting on furniture in your dairy** holds this off longer: dozing at 30 minutes, asleep at 45.
- After **12 hours** of making milk in a day you get **tired** and make milk at half speed (so half the pay) until the next day.

---

## Backup and restore (new PC, reinstall, lost settings)

Your save is backed up on the Eorzea Milkers server every few minutes while you play (with the network on). The backup never includes your network key or developer phrase. If you lose your save, you get everything back: progress, coins, pantry and upgrades, plus your dairy, memberships and roles.

**Backup setup comes first.** Before you play (right after the 18+ check, and once for existing players after updating), the plugin backs up your save and shows your recovery code. Type its last 5 characters to show you saved it, then press **Done, let me play**. Until then the game is paused and the HUD is hidden. If you'd rather not use the network, tick "I understand that if I lose my save, it can't be recovered" and press **Play without backups**. That turns the network off. You can set backups up later in Settings.

**Settings → Backup and restore**, or `/milkers restore`:
- **Recovery code:** press **Show my recovery code** and write it down or screenshot it. It starts with `EMR1-`. On a new install, paste it under **Restore with a recovery code** and press **Restore** twice. Anyone with this code can sign in as you, so keep it private.
- **No code? Verify in the Discord.** Go to **#restore-save** in the Eorzea Milkers Discord (or type `/restore` there) and press **Start**:
  1. Type your character's name and home world.
  2. Put the code you get (like `MILK-ABCD2345`) anywhere in your Lodestone **Character Profile** and save.
  3. Press **Verify**. The bot gives you a **restore key** (`MILKR-...`) that only you can see.
  4. In game, log in as that character, open `/milkers restore`, paste the key under **Restore with a code** and press **Restore** twice.

  The key works once, for a day, and only for that character. Take the code out of your Lodestone profile afterwards.
- **Without Discord:** the same window can verify on the Lodestone itself. Press **Get a code**, put it in your profile, then press **Check the Lodestone and restore** twice.
- When a brand new save starts on a character that has played on another install, the window opens by itself and offers the restore.
- **Make a new code:** if your recovery code got out (a screenshot, a shared file), press **Make a new code** twice. Your old code stops working; save the new one.
- Restoring replaces the save on this character. The save it replaces is kept on your PC as `before-restore-*.json`, in the plugin's settings folder.
- Players from before version 0.9.84 have no cloud backup of their progress yet. The Lodestone restore still brings back their dairy, memberships and roles.

**If a save fails its check:** saves are signed, so editing the plugin's files is caught. When that happens, the last good local backup is used. If there's none, progress starts over, but your dairy and network identity are kept. The rejected save is kept as `rejected-save-*.json`, and your cloud backup is offered back. A save that started over never replaces your cloud backup.

**Dev builds** (loaded from Dev Plugin Locations) keep their own save (`dev-config.json`) and never upload a backup, so trying one can't touch your real progress.

## Privacy and safety

- Nothing is ever said in public chat. The plugin never moves your character.
- The only time it does an emote for you is when you press **Milk**.
- Only farmers of your own dairy can milk you, and only when they press Milk on you.
- **Pause** in Settings freezes everything. 18+ only. Not available for Lalafell characters.

---

## Commands

| Command | What it does |
|---|---|
| `/milkers` | Open or close the main window |
| `/milkers hud` | Show or hide the HUD |
| `/milkers guide` | Open the how-to guide |
| `/milkers tutorial` | Run the first-time walkthrough again |
| `/milkers changes` | See what's new in recent updates |
| `/milkers restore` | Backup and restore: your recovery code, or get your save back on a new install |
| `/milkers support` | Copy your support info for #bug-reports |

---

## Testing builds (for testers)

Want new features before everyone else?
1. `/xlsettings` → **Experimental** → tick **Get plugin testing builds** → **Save**.
2. `/xlplugins` → **Installed** → Eorzea Milkers's menu → tick **Receive plugin testing versions**.

Untick either one to go back to the normal release.

---

## Credits

- Milking machine sound: "Milk Pump Step02" by Erdie ([Freesound](https://freesound.org/s/49877/))
- Young Serif font: SIL Open Font License
