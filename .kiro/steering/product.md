# Product: Phish

Phish is a multiplayer fishing game built in Unity where players catch fish, build their operation, battle their fish against others, and participate in a real Steam-based fish economy. The tone is chaotic and social — think friend group slop game with meaningful progression and a real collectible economy underneath.

> This vision is subject to change. Treat this as the current north star, not a locked spec.

---

## Elevator Pitch

"Catch rare fish, level them up, and battle your friends — while they're trying to do the same thing to you."

---

## Core Pillars

### 1. Fishing Loop (Core Gameplay)
- First-person fishing in a shared world
- Cast, wait for a bite, complete a reel-in minigame to land the fish
- Fish rarity is determined server-side (common → uncommon → rare → legendary)
- Seasonal and limited fish exist with hard supply caps (e.g. Halloween fish, launch exclusives)
- Upgrade your rod, bait, and fishing equipment to improve catch rates and access new fishing spots
- Tycoon-style progression: automate and optimize your fishing operation over time

### 2. Fish Battles
- Catch fish and build a team to battle with
- Battle format: auto-battler or turn-based (TBD — needs prototyping to get right)
- Fish have stats, types, and unique abilities — rarer fish have higher ceilings
- Players can battle each other (PvP) or fight NPCs to level up fish and unlock new areas
- **Staking**: players can optionally stake a fish on a battle — winner takes the loser's fish
- Daily XP/level caps enforced server-side — prevents botting from outpacing dedicated players
- Battle system must feel skill-expressive, not purely stat-based

### 2.5. Fishdex (Collection)
- Grid UI showing silhouettes of all fish species in the game
- Uncaught fish show as a blanked-out icon of the fish's shape — you can see the silhouette but not the details
- Once caught, the entry becomes fully colored in with art, name, stats, rarity, and lore
- "Caught" status is permanent — trading a fish away doesn't remove it from your Fishdex
- Fishdex is stored in Steam Cloud save (separate from inventory — it's a record of what you've caught, not what you own)
- Steam Achievement unlocked per species caught — public achievement percentages show rarity
- Completion milestones: percentage-based rewards for catching X% of all species
- Seasonal/limited fish entries are visible in the Fishdex year-round but can only be caught during their active window — creates FOMO and shows players what they missed

### 3. Fish Economy
- All fish are Steam Inventory items — minted on successful catch, fully tradeable
- Common fish are stackable (e.g. "Bass x47") — high supply, low value, tradeable
- Rare and legendary fish are non-stackable — low supply, high value, tradeable on Steam Community Market
- In-game currency earned from fishing funds upgrades, bait, and shop purchases
- Player-to-player fish trading handled via server-side atomic swap (escrow model)
- Server-side validation for all economy actions: catch grants, trades, currency transactions
- Fish minting is on-demand server-side — common fish have no supply cap, rare/legendary fish have configurable caps per type
- Fish levels are tracked in Steam Cloud save (progression data, not part of the tradeable item)
- Seasonal fish: limited supply + limited time window enforced by server config

### 4. Player World (TBD)
- Players may have personal lots they can build and decorate
- Lots could serve as fishing spots, fish tank displays, and social spaces
- Other players can visit your lot
- Shared world vs instanced lots — undecided, depends on core loop direction

### 5. Social Chaos
- Players share a physical space and can interact with each other
- Physics-based chaos: knockback, impulse effects, vehicles
- Upgrades and items that mess with other players (tasers, knockback, etc.)
- Not competitive — the chaos is for fun, not to determine a winner
- Voice chat via Steam Voice API

---

## Multiplayer Model

- Steam P2P via NGO + SteamNetworkingSocketsTransport
- Friends-only lobbies, invite-only, and public lobbies (browseable with search/filter)
- P2P host runs physics and game events — not authoritative over player economy data
- Each player's save data is only written by their own client
- A lightweight backend server (Cloudflare Workers or AWS Lambda) handles all economy actions:
  - Fish catch grants (rarity roll + Steam Inventory transfer)
  - Trade execution (atomic escrow swap)
  - Session ticket authentication (Steam AuthenticateUserTicket)
  - Rate limiting and daily caps
  - Seasonal fish supply counters

---

## Economy Architecture

- **All fish**: minted on successful catch via server-side Steam Inventory grant
- **Common fish**: stackable Steam Inventory items, unlimited supply, fully tradeable
- **Rare/legendary fish**: non-stackable Steam Inventory items, configurable supply cap, tradeable
- **Seasonal fish**: time-window + supply cap enforced server-side, minted during active window only
- **Catch flow**: client casts → server rolls rarity and fish type → server sends tier token to client (drives animation/difficulty) → player reels in → server confirms catch → Steam Inventory grant (new item minted to player's inventory)
- **Trade flow**: both players propose → server holds escrow → both confirm → server executes atomic swap → escrow releases
- **Identity**: Steam session tickets authenticate every server request — cannot be forged
- **Fish levels**: tracked in Steam Cloud save (not in Steam Inventory) — leveling is progression data, not a tradeable property of the item itself

---

## Monetization

- Game price: $5 on Steam (one-time purchase, no pay-to-win)
- Players do not pay to fish, trade, or battle after purchase
- Revenue streams:
  - Box price (primary)
  - Steam Community Market cut on every fish trade (passive, ongoing — set at launch)
  - Future DLC: cosmetics, new fishing locations, character items (none affecting economy)

---

## Legal / Regulatory Notes

- No paid randomized loot boxes — fish are earned through gameplay only
- This keeps the game outside gambling regulations in most jurisdictions
- Fish battle mechanic inspired by Pokémon-style systems — do not use "Pokémon" in marketing copy
- Describe as "catch fish and battle them" not "fish Pokémon"

---

## Current State

Early development. Core multiplayer infrastructure is in place:
- Steam lobby system (friends-only + public)
- NGO session lifecycle (host/client, scene sync)
- Player movement (owner-authoritative Rigidbody, first-person)
- Voice routing via Steam Voice API
- Player state foundation (pending: full state machine for physics effects)

Pending implementation:
- Fishing mechanic (cast, bite, reel minigame)
- Fish battle system (format TBD)
- Fish inventory UI
- Player lot / world structure (TBD)
- Backend server (Cloudflare Workers)
- Steam Inventory integration
- Upgrade/shop system
- NPC battles and progression areas

---

## Priorities Before Adding Features

1. Lock down core fishing loop feel — casting, waiting, reeling must feel good
2. Prototype one battle (auto-battler vs turn-based) and validate it's fun before building the full system
3. Build backend server with mock inventory before wiring Steam Inventory
4. Ship with friends-only lobbies working cleanly before opening public lobbies
