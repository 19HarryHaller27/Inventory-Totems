# Inventory Totems — full audit (v0.2.4)

## Purpose

Server-side mod: items in **equipped bag storage** (`GlobalConstants.backpackInvClassName`) grant **passive move speed** and **puzzle max HP** bonuses. Hotbar, crafting grid, and character gear inventories are **not** scanned for effects.

## Patch milestones (code comments)

- **Patch 2 (stable):** Puzzle and passive logic use **per equipped bag** (`BagIndex`) and `SlotInBag` order (fixes earlier “10 flat slots / 2×5 grid” mismatch).
- **Patch 3 (v0.2.0):** Row-major **bag grid** from `PuzzleGridRows(n)` (1 row by default; **2 rows** when even `n` and `n/2 >= 3`).
- **Patch 3+ (v0.2.1):** On that grid, chains are **orthogonal paths** (up, down, left, right): A adjacent to B adjacent to C adjacent to D, same tier, no diagonals, no slot reuse within a match. DFS finds ABCD / ABC / AB (longest first).

## Source files

| File | Role |
|------|------|
| `InventoryTotemsServerSystem.cs` | Mod system: item class registration, **server tick** scan (~300ms), **`/it`** commands, **passive tier**, **puzzle match** resolution, **ApplyState** (stats + HP), notifications, **totem look** slot pass via `PlayerNowPlaying` + scan, **patch** notes in class summary. |
| `InventoryTotemConstants.cs` | Mod id/version, stat **layer names**, passive % per tier, puzzle **HP bonus** table, **AssetLocation** codes for passive and puzzle pieces. |
| `EntityHealthBonusHelper.cs` | Reflection call into `EntityBehaviorHealth.SetMaxHealthModifiers` so **max HP** updates in UI (no survival DLL reference at compile time). |
| `ItemInventoryTotem.cs` | Item class: **blocks** default use/interact; **OnCreatedByCrafting** assigns look; **OnBeforeRender** swaps mesh + **transform** from a random vanilla `game:` antler/bone item (`TotemLookVisuals`). |
| `TotemLookHelper.cs` | Server: **random `it-look`** index with **valid** `GetItem`; client path uses `TryGetDisplayItem` for missing codes. |
| `TotemLookVisuals.cs` | Pool of **`game:`** item codes for visuals; attribute name `it-look`. |

## Assets

- `assets/inventorytotems/itemtypes/` — JSON for **passive** (`totem-movespeed-t1..t3`) and **puzzle** A/B/C/D (`totem-puzzle-*-t1..t3`), `class: ItemInventoryTotem`, shapes/textures baseline.
- `assets/inventorytotems/lang/en.json` — Display names and descriptions (orthogonal puzzle wording from v0.2.1).

## Behavior summary

### Passive (move speed)

- Scans **all** flattened backpack `SlotEntry` codes.
- Highest tier **3 > 2 > 1** wins.
- Applies `walkspeed` and `sprintSpeed` on layers `inventorytotems-passive` (WeightedSum-style deltas).

### Puzzle (max HP)

- Slots grouped by **`BagIndex`**, sorted by **`SlotInBag`**.
- One topology **`bag-grid`** per bag: `FromRows("bag-grid", n, PuzzleGridRows(n))` — row-major indices; **`OrthogonalNeighbors`** yields N/E/S/W within bounds (respects short last row).
- Patterns tried in order: **ABCD**, **ABC**, **AB** (longest first per chain attempt from each unused A).
- **`used`** per tier marks slots consumed by an accepted chain so they are not reused. **`ToAppliedState`** picks a **single** best puzzle by tier then chain length for the applied HP bonus.

### Health application

- `EntityHealthBonusHelper` sets/clears flat max HP modifier on the **`inventorytotems-puzzle`** layer.

### Visuals (client)

- Stack attribute **`it-look`**: integer index into `TotemLookVisuals.GameItemCodes`.
- **OnBeforeRender:** tesselates referenced vanilla item, **UploadMultiTextureMesh**, caches by resolved index; copies **Gui / FP / TP / offhand / ground** `ModelTransform` from source item to fix scale vs stick-only JSON.

### Commands (`/it` …)

