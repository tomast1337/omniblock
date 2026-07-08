using System.Text.Json;
using BetaSharp.Blocks.Materials;

namespace BetaSharp.Blocks.Behaviors;

internal static class BehaviorRegistry
{
    public delegate object BehaviorFactory(JsonElement json);

    private static readonly Dictionary<string, BehaviorFactory> s_factories = new()
    {
        // Parameterized behaviors extract their state from JSON
        ["door"] = json => new DoorBehavior(MaterialRegistry.Get(json.GetProperty("material").GetString() ?? "wood")),
        ["trap_door"] = json => new TrapDoorBehavior(MaterialRegistry.Get(json.GetProperty("material").GetString() ?? "wood")),
        ["furnace"] = json => new FurnaceBehavior(json.TryGetProperty("lit", out var lit) && lit.GetBoolean()),
        ["rail"] = json => new RailBehavior(json.TryGetProperty("powered", out var p) && p.GetBoolean()),
        ["slab"] = json => new SlabBehavior(json.TryGetProperty("is_double", out var d) && d.GetBoolean()),
        ["sign"] = json => new SignBehavior(json.TryGetProperty("standing", out var s) && s.GetBoolean()),
        ["pumpkin"] = json => new PumpkinBehavior(json.TryGetProperty("lit", out var pl) && pl.GetBoolean()),
        ["piston_base"] = json => new PistonBaseBehavior(json.TryGetProperty("sticky", out var st) && st.GetBoolean()),
        ["pressure_plate"] = json => new PressurePlateBehavior(Enum.Parse<PressurePlateActiviationRule>(json.GetProperty("activation_rule").GetString() ?? "EVERYTHING", true)),
        ["glass_visual"] = json => new GlassVisualBehavior(json.TryGetProperty("hide_adjacent_faces", out var h) && h.GetBoolean()),
        ["wall_mount"] = json => new WallMountBehavior(json.TryGetProperty("is_ladder", out var l) && l.GetBoolean()),

        // Cross-block references — must only run in the second (cross-reference) pass,
        // once every BlockDefinition has been registered by name. See wrinkle #1 in the
        // migration doc: Block.ByName returns Block?, so failures fail loud via ResolveBlock
        // rather than silently null-dereferencing.
        ["stairs"] = json => new StairsBehavior(() => ResolveBlock(json.GetProperty("base").GetString()!)),
        ["plant_survival"] = json => json.TryGetProperty("valid_soil", out var soil)
            ? new PlantSurvivalBehavior(id => id == ResolveBlock(soil.GetString()!).Id)
            : new PlantSurvivalBehavior(),
        ["melt"] = json => new MeltBehavior(
            () => ResolveBlock(json.GetProperty("melt_replacement").GetString()!).Id,
            json.TryGetProperty("subtract_opacity", out var sub) && sub.GetBoolean(),
            json.TryGetProperty("broken_replacement", out var broken) ? () => ResolveBlock(broken.GetString()!).Id : null),

        // Nested/composite behaviors — construct their own private sub-behavior inline
        // rather than reference another JSON-declared entry (see wrinkle #2): these
        // classes are stateless, so a duplicate instance costs nothing.
        ["redstone_torch"] = _ => new RedstoneTorchBehavior(new WallMountBehavior(false)),

        // Stateless, parameterless behaviors
        ["bed"] = _ => new BedBehavior(),
        ["button"] = _ => new ButtonBehavior(),
        ["cactus"] = _ => new CactusBehavior(),
        ["cake"] = _ => new CakeBehavior(),
        ["chest"] = _ => new ChestBehavior(),
        ["cloth_visual"] = _ => new ClothVisualBehavior(),
        ["crop"] = _ => new CropBehavior(),
        ["detector_rail"] = _ => new DetectorRailBehavior(),
        ["dispenser"] = _ => new DispenserBehavior(),
        ["falling_block"] = _ => new FallingBlockBehavior(),
        ["farmland"] = _ => new FarmlandBehavior(),
        ["fence"] = _ => new FenceBehavior(),
        ["fire"] = _ => new FireBehavior(),
        ["flowing_fluid"] = _ => new FlowingFluidBehavior(),
        ["grass_ticker"] = _ => new GrassTickerBehavior(),
        ["grass_visual"] = _ => new GrassVisualBehavior(),
        ["jukebox"] = _ => new JukeboxBehavior(),
        ["leaves"] = _ => new LeavesBehavior(),
        ["lever"] = _ => new LeverBehavior(),
        ["locked_chest"] = _ => new LockedChestBehavior(),
        ["log"] = _ => new LogBehavior(),
        ["mushroom"] = _ => new MushroomBehavior(),
        ["noteblock"] = _ => new NoteBlockBehavior(),
        ["piston_extension"] = _ => new PistonExtensionBehavior(),
        ["piston_moving"] = _ => new PistonMovingBehavior(),
        ["portal"] = _ => new PortalBehavior(),
        ["redstone_ore"] = _ => new RedstoneOreBehavior(),
        ["redstone_wire"] = _ => new RedstoneWireBehavior(),
        ["reed"] = _ => new ReedBehavior(),
        ["repeater"] = _ => new RepeaterBehavior(),
        ["sapling"] = _ => new SaplingBehavior(),
        ["snow"] = _ => new SnowBehavior(),
        ["soul_sand"] = _ => new SoulSandBehavior(),
        ["sponge_lifecycle"] = _ => new SpongeLifecycleBehavior(),
        ["stationary_fluid"] = _ => new StationaryFluidBehavior(),
        ["tall_grass"] = _ => new TallGrassBehavior(),
        ["tile_entity_lifecycle"] = _ => new TileEntityLifecycleBehavior(),
        ["tnt"] = _ => new TNTBehavior(),
        ["web"] = _ => new WebBehavior(),
        ["workbench_interact"] = _ => new WorkbenchInteractBehavior(),
    };

    public static object Build(string type, JsonElement json) =>
        s_factories.TryGetValue(type, out BehaviorFactory? factory)
            ? factory(json)
            : throw new ArgumentException($"Unknown block behavior type '{type}'");

    private static Block ResolveBlock(string name) =>
        Block.ByName(name) ?? throw new ArgumentException($"Unknown block: '{name}'");
}
