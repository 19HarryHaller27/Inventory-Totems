using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace InventoryTotems;

/// <summary>
/// Patch 2 baseline: per-bag puzzle on a row-major grid from <see cref="PuzzleGridRows"/> / <see cref="LayoutTopology"/>.
/// Patch 3+: A to B to C to D uses orthogonal steps (N/E/S/W) on that grid, not only left-to-right or top-to-bottom.
/// </summary>
public sealed class InventoryTotemsServerSystem : ModSystem
{
    private enum PuzzleScope
    {
        Global,
        PerBag
    }

    private ICoreServerAPI? sapi;
    private long scanListenerId;
    private bool playerEventHooked;
    private bool playerDisconnectHooked;
    private readonly Dictionary<string, AppliedState> lastStateByPlayer = [];
    private readonly HashSet<string> watchPlayers = [];
    /// <summary>Per UID: show puzzle-debug checklist once per connection (cleared on disconnect).</summary>
    private readonly HashSet<string> debugLoginPromptShownUid = [];
    private PuzzleScope puzzleScope = PuzzleScope.Global;

    public override void StartPre(ICoreAPI api)
    {
        api.RegisterItemClass("ItemInventoryTotem", typeof(ItemInventoryTotem));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        RegisterCommands(api);
        scanListenerId = api.Event.RegisterGameTickListener(_ => ScanAllPlayers(false), 300);
        api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
        api.Event.PlayerDisconnect += OnPlayerDisconnect;
        playerEventHooked = true;
        playerDisconnectHooked = true;
    }

    public override void Dispose()
    {
        if (sapi is not null)
        {
            if (playerEventHooked) sapi.Event.PlayerNowPlaying -= OnPlayerNowPlaying;
            if (playerDisconnectHooked) sapi.Event.PlayerDisconnect -= OnPlayerDisconnect;
            if (scanListenerId != 0) sapi.Event.UnregisterGameTickListener(scanListenerId);
        }
        playerEventHooked = false;
        playerDisconnectHooked = false;
    }

    private void RegisterCommands(ICoreServerAPI api)
    {
        IChatCommandApi c = api.ChatCommands;
        c.GetOrCreate("it")
            .WithAlias("inventorytotems")
            .WithDescription("Inventory Totems diagnostics. Try /it help")
            .RequiresPlayer()
            .RequiresPrivilege("chat")
            .WithArgs(c.Parsers.OptionalAll("itArgs"))
            .HandleWith(OnCmdIt);
    }

    private TextCommandResult OnCmdIt(TextCommandCallingArgs args)
    {
        var all = (args[0] as string ?? string.Empty).Trim();
        var parts = all.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sub = parts.Length > 0 ? parts[0].ToLowerInvariant() : "help";

        return sub switch
        {
            "status" => OnCmdStatus(args),
            "audit" => OnCmdAudit(args),
            "probe" => OnCmdProbe(args),
            "map" => OnCmdMap(args),
            "layout" => OnCmdLayout(args),
            "scan" => OnCmdScan(args),
            "explain" => OnCmdExplain(args),
            "scope" => parts.Length > 1 ? OnCmdScope(args, parts[1]) : OnCmdScopeStatus(args),
            "neighbors" => parts.Length > 1 ? OnCmdNeighbors(args, parts[1]) : TextCommandResult.Error("Usage: /it neighbors <flat>"),
            "watch" => parts.Length > 1
                ? OnCmdWatch(args, parts[1].Equals("on", StringComparison.OrdinalIgnoreCase))
                : OnCmdWatchStatus(args),
            "help" => TextCommandResult.Success("Usage: /it status | scan | explain | scope [perbag|global] | neighbors <flat> | audit | probe | map | layout | watch [on|off]"),
            _ => TextCommandResult.Error("Unknown subcommand. Try /it help")
        };
    }

    private TextCommandResult OnCmdStatus(TextCommandCallingArgs args)
    {
        var player = args.Caller.Player as IServerPlayer;
        if (player is null) return TextCommandResult.Error("Players only.");
        var state = ScanOnePlayer(player, true);
        return TextCommandResult.Success(FormatStateLine(state));
    }

