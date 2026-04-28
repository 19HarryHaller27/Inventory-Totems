# Inventory Totems

Server-authoritative backpack totem mechanics for Vintage Story 1.22+.

Inventory Totems provides two systems:

- Passive movement totems (highest tier wins)
- Puzzle HP totems (`A -> B -> C -> D`) using orthogonal adjacency

Puzzle scope is global by default (cross-backpack), with runtime switching:

- `/it scope global`
- `/it scope perbag`

## Why this mod exists

This project is built to make totem behavior practical in real gameplay instead of ambiguous in theory.

The hard part is not defining puzzle letters. The hard part is making chain detection reliable across actual backpack slot mapping, orientation ambiguity, and server timing while still giving players proof for why a chain did or did not connect.

This mod solves that by pairing deterministic server logic with in-game diagnostics.

## Core behavior

- Effects read backpack storage only (equipped bag inner slots).
- Passive: highest tier present applies movespeed bonus.
- Puzzle: same-tier chains with orthogonal neighbors only.
- No slot reuse inside one resolved puzzle match.
- Best candidate topology/view is selected with score-based comparison.

## In-game commands

- `/it help`
- `/it status`
- `/it scan`
- `/it explain`
- `/it scope [perbag|global]`
- `/it neighbors <flat>`
- `/it audit`
- `/it probe`
- `/it map`
- `/it layout`
- `/it watch [on|off]`

## Debug-first workflow

When placement feels wrong:

1. Run `/it probe` to see resolved matches.
2. Run `/it audit` to inspect candidate scoring and selected rationale.
3. Run `/it neighbors <flat>` on relevant pieces.
4. Run `/it map` if you need exact `#flat` lookup.

## Build

From this folder:

`dotnet build "InventoryTotems.csproj" -c Release`

## Version

Current: `0.2.8`

## License / use

Free to use, copy, modify, and redistribute, including in packs. Credit the original author (`Squidbat`) in your readme, mod page, or repository credits so users can find upstream context and support.

If you need a formal SPDX-style license file for a specific distribution platform, add one before publishing there.
