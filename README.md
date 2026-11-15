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
- The full guide is the **?** in the main window, or `/milkers guide`. `/milkers tutorial` runs the walkthrough again.

---

## Roles

You can hold several roles at once, and each role levels up on its own. Your progress is never lost if a role is added or removed.

| Role | What you do | How you get it |
|---|---|---|
| **Cow** | Make milk, get milked, earn pay for every ounce | Add it to yourself, or be invited |
| **Farmer** | Hand milk cows, groom them, clean stalls, carry and deliver milk | Given by the owner or another farmer |
| **Owner** | Run the dairy: place stalls, invite people, post buy orders | Start your own dairy |
| **Bull** | Make seed and get milked (like milk, but much less), breed your dairy's cows with the [Corruption](https://github.com/RaylaPetal/corruption) plugin. *Still in testing: details will change.* | Add it to yourself |

Only the owner can take a role away from someone.

---

## The HUD

- The small HUD shows your bottle (and seed vials for bulls), your grade and what's happening now. Double-click or right-click it to open the main window.
- Its buttons: **Herd Care** (farmers), **Kitchen** (farmers), and **Scan** while you target another player close by.
- **It only shows while you're in a dairy** (yours, or one you're visiting), or while you're being milked or in a breeding scene. Elsewhere it hides to keep your screen clear. Want it everywhere? Untick **Only while I'm in a dairy** in Settings. `/milkers hud` (or the Settings box) hides it completely.

---

## Your dairy

- **The public area** is a box you mark out anywhere: a field, a corner of a city, or a room in a house. Visitors can find it and deliver to it in person.
- **The private place** (optional) is a second spot just for members, like your home or apartment. Use the whole house, or draw borders around part of it. Everything works there, but visitors never see it.
- **Stalls** are seats (a chair, bench or bed) that milk whoever sits on them. Put them in either place. The first is free; each one after costs more from the dairy fund (250, 1,000, 2,250, 4,000…). Upgrade them to Wooden, Padded, Velvet and Gilded for faster milking.
- **Stall markers** (Settings, on by default): a small green dot above each stall, only on your screen, so you can see which seats milk you.
- **The dairy fund** is shared money, filled by members' donations and the weekly contract. It pays for stalls and stall upgrades.
- **The dairy shelf** (Pantry tab) sells treats your farmers baked, like Butter cookies (Happy and a milk boost). Any member can buy them inside the dairy; the baker gets 80% and the fund 20%. The owner sets the price.
- **The milk tank** holds the dairy's milk. Every milking adds to it, and farmers fill their jugs from it.
- **Dairy levels** come from members playing together and unlock better stall tiers and higher pay. The **Dairies** tab ranks every dairy, with the highest level at the top.

---

## Playing as a cow

**Making milk**
- Milk builds up while you play. You start with one 8 oz bottle. Buy bigger bottles (up to a 48 oz churn) and extra bottles (up to 5) in the Shop.
- When every bottle is full, no more milk comes, so get milked.
- No milk is made while you're logged out.

**Getting milked** (only inside your dairy)
- **At a stall:** sit on its seat and it starts by itself. You don't need to get up between milkings: stay seated and it starts again once half a bottle has refilled, and sitting there counts as resting, so you keep the resting bonus. (After 30 minutes without doing anything you start dozing and it won't start a new milking until you move, emote or click something.)
- **By hand:** a farmer of your dairy stands close, targets you and presses **Milk**. Hand milking is faster, pays more and makes up a lost grade.
- You're **paid the moment you're milked**, by grade: C 6, B 8, A 10, S 12, S+ 14 coins an ounce, plus a bonus for your dairy's level.
- **Daily earnings taper off:** everything the game pays you in a day (milking, farmer shares, orders, goals, awards) counts together. The first 3,000 coins are paid in full, then half up to 8,000, then a tenth. XP is never reduced, coins from other players don't count, and it resets each day.
- **Ask first (optional):** turn off "Farmers can start milking me without asking" on Roles → Cow and you get an Allow / Not now pop-up each time a farmer presses Milk on you.

