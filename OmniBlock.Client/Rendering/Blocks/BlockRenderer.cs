using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks.Renderers;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Blocks;

public class BlockRenderer
{
    private static readonly ReedRenderer s_reed = new();
    private static readonly TorchRenderer s_torch = new();
    private static readonly FireRenderer s_fire = new();
    private static readonly FluidsRenderer s_fluids = new();
    private static readonly RedstoneWireRenderer s_wire = new();
    private static readonly CropsRenderer s_crops = new();
    private static readonly DoorRenderer s_door = new();
    private static readonly LadderRenderer s_ladder = new();
    private static readonly MinecartTrackRenderer s_track = new();
    private static readonly StairsRenderer s_stairs = new();
    private static readonly FenceRenderer s_fence = new();
    private static readonly LeverRenderer s_lever = new();
    private static readonly CactusRenderer s_cactus = new();
    private static readonly BedRenderer s_bed = new();
    private static readonly RepeaterRenderer s_repeater = new();
    private static readonly PistonBaseRenderer s_pistonBase = new();
    private static readonly PistonExtensionRenderer s_pistonExt = new();


    public static bool RenderBlockByRenderType(IBlockReader world, IBlockRuntimeView blocks, ILightProvider lighting, Block block, BlockPos pos, Tessellator tess, int overrideTexture = -1, bool renderAllFaces = false, bool doVariance = false)
    {
        var type = block.RenderType;

        block.UpdateBoundingBox(world, pos.X, pos.Y, pos.Z);

        var topRule = doVariance ? block.TopVariance : TextureVariance.None;
        var botRule = doVariance ? block.BottomVariance : TextureVariance.None;
        var sideRule = doVariance ? block.SideVariance : TextureVariance.None;

        var topHash = topRule != TextureVariance.None ? BlockRenderContext.GetTextureVarianceHash(pos.X, pos.Y, pos.Z) : 0;
        var botHash = botRule != TextureVariance.None ? BlockRenderContext.GetTextureVarianceHash(pos.X, pos.Y - 1, pos.Z) : 0;
        var sideHash = sideRule != TextureVariance.None ? BlockRenderContext.GetTextureVarianceHash(pos.X, pos.Y, pos.Z) : 0;

        var topRot = BlockRenderContext.ApplyVariance(topHash, topRule, out var flipTop);
        var botRot = BlockRenderContext.ApplyVariance(botHash, botRule, out var flipBot);
        var sideRot = BlockRenderContext.ApplyVariance(sideHash, sideRule, out var flipSide);

        var ctx = new BlockRenderContext(
            tess: tess,
            lighting: lighting,
            blockReader: world,
            blocks: blocks,
            overrideTexture: overrideTexture,
            renderAllFaces: renderAllFaces,
            flipTexture: false,
            uvTop: topRot,
            uvBottom: botRot,
            uvNorth: sideRot,
            uvSouth: sideRot,
            uvEast: sideRot,
            uvWest: sideRot,
            flipTop: flipTop,
            flipBottom: flipBot,
            flipNorth: flipSide,
            flipSouth: flipSide,
            flipEast: flipSide,
            flipWest: flipSide,
            aoBlendMode: 1,
            customFlag: type == BlockRendererType.PistonExtension
        );

        if (type == BlockRendererType.Standard)
        {
            return ctx.DrawBlock(block, pos);
        }

        return type switch
        {
            BlockRendererType.Reed => s_reed.Draw(block, pos, ref ctx),
            BlockRendererType.Torch => s_torch.Draw(block, pos, ref ctx),
            BlockRendererType.Fire => s_fire.Draw(block, pos, ref ctx),
            BlockRendererType.Fluids => s_fluids.Draw(block, pos, ref ctx),
            BlockRendererType.RedstoneWire => s_wire.Draw(block, pos, ref ctx),
            BlockRendererType.Crops => s_crops.Draw(block, pos, ref ctx),
            BlockRendererType.Door => s_door.Draw(block, pos, ref ctx),
            BlockRendererType.Ladder => s_ladder.Draw(block, pos, ref ctx),
            BlockRendererType.MinecartTrack => s_track.Draw(block, pos, ref ctx),
            BlockRendererType.Stairs => s_stairs.Draw(block, pos, ref ctx),
            BlockRendererType.Fence => s_fence.Draw(block, pos, ref ctx),
            BlockRendererType.Lever => s_lever.Draw(block, pos, ref ctx),
            BlockRendererType.Cactus => s_cactus.Draw(block, pos, ref ctx),
            BlockRendererType.Bed => s_bed.Draw(block, pos, ref ctx),
            BlockRendererType.Repeater => s_repeater.Draw(block, pos, ref ctx),
            BlockRendererType.PistonBase => s_pistonBase.Draw(block, pos, ref ctx),
            BlockRendererType.PistonExtension => s_pistonExt.Draw(block, pos, ref ctx),
            _ => false
        };
    }