    private TextCommandResult OnCmdAudit(TextCommandCallingArgs args)
    {
        var player = args.Caller.Player as IServerPlayer;
        if (player is null) return TextCommandResult.Error("Players only.");

        var bagSlots = CollectBackpackBagSlots(player);
        var lines = new StringBuilder();
        lines.AppendLine("Topology audit (per bag): compares candidate grid orientations and their best chain results.");

        foreach (var group in bagSlots.Where(s => s.BagIndex >= 0).GroupBy(s => s.BagIndex).OrderBy(g => g.Key))
        {
            var mapped = BuildBagLocalSlots(group);
            var slotCount = mapped.Count;
            var candidates = CandidateTopologiesForBag(slotCount);
            lines.AppendLine($"bag B{group.Key}: innerSlots={group.Count()} mappedSlots={slotCount} candidates={candidates.Count}");

            List<PuzzleMatch>? best = null;
            MatchSetScore? bestScore = null;
            string rationale = "none";
            foreach (var topo in candidates)
            {
                var matches = ResolvePuzzleMatches(mapped, topo, group.Key);
                var score = EvaluateMatchSet(matches);
                var top = matches
                    .OrderByDescending(m => m.Tier)
                    .ThenByDescending(m => m.ChainLength)
                    .ThenByDescending(m => m.MaxHpBonus)
                    .FirstOrDefault();
                var topText = top is null
                    ? "none"
                    : $"t{top.Tier} len={top.ChainLength} hp={top.MaxHpBonus:0} flat=[{string.Join(",", top.FlatIndices)}]";
                lines.AppendLine($"  - topo={topo.Name} grid={topo.Rows}x{topo.Cols} score=[tier={score.BestTier},len={score.BestLength},hp={score.TotalHp:0},matches={score.MatchCount}] top={topText}");
                if (best is null)
                {
                    best = matches;
                    bestScore = score;
                }
                else if (bestScore is { } prior)
                {
                    var reason = CompareScoresReason(score, prior);
                    if (reason is not null)
                    {
                        best = matches;
                        bestScore = score;
                        rationale = reason;
                    }
                }
            }

            if (best is null || best.Count == 0)
            {
                lines.AppendLine("  => selected: none (rationale: no valid chains)");
                continue;
            }

            var selected = best
                .OrderByDescending(m => m.Tier)
                .ThenByDescending(m => m.ChainLength)
                .ThenByDescending(m => m.MaxHpBonus)
                .First();
            lines.AppendLine($"  => selected: topo={selected.TopologyName} t{selected.Tier} len={selected.ChainLength} hp={selected.MaxHpBonus:0} flat=[{string.Join(",", selected.FlatIndices)}] (rationale: {rationale})");
        }

        var globalViews = CandidateGlobalViews(bagSlots);
        lines.AppendLine($"GLOBAL: views={globalViews.Count}");
        List<PuzzleMatch>? globalBest = null;
        MatchSetScore? globalBestScore = null;
        var globalRationale = "none";
        foreach (var view in globalViews)
        {
            var globalCandidates = CandidateTopologiesForBag(view.Slots.Count);
            lines.AppendLine($"  view={view.Name} mappedSlots={view.Slots.Count} candidates={globalCandidates.Count}");
            foreach (var topo in globalCandidates)
            {
                var matches = ResolvePuzzleMatches(view.Slots, topo, -1);
                var tagged = TagMatches(matches, view.Name);
                var score = EvaluateMatchSet(tagged);
                var top = tagged
                    .OrderByDescending(m => m.Tier)
                    .ThenByDescending(m => m.ChainLength)
                    .ThenByDescending(m => m.MaxHpBonus)
                    .FirstOrDefault();
                var topText = top is null
                    ? "none"
                    : $"t{top.Tier} len={top.ChainLength} hp={top.MaxHpBonus:0} flat=[{string.Join(",", top.FlatIndices)}]";
                lines.AppendLine($"    - topo={topo.Name} grid={topo.Rows}x{topo.Cols} score=[tier={score.BestTier},len={score.BestLength},hp={score.TotalHp:0},matches={score.MatchCount}] top={topText}");
                if (globalBest is null)
                {
                    globalBest = tagged;
                    globalBestScore = score;
                }
                else if (globalBestScore is { } prior)
                {
                    var reason = CompareScoresReason(score, prior);
                    if (reason is not null)
                    {
                        globalBest = tagged;
                        globalBestScore = score;
                        globalRationale = reason;
                    }
                }
            }
        }
        if (globalBest is null || globalBest.Count == 0)
        {
            lines.AppendLine("  => selected: none (rationale: no valid chains)");
        }
        else
        {
            var selected = globalBest
                .OrderByDescending(m => m.Tier)
                .ThenByDescending(m => m.ChainLength)
                .ThenByDescending(m => m.MaxHpBonus)
                .First();
            lines.AppendLine($"  => selected: topo={selected.TopologyName} t{selected.Tier} len={selected.ChainLength} hp={selected.MaxHpBonus:0} flat=[{string.Join(",", selected.FlatIndices)}] (rationale: {globalRationale})");
        }

        return SendChunkedDiagnostics(player, lines.ToString(), "audit");
    }

    private TextCommandResult OnCmdProbe(TextCommandCallingArgs args)
    {
        var player = args.Caller.Player as IServerPlayer;
        if (player is null) return TextCommandResult.Error("Players only.");
        var snapshot = BuildSnapshot(player);
        var lines = new StringBuilder();
        lines.AppendLine($"slots={snapshot.Slots.Count}");
        lines.AppendLine($"items={string.Join(", ", snapshot.Slots.Select(FormatSlot))}");
        lines.AppendLine($"passiveTier={snapshot.PassiveTier}");
        lines.AppendLine($"puzzleMatches={snapshot.PuzzleMatches.Count}");
        lines.AppendLine($"scope={FormatScope(puzzleScope)}");
        var applied = snapshot.PuzzleMatches
            .OrderByDescending(m => m.Tier)
            .ThenByDescending(m => m.ChainLength)
            .ThenByDescending(m => m.MaxHpBonus)
            .FirstOrDefault();
        if (applied is not null)
        {
            var bag = applied.BagIndex >= 0 ? $"B{applied.BagIndex}" : "GLOBAL";
            lines.AppendLine($"applied=t{applied.Tier} len={applied.ChainLength} hp={applied.MaxHpBonus:0} bag={bag} topo={applied.TopologyName}");
        }
        foreach (var match in snapshot.PuzzleMatches)
        {
            lines.AppendLine($"- {match}");
        }

        return SendChunkedDiagnostics(player, lines.ToString(), "probe");
    }

