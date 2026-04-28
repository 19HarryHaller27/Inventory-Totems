using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace InventoryTotems;

public static class TotemLookHelper
{
    public static bool EnsureLook(IWorldAccessor? world, ItemStack stack)
    {
        if (stack?.Collectible is not ItemInventoryTotem) return false;
        if (stack.Attributes is null) return false;
        if (stack.Attributes.HasAttribute(TotemLookVisuals.AttrName)) return false;
        if (world is null) return false;
        if (!TryPickRandomValidLookIndex(world, out var pick)) return false;
        stack.Attributes.SetInt(TotemLookVisuals.AttrName, pick);
        return true;
    }

    public static bool TryGetDisplayItem(IWorldAccessor world, int attrInt, out Item? item, out int resolvedIndex)
    {
        item = null;
        var n = TotemLookVisuals.Count;
        var first = (attrInt % n + n) % n;
        for (var o = 0; o < n; o++)
        {
            var j = (first + o) % n;
            var g = world.GetItem(new AssetLocation("game", TotemLookVisuals.GameItemCodes[j]));
            if (g is not null)
            {
                item = g;
                resolvedIndex = j;
                return true;
            }
        }

        resolvedIndex = 0;
        return false;
    }

    private static bool TryPickRandomValidLookIndex(IWorldAccessor world, out int index)
    {
        var n = TotemLookVisuals.Count;
        for (var attempt = 0; attempt < 24; attempt++)
        {
            var p = world.Rand.Next(0, n);
            if (world.GetItem(new AssetLocation("game", TotemLookVisuals.GameItemCodes[p])) is not null)
            {
                index = p;
                return true;
            }
        }

        for (var j = 0; j < n; j++)
        {
            if (world.GetItem(new AssetLocation("game", TotemLookVisuals.GameItemCodes[j])) is not null)
            {
                index = j;
                return true;
            }
        }

        index = 0;
        return false;
    }
}
