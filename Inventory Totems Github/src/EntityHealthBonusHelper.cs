using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace InventoryTotems;

/// <summary>
/// Flat max HP from puzzle totems via <c>EntityBehaviorHealth.SetMaxHealthModifiers</c>
/// so health and Character stats update; survival assembly is not referenced at compile time.
/// </summary>
internal static class EntityHealthBonusHelper
{
    private static MethodInfo? setMaxHealthModifiersMethod;
    private static Type? cachedBehaviorType;

    public static void SetFlatMaxHpBonus(EntityAgent entity, string key, float bonusHp)
    {
        if (entity is null || bonusHp <= 0f)
        {
            return;
        }

        var beh = entity.GetBehavior("health");
        if (beh is null)
        {
            return;
        }

        ResolveMethodIfNeeded(beh.GetType())?.Invoke(beh, [key, bonusHp]);
    }

    public static void ClearFlatMaxHpBonus(EntityAgent? entity, string key)
    {
        var beh = entity?.GetBehavior("health");
        if (beh is null)
        {
            return;
        }

        ResolveMethodIfNeeded(beh.GetType())?.Invoke(beh, [key, 0f]);
    }

    private static MethodInfo? ResolveMethodIfNeeded(Type behaviorType)
    {
        if (cachedBehaviorType == behaviorType && setMaxHealthModifiersMethod is not null)
        {
            return setMaxHealthModifiersMethod;
        }

        cachedBehaviorType = behaviorType;
        setMaxHealthModifiersMethod = behaviorType.GetMethod(
            "SetMaxHealthModifiers",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            [typeof(string), typeof(float)],
            null);

        return setMaxHealthModifiersMethod;
    }
}