    private TextCommandResult OnCmdMap(TextCommandCallingArgs args)
    {
        var player = args.Caller.Player as IServerPlayer;
        if (player is null) return TextCommandResult.Error("Players only.");
        var lines = new StringBuilder();
        lines.AppendLine("Engine inventory class names (GlobalConstants):");
        lines.AppendLine($"  hotbar={GlobalConstants.hotBarInvClassName} backpack={GlobalConstants.backpackInvClassName}");
        lines.AppendLine($"  character={GlobalConstants.characterInvClassName} crafting={GlobalConstants.craftingInvClassName}");
        lines.AppendLine("Totems use only backpack: flattened storage from every equipped bag (not hotbar, not crafting).");

        AppendInventorySummary(lines, player, GlobalConstants.hotBarInvClassName, "hotbar");
        AppendInventorySummary(lines, player, GlobalConstants.backpackInvClassName, "backpack (bag storage)");
        AppendInventorySummary(lines, player, GlobalConstants.craftingInvClassName, "crafting grid");
        AppendInventorySummary(lines, player, GlobalConstants.characterInvClassName, "character gear");

        var bagSlots = CollectBackpackBagSlots(player);
        lines.AppendLine("backpack detail (flat index = order in BagInventory; bag B / slot S = equipped bag pocket and slot inside that bag):");
        if (bagSlots.Count == 0)
        {
            lines.AppendLine("  (no backpack inventory or empty)");
        }
        else
        {
            foreach (var s in bagSlots)
            {
                lines.AppendLine($"  #{s.FlatIndex} B{s.BagIndex} S{s.SlotInBag} {s.SlotKind} {s.Code}");
            }

            lines.AppendLine("slots per equipped bag index (B):");
            foreach (var g in bagSlots.Where(s => s.BagIndex >= 0).GroupBy(s => s.BagIndex).OrderBy(g => g.Key))
            {
                lines.AppendLine($"  B{g.Key}: count={g.Count()}");
            }
        }

        return SendChunkedDiagnostics(player, lines.ToString(), "map");
    }

    private static void AppendInventorySummary(StringBuilder lines, IServerPlayer player, string className, string label)
    {
        var inv = player.InventoryManager.GetOwnInventory(className);
        if (inv is null)
        {
            lines.AppendLine($"{label}: (null)");
            return;
        }

        lines.AppendLine($"{label}: class={inv.ClassName} slots={inv.Count}");
    }

    private TextCommandResult OnCmdLayout(TextCommandCallingArgs args)
    {
        var player = args.Caller.Player as IServerPlayer;
        if (player is null) return TextCommandResult.Error("Players only.");
        var backpack = CollectBackpackBagSlots(player);
        var count = backpack.Count;
        var lines = new StringBuilder();
        lines.AppendLine($"totalFlatSlots={count} (sum of inner slots on all equipped bags)");
        foreach (var g in backpack.Where(s => s.BagIndex >= 0).GroupBy(s => s.BagIndex).OrderBy(x => x.Key))
        {
            var mappedSlots = BuildBagLocalSlots(g).Count;
            var gRows = PuzzleGridRows(mappedSlots);
            var grid = LayoutTopology.FromRows("bag-grid", mappedSlots, gRows);
            lines.AppendLine($"bag B{g.Key}: innerSlots={g.Count()} mappedSlots={mappedSlots} puzzleGrid={grid.Rows}x{grid.Cols} (orthogonal chains)");
        }

        return SendChunkedDiagnostics(player, lines.ToString(), "layout");
    }

    private TextCommandResult OnCmdScan(TextCommandCallingArgs args)
    {
        var player = args.Caller.Player as IServerPlayer;
        if (player is null) return TextCommandResult.Error("Players only.");
        var state = ScanOnePlayer(player, true);
        return TextCommandResult.Success($"scan complete: {FormatStateLine(state)}");
    }

    private TextCommandResult OnCmdExplain(TextCommandCallingArgs args)
    {
        var player = args.Caller.Player as IServerPlayer;
        if (player is null) return TextCommandResult.Error("Players only.");
        var snapshot = BuildSnapshot(player);
        var lines = new StringBuilder();
        lines.AppendLine("Inventory Totems rules:");
        lines.AppendLine("- Effects use backpack storage only (worn bag inner slots). Hotbar/crafting/character gear do not count.");
        lines.AppendLine("- Passive totems: highest tier in backpack wins move speed (T3 > T2 > T1).");
        lines.AppendLine("- Puzzle totems: A -> B (+C)(+D), same tier, each step orthogonally adjacent (no diagonals).");
        lines.AppendLine($"- Puzzle scope is currently '{FormatScope(puzzleScope)}' (change with /it scope perbag|global).");
        lines.AppendLine("- No slot reuse inside puzzle matches.");
        lines.AppendLine("- Need proof for adjacency? Use /it neighbors <flat> and /it audit.");
        lines.AppendLine("- Notifications are sent on state change (or with /it watch on).");
        lines.AppendLine($"Current: {FormatStateLine(ToAppliedState(snapshot))}");
        return TextCommandResult.Success(lines.ToString());
    }

    private TextCommandResult OnCmdWatch(TextCommandCallingArgs args, bool enable)
    {
        var uid = args.Caller.Player.PlayerUID;
        if (enable) watchPlayers.Add(uid);
        else watchPlayers.Remove(uid);
        return TextCommandResult.Success($"watch={(enable ? "on" : "off")}");
    }

    private TextCommandResult OnCmdWatchStatus(TextCommandCallingArgs args)
    {
        var uid = args.Caller.Player.PlayerUID;
        return TextCommandResult.Success(watchPlayers.Contains(uid)
            ? "watch=on; use /it watch off to disable."
            : "watch=off; use /it watch on to enable.");
    }

