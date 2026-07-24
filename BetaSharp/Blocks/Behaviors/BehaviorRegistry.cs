using System.Text.Json;
using BetaSharp.Blocks.Materials;
using BetaSharp.Items;

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
        // once every BlockDefinition has been registered by name.
        ["stairs"] = json => new StairsBehavior(() => ResolveBlock(json.GetProperty("base").GetString()!)),
        ["plant_survival"] = json => json.TryGetProperty("valid_soil", out var soil)
            ? new PlantSurvivalBehavior(id => id == ResolveBlock(soil.GetString()!).id)
            : new PlantSurvivalBehavior(),
        ["melt"] = json => new MeltBehavior(
            () => ResolveBlockOrAir(json.GetProperty("melt_replacement").GetString()!),
            json.TryGetProperty("subtract_opacity", out var sub) && sub.GetBoolean(),
            json.TryGetProperty("broken_replacement", out var broken) ? () => ResolveBlockOrAir(broken.GetString()!) : null),

        // Nested/composite behaviors — construct their own private sub-behavior inline
        // rather than reference another JSON-declared entry (see wrinkle #2): these
        // classes are stateless, so a duplicate instance costs nothing.
        ["redstone_torch"] = _ => new RedstoneTorchBehavior(new WallMountBehavior(false)),

        // Stateless, parameterless behaviors
        ["bed"] = _ => new BedBehavior(),
        ["button"] = _ => new ButtonBehavior(),
        ["cactus"] = json => new CactusBehavior(ResolveBlock(json.GetProperty("stem").GetString()!), ResolveBlock(json.GetProperty("soil").GetString()!)),
        ["cake"] = _ => new CakeBehavior(),
        ["chest"] = _ => new ChestBehavior(),
        ["cloth_visual"] = _ => new ClothVisualBehavior(),
        ["crop"] = json => new CropBehavior(ResolveBlock(json.GetProperty("required_soil").GetString()!), ResolveItem(json.GetProperty("mature_crop_item").GetString()!), ResolveItem(json.GetProperty("seeds").GetString()!)),
        ["detector_rail"] = _ => new DetectorRailBehavior(),
        ["dispenser"] = json => new DispenserBehavior(ResolveItem(json.GetProperty("arrow").GetString()!), ResolveItem(json.GetProperty("egg").GetString()!), ResolveItem(json.GetProperty("snowball").GetString()!)),
        ["falling_block"] = json => new FallingBlockBehavior(ResolveBlockArray(json.GetProperty("passable"))),
        ["farmland"] = json => new FarmlandBehavior(ResolveBlock(json.GetProperty("revert_block").GetString()!), ResolveBlock(json.GetProperty("crop").GetString()!)),
        ["fence"] = _ => new FenceBehavior(),
        ["fire"] = json => new FireBehavior(ResolveBlock(json.GetProperty("portal_base").GetString()!), ResolveBlock(json.GetProperty("portal_fill").GetString()!), ResolveBlock(json.GetProperty("eternal_fuel").GetString()!), ResolveBlock(json.GetProperty("explosive").GetString()!)),
        ["flowing_fluid"] = json => new FlowingFluidBehavior(ResolveBlockArray(json.GetProperty("passable")), ResolveBlock(json.GetProperty("source_solidified").GetString()!), ResolveBlock(json.GetProperty("flow_solidified").GetString()!)),
        ["grass_ticker"] = json => new GrassTickerBehavior(ResolveBlock(json.GetProperty("soil").GetString()!)),
        ["grass_visual"] = _ => new GrassVisualBehavior(),
        ["jukebox"] = _ => new JukeboxBehavior(),
        ["leaves"] = json => new LeavesBehavior(ResolveBlock(json.GetProperty("trunk").GetString()!), ResolveBlock(json.GetProperty("sapling").GetString()!), ResolveItem(json.GetProperty("harvest_tool").GetString()!)),
        ["lever"] = _ => new LeverBehavior(),
        ["locked_chest"] = _ => new LockedChestBehavior(),
        ["log"] = json => new LogBehavior(ResolveBlock(json.GetProperty("canopy").GetString()!)),
        ["mushroom"] = json => new MushroomBehavior(ResolveBlockArray(json.GetProperty("valid_ground"))),
        ["noteblock"] = _ => new NoteBlockBehavior(),
        ["piston_extension"] = _ => new PistonExtensionBehavior(),
        ["piston_moving"] = _ => new PistonMovingBehavior(),
        ["portal"] = json => new PortalBehavior(ResolveBlock(json.GetProperty("portal_base").GetString()!)),
        ["redstone_ore"] = _ => new RedstoneOreBehavior(),
        ["redstone_wire"] = json => new RedstoneWireBehavior(ResolveBlock(json.GetProperty("wire").GetString()!), ResolveBlockArray(json.GetProperty("conductors")), ResolveBlock(json.GetProperty("repeater").GetString()!), ResolveBlock(json.GetProperty("powered_repeater").GetString()!)),
        ["reed"] = json => new ReedBehavior(ResolveBlockArray(json.GetProperty("valid_ground"))),
        ["repeater"] = _ => new RepeaterBehavior(),
        ["sapling"] = _ => new SaplingBehavior(),
        ["snow"] = json => new SnowBehavior(ResolveItem(json.GetProperty("drop_item").GetString()!)),
        ["soul_sand"] = _ => new SoulSandBehavior(),
        ["sponge_lifecycle"] = _ => new SpongeLifecycleBehavior(),
        ["stationary_fluid"] = json => new StationaryFluidBehavior(ResolveBlock(json.GetProperty("ignition_target").GetString()!), ResolveBlock(json.GetProperty("source_solidified").GetString()!), ResolveBlock(json.GetProperty("flow_solidified").GetString()!)),
        ["tall_grass"] = json => new TallGrassBehavior(ResolveItem(json.GetProperty("seeds").GetString()!)),
        ["tile_entity_lifecycle"] = _ => new TileEntityLifecycleBehavior(),
        ["tnt"] = _ => new TNTBehavior(),
        ["web"] = _ => new WebBehavior(),
        ["workbench_interact"] = _ => new WorkbenchInteractBehavior(),
    };

    public static object Build(string type, JsonElement json) =>
        s_factories.TryGetValue(type, out BehaviorFactory? factory)
            ? factory(json)
            : throw new ArgumentException($"Unknown block behavior type '{type}'");

    // BlockRegistry.Get, not Block.ByName: the latter keys on RegistryName (the legacy
    // TranslationKey), which several blocks' JSON gives a completely different value from their
    // unique Name — e.g. Cobblestone's TranslationKey is "stonebrick" (an old Beta-era quirk).
    // The "base"/"valid_soil"/"melt_replacement" JSON fields always name the unique Name.
    private static Block ResolveBlock(string name) => BlockRegistry.Get(name);

    // Air (id 0) never has a registered Block instance, so BlockRegistry.Get can't resolve it —
    // snow's melt-to-air case needs this sentinel alongside real block-name lookups.
    private static int ResolveBlockOrAir(string name) =>
        name == "air" ? 0 : ResolveBlock(name).id;

    private static Item ResolveItem(string name) => Item.ByName(name);

    private static Block[] ResolveBlockArray(JsonElement array)
    {
        Block[] blocks = new Block[array.GetArrayLength()];
        int i = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            blocks[i++] = ResolveBlock(element.GetString()!);
        }

        return blocks;
    }
}
