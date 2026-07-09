using BetaSharp.Blocks;
using BetaSharp.Blocks.Entities;
using BetaSharp.Entities;
using BetaSharp.Items;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;
using Brigadier.NET.Builder;
using Brigadier.NET.Context;

namespace BetaSharp.Server.Commands;

public class BlockCommand : Command.Command
{

    public override string Usage => "block get [position]";
    public override string Description => "Get block info at position (default: block under player)";
    public override string[] Names => ["block"];

    public override LiteralArgumentBuilder<CommandSource> Register(LiteralArgumentBuilder<CommandSource> argBuilder) =>
        argBuilder
            .Then(Literal("get")
                .Executes(BlockGet)
                .Then(ArgumentPos("position").Executes(c => BlockGet(c, c.GetArgument<Vec3D>("position"))))
            ).Then(Literal("set").Then(ArgumentBlock("block")
                .Executes(c => BlockSet(c, c.GetArgument<(int id, int meta)>("block")))
                .Then(ArgumentPos("position").Executes(c => BlockSet(c, c.GetArgument<(int id, int meta)>("block"), c.GetArgument<Vec3D>("position"))))));

    private static int BlockGet(CommandContext<CommandSource> context)
    {
        ServerPlayerEntity? player = GetSenderPlayer(context);
        if (player == null)
        {
            return 1;
        }

        return BlockGet(context, player.World, new Vec3i((int)Math.Floor(player.Position.x), (int)Math.Floor(player.Position.y) - 1, (int)Math.Floor(player.Position.z)));
    }

    private static int BlockGet(CommandContext<CommandSource> context, Vec3D p)
    {
        ServerPlayerEntity? player = GetSenderPlayer(context);
        if (player == null)
        {
            return 1;
        }

        return BlockGet(context, player.World, new Vec3i((int)Math.Floor(p.x), (int)Math.Floor(p.y), (int)Math.Floor(p.z)));
    }

    private static int BlockGet(CommandContext<CommandSource> context, IWorldContext world, Vec3i p)
    {
        int id = world.Reader.GetBlockId(p.X, p.Y, p.Z);
        int meta = world.Reader.GetBlockMeta(p.X, p.Y, p.Z);
        BlockEntity? blockEntity = world.Entities.GetBlockEntity<BlockEntity>(p.X, p.Y, p.Z);

        if (Block.Blocks.Length <= id || Block.Blocks[id] == null)
        {
            context.Source.Output.SendMessage($"Block at {p.X} {p.Y} {p.Z} -> {id}:{meta}");
        }
        else
        {
            context.Source.Output.SendMessage($"Block at {p.X} {p.Y} {p.Z} -> {id}:{meta} ({Block.Blocks[id].translateBlockName()})");
        }

        if (blockEntity != null)
        {
            context.Source.Output.SendMessage($"Block entity: {blockEntity.GetType().Name}");
        }

        return 1;
    }

    private static int BlockSet(CommandContext<CommandSource> context, (int id, int meta) block)
    {
        ServerPlayerEntity? player = GetSenderPlayer(context);
        if (player == null)
        {
            return 1;
        }

        return BlockSet(context, player.World, block, new Vec3i((int)Math.Floor(player.Position.x), (int)Math.Floor(player.Position.y) - 1, (int)Math.Floor(player.Position.z)));
    }

    private static int BlockSet(CommandContext<CommandSource> context, (int id, int meta) block, Vec3D p)
    {
        ServerPlayerEntity? player = GetSenderPlayer(context);
        if (player == null)
        {
            return 1;
        }

        return BlockSet(context, player.World, block, new Vec3i((int)Math.Floor(p.x), (int)Math.Floor(p.y), (int)Math.Floor(p.z)));
    }

    private static int BlockSet(CommandContext<CommandSource> context, IWorldContext world, (int id, int meta) block, Vec3i p)
    {
        world.Writer.SetBlock(p.X, p.Y, p.Z, block.id, block.meta);
        return 1;
    }
}
