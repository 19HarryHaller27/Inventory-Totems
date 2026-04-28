namespace InventoryTotems;

/// <summary>
/// Visual pool: vanilla survival item codes (game domain) whose item models are antlers, horns, and bones.
/// Each totem stack gets a random index into this list (stored as stack attribute) and renders with that item's mesh.
/// </summary>
internal static class TotemLookVisuals
{
    /// <summary>Stack attribute; int index into <see cref="GameItemCodes"/>.</summary>
    public const string AttrName = "it-look";

    /// <summary>Valid <c>game:</c> item codes. Keep in sync with 1.22+ survival; missing codes are skipped at runtime.</summary>
    public static readonly string[] GameItemCodes =
    [
        // Antlers (shape variety from deer / goat / sheep; same pool as in-game antler drops)
        "antler-whitetail-01", "antler-whitetail-02", "antler-whitetail-03", "antler-whitetail-04", "antler-whitetail-05", "antler-whitetail-06",
        "antler-redbrocket-01", "antler-redbrocket-02", "antler-redbrocket-03", "antler-redbrocket-04",
        "antler-marsh-01", "antler-marsh-02", "antler-marsh-03", "antler-marsh-04", "antler-marsh-05", "antler-marsh-06", "antler-marsh-07",
        "antler-water-01",
        "antler-caribou-01", "antler-caribou-02", "antler-caribou-03", "antler-caribou-04", "antler-caribou-05", "antler-caribou-06", "antler-caribou-07", "antler-caribou-08",
        "antler-pudu-01", "antler-pudu-02", "antler-pudu-03", "antler-pudu-04",
        "antler-elk-01", "antler-elk-02", "antler-elk-03", "antler-elk-04", "antler-elk-05", "antler-elk-06", "antler-elk-07", "antler-elk-08",
        "antler-taruca-01", "antler-taruca-02", "antler-taruca-03", "antler-taruca-04", "antler-taruca-05",
        "antler-moose-01", "antler-moose-02", "antler-moose-03", "antler-moose-04", "antler-moose-05", "antler-moose-06", "antler-moose-07", "antler-moose-08", "antler-moose-09",
        "antler-fallow-01", "antler-fallow-02", "antler-fallow-03", "antler-fallow-04", "antler-fallow-05", "antler-fallow-06", "antler-fallow-07", "antler-fallow-08",
        "antler-ibexalp-01", "antler-ibexalp-02", "antler-ibexalp-03", "antler-ibexalp-04", "antler-ibexalp-05", "antler-ibexalp-06", "antler-ibexalp-07",
        "antler-markhor-01", "antler-markhor-02", "antler-markhor-03", "antler-markhor-04",
        "antler-bighorn-01", "antler-bighorn-02", "antler-bighorn-03", "antler-bighorn-04", "antler-bighorn-05",
        "antler-mouflon-01", "antler-mouflon-02", "antler-mouflon-03", "antler-mouflon-04", "antler-mouflon-05", "antler-mouflon-06",
        "antler-muskox-01", "antler-muskox-02", "antler-muskox-03", "antler-muskox-04", "antler-muskox-05",
        "antler-chital-01", "antler-chital-02", "antler-chital-03", "antler-chital-04", "antler-chital-05", "antler-chital-06", "antler-chital-07", "antler-chital-08",
        "antler-guemal-01", "antler-guemal-02", "antler-guemal-03", "antler-guemal-04", "antler-guemal-05",
        "antler-pampas-01", "antler-pampas-02", "antler-pampas-03", "antler-pampas-04", "antler-pampas-05", "antler-pampas-06", "antler-pampas-07",
        // Plain bones and bone fragments (variant items from game:bone)
        "bone", "bone-tiny", "bone-fish"
    ];

    public static int Count => GameItemCodes.Length;
}