    private TextCommandResult OnCmdScope(TextCommandCallingArgs args, string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        var target = normalized switch
        {
            "global" => PuzzleScope.Global,
            "perbag" => PuzzleScope.PerBag,
            _ => (PuzzleScope?)null
        };
        if (target is null)
        {
            return TextCommandResult.Error("Usage: /it scope perbag|global");
        }

        if (puzzleScope == target.Value)
        {
            return TextCommandResult.Success($"scope unchanged: {FormatScope(puzzleScope)}");
        }

        puzzleScope = target.Value;
        ScanAllPlayers(true);
        return TextCommandResult.Success($"scope set: {FormatScope(puzzleScope)}");
    }

    private TextCommandResult OnCmdScopeStatus(TextCommandCallingArgs args)
    {
        return TextCommandResult.Success($"scope={FormatScope(puzzleScope)}; use /it scope perbag|global to change.");
    }

    private TextCommandResult OnCmdNeighbors(TextCommandCallingArgs args, string flatText)
    {
        var player = args.Caller.Player as IServerPlayer;
        if (player is null) return TextCommandResult.Error("Players only.");
        if (!int.TryParse(flatText, out var flat) || flat < 0)
        {
            return TextCommandResult.Error("Usage: /it neighbors <flat> (flat must be >= 0)");
        }

        var bagSlots = CollectBackpackBagSlots(player);
        var globalViews = CandidateGlobalViews(bagSlots);
        if (globalViews.Count == 0)
        {
            return TextCommandResult.Error("No backpack bag slots found.");
        }

        var lines = new StringBuilder();
        lines.AppendLine($"neighbors for flat #{flat}:");
        lines.AppendLine($"scope={FormatScope(puzzleScope)}");
        var mappedInAnyView = false;
        foreach (var view in globalViews)
        {
            var localIndex = view.Slots.FindIndex(s => s.FlatIndex == flat);
            lines.AppendLine($"view={view.Name} mappedSlots={view.Slots.Count}");
            if (localIndex < 0)
            {
                lines.AppendLine("  - flat slot not mapped in this view");
                continue;
            }

            mappedInAnyView = true;
            lines.AppendLine($"  - slot code={view.Slots[localIndex].Code} local={localIndex}");
            var candidates = CandidateTopologiesForBag(view.Slots.Count);
            foreach (var topo in candidates)
            {
                var neighbors = topo.OrthogonalNeighbors(localIndex)
                    .Select(nb =>
                    {
                        var nbSlot = view.Slots[nb];
                        return $"local={nb} flat={nbSlot.FlatIndex} code={nbSlot.Code}";
                    })
                    .ToList();
                var text = neighbors.Count == 0 ? "(none)" : string.Join(" | ", neighbors);
                lines.AppendLine($"  - topo={topo.Name} grid={topo.Rows}x{topo.Cols} neighbors: {text}");
            }
        }

        if (!mappedInAnyView)
        {
            return TextCommandResult.Error($"Flat slot #{flat} is not a mapped backpack bag slot.");
        }

        return SendChunkedDiagnostics(player, lines.ToString(), "neighbors");
    }

    private void ScanAllPlayers(bool forceNotify)
    {
        if (sapi is null) return;
        foreach (var player in sapi.World.AllOnlinePlayers)
        {
            if (player is IServerPlayer sp) ScanOnePlayer(sp, forceNotify);
        }
    }

    private void OnPlayerNowPlaying(IServerPlayer player)
    {
        EnsureTotemLookOnPlayerInventories(player);
        if (!debugLoginPromptShownUid.Add(player.PlayerUID))
            return;
        SendPuzzleDebugLoginPrompt(player);
    }

    private static void SendPuzzleDebugLoginPrompt(IServerPlayer player)
    {
        void Line(string text) =>
            player.SendMessage(GlobalConstants.GeneralChatGroup, text, EnumChatType.Notification);

        Line("[IT] Puzzle debug — if placement feels wrong, capture this for reports:");
        Line("A) /it audit — per-bag/global topology candidates + selected chain rationale");
        Line("B) /it layout — copy each line: bag B# innerSlots=n puzzleGrid=RxC");
        Line("C) /it probe — full output; lines like - tier= len= flat=[…]");
        Line("D) /it map — backpack block #flat B# S# (bag index + slot order)");
        Line("E) Screenshot when it fails + tier I / II / III on pieces");
    }

    private void OnPlayerDisconnect(IServerPlayer player)
    {
        lastStateByPlayer.Remove(player.PlayerUID);
        watchPlayers.Remove(player.PlayerUID);
        debugLoginPromptShownUid.Remove(player.PlayerUID);
    }

    private static void EnsureTotemLookOnPlayerInventories(IServerPlayer player)
    {
        IWorldAccessor? w = player.Entity?.World;
        if (w is null) return;

        void Scan(string invClass)
        {
            var inv = player.InventoryManager.GetOwnInventory(invClass);
            if (inv is null) return;
            for (var i = 0; i < inv.Count; i++)
            {
                var stack = inv[i].Itemstack;
                if (stack is null) continue;
                if (TotemLookHelper.EnsureLook(w, stack)) inv[i].MarkDirty();
            }
        }

        Scan(GlobalConstants.hotBarInvClassName);
        Scan(GlobalConstants.backpackInvClassName);
        Scan(GlobalConstants.mousecursorInvClassName);
    }

