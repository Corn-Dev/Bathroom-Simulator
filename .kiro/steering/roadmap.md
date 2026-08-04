# Roadmap

Priority-ordered feature list. Work top to bottom. Check off as completed.

Items marked [x] mean the SYSTEM works functionally, not that it's polished or uses final assets.

---

## MVP Core (Must ship with all of these)

### Fishing Loop Polish
- [x] Cast with power bar (functional)
- [x] Bobber arc + water detection (functional)
- [x] Reel minigame — physics-based, anti-feather (functional)
- [x] Catch grants fish — Steam Inventory (functional, publisher account only)
- [x] Bobber networked — other players see it (functional)
- [x] Fishing line — rod tip to bobber (functional, placeholder visual)
- [ ] Bite indicator (bobber jerk visual + sound)
- [ ] Catch reveal feels satisfying (popup + billboard sprite + sound)
- [ ] Third-person rod visible and positioned correctly on character model
- [ ] Rod animation on cast/reel (even subtle tilt)
- [ ] Reel minigame tuned per species difficulty

### Size & Rarity
- [ ] Size roll on catch (bell curve, stored per fish)
- [ ] Size displayed in catch reveal
- [ ] Rarity roll (weighted from fish species data — already partially in MockFishingBackend)
- [ ] Visual distinction for rare catches (glow, different popup color)

### Personal Bests
- [ ] Track biggest catch per species (persistent)
- [ ] "New Personal Best!" celebration when beaten
- [ ] Display PB in Fishdex entry

### Fishdex
- [x] Grid UI with caught/uncaught (functional, needs visual polish)
- [x] Silhouettes for uncaught (functional)
- [x] Counter X / Total (functional)
- [ ] Steam Achievement per species
- [ ] Fishdex visual polish (bigger cards, info on click)
- [ ] Lore/description per species visible in Fishdex

### NPC Shop
- [ ] Currency system (earn from selling fish)
- [ ] Sell fish UI (select fish, confirm, receive currency)
- [ ] Buy bait UI (tiers, prices, quantity)
- [ ] Buy rod upgrades UI
- [ ] Price display for fish (fixed table or formula based on species + size)

### Gear & Zones
- [ ] Rod upgrade tiers (3-4 rods with increasing stats)
- [ ] Bait tiers gate rarity access
- [ ] Zone unlock system (zone requires gear tier to enter)
- [ ] Zone-specific fish tables (different species per area)
- [ ] 2-3 distinct zones designed and playable

### Fish Content
- [x] 5 fish species data + placeholder art
- [ ] 10 fish species
- [ ] 15 fish species
- [ ] 20+ fish species
- [ ] Each species has unique reel difficulty
- [ ] Each species has size range variation
- [ ] Final art style for all species

### Save System
- [ ] Save: currency, Fishdex, personal bests, owned gear, current rod/bait
- [ ] Load on lobby join
- [ ] Steam Cloud integration
- [ ] Save on catch, on purchase, on zone change

### Multiplayer
- [x] Lobby system — host/join (functional)
- [x] Movement synced (functional)
- [x] Bobber synced (functional)
- [ ] Other players see fishing line
- [ ] Other players see rod
- [ ] Fishing state visible to others (casting animation, reeling)
- [ ] Public lobby browser with search/filter

### Environment & Art
- [ ] Water shader (animated, depth-based color) — in progress
- [ ] Zone 1 final art (terrain, props, lighting, atmosphere)
- [ ] Zone 2 designed and built
- [ ] Zone 3 designed and built
- [ ] Player model (replace red capsule)
- [ ] Fishing rod final model
- [ ] Bobber final model
- [ ] Toon/stylized shader for 3D objects
- [ ] Consistent lighting that works

### Audio
- [ ] Cast sound
- [ ] Splash on bobber landing
- [ ] Bite indicator sound
- [ ] Reel tension sound
- [ ] Catch celebration sound
- [ ] Ambient nature sounds per zone
- [ ] Background music

### UI
- [ ] Main menu redesign
- [ ] In-game HUD (currency, current bait, rod)
- [ ] Shop UI
- [ ] Fishdex polish
- [ ] Catch reveal polish (animation, size display, rarity flair)
- [ ] Settings menu (volume, sensitivity, keybinds)

---

## Should-Have (if time allows before launch)

- [ ] Weather/tide system affecting bite behavior
- [ ] Friend comparison (trophy shelf or PB leaderboard)
- [ ] Seasonal fish (limited-time species)
- [ ] Cosmetic rod skins (currency sink)
- [ ] Trophy display (show your biggest catches)

---

## Post-Launch Updates

- [ ] Steam Inventory economy (tradeable fish, Community Market)
- [ ] Personal lots (build/decorate)
- [ ] Player-to-player trading
- [ ] Fantasy/mythical creatures
- [ ] More zones (5+)
- [ ] Events / seasonal content calendar
