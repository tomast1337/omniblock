using Brigadier.NET.Builder;
using Brigadier.NET.Context;
using OmniBlock.Server.Command;
using OmniBlock.Server.Worlds;

namespace OmniBlock.Server.Commands;

public sealed class WorldGenerationCommand : Command.Command
{
    public override string Usage =>
        "worldgen <start <id> here <radius>|start <id> area <dimension> <centerChunkX> <centerChunkZ> <radius>|list|inspect <id>|pause <id>|resume <id>|cancel <id>>";
    public override string Description => "Manage persistent fixed-area world pregeneration";
    public override string[] Names => ["worldgen", "pregen"];

    public override LiteralArgumentBuilder<CommandSource> Register(LiteralArgumentBuilder<CommandSource> root) =>
        root
            .Then(Literal("start")
                .Then(ArgumentString("id")
                    .Then(Literal("here")
                        .Then(ArgumentInt("radius").Executes(StartHere)))
                    .Then(Literal("area")
                        .Then(ArgumentInt("dimension")
                            .Then(ArgumentInt("centerChunkX")
                                .Then(ArgumentInt("centerChunkZ")
                                    .Then(ArgumentInt("radius").Executes(StartArea))))))))
            .Then(Literal("list").Executes(List))
            .Then(Literal("inspect").Then(ArgumentString("id").Executes(Inspect)))
            .Then(Literal("pause").Then(ArgumentString("id").Executes(Pause)))
            .Then(Literal("resume").Then(ArgumentString("id").Executes(Resume)))
            .Then(Literal("cancel").Then(ArgumentString("id").Executes(Cancel)));

    private static int StartHere(CommandContext<CommandSource> context)
    {
        var player = GetSenderPlayer(context);
        if (player is null) return 0;
        return Start(context, player.DimensionId, (int)Math.Floor(player.X) >> 4,
            (int)Math.Floor(player.Z) >> 4);
    }

    private static int StartArea(CommandContext<CommandSource> context) => Start(
        context,
        context.GetArgument<int>("dimension"),
        context.GetArgument<int>("centerChunkX"),
        context.GetArgument<int>("centerChunkZ"));

    private static int Start(
        CommandContext<CommandSource> context,
        int dimension,
        int centerChunkX,
        int centerChunkZ)
    {
        if (dimension is not (0 or -1))
        {
            context.Source.Output.SendMessage($"Dimension {dimension} does not exist.");
            return 0;
        }

        var world = context.Source.Server.getWorld(dimension);
        var id = context.GetArgument<string>("id");
        foreach (var knownDimension in new[] { 0, -1 })
        {
            if (Service(context, knownDimension).InspectAll()
                .Any(candidate => candidate.Definition.Id == id))
                throw new InvalidOperationException(
                    $"Pregeneration job '{id}' already exists in dimension {knownDimension}.");
        }
        var snapshot = Service(context, dimension).Start(
            id, world, centerChunkX, centerChunkZ,
            context.GetArgument<int>("radius"));
        context.Source.Output.SendMessage(
            $"Started pregeneration '{snapshot.Definition.Id}': {snapshot.Definition.TotalTargets} " +
            $"chunks in radius {snapshot.Definition.RadiusChunks} around " +
            $"{snapshot.Definition.CenterChunkX},{snapshot.Definition.CenterChunkZ} " +
            $"in dimension {dimension}.");
        return 1;
    }

    private static int List(CommandContext<CommandSource> context)
    {
        var any = false;
        foreach (var dimension in new[] { 0, -1 })
        foreach (var snapshot in Service(context, dimension).InspectAll())
        {
            any = true;
            context.Source.Output.SendMessage(Format(snapshot));
        }
        if (!any) context.Source.Output.SendMessage("No pregeneration jobs.");
        return 1;
    }

    private static int Inspect(CommandContext<CommandSource> context)
    {
        var snapshot = Find(context, context.GetArgument<string>("id"));
        context.Source.Output.SendMessage(Format(snapshot));
        if (snapshot.LastError is not null)
            context.Source.Output.SendMessage($"Last error: {snapshot.LastError}");
        return 1;
    }

    private static int Pause(CommandContext<CommandSource> context) =>
        Change(context, static (service, id) => service.Pause(id));

    private static int Resume(CommandContext<CommandSource> context) =>
        Change(context, static (service, id) => service.Resume(id));

    private static int Cancel(CommandContext<CommandSource> context) =>
        Change(context, static (service, id) => service.Cancel(id));

    private static int Change(
        CommandContext<CommandSource> context,
        Func<FixedAreaPregenerationService, string, FixedAreaPregenerationSnapshot> change)
    {
        var id = context.GetArgument<string>("id");
        foreach (var dimension in new[] { 0, -1 })
        {
            var service = Service(context, dimension);
            if (service.InspectAll().All(snapshot => snapshot.Definition.Id != id)) continue;
            context.Source.Output.SendMessage(Format(change(service, id)));
            return 1;
        }
        throw new KeyNotFoundException($"Unknown pregeneration job '{id}'.");
    }

    private static FixedAreaPregenerationSnapshot Find(
        CommandContext<CommandSource> context, string id)
    {
        foreach (var dimension in new[] { 0, -1 })
        {
            var snapshot = Service(context, dimension).InspectAll()
                .FirstOrDefault(candidate => candidate.Definition.Id == id);
            if (snapshot is not null) return snapshot;
        }
        throw new KeyNotFoundException($"Unknown pregeneration job '{id}'.");
    }

    private static FixedAreaPregenerationService Service(
        CommandContext<CommandSource> context, int dimension) =>
        context.Source.Server.playerManager.GetChunkMapForDimension(dimension).Pregeneration;

    private static string Format(FixedAreaPregenerationSnapshot snapshot)
    {
        var percent = snapshot.Definition.TotalTargets == 0
            ? 100
            : snapshot.NextTarget * 100.0 / snapshot.Definition.TotalTargets;
        return $"{snapshot.Definition.Id} [{snapshot.Status}] {snapshot.NextTarget}/" +
               $"{snapshot.Definition.TotalTargets} ({percent:F1}%): prepared " +
               $"{snapshot.PreparedTargets}, decorated {snapshot.DecoratedTargets}, saved " +
               $"{snapshot.SavedTargets}, skipped {snapshot.SkippedTargets}, chunks written " +
               $"{snapshot.WrittenChunks}, retained {snapshot.RetainedBytes} B " +
               $"(peak {snapshot.PeakRetainedBytes} B), disk +{snapshot.DiskBytes} B, " +
               $"{snapshot.TargetsPerSecond:F2} targets/s; {snapshot.ThrottleReason}";
    }
}