    private AppliedState ScanOnePlayer(IServerPlayer player, bool forceNotify)
    {
        EnsureTotemLookOnPlayerInventories(player);
        var snapshot = BuildSnapshot(player);
        var state = ToAppliedState(snapshot);
        ApplyState(player, state);
        NotifyStateChanges(player, state, forceNotify, snapshot);
        lastStateByPlayer[player.PlayerUID] = state;
        return state;
    }

    private Snapshot BuildSnapshot(IServerPlayer player)
    {
        var slots = CollectBackpackBagSlots(player);
        var passiveTier = ResolvePassiveTier(slots);
        var puzzleMatches = puzzleScope switch
        {
            PuzzleScope.PerBag => ResolvePuzzleMatchesPerBag(slots),
            _ => ResolvePuzzleMatchesGlobal(slots)
        };
        return new Snapshot(slots, passiveTier, puzzleMatches);
    }

    private static AppliedState ToAppliedState(Snapshot snapshot)
    {
        var passivePct = PassiveTierToPct(snapshot.PassiveTier);
        var bestPuzzle = snapshot.PuzzleMatches
            .OrderByDescending(m => m.Tier)
            .ThenByDescending(m => m.ChainLength)
            .FirstOrDefault();

        var puzzleTier = bestPuzzle?.Tier ?? 0;
        var puzzleHp = bestPuzzle?.MaxHpBonus ?? 0f;
        var topo = bestPuzzle?.TopologyName ?? "none";
        return new AppliedState(snapshot.PassiveTier, passivePct, puzzleTier, puzzleHp, topo);
    }

    private void ApplyState(IServerPlayer player, AppliedState state)
    {
        var ent = player.Entity;
        if (ent is null) return;

        // WeightedSum: base layer is 1; trait-style bonuses add deltas (e.g. fleetfooted uses 0.1 for +10%).
        if (state.PassiveMoveSpeedPercent > 0f)
        {
            ent.Stats.Set("walkspeed", InventoryTotemConstants.WalkSpeedLayer, state.PassiveMoveSpeedPercent, true);
            ent.Stats.Set("sprintSpeed", InventoryTotemConstants.SprintSpeedLayer, state.PassiveMoveSpeedPercent, true);
        }
        else
        {
            ent.Stats.Remove("walkspeed", InventoryTotemConstants.WalkSpeedLayer);
            ent.Stats.Remove("sprintSpeed", InventoryTotemConstants.SprintSpeedLayer);
        }

        if (ent is EntityAgent agent)
        {
            if (state.PuzzleMaxHpBonus > 0f)
            {
                EntityHealthBonusHelper.SetFlatMaxHpBonus(agent, InventoryTotemConstants.MaxHpLayer, state.PuzzleMaxHpBonus);
            }
            else
            {
                EntityHealthBonusHelper.ClearFlatMaxHpBonus(agent, InventoryTotemConstants.MaxHpLayer);
            }
        }
    }

    private void NotifyStateChanges(IServerPlayer player, AppliedState now, bool forceNotify, Snapshot snapshot)
    {
        if (sapi is null) return;
        lastStateByPlayer.TryGetValue(player.PlayerUID, out var prev);
        var watching = watchPlayers.Contains(player.PlayerUID);

        if (forceNotify || prev is null || prev.PassiveTier != now.PassiveTier)
        {
            if (now.PassiveTier > 0 && prev is not null && now.PassiveTier > prev.PassiveTier)
            {
                Notify(player, $"[IT] passive override: T{prev.PassiveTier} to T{now.PassiveTier} movespeed.");
            }

            Notify(player, now.PassiveTier > 0
                ? $"[IT] passive active: movespeed T{now.PassiveTier} (+{now.PassiveMoveSpeedPercent * 100f:0}%)."
                : "[IT] passive inactive: no backpack passive totem.");
        }

        if (forceNotify || prev is null || prev.PuzzleTier != now.PuzzleTier || Math.Abs(prev.PuzzleMaxHpBonus - now.PuzzleMaxHpBonus) > 0.001f)
        {
            if (now.PuzzleTier > 0 && prev is not null && now.PuzzleTier > prev.PuzzleTier)
            {
                Notify(player, $"[IT] puzzle override: T{prev.PuzzleTier} to T{now.PuzzleTier}.");
            }

            Notify(player, now.PuzzleTier > 0
                ? $"[IT] puzzle active: T{now.PuzzleTier} +{now.PuzzleMaxHpBonus:0} max HP ({now.TopologyName})."
                : "[IT] puzzle inactive: no valid orthogonal chain.");
        }

        if (watching && (forceNotify || prev is null || prev != now))
        {
            Notify(player, $"[IT] watch: {FormatStateLine(now)} matches={snapshot.PuzzleMatches.Count}");
        }
    }

    private void Notify(IServerPlayer player, string message)
    {
        player.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
    }

    private static string FormatStateLine(AppliedState state)
    {
        return $"passiveTier={state.PassiveTier} move={state.PassiveMoveSpeedPercent * 100f:0}% | puzzleTier={state.PuzzleTier} maxhp={state.PuzzleMaxHpBonus:0} topo={state.TopologyName}";
    }

    private static string FormatSlot(SlotEntry slot)
    {
        return $"#{slot.FlatIndex} B{slot.BagIndex}S{slot.SlotInBag}:{slot.Code}";
    }