**Your grade**
- Your grade is about you, not your milk. Higher grades pay more, milk you faster and get more milk out: B +10%, A +20%, S +35%, S+ +50%. Milk itself is just milk: any of it fills any order.
- Everyone starts at **Grade C**. Being milked, deliveries and quality treats build your **milk quality**, which unlocks B (150), A (800), S (2,600) and S+ (6,000).
- **Keep getting milked:** after 3 days without a milking your grade slips one step. One milking brings it back. Going away? A **Dry-off tonic** holds your grade for 7 days.

**Bonuses** (the Barn tab)
- **Fed** (food), **Watered** (drink), **Comfy** (rest) and **Happy** (treats) each make milk come faster.
- Each use adds 30 minutes to that bar, up to an hour, and each has a 15-minute cooldown. Better food and treats give bigger bonuses.
- **Comfy:** sit on furniture in your dairy (a stall seat counts); there's no button. You get no Comfy until you've stayed seated **15 minutes**, then it adds 30 minutes to the bar. Keep sitting and every further 15 minutes adds another 30, up to a full hour. Get up sooner and that sit gives nothing. The Care row shows how long is left.
- **All four at once** makes you **Contented**, a bit faster still.
- Bars only drain while you're awake and playing. Running out never hurts you; you're just back to normal speed.

**Spoiling:** if your bottles stay full for **72 hours**, the milk spoils and has to be dumped, which lowers your grade for a while. Empty your bottles before a long break.

---

## Playing as a farmer

- **Hand milking:** press **Herd Care** on the HUD (or Roles → Farmer), stand near a cow, press **Target**, then **Milk**. Your character does the milking emote (/beesknees by default, or one you choose) until the cow is empty, then stops by itself. You earn 25% of what the cow is paid, on top of their pay.
- **Your own animation:** if you use Penumbra, pick an animation mod and its options with **Animation…** in the Herd Care window. It switches on only while you milk.
- **Chores:** **Groom** a cow or bull (they get 30 minutes of Happy), **Feed** a cow or bull a treat from the dairy shelf (press Feed and pick which; free, but not to yourself), and **Clean** an empty stall (cows milked there get a bonus for an hour). All earn farmer XP.
- **The Kitchen** (the Kitchen button on the HUD): bake with milk from your jugs. Recipes unlock with your farmer level:
  - **Butter** (level 1): 16 oz of milk → 1 butter.
  - **Butter cookies** (level 3): 1 butter + 8 oz of milk → 3 cookies, for the dairy shelf.
  - **Flour** (level 6): 2 wheat grain (from the Shop) → 1 flour.
  - **Cream buns** (level 10): 1 flour + 1 butter + 12 oz of milk → 2 buns, for the dairy shelf.
  
  Pick how many with − and +; each one in a batch takes a little longer than the last. A progress bar shows how it's going; it pauses if you leave the dairy, and **Stop** keeps what's finished. **Ingredients are shared:** butter and flour go into the dairy pantry, and every farmer can bake with them. Treats go on the **dairy shelf**, where members buy them (you get 80%) or farmers feed them to cows and bulls.
- **Jugs:** fill them from the dairy tank at the top of the **Kitchen** window. Bigger farmer tanks unlock as your farmer level grows.
- **Deliveries:**
  - **Customer orders**, with a new one every 20 minutes.
  - **Other dairies' buy orders**, public or private. Fill them from inside your own dairy, or in person at a public dairy for **+10%**.
  - **The weekly contract** (500 oz at dairy level 1, up to 1,500), delivered in person at any **Grand Creamery**. **Dairy → Contract** shows where they are. Fill it for the full reward; if it isn't full by Monday, your dairy still gets the share it delivered.

---

## Bulls and breeding (still in testing)

