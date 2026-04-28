using Vintagestory.API.Common;

namespace InventoryTotems;

/// <summary>Mod-wide constants. Backpack/totem effect logic: see <see cref="InventoryTotemsServerSystem"/> (primary fix target if effects break).</summary>
internal static class InventoryTotemConstants
{
    public const string ModId = "inventorytotems";
    public const string ModVersion = "0.2.8";

    public const string ChatGroup = "it";
    public const string WalkSpeedLayer = "inventorytotems-passive";
    public const string SprintSpeedLayer = "inventorytotems-passive";
    public const string MaxHpLayer = "inventorytotems-puzzle";

    public const string PassiveStatCode = "movespeed";
    public const string PuzzleStatCode = "maxhp";

    public const float PassiveMoveSpeedTier1 = 0.05f;
    public const float PassiveMoveSpeedTier2 = 0.10f;
    public const float PassiveMoveSpeedTier3 = 0.15f;

    public const float PuzzleMaxHpTier1Len2 = 5f;
    public const float PuzzleMaxHpTier2Len2 = 10f;
    public const float PuzzleMaxHpTier3Len2 = 15f;
    public const float PuzzleMaxHpTier1Len3 = 8f;
    public const float PuzzleMaxHpTier2Len3 = 16f;
    public const float PuzzleMaxHpTier3Len3 = 24f;
    public const float PuzzleMaxHpTier1Len4 = 12f;
    public const float PuzzleMaxHpTier2Len4 = 24f;
    public const float PuzzleMaxHpTier3Len4 = 36f;

    public static readonly AssetLocation[] PassiveMoveSpeedTotems =
    [
        new AssetLocation(ModId, "totem-movespeed-t1"),
        new AssetLocation(ModId, "totem-movespeed-t2"),
        new AssetLocation(ModId, "totem-movespeed-t3")
    ];

    public static readonly AssetLocation[] PuzzlePieceA =
    [
        new AssetLocation(ModId, "totem-puzzle-a-t1"),
        new AssetLocation(ModId, "totem-puzzle-a-t2"),
        new AssetLocation(ModId, "totem-puzzle-a-t3")
    ];

    public static readonly AssetLocation[] PuzzlePieceB =
    [
        new AssetLocation(ModId, "totem-puzzle-b-t1"),
        new AssetLocation(ModId, "totem-puzzle-b-t2"),
        new AssetLocation(ModId, "totem-puzzle-b-t3")
    ];

    public static readonly AssetLocation[] PuzzlePieceC =
    [
        new AssetLocation(ModId, "totem-puzzle-c-t1"),
        new AssetLocation(ModId, "totem-puzzle-c-t2"),
        new AssetLocation(ModId, "totem-puzzle-c-t3")
    ];

    public static readonly AssetLocation[] PuzzlePieceD =
    [
        new AssetLocation(ModId, "totem-puzzle-d-t1"),
        new AssetLocation(ModId, "totem-puzzle-d-t2"),
        new AssetLocation(ModId, "totem-puzzle-d-t3")
    ];
}