    private static List<SlotEntry> CollectBackpackBagSlots(IServerPlayer player)
    {
        var list = new List<SlotEntry>();
        var inv = player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
        if (inv is null) return list;

        for (var i = 0; i < inv.Count; i++)
        {
            var slot = inv[i];
            var stack = slot.Itemstack;
            var code = stack?.Collectible?.Code?.ToShortString() ?? "-";
            if (slot is ItemSlotBagContent bag)
            {
                list.Add(new SlotEntry(i, bag.BagIndex, bag.SlotIndex, "bag", code));
            }
            else
            {
                var kind = slot.GetType().Name;
                list.Add(new SlotEntry(i, -1, -1, kind, code));
            }
        }

        return list;
    }

    private static int ResolvePassiveTier(List<SlotEntry> slots)
    {
        for (var tier = 3; tier >= 1; tier--)
        {
            var code = InventoryTotemConstants.PassiveMoveSpeedTotems[tier - 1].ToShortString();
            if (slots.Any(s => s.Code == code)) return tier;
        }

        return 0;
    }

    private static float PassiveTierToPct(int tier) => tier switch
    {
        1 => InventoryTotemConstants.PassiveMoveSpeedTier1,
        2 => InventoryTotemConstants.PassiveMoveSpeedTier2,
        3 => InventoryTotemConstants.PassiveMoveSpeedTier3,
        _ => 0f
    };

    private static List<PuzzleMatch> ResolvePuzzleMatchesGlobal(List<SlotEntry> allSlots)
    {
        var views = CandidateGlobalViews(allSlots);
        if (views.Count == 0) return [];

        List<PuzzleMatch>? best = null;
        foreach (var view in views)
        {
            var topologies = CandidateTopologiesForBag(view.Slots.Count);
            foreach (var topo in topologies)
            {
                var matches = ResolvePuzzleMatches(view.Slots, topo, -1);
                var tagged = TagMatches(matches, view.Name);
                if (best is null || IsBetterMatchSet(tagged, best))
                {
                    best = tagged;
                }
            }
        }
        return best ?? [];
    }

    private static List<PuzzleMatch> ResolvePuzzleMatchesPerBag(List<SlotEntry> allSlots)
    {
        var results = new List<PuzzleMatch>();
        foreach (var group in allSlots.Where(s => s.BagIndex >= 0).GroupBy(s => s.BagIndex))
        {
            var mapped = BuildBagLocalSlots(group);
            if (mapped.Count == 0) continue;
            var topologies = CandidateTopologiesForBag(mapped.Count);
            List<PuzzleMatch>? best = null;
            foreach (var topo in topologies)
            {
                var matches = ResolvePuzzleMatches(mapped, topo, group.Key);
                if (best is null || IsBetterMatchSet(matches, best))
                {
                    best = matches;
                }
            }
            if (best is not null) results.AddRange(best);
        }
        return results;
    }

    /// <summary>Row count for row-major bag grid (SlotInBag order). Single row => only left/right neighbors.</summary>
    private static int PuzzleGridRows(int slotCount)
    {
        if (slotCount <= 1) return 1;
        if (slotCount % 2 == 0 && slotCount / 2 >= 3) return 2;
        return 1;
    }

    private static List<LayoutTopology> CandidateTopologiesForBag(int slotCount)
    {
        var list = new List<LayoutTopology>();
        var rows = PuzzleGridRows(slotCount);
        var primary = LayoutTopology.FromRows($"bag-grid-r{rows}", slotCount, rows);
        list.Add(primary);

        // Some bag UIs appear transposed relative to SlotInBag order; try the transpose too.
        if (primary.Rows > 1 && primary.Cols > 1 && primary.Rows != primary.Cols)
        {
            list.Add(LayoutTopology.FromRows($"bag-grid-r{primary.Cols}", slotCount, primary.Cols));
        }

        return list;
    }

    private static List<PuzzleSlot> BuildBagLocalSlots(IEnumerable<SlotEntry> bagSlots)
    {
        var ordered = bagSlots.Where(s => s.SlotInBag >= 0).OrderBy(s => s.SlotInBag).ToList();
        if (ordered.Count == 0) return [];
        var max = ordered.Max(s => s.SlotInBag);
        var mapped = Enumerable.Range(0, max + 1)
            .Select(_ => new PuzzleSlot(-1, "-"))
            .ToList();
        foreach (var slot in ordered)
        {
            mapped[slot.SlotInBag] = new PuzzleSlot(slot.FlatIndex, slot.Code);
        }

        return mapped;
    }

    private static List<GlobalPuzzleView> CandidateGlobalViews(List<SlotEntry> allSlots)
    {
        var bagSlots = allSlots.Where(s => s.BagIndex >= 0).OrderBy(s => s.FlatIndex).ToList();
        if (bagSlots.Count == 0) return [];

        var views = new List<GlobalPuzzleView>();
        var preservingHoles = BuildGlobalSlotsPreservingHoles(bagSlots);
        views.Add(new GlobalPuzzleView("global-flat-holes", preservingHoles));

        var dense = BuildGlobalSlotsDense(bagSlots);
        if (dense.Count != preservingHoles.Count)
        {
            views.Add(new GlobalPuzzleView("global-flat-dense", dense));
        }

        return views;
    }

    private static List<PuzzleSlot> BuildGlobalSlotsPreservingHoles(List<SlotEntry> bagSlots)
    {
        if (bagSlots.Count == 0) return [];
        var max = bagSlots.Max(s => s.FlatIndex);
        var mapped = Enumerable.Range(0, max + 1)
            .Select(_ => new PuzzleSlot(-1, "-"))
            .ToList();
        foreach (var slot in bagSlots)
        {
            mapped[slot.FlatIndex] = new PuzzleSlot(slot.FlatIndex, slot.Code);
        }

        return mapped;
    }