Breeding is new and still being tuned: numbers and details will change.
- **Bulls** make seed and get milked, the way cows make milk, with the same four bonuses. Seed fills small vials and pays ten times milk's price per ounce. Add the Bull role to yourself on the Roles tab.
- **Breeding needs** the [Corruption](https://github.com/RaylaPetal/corruption) plugin (0.3.5 or newer) and Cummy, for both of you. The bull's Corruption needs **Let other plugins control Corruption** turned on (Settings → Other plugins). The cow's Cummy blocks always apply.
- **When a bull of your dairy finishes in a Corruption scene with a cow of your dairy, she's bred:** +5% milk for 30 minutes (less if the bull's seed output is low that day). Once per scene, and a cow can be bred again 20 minutes after the last time. The bull gets up to 40 bull xp; after 20 breedings in a day his seed output (and xp) drops, resetting at midnight UTC.
- **Who breeds whom** follows what Corruption says your bodies can do (who can breed, who can carry).
- **Both cow and bull?** With a partner who's also both, a pop-up asks **Who breeds this scene?** before you start: pick **I breed them**, **They breed me**, or **Not now**. Only that way counts, and finishing doesn't breed anyone until one of you picks. Each new scene asks again.
- **Ready again:** when a cow's 30-minute bred bonus runs out, everyone with the HUD in the dairy she's standing in gets a small pop-up with a soft bell, and so does she.
- **Pregnancy** comes from Corruption (a bull always finishes inside, so it rolls for conception). Once it's confirmed, a pregnant cow gives +5%, +10% and +15% milk by trimester until the birth. Roles → Cow shows the trimester, due date, Sire and past births.
- During a scene the HUD shows who you're with and your arousal; the main window says who breeds whom.

---

## Joining a dairy

- Get **invited** by the owner or a farmer, or **apply**: on the **Dairies** tab, dairies that are looking for members show what they want (Cows, Farmers, Bulls) and an **Apply** button. Pick your roles, add a short note and send it.
- **You can only be in one dairy at a time.** You can apply to several, but when one accepts you, your other applications are withdrawn. To move to another dairy, leave yours first (**Dairy** tab).
- If a dairy says no, you can apply to it again after 3 days.

---

## Scanning other players

Target someone with Eorzea Milkers and press **Scan** on the HUD (or right-click them → Eorzea Milkers → Scan) to see their milk, bonuses, bred bonus, pregnancy and when they can be bred again. Anyone can scan; each player chooses in Settings who can scan them (anyone, their dairy only, or nobody).

---

## Running a dairy (owners)

- Invite people with **right-click → Eorzea Milkers**, or on **Dairy → Overview**.
- Take **applications**: in **Members → Applications**, tick what you're looking for, then accept or deny. You can let your farmers answer applications too (off unless you turn it on).
- **Manage members** shows everyone's levels, milk, quality, how much they've given and when they were last online.
- The **owner HUD** shows who's online, who's in the dairy and how full each cow is.
- Post **buy orders** to bring milk into your tank from other dairies. This works even if your dairy is private.
- Fill the **weekly contract** together for dairy XP and fund coins.

---

## Levels, coins and goals

- **Levels go to 100.** Each takes longer than the last: the first ten come quickly, the last ones take days. Reaching 100 is about six months for a dedicated player or a year of casual play.
- **Cow levels** make milk faster (up to level 50) and unlock feed and upgrades. **Farmer levels** unlock bigger tanks and better orders.
- **Daily goals** (Progress tab): four for cows and four for farmers, with a bonus for finishing a set.
- **Awards:** collect them for coins.
- **The Shop** sells feed, treats, bottles, comfort upgrades and things to wear.

---

## Staying awake

Milk is for people who are actually playing.

- **15 minutes** with no activity (moving, emotes, targeting, chatting, or clicking in Eorzea Milkers) and you start **dozing**: half-speed milk.
- **30 minutes** and you're **asleep**: no milk, no XP.
- **Sitting on furniture in your dairy** holds this off longer: dozing at 30 minutes, asleep at 45.
- After **12 hours** of making milk in a day you get **tired** and make milk at half speed until the next day.

---

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
