# Product: Phish

Phish is a multiplayer fishing game built in Unity where players catch creatures, build their operation, battle their catches against others, and participate in a real Steam-based economy. The tone is chaotic and social — think friend group slop game with meaningful progression and a real collectible economy underneath.

> This vision is subject to change. Treat this as the current north star, not a locked spec.

---

## Elevator Pitch

"Catch rare creatures, level them up, and battle your friends — while they're trying to do the same thing to you."

---

## Core Pillars

### 1. Fishing Loop (Core Gameplay)
- First-person fishing in a shared world
- **Cast mechanic**: hold click to charge a power bar (bounces up then down — release to set distance). Upward force ramps with hold duration to prevent feathering. Rod stats affect bar speed.
- **Reel mechanic**: physics-based tug-of-war. Hold click to accelerate upward (reeling in), release to let gravity pull down (fish escaping). Upward force ramps with hold duration. Fish fight varies gravity unpredictably. Progress = total hold time. Hit top = line breaks. Hit bottom = fish escapes.
- **Catch visual**: 2D sprite billboard appears in-world on the line (faces camera, scales with fish size)
- Fish rarity is determined server-side (common → uncommon → rare → epic → legendary)
- **Size system**: each catch rolls a size (weight/length) on a bell curve. Stored as Steam Inventory dynamic property. Extreme sizes are rare and have collector/market value. Does NOT affect battle stats.
- Seasonal and limited creatures exist with hard supply caps (e.g. Halloween creatures, launch exclusives)
- **Bait system (equivalent to Pokéballs)**: different bait tiers influence what rarity you can attract
  - Basic bait: common only
  - Mid-tier bait: uncommon + chance at rare
  - Premium bait: unlocks legendary pool
  - Bait is consumed on cast — purchased with in-game currency
- Upgrade your rod, bait, and fishing equipment to improve catch rates and access new fishing spots
- Tycoon-style progression: automate and optimize your fishing operation over time

### 2. Creature Battles
- Catch creatures and build a team to battle with
- Battle format: **Super Auto Pets style auto-battler** — build your lineup, battles play out automatically in 2D
- Skill expression is in team composition, ordering, and synergies — not mechanical input
- **Balance philosophy:**
  - Rarity ≠ auto-win. Legendary creatures have unique abilities, not just bigger stats.
  - Team composition and synergies matter more than individual power
  - Types/categories (freshwater, saltwater, deep, etc.) create synergy bonuses
  - Ordering matters (front creatures tank, back creatures deal damage)
  - A well-synergized common team can beat a random legendary team
  - Common creatures stay relevant forever because synergies need them
- The "shop" is the lake — you fish for your team units instead of buying from a random shop each round
- Players can battle each other (PvP) or fight NPCs to level up creatures and unlock new areas
- **Staking**: players can optionally stake a creature on a battle — winner takes the loser's
- Daily XP/level caps enforced server-side — prevents botting from outpacing dedicated players
- Battles are fun to watch and streamable — automatic resolution with visual flair
- Battle server determines winner — zero client input on outcome (tamper-proof)