    private static List<PuzzleSlot> BuildGlobalSlotsDense(List<SlotEntry> bagSlots)
    {
        return bagSlots
            .Select(s => new PuzzleSlot(s.FlatIndex, s.Code))
            .ToList();
    }

    private static List<PuzzleMatch> TagMatches(List<PuzzleMatch> matches, string viewName)
    {
        return matches
            .Select(match => match with { TopologyName = $"{viewName}:{match.TopologyName}" })
            .ToList();
    }

    private static bool IsBetterMatchSet(List<PuzzleMatch> a, List<PuzzleMatch> b)
    {
        var aScore = EvaluateMatchSet(a);
        var bScore = EvaluateMatchSet(b);
        var aBestTier = aScore.BestTier;
        var bBestTier = bScore.BestTier;
        if (aBestTier != bBestTier) return aBestTier > bBestTier;

        var aBestLen = aScore.BestLength;
        var bBestLen = bScore.BestLength;
        if (aBestLen != bBestLen) return aBestLen > bBestLen;

        var aTotalHp = aScore.TotalHp;
        var bTotalHp = bScore.TotalHp;
        if (Math.Abs(aTotalHp - bTotalHp) > 0.001f) return aTotalHp > bTotalHp;

        return aScore.MatchCount > bScore.MatchCount;
    }

    private static MatchSetScore EvaluateMatchSet(List<PuzzleMatch> matches)
    {
        if (matches.Count == 0) return new MatchSetScore(0, 0, 0f, 0);
        return new MatchSetScore(
            matches.Max(m => m.Tier),
            matches.Max(m => m.ChainLength),
            matches.Sum(m => m.MaxHpBonus),
            matches.Count
        );
    }

    private static string? CompareScoresReason(MatchSetScore candidate, MatchSetScore current)
    {
        if (candidate.BestTier != current.BestTier)
            return candidate.BestTier > current.BestTier ? $"better best tier ({candidate.BestTier}>{current.BestTier})" : null;
        if (candidate.BestLength != current.BestLength)
            return candidate.BestLength > current.BestLength ? $"same tier, better best length ({candidate.BestLength}>{current.BestLength})" : null;
        if (Math.Abs(candidate.TotalHp - current.TotalHp) > 0.001f)
            return candidate.TotalHp > current.TotalHp ? $"same tier/len, better total hp ({candidate.TotalHp:0}>{current.TotalHp:0})" : null;
        if (candidate.MatchCount != current.MatchCount)
            return candidate.MatchCount > current.MatchCount ? $"same tier/len/hp, more matches ({candidate.MatchCount}>{current.MatchCount})" : null;
        return null;
    }

    private static string FormatScope(PuzzleScope scope) => scope == PuzzleScope.Global ? "global" : "perbag";

    private static List<PuzzleMatch> ResolvePuzzleMatches(List<PuzzleSlot> slots, LayoutTopology topo, int bagIndex)
    {
        var results = new List<PuzzleMatch>();
        for (var tier = 1; tier <= 3; tier++)
        {
            var used = new HashSet<int>();
            foreach (var start in Enumerable.Range(0, slots.Count))
            {
                if (used.Contains(start)) continue;
                if (slots[start].Code != InventoryTotemConstants.PuzzlePieceA[tier - 1].ToShortString()) continue;

                var best = TryMatchLongestChain(slots, topo, start, tier, used, bagIndex);
                if (best is null) continue;
                results.Add(best);
                foreach (var idx in best.LocalIndices) used.Add(idx);
            }
        }

        return results;
    }

    private static PuzzleMatch? TryMatchLongestChain(List<PuzzleSlot> slots, LayoutTopology topo, int start, int tier, HashSet<int> used, int bagIndex)
    {
        var patterns = new[]
        {
            new[] { 'A', 'B', 'C', 'D' },
            new[] { 'A', 'B', 'C' },
            new[] { 'A', 'B' }
        };

        foreach (var pattern in patterns)
        {
            var found = TryMatchPatternOrthogonal(slots, topo, start, tier, pattern, used, bagIndex);
            if (found is not null) return found;
        }

        return null;
    }

    private static PuzzleMatch? TryMatchPatternOrthogonal(
        List<PuzzleSlot> slots,
        LayoutTopology topo,
        int start,
        int tier,
        char[] pattern,
        HashSet<int> used,
        int bagIndex)
    {
        if (!CodeMatchesPatternPiece(slots[start].Code, pattern[0], tier) || used.Contains(start)) return null;

        var path = new List<int> { start };
        var inPath = new HashSet<int> { start };

        bool Extend(int cur)
        {
            if (path.Count == pattern.Length) return true;
            var nextCh = pattern[path.Count];
            foreach (var nb in topo.OrthogonalNeighbors(cur))
            {
                if (used.Contains(nb) || inPath.Contains(nb)) continue;
                if (!CodeMatchesPatternPiece(slots[nb].Code, nextCh, tier)) continue;
                path.Add(nb);
                inPath.Add(nb);
                if (Extend(nb)) return true;
                inPath.Remove(nb);
                path.RemoveAt(path.Count - 1);
            }

            return false;
        }

        if (!Extend(start)) return null;
        if (path.Any(i => slots[i].FlatIndex < 0)) return null;

        var bonus = ResolveMaxHpBonus(tier, pattern.Length);
        var flatIndices = path.Select(i => slots[i].FlatIndex).ToList();
        return new PuzzleMatch(tier, pattern.Length, bonus, $"{topo.Name}-ortho", bagIndex, path, flatIndices);
    }