| Subcommand | Function |
|------------|----------|
| `help` | Usage |
| `status` / `scan` | Force scan + state line |
| `probe` | Slot dump, passive tier, puzzle match lines |
| `map` | Inventory class names + backpack slot detail (B/S/flat) |
| `layout` | Per-bag slot counts + puzzle **grid** dimensions (orthogonal chains) |
| `explain` | Rules text + current state |
| `watch on\|off` | Verbose watch notifications |

## Dependencies

- `modinfo.json`: `game` **1.22.0**
- Project references **`VintagestoryAPI.dll`** via `VintageStoryPath` / `Directory.Build.props`.

## Known limitations

- **Grid rows** are **heuristic**, not read from each bag item’s UI layout; very unusual mods may disagree with engine slot order.
- **Single-row** bags (`PuzzleGridRows` ⇒ 1) only allow **left/right** links (no cell above/below in the model).
- **Best puzzle only** applies to max HP even if multiple disjoint chains exist.
- Visual pool depends on **vanilla** `game:` items existing; client skips missing codes.

## Deploy

- Build outputs **DLL** to project folder and optionally `%AppData%\Roaming\VintagestoryData\Mods\InventoryTotems` (Windows target in `.csproj`).

---

## Deep audit (2026-04) — architecture, risks, follow-ups

### 1) Architecture (what every layer does)

| Area | Mechanism | Notes |
|------|-----------|--------|
| **Discovery** | `CollectBackpackBagSlots` → `ItemSlotBagContent` for `GlobalConstants.backpackInvClassName` | Non-bag rows use `SlotKind` and `B -1` — not used for totems, only listed in `/it map` detail. |
| **Passive** | Any matching passive totem code → max tier 3/2/1, single `%` to `walkspeed` / `sprintSpeed` on layers `inventorytotems-passive` | Cleared when 0. WeightedSum-style deltas. |
| **Puzzle** | Per `BagIndex`, `OrderBy(SlotInBag)` (from API `SlotIndex`), row-major grid from `PuzzleGridRows`, orthogonal DFS, patterns ABCD → ABC → AB, `used` per tier | **Best** applied puzzle: max tier then chain length (`ToAppliedState`). |
| **HP** | `EntityHealthBonusHelper` → reflection `SetMaxHealthModifiers` on `EntityBehaviorHealth` | No `Vintagestory` survival DLL at compile time; if API renames, bonus silently fails. |
| **Client look** | `it-look` on stack; `OnBeforeRender` meshes from `game:` items from `TotemLookVisuals` | Pool can miss items in stripped clients; `TryGetDisplayItem` falls through. |
| **Tick** | 300ms all online players; `PlayerNowPlaying` + scan ensure look on inv | CPU scales with player count; acceptable for small/medium. |

### 2) Correctness & edge cases