    public static void RenderBlockOnInventory(IBlockRuntimeView blocks, Block block, int metadata, float brightness, Tessellator tess)
    {
        var renderType = block.RenderType;
        var uiCtx = new BlockRenderContext(
            NullBlockReader.Instance,
            blocks,
            tess,
            null,
            renderAllFaces: true,
            enableAo: false,
            overrideTexture: -1
        );

        var origin = new Vec3D(0, 0, 0);
        var dummyColors = new FaceColors();

        if (renderType == BlockRendererType.Standard || renderType == BlockRendererType.PistonBase)
        {
            var isPiston = renderType == BlockRendererType.PistonBase;

            void SetFaceColor(int face)
            {
                var c = block.GetColorForFace(metadata, face);
                GLManager.Color = new Vector4D<float>(((c >> 16) & 255) / 255.0F * brightness, ((c >> 8) & 255) / 255.0F * brightness, (c & 255) / 255.0F * brightness, 1.0F);
            }

            block.SetupRenderBoundingBox();
            GLManager.ModelView.Translate(-0.5F, -0.5F, -0.5F);

            tess.startDrawingQuads();
            tess.setNormal(0.0F, -1.0F, 0.0F);
            SetFaceColor(0);
            uiCtx.DrawBottomFace(block, origin, dummyColors, isPiston ? block.GetTexture(Side.Down) : block.GetTexture(Side.Down, metadata));
            tess.draw(ProgramSlot.Gui);

            tess.startDrawingQuads();
            tess.setNormal(0.0F, 1.0F, 0.0F);
            SetFaceColor(1);
            uiCtx.DrawTopFace(block, origin, dummyColors,
                isPiston ? block.GetTexture(Side.Up) : block.GetTexture(Side.Up, metadata));
            tess.draw(ProgramSlot.Gui);

            tess.startDrawingQuads();
            tess.setNormal(0.0F, 0.0F, -1.0F);
            SetFaceColor(2);
            uiCtx.DrawEastFace(block, origin, dummyColors,
                isPiston ? block.GetTexture(Side.North) : block.GetTexture(Side.North, metadata));
            tess.draw(ProgramSlot.Gui);

            tess.startDrawingQuads();
            tess.setNormal(0.0F, 0.0F, 1.0F);
            SetFaceColor(3);
            uiCtx.DrawWestFace(block, origin, dummyColors,
                isPiston ? block.GetTexture(Side.South) : block.GetTexture(Side.South, metadata));
            tess.draw(ProgramSlot.Gui);

            tess.startDrawingQuads();
            tess.setNormal(-1.0F, 0.0F, 0.0F);
            SetFaceColor(4);
            uiCtx.DrawNorthFace(block, origin, dummyColors,
                isPiston ? block.GetTexture(Side.West) : block.GetTexture(Side.West, metadata));
            tess.draw(ProgramSlot.Gui);

            tess.startDrawingQuads();
            tess.setNormal(1.0F, 0.0F, 0.0F);
            SetFaceColor(5);
            uiCtx.DrawSouthFace(block, origin, dummyColors,
                isPiston ? block.GetTexture(Side.East) : block.GetTexture(Side.East, metadata));
            tess.draw(ProgramSlot.Gui);

            GLManager.ModelView.Translate(0.5F, 0.5F, 0.5F);
        }
        else
        {
            var color = block.GetColor(metadata);
            GLManager.Color = new Vector4D<float>(((color >> 16) & 255) / 255.0F * brightness, ((color >> 8) & 255) / 255.0F * brightness, (color & 255) / 255.0F * brightness, 1.0F);
            GLManager.ModelView.Translate(-0.5F, -0.5F, -0.5F);
            var itemWorld = new ItemRenderBlockAccess(block.Id, metadata, brightness);
            BlockPos itemPos = new(0, 0, 0);
            tess.startDrawingQuads();
            tess.setNormal(0.0F, 1.0F, 0.0F);
            RenderBlockByRenderType(itemWorld, blocks, itemWorld, block, itemPos, tess, uiCtx.OverrideTexture, true);
            tess.draw(ProgramSlot.Gui);
            GLManager.ModelView.Translate(0.5F, 0.5F, 0.5F);
        }
    }