    private static bool CodeMatchesPatternPiece(string code, char piece, int tier)
    {
        var expected = piece switch
        {
            'A' => InventoryTotemConstants.PuzzlePieceA[tier - 1].ToShortString(),
            'B' => InventoryTotemConstants.PuzzlePieceB[tier - 1].ToShortString(),
            'C' => InventoryTotemConstants.PuzzlePieceC[tier - 1].ToShortString(),
            'D' => InventoryTotemConstants.PuzzlePieceD[tier - 1].ToShortString(),
            _ => ""
        };

        return code == expected;
    }

    private static float ResolveMaxHpBonus(int tier, int length)
    {
        return (tier, length) switch
        {
            (1, 2) => InventoryTotemConstants.PuzzleMaxHpTier1Len2,
            (2, 2) => InventoryTotemConstants.PuzzleMaxHpTier2Len2,
            (3, 2) => InventoryTotemConstants.PuzzleMaxHpTier3Len2,
            (1, 3) => InventoryTotemConstants.PuzzleMaxHpTier1Len3,
            (2, 3) => InventoryTotemConstants.PuzzleMaxHpTier2Len3,
            (3, 3) => InventoryTotemConstants.PuzzleMaxHpTier3Len3,
            (1, 4) => InventoryTotemConstants.PuzzleMaxHpTier1Len4,
            (2, 4) => InventoryTotemConstants.PuzzleMaxHpTier2Len4,
            (3, 4) => InventoryTotemConstants.PuzzleMaxHpTier3Len4,
            _ => 0f
        };
    }

    private readonly record struct SlotEntry(int FlatIndex, int BagIndex, int SlotInBag, string SlotKind, string Code);

    private readonly record struct PuzzleSlot(int FlatIndex, string Code);
    private readonly record struct MatchSetScore(int BestTier, int BestLength, float TotalHp, int MatchCount);

    private sealed record AppliedState(int PassiveTier, float PassiveMoveSpeedPercent, int PuzzleTier, float PuzzleMaxHpBonus, string TopologyName);

    private sealed record Snapshot(
        List<SlotEntry> Slots,
        int PassiveTier,
        List<PuzzleMatch> PuzzleMatches
    );

    private sealed record GlobalPuzzleView(string Name, List<PuzzleSlot> Slots);

    private sealed record PuzzleMatch(int Tier, int ChainLength, float MaxHpBonus, string TopologyName, int BagIndex, List<int> LocalIndices, List<int> FlatIndices)
    {
        public override string ToString()
        {
            var bag = BagIndex >= 0 ? $"B{BagIndex}" : "GLOBAL";
            return $"tier={Tier} len={ChainLength} hp={MaxHpBonus:0} bag={bag} topo={TopologyName} flat=[{string.Join(",", FlatIndices)}]";
        }
    }

    private sealed record LayoutTopology(string Name, int SlotCount, int Rows, int Cols)
    {
        public static LayoutTopology FromRows(string name, int slotCount, int rows)
        {
            var safeRows = Math.Max(1, rows);
            var cols = (int)Math.Ceiling(slotCount / (double)safeRows);
            return new LayoutTopology(name, slotCount, safeRows, Math.Max(1, cols));
        }

        public IEnumerable<int> OrthogonalNeighbors(int index)
        {
            if (index < 0 || index >= SlotCount) yield break;
            var row = index / Cols;
            var col = index % Cols;

            if (col > 0)
            {
                var L = index - 1;
                if (L >= 0) yield return L;
            }

            if (col < Cols - 1)
            {
                var R = index + 1;
                if (R < SlotCount && R / Cols == row) yield return R;
            }

            if (row > 0)
            {
                var U = index - Cols;
                if (U >= 0) yield return U;
            }

            if (row < Rows - 1)
            {
                var D = index + Cols;
                if (D < SlotCount && D % Cols == col) yield return D;
            }
        }
    }

    private static TextCommandResult SendChunkedDiagnostics(IServerPlayer player, string text, string commandName)
    {
        const int maxChunk = 650;
        var lines = text.Replace("\r", string.Empty).Split('\n', StringSplitOptions.None);
        var chunk = new StringBuilder();
        var sent = 0;

        foreach (var line in lines)
        {
            var toAdd = line.Length == 0 ? " " : line;
            if (chunk.Length > 0 && chunk.Length + toAdd.Length + 1 > maxChunk)
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, chunk.ToString(), EnumChatType.Notification);
                chunk.Clear();
                sent++;
            }

            if (toAdd.Length > maxChunk)
            {
                var start = 0;
                while (start < toAdd.Length)
                {
                    var take = Math.Min(maxChunk, toAdd.Length - start);
                    var part = toAdd.Substring(start, take);
                    if (chunk.Length > 0)
                    {
                        player.SendMessage(GlobalConstants.GeneralChatGroup, chunk.ToString(), EnumChatType.Notification);
                        chunk.Clear();
                        sent++;
                    }
                    player.SendMessage(GlobalConstants.GeneralChatGroup, part, EnumChatType.Notification);
                    sent++;
                    start += take;
                }
                continue;
            }

            if (chunk.Length > 0) chunk.Append('\n');
            chunk.Append(toAdd);
        }

        if (chunk.Length > 0)
        {
            player.SendMessage(GlobalConstants.GeneralChatGroup, chunk.ToString(), EnumChatType.Notification);
            sent++;
        }

        return TextCommandResult.Success($"[IT] /it {commandName} output sent in {sent} chat block(s).");
    }
}