- **`SlotInBag` naming vs API:** [ItemSlotBagContent](https://apidocs.vintagestory.at/api/Vintagestory.API.Common.ItemSlotBagContent.html) exposes **`SlotIndex`**. The code **correctly** passes `bag.SlotIndex` into the struct; the struct field is **badly named** `SlotInBag` (semantics OK, name confusing for maintainers). No ordering bug.
- **Grid model:** `PuzzleGridRows` is **heuristic** (1 row, or 2 if even `n` and `n/2 >= 3`). Odd wide bags or modded bags with non-row-major UIs can disagree with the **logical** `SlotIndex` layout — this is a **documented** limitation, not a silent bug in code.
- **Orthogonal DFS** neighbor order is L/R/U/D — if multiple valid paths exist, the **first found** (depth-first) wins, not the “most intuitive” path. Deterministic, may surprise players; rare.
- **Multiple matches:** `PuzzleMatches` can list **several** winning chains; **only one** HP/tier applies via `ToAppliedState`. `/it watch` can show `matches=2+` with **one** applied line — by design, easy to read as a bug; document or extend `/it probe` to mark “**applied**”.
- **Reflection health:** if `SetMaxHealthModifiers` signature/behavior changes in a game minor update, this mod could **set nothing**; no build-time check. **Mitigation:** one-time test after game updates, or add a one-line server log on first success (optional).

### 3) Build / repo hygiene (action items)

- **`PackageForMods`:** copies DLL + `modinfo` + **`assets/`** into `out/ForMods/` (complete portable folder).
- **Duplicate tree:** ~~nested `InventoryTotems/InventoryTotems/`~~ **removed** — single tree at mod root only.
- **`Directory.Build.props`:** has a **machine-specific** default `C:\VSDev\Vintagestory` — great for you; other devs (or a future you) should use the [GitHub-style] placeholder pattern from other `Take2Mod\GitHub\*` projects before publishing source.

### 4) Scalability & long-running servers

- `lastStateByPlayer`: entries are **removed on `PlayerDisconnect`** (and `watchPlayers` pruned for that UID) to avoid unbounded growth on busy servers.
- Full scan of **all** `bagContents` every 300ms per online player: **O(players × slots)**. Unlikely to be an issue for typical MP; large slot mods could increase cost.

### 5) Mod integration / balance

- Passive and puzzle are **independent** stats (movespeed vs max HP) — can stack. Other mods (traits, drunks, `HealthEffects` curve, etc.) may also touch `walkspeed` or `EntityStats` / health — only **in-game** balance testing proves “feel.”
- Not using **IStatSource**-style or explicit ordering vs traits — if conflicts appear, your **layer names** (`inventorytotems-*`) are the levers; document priority intent.

### 6) Cross-project brainstorm (this workspace)

- **[Take2Mod/HealthEffects](.):** Watches health % and `EntityStats` patterns — *idea:* optional Character panel or tooltip line for “active totem / puzzle” using same `WatchedAttribute` style if you want player-visible feedback without chat spam.
- **[Take2Mod/HungerEffects](.):** Satiety → one stat layer; same family as your passive (single backpack-derived scalar).
- **[Take2Mod/FullStatGUI](.):** Lists blended stats and watched attributes — *idea:* use as a **debug** companion to verify `walkspeed` and max HP in dev.
- **[Take2Mod/InventoryMoveSpeed](.):** Backpack **fullness** as speed — different rule set; be careful if both mods ever merge into one “totem + fullness” product (competing `walkspeed` layers).
- **[No Touch Items / TraitsMods / Effect Meter](.):** HUD and server authoritativeness patterns — *idea:* (older idea, dropped) optional edge overlay for “which slots form the chain” — would be client UI work + sync, non-trivial.

### 7) Innovation backlog (optional)

- **Puzzle `topology` in notifications:** when multiple disjoint chains, show **which bag index** the applied chain came from (`PuzzleMatch` + bag index in snapshot).
- **Config file:** `PuzzleGridRows` as JSON override per slot count, or per-modded-bag heuristics, without recompile.
- **Tests:** A **pure** static test project feeding synthetic `PuzzleSlot[]` + `LayoutTopology` to validate orthogonals and `used` — no game runtime.
- **Asset zip:** one MSBuild target that always produces `out/ForMods/InventoryTotems.zip` with `dll` + `modinfo` + full `assets/`.
- **Sprint-only passive:** if design wants “walking only, not run,” split layers or add config (currently both walk and sprint get same delta).

### 8) Re-audit quick checklist (before a release)

- [ ] `modinfo` version = `ModVersion` constant = packaged zip name.  
- [ ] Build from clean clone with **placeholder** or CI `VintageStoryPath`.  
- [ ] In-game: passive only, puzzle only, both, neither; two bags, one bag, empty bag.  
- [ ] After a **game** patch: confirm **HP** and **move** still change in Character stats.  
- [ ] `out/ForMods` contains full mod (DLL + modinfo + assets).  
- [ ] No duplicate nested `InventoryTotems` asset tree.

## Hardening updates in v0.2.4

- Puzzle matching now evaluates both primary and transposed bag topologies and applies the strongest valid chain result.
- `/it probe` now prints the currently applied chain summary (`bag`, `topology`, `tier`, `len`, `hp`) to reduce ambiguity while debugging.
- Puzzle item JSONs now use `game:item/stick` texture path (fixes missing `game:textures/item/resource/stick.png` warnings).
- Reflection for health max-bonus writes now caches method discovery for lower per-tick overhead and safer runtime behavior.
