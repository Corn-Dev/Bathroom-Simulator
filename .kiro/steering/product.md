# Product: Phish

Phish is a multiplayer fishing game built in Unity. Players cast, reel, and collect fish in a shared world — chasing bigger catches, rarer species, and better gear. The tone is relaxed and social — fish with friends or strangers, show off your records, and build your collection.

> This vision is subject to change. Treat this as the current north star, not a locked spec.

---

## Elevator Pitch

"Catch fish, chase records, unlock new waters. Every cast could be your biggest catch."

---

## Core MVP (the game needs ALL of this to ship)

### 1. Fishing Loop
- Third-person fishing in a shared world
- **Cast**: hold to charge power bar (bounces up/down, release to set distance). Direction locked at cast start. Arc to water surface.
- **Reel**: physics-based minigame. Hold to accelerate up (ramps with hold duration, anti-feather). Gravity pulls down. Fish fight varies gravity unpredictably. Hit top = line breaks. Hit bottom = fish escapes. Progress = total hold time.
- **Catch visual**: 2D sprite billboard in-world (faces camera, scales with size)
- **Bobber**: networked object visible to all players, line drawn from rod tip
- Water detection determines valid cast

### 2. Size & Rarity System
- Every catch rolls a size (weight/length) on a continuous bell curve
- Extreme sizes (tiny/giant) are rare — collector value
- Rarity tiers: common → uncommon → rare → epic → legendary
- Size does NOT affect gameplay — purely collectible/bragging rights

### 3. Personal-Best Tracking
- Per-species biggest catch recorded
- "New personal best!" celebration on beating your record
- This never runs dry — there's always a bigger fish to chase

### 4. Fishdex (Collection)
- Grid UI showing silhouettes for uncaught, full art for caught
- "Caught" status is permanent
- Steam Achievement per species caught
- Counter: X / Total caught
- Completion milestones for catching X% of all species

### 5. NPC Shop (Sell/Buy)
- Sell caught fish for currency (fixed or slowly-drifting prices)
- Buy bait (tiers gate what rarity you can attract)
- Buy rod upgrades
- Buy cosmetics / trophy displays
- No player-to-player economy — just NPC vendor

### 6. Gear Tiers Gating Zones
- Rod + bait upgrades unlock new fishing areas
- Data-driven: zone → required gear tier → fish table
- Adding new zones is content, not code
- Each zone has unique fish species not available elsewhere

### 7. Shared Lobbies
- Friends and strangers fishing near each other
- See each other cast, reel, catch
- Visit the same fishing spots
- No PvP, no trading, no staking at launch
- Voice chat via Steam Voice API

### 8. Save Persistence
- Progress follows you into any lobby (Steam Cloud)
- Currency, Fishdex, personal bests, gear owned — all saved
- Load on lobby join, save on key events

---

## Should-Have (do if MVP feels thin)

- **Weather/tide** affecting bite behavior — cheap ongoing freshness
- **Friend comparison** — trophy shelf or leaderboard showing PBs vs friends
- **Seasonal fish** — limited time species that appear during events

---

## Post-Launch Features (NOT in MVP)

- **Steam Inventory economy** (tradeable fish, Community Market)
- **Personal lots** (build/decorate your own space)
- **Trading between players**
- **Fantasy/mythical creatures** (expansion content)
- **More zones** (5+)
- **Events / seasonal content calendar**

---

## Creature Design

- Real fish species for launch (recognizable, easy to reference)
- Stylized art style (painterly/watercolor-inspired, no black outlines)
- 1024x1024 PNG, transparent background
- May add fantasy creatures post-launch as legendary tier
- Each creature needs: full color icon + silhouette version
- In-world: 2D sprite billboard (faces camera, scales with size)

---

## Multiplayer Model

- Steam P2P via NGO + SteamNetworkingSocketsTransport
- Friends-only, invite-only, and public lobbies
- P2P host runs physics and game events
- Each player's save is client-authoritative (Steam Cloud)
- No backend server needed for MVP (NPC shop is local, no player trading)

---

## Monetization

- Game price: $5 on Steam
- No microtransactions, no pay-to-win
- Revenue: box price + future DLC (new zones, cosmetics)
- Steam Community Market deferred to post-launch with economy update

---

## Current State (July 2026)

Working:
- Multiplayer lobby (host/join/friends)
- Player movement (owner-authoritative Rigidbody, third-person orbit camera)
- Fishing loop: cast with power bar, bobber arc, water detection, reel minigame, catch
- Bobber synced as NetworkObject (all players see it)
- Fishing line (LineRenderer from rod tip to bobber)
- Steam Inventory grants on catch (publisher account)
- Fishdex UI with caught/uncaught states
- Catch reveal popup
- Fish art (5 species, placeholder style)
- Dev console for build debugging
- Terrain environment with sculpted lake
- URP render pipeline
- Custom water shader (in progress)

Pending for MVP:
- Size/weight roll per catch + personal best tracking
- NPC shop (sell fish, buy gear/bait)
- Gear tier system + zone gating
- Multiple fishing zones (2-3 areas)
- More fish species (15-20 for launch)
- Save persistence (Steam Cloud)
- UI polish (Fishdex, catch reveal, shop)
- Environment art (terrain, props, atmosphere)
- Sound effects and music
- Fishing rod model visible on character in third-person

---

## Timeline (Target: Early Access)

### Phase 1 — Marketable Demo (Now - End of July)
- Environment looks intentional (terrain, water shader, props)
- Fishing loop polished and recorded for content
- Steam store page live (Coming Soon)
- First short-form content posted

### Phase 2 — Core Systems (August - October)
- Size/weight system + personal bests
- NPC shop + currency
- Gear upgrades + zone unlocking
- 2-3 distinct fishing zones with unique fish
- 15-20 fish species
- Save persistence (Steam Cloud)
- Multiplayer polish (others see you fishing correctly)

### Phase 3 — Polish + Ship (November - January 2027)
- 20+ fish species
- Sound effects and music
- UI polish pass
- Environment polish (multiple zones look distinct)
- Playtesting and bug fixing
- Trailer and marketing push
- Early access launch
