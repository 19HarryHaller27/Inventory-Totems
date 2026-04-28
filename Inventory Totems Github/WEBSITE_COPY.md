# Inventory Totems - Website Copy

## What it is

Designed as a practical, debuggable framework modders can extend.

Inventory Totems adds two clean mechanics that live entirely in backpack storage:

- passive movement totems (tier override style), and
- puzzle HP totems (`A -> B -> C -> D`) resolved by orthogonal adjacency.

At a glance, this sounds simple: put pieces near each other, get bonuses.  
In practice, the solved problem is deeper:

- resolving adjacency from real server-side slot mapping instead of only GUI assumptions,
- handling multiple candidate layout interpretations when slot order and visual orientation disagree,
- preventing fake chains caused by index compression or slot reuse,
- preserving deterministic behavior under continuous scan/update cycles,
- and giving players evidence-rich diagnostics in chat so failures are explainable.

Inventory Totems is a server-authoritative totem framework for Vintage Story 1.22+ that turns a potentially confusing inventory puzzle into a predictable gameplay contract:

- if a valid chain exists in the selected scope, the server applies it,
- if no valid chain exists, the server says so clearly,
- and if topology assumptions are ambiguous, diagnostics expose exactly what was evaluated.

This is not just "a few bonus items."  
It is an interaction-hardened inventory system with observable behavior, runtime controls, and clear failure modes.

## How to use

### 1) Passive move-speed totems

Put movement totems in backpack storage.  
Highest tier wins (`T3 > T2 > T1`).

### 2) Puzzle HP totems

Place same-tier puzzle pieces in valid order:

- `A -> B` (minimum)
- optional continuation: `-> C -> D`

Rules:

- orthogonal adjacency only (no diagonals),
- no slot reuse in one resolved match set,
- solved server-side.

### 3) Scope control (important)

Default behavior is global, meaning puzzle matching can connect across backpack boundaries.

Use:

- `/it scope global`
- `/it scope perbag`
- `/it scope` (current value)

### 4) Quick verify

- `/it status` - current passive/puzzle application
- `/it probe` - full resolved slot and chain picture
- `/it audit` - candidate topology scores and why one was selected

### 5) Up/down and neighbor debugging

Use:

- `/it map` to get flat slot indices (`#flat`)
- `/it neighbors <flat>` to inspect exact orthogonal neighbors for that slot

This is the fastest way to prove why a vertical or horizontal step did or did not connect.

### 6) Troubleshooting checklist

- confirm game/mod version is the build you just shipped,
- run `/it scope` before testing assumptions,
- keep `/it watch off` while debugging to reduce chat noise,
- use `/it probe` + `/it audit` as the primary truth source.

## Design philosophy

Inventory Totems is built on four non-negotiable principles.

First, server truth is absolute.  
Effects are decided and enforced by the server, not guessed from client visuals.

Second, deterministic beats magical.  
The mod prefers explicit resolution rules and tie-breaking scores over hidden heuristics.

Third, diagnostics are product features.  
`/it probe`, `/it audit`, `/it neighbors`, and `/it map` exist because trust requires inspectability.

Fourth, composability over cleverness.  
The system is organized for extension: constants are explicit, matching logic is isolated, and behavior can be tuned without reverse-engineering surprises.

## What it is not

Inventory Totems is not:

- a territory/claims politics system,
- a generalized anti-grief platform,
- a large client UI overhaul,
- or a replacement for progression balancing mods.

It is a focused server gameplay framework for reliable inventory-driven passive and puzzle bonuses with first-class observability.

## License / use

Free to use, copy, modify, and redistribute this mod and derived works for any purpose, including commercial packs, provided you credit the original author (`Squidbat`) in your readme, mod page, or repository credits so users can find upstream context for questions and fixes.

If you want, this section can be replaced with a formal license file language to match your publishing platform.