### 2.5. Fishdex (Collection)
- Grid UI showing silhouettes of all species in the game
- Uncaught species show as a blanked-out icon of the creature's shape
- Once caught, the entry becomes fully colored in with art, name, stats, rarity, and lore
- "Caught" status is permanent — trading away doesn't remove it from your Fishdex
- Fishdex is stored in Steam Cloud save (record of what you've caught, not what you own)
- Steam Achievement unlocked per species caught — public achievement percentages show rarity
- Completion milestones: percentage-based rewards for catching X% of all species
- Seasonal/limited entries are visible year-round but can only be caught during their active window

### 3. Economy
- All creatures are Steam Inventory items — minted on successful catch, fully tradeable
- Common creatures are stackable — high supply, low value, tradeable
- Rare and legendary creatures are non-stackable — low supply, high value, tradeable on Steam Community Market
- In-game currency earned from fishing funds upgrades, bait, and shop purchases
- Player-to-player trading handled via server-side atomic swap (escrow model)
- Server-side validation for all economy actions: catch grants, trades, currency transactions
- Minting is on-demand server-side — common creatures have no supply cap, rare/legendary have configurable caps
- **Dynamic properties per item**: size, level, XP, nickname — stored on the Steam Inventory item instance, travel with trades
- Seasonal creatures: limited supply + limited time window enforced by server config

### 4. Player World (TBD)
- Players may have personal lots they can build and decorate
- Lots could serve as fishing spots, creature displays, and social spaces
- Other players can visit your lot
- Shared world vs instanced lots — undecided, depends on core loop direction

### 5. Social Chaos
- Players share a physical space and can interact with each other
- Physics-based chaos: knockback, impulse effects, vehicles
- Fishing rod can hook other players (pull them toward you)
- Upgrades and items that mess with other players (tasers, knockback, etc.)
- Not competitive — the chaos is for fun, not to determine a winner
- Voice chat via Steam Voice API

---

## Creature Design

- Creatures are stylized/fantasy-inspired, not photorealistic animals
- Original names and designs (inspired by real fish/animals but with fantastical twists)
- May include non-fish creatures later (frogs, turtles, crabs, fantasy animals)
- **Art style**: hand-drawn 2D in GIMP
  - 512x512 PNG, transparent background
  - 4px black outline, flat color fills with 1-2 shading tones
  - Each creature at its most recognizable angle (side for fish, 3/4 for others)
  - Consistent eye style, outline weight, and detail level across all creatures
- Each creature needs: full color icon + silhouette version
- In-world representation: 2D sprite billboard (faces camera, scales with size)
- 3D models are post-launch polish — not required for early access
- Art serves: Fishdex grid, Steam Inventory icon, catch reveal, battle sprites (one asset, multiple uses)

---

## Multiplayer Model

- Steam P2P via NGO + SteamNetworkingSocketsTransport
- Friends-only lobbies, invite-only, and public lobbies (browseable with search/filter)
- P2P host runs physics and game events — not authoritative over player economy data
- Each player's save data is only written by their own client
- A lightweight **fixed-cost VPS** ($12-24/month) handles all economy actions:
  - Fish catch grants (rarity roll + Steam Inventory grant)
  - Battle resolution (server simulates, sends replay to clients)
  - Trade execution (atomic escrow swap)
  - Session ticket authentication (Steam AuthenticateUserTicket)
  - Rate limiting and daily caps
  - Seasonal creature supply counters
  - Redis for pending state and rate limit tracking

---

## Economy Architecture

- **All creatures**: minted on successful catch via server-side Steam Inventory grant
- **Common**: stackable, unlimited supply, fully tradeable
- **Rare/legendary**: non-stackable, configurable supply cap, tradeable
- **Seasonal**: time-window + supply cap enforced server-side
- **Catch flow**: client casts → server rolls rarity and species → server sends difficulty to client → player reels in → server confirms catch and grants item
- **Battle flow**: both players submit lineups → server validates ownership → both confirm opponent's lineup → server simulates battle → server awards staked creature → server sends replay to both clients
- **Trade flow**: both players propose → server holds escrow → both confirm → server executes atomic swap
- **Identity**: Steam session tickets authenticate every server request
- **Per-item properties** (Steam Inventory dynamic properties): size, level, XP, caught_date, nickname

---

## Monetization

- Game price: $5 on Steam (one-time purchase, no pay-to-win)
- Players do not pay to fish, trade, or battle after purchase
- Revenue streams:
  - Box price (primary)
  - Steam Community Market cut on every creature trade (passive, ongoing)
  - Future DLC: cosmetics, new fishing locations, character items (none affecting economy)

---

## Legal / Regulatory Notes

- No paid randomized loot boxes — creatures are earned through gameplay only
- This keeps the game outside gambling regulations in most jurisdictions
- Battle mechanic inspired by auto-battler genre — do not use "Pokémon" in marketing copy
- Describe as "catch creatures and battle them"

---

## Current State (June 2026)

Working:
- Steam lobby system (friends-only + public)
- NGO session lifecycle (host/client, scene sync)
- Player movement (owner-authoritative Rigidbody, first-person)
- Fishing loop: cast with power bar, bobber arc, line renderer, water detection, reel minigame, catch success
- Steam Inventory integration: granting fish on catch (publisher account)
- Fishdex UI: grid with caught/uncaught states
- Catch reveal popup
- Dev console for build debugging
- Voice routing via Steam Voice API

Pending:
- Original creature art (replacing AI placeholders)
- Environment polish (one lake area)
- Battle system
- Backend server (VPS + Redis)
- Multiplayer fishing state sync (others seeing you fish)
- Upgrade/shop system
- NPC battles and progression areas
- Player lot / world structure

---

## Timeline (Target: Early Access January 2027)

### Phase 1 — Marketable Demo (June - July 2026)
- Art style locked (3 original creatures, hand-drawn)
- One polished environment area
- Fishing loop polished (rod visible, satisfying catch feel)
- Steam store page live (Coming Soon + wishlist)
- First short-form content posted

### Phase 2 — Core Game (August - October 2026)
- Battle system working (SAP-style, 2D)
- 15-20 creatures designed, drawn, and in-game
- Backend server handling grants + rate limiting
- Multiplayer: friends can fish together and see each other fishing
- Fishdex feels satisfying
- Basic progression (currency, upgrades to rod/bait)

### Phase 3 — Polish + Ship (November 2026 - January 2027)
- 30+ creatures
- Sound effects and music
- UI polish pass (Fishdex, catch reveal, menus)
- Multiplayer chaos elements (at least one "mess with friends" mechanic)
- Playtesting and bug fixing
- Trailer and marketing push
- Early access launch
