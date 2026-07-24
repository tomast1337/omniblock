using System.Text.Json;
using BetaSharp.Blocks.Materials;
using BetaSharp.Items;

namespace BetaSharp.Blocks.Behaviors;

internal static class BehaviorRegistry
{
    public delegate object BehaviorFactory(JsonElement json);

    private static readonly Dictionary<string, BehaviorFactory> s_factories = new()
    {
        // Parameterized behaviors extract their state from JSON data
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
        ["stairs"] = json => new StairsBehavior(() => ResolveBlock(json.GetProperty("base").GetString()!)),
        ["plant_survival"] = json => new PlantSurvivalBehavior(ResolveBlockArray(json.GetProperty("valid_ground"))),
        ["melt"] = json => new MeltBehavior(
            () => ResolveBlockOrAir(json.GetProperty("melt_replacement").GetString()!),
            json.TryGetProperty("subtract_opacity", out var sub) && sub.GetBoolean(),
            json.TryGetProperty("broken_replacement", out var broken) ? () => ResolveBlockOrAir(broken.GetString()!) : null),
        ["redstone_torch"] = _ => new RedstoneTorchBehavior(new WallMountBehavior(false)),
        ["bed"] = _ => new BedBehavior(),
        ["button"] = _ => new ButtonBehavior(),
        ["cactus"] = json => new CactusBehavior(ResolveBlock(json.GetProperty("stem").GetString()!), ResolveBlock(json.GetProperty("soil").GetString()!), json.GetProperty("max_height").GetInt32()),
        ["cake"] = _ => new CakeBehavior(),
        ["chest"] = _ => new ChestBehavior(),
        ["cloth_visual"] = _ => new ClothVisualBehavior(),
        ["crop"] = json => new CropBehavior(ResolveBlock(json.GetProperty("required_soil").GetString()!), ResolveItem(json.GetProperty("mature_crop_item").GetString()!), ResolveItem(json.GetProperty("seeds").GetString()!),
            json.GetProperty("drop_spread").GetSingle(), json.GetProperty("seed_scatter_chance_bound").GetInt32(), json.GetProperty("growth_chance_denominator").GetInt32()),
        ["detector_rail"] = _ => new DetectorRailBehavior(),
        ["dispenser"] = json => new DispenserBehavior(ResolveItem(json.GetProperty("arrow").GetString()!), ResolveItem(json.GetProperty("egg").GetString()!), ResolveItem(json.GetProperty("snowball").GetString()!)),
        ["falling_block"] = json => new FallingBlockBehavior(ResolveBlockArray(json.GetProperty("passable")), json.GetProperty("region_load_check_radius").GetInt32()),
        ["farmland"] = json => new FarmlandBehavior(ResolveBlock(json.GetProperty("revert_block").GetString()!), ResolveBlock(json.GetProperty("crop").GetString()!),
            json.GetProperty("trample_chance_one_in").GetInt32(), json.GetProperty("tick_chance_one_in").GetInt32(), json.GetProperty("water_check_radius").GetInt32()),
        ["fence"] = _ => new FenceBehavior(),
        ["fire"] = json => new FireBehavior(ResolveBlock(json.GetProperty("portal_base").GetString()!), ResolveBlock(json.GetProperty("portal_fill").GetString()!), ResolveBlock(json.GetProperty("eternal_fuel").GetString()!),
            ResolveBlock(json.GetProperty("explosive").GetString()!), json.GetProperty("max_age").GetInt32(), json.GetProperty("crackle_sound_chance_one_in").GetInt32()),
        ["flowing_fluid"] = json => new FlowingFluidBehavior(ResolveBlockArray(json.GetProperty("passable")), ResolveBlock(json.GetProperty("source_solidified").GetString()!), ResolveBlock(json.GetProperty("flow_solidified").GetString()!)),
        ["grass_ticker"] = json => new GrassTickerBehavior(ResolveBlock(json.GetProperty("soil").GetString()!), json.GetProperty("die_light_threshold").GetInt32(), json.GetProperty("die_chance_one_in").GetInt32(), json.GetProperty("spread_light_threshold").GetInt32()),
        ["grass_visual"] = _ => new GrassVisualBehavior(),
        ["jukebox"] = json => new JukeboxBehavior(json.GetProperty("drop_spread").GetSingle()),
        ["leaves"] = json => new LeavesBehavior(ResolveBlock(json.GetProperty("trunk").GetString()!), ResolveBlock(json.GetProperty("sapling").GetString()!), ResolveItem(json.GetProperty("harvest_tool").GetString()!)),
        ["lever"] = _ => new LeverBehavior(),
        ["locked_chest"] = _ => new LockedChestBehavior(),
        ["log"] = json => new LogBehavior(ResolveBlock(json.GetProperty("canopy").GetString()!), json.GetProperty("search_radius").GetInt32()),
        ["mushroom"] = json => new MushroomBehavior(ResolveBlockArray(json.GetProperty("valid_ground")), json.GetProperty("spread_chance_one_in").GetInt32(), json.GetProperty("max_brightness").GetInt32()),
        ["noteblock"] = _ => new NoteBlockBehavior(),
        ["piston_extension"] = _ => new PistonExtensionBehavior(),
        ["piston_moving"] = _ => new PistonMovingBehavior(),
        ["portal"] = json => new PortalBehavior(ResolveBlock(json.GetProperty("portal_base").GetString()!)),
        ["redstone_ore"] = json => new RedstoneOreBehavior(ResolveBlock(json.GetProperty("unlit_ore").GetString()!), ResolveBlock(json.GetProperty("lit_ore").GetString()!)),
        ["redstone_wire"] = json => new RedstoneWireBehavior(ResolveBlock(json.GetProperty("wire").GetString()!), ResolveBlockArray(json.GetProperty("conductors")), ResolveBlock(json.GetProperty("repeater").GetString()!),
            ResolveBlock(json.GetProperty("powered_repeater").GetString()!)),
        ["reed"] = json => new ReedBehavior(ResolveBlockArray(json.GetProperty("valid_ground"))),
        ["repeater"] = _ => new RepeaterBehavior(),
        ["sapling"] = _ => new SaplingBehavior(),
        ["snow"] = json => new SnowBehavior(ResolveItem(json.GetProperty("drop_item").GetString()!), json.GetProperty("drop_spread").GetSingle()),
        ["soul_sand"] = json => new SoulSandBehavior(json.GetProperty("speed_factor").GetDouble()),
        ["sponge_lifecycle"] = json => new SpongeLifecycleBehavior(json.GetProperty("absorb_radius").GetInt32()),
        ["stationary_fluid"] = json => new StationaryFluidBehavior(ResolveBlock(json.GetProperty("ignition_target").GetString()!), ResolveBlock(json.GetProperty("source_solidified").GetString()!),
            ResolveBlock(json.GetProperty("flow_solidified").GetString()!)),
        ["tall_grass"] = json => new TallGrassBehavior(ResolveItem(json.GetProperty("seeds").GetString()!), json.GetProperty("seed_drop_chance_one_in").GetInt32()),
        ["tile_entity_lifecycle"] = _ => new TileEntityLifecycleBehavior(),
        ["tnt"] = json => new TNTBehavior(ResolveItem(json.GetProperty("igniter").GetString()!)),
        ["web"] = _ => new WebBehavior(),
        ["workbench_interact"] = _ => new WorkbenchInteractBehavior(),
    };

    public static object Build(string type, JsonElement json) =>
        s_factories.TryGetValue(type, out BehaviorFactory? factory)
            ? factory(json)
            : throw new ArgumentException($"Unknown block behavior type '{type}'");
    
    private static Block ResolveBlock(string name) => BlockRegistry.Get(name);

    private static int ResolveBlockOrAir(string name) => name == "air" ? 0 : ResolveBlock(name).id;

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
