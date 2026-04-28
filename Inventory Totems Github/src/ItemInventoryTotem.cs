using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace InventoryTotems;

public class ItemInventoryTotem : Item
{
    private static readonly Dictionary<int, MultiTextureMeshRef> LookMeshCache = [];
    private static ICoreClientAPI? lookCacheApi;

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection? blockSel, EntitySelection? entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        handling = EnumHandHandling.PreventDefault;
    }

    public override void OnCreatedByCrafting(ItemSlot[] allInputSlots, ItemSlot outputSlot, IRecipeBase byRecipe)
    {
        base.OnCreatedByCrafting(allInputSlots, outputSlot, byRecipe);
        IWorldAccessor? w = allInputSlots
            .Select(s => s?.Inventory)
            .OfType<InventoryBase>()
            .Select(b => b.Api)
            .OfType<ICoreAPI>()
            .Select(api => api.World)
            .FirstOrDefault();
        w ??= (outputSlot?.Inventory as InventoryBase)?.Api?.World;
        if (outputSlot?.Itemstack is { } st && TotemLookHelper.EnsureLook(w, st)) outputSlot.MarkDirty();
    }

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
    {
        base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
        if (capi.World is null) return;
        if (itemstack.Attributes is null) return;
        if (!itemstack.Attributes.HasAttribute(TotemLookVisuals.AttrName)) return;

        var attr = itemstack.Attributes.GetInt(TotemLookVisuals.AttrName);
        if (!TotemLookHelper.TryGetDisplayItem(capi.World, attr, out var visual, out var resIdx) || visual is null) return;

        if (lookCacheApi != capi)
        {
            lookCacheApi = capi;
            LookMeshCache.Clear();
        }

        if (!LookMeshCache.TryGetValue(resIdx, out var modelRef))
        {
            capi.Tesselator.TesselateItem(visual, out MeshData mesh);
            if (mesh is null || mesh.VerticesCount < 1) return;
            modelRef = capi.Render.UploadMultiTextureMesh(mesh);
            if (modelRef is null) return;
            LookMeshCache[resIdx] = modelRef;
        }

        renderinfo.ModelRef = modelRef;
        renderinfo.Transform = SelectTransform(visual, this, target) ?? renderinfo.Transform;
    }

    private static ModelTransform? SelectTransform(Item source, Item totem, EnumItemRenderTarget t)
    {
        // API still uses HandFp for first-person; enum member is marked obsolete in 1.22+.
#pragma warning disable CS0618
        return t switch
        {
            EnumItemRenderTarget.Gui => source.GuiTransform ?? totem.GuiTransform,
            EnumItemRenderTarget.HandFp => source.FpHandTransform ?? totem.FpHandTransform,
            EnumItemRenderTarget.HandTp => source.TpHandTransform ?? totem.TpHandTransform,
            EnumItemRenderTarget.HandTpOff => source.TpOffHandTransform ?? totem.TpOffHandTransform,
            EnumItemRenderTarget.Ground => source.GroundTransform ?? totem.GroundTransform,
            _ => source.GuiTransform ?? totem.GuiTransform
        };
#pragma warning restore CS0618
    }
}