    public static void RenderBlockFallingSand(Block block, IWorldContext world, int x, int y, int z, Tessellator tess)
    {
        // Directional shading multipliers for fake 3D depth
        var lightBottom = 0.5F;
        var lightTop = 1.0F;
        var lightZ = 0.8F; // East/West faces
        var lightX = 0.6F; // North/South faces

        var entityCtx = new BlockRenderContext(
            world.Reader,
            world.Content.Blocks,
            lighting: world.Lighting,
            tess: tess,
            renderAllFaces: true,
            enableAo: false
        );

        tess.startDrawingQuads();

        // Base luminance at the entity's current position
        var currentLuminance = block.GetLuminance(world.Lighting, x, y, z);
        var localOrigin = new Vec3D(-0.5, -0.5, -0.5);
        var dummyColors = new FaceColors();

        // Bottom Face
        var faceLum = Math.Max(currentLuminance, block.GetLuminance(world.Lighting, x, y - 1, z));
        tess.setColorOpaque_F(lightBottom * faceLum, lightBottom * faceLum, lightBottom * faceLum);
        entityCtx.DrawBottomFace(block, localOrigin, dummyColors, block.GetTexture(Side.Down));

        // Top Face
        faceLum = Math.Max(currentLuminance, block.GetLuminance(world.Lighting, x, y + 1, z));
        tess.setColorOpaque_F(lightTop * faceLum, lightTop * faceLum, lightTop * faceLum);
        entityCtx.DrawTopFace(block, localOrigin, dummyColors, block.GetTexture(Side.Up));

        // East/West Faces
        faceLum = Math.Max(currentLuminance, block.GetLuminance(world.Lighting, x, y, z - 1));
        tess.setColorOpaque_F(lightZ * faceLum, lightZ * faceLum, lightZ * faceLum);
        entityCtx.DrawEastFace(block, localOrigin, dummyColors, block.GetTexture(Side.North));

        faceLum = Math.Max(currentLuminance, block.GetLuminance(world.Lighting, x, y, z + 1));
        tess.setColorOpaque_F(lightZ * faceLum, lightZ * faceLum, lightZ * faceLum);
        entityCtx.DrawWestFace(block, localOrigin, dummyColors, block.GetTexture(Side.South));

        // North/South Faces
        faceLum = Math.Max(currentLuminance, block.GetLuminance(world.Lighting, x - 1, y, z));
        tess.setColorOpaque_F(lightX * faceLum, lightX * faceLum, lightX * faceLum);
        entityCtx.DrawNorthFace(block, localOrigin, dummyColors, block.GetTexture(Side.West));

        faceLum = Math.Max(currentLuminance, block.GetLuminance(world.Lighting, x + 1, y, z));
        tess.setColorOpaque_F(lightX * faceLum, lightX * faceLum, lightX * faceLum);
        entityCtx.DrawSouthFace(block, localOrigin, dummyColors, block.GetTexture(Side.East));

        tess.draw(ProgramSlot.Entities);
    }

    public static bool IsSideLit(BlockRendererType renderType)
    {
        return renderType == BlockRendererType.Standard ||
               renderType == BlockRendererType.Stairs ||
               renderType == BlockRendererType.Fence ||
               renderType == BlockRendererType.Cactus ||
               renderType == BlockRendererType.PistonBase;
    }
}
