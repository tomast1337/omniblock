using System.Collections.Frozen;
using System.Text.Json;
using OmniBlock.Blocks.Materials;
using OmniBlock.Items;

namespace OmniBlock.Blocks.Behaviors;

internal sealed class BlockBehaviorProviderRegistry : IBlockBehaviorProviderRegistry
{
    public delegate object BehaviorFactory(JsonElement json);

    private readonly BehaviorBuildContext _context;
    private readonly FrozenDictionary<ResourceLocation, BehaviorFactory> _factories;

    public BlockBehaviorProviderRegistry(BehaviorBuildContext context)
    {
        _context = context;
        _factories = BuiltInFactories().ToFrozenDictionary(
            static pair => ResourceLocation.Parse(pair.Key),
            static pair => pair.Value);
    }

    public object Build(ResourceLocation type, JsonElement definition, in BehaviorBuildContext context)
    {
        if (!_factories.TryGetValue(type, out var factory))
            throw new ArgumentException($"Unknown block behavior type '{type}'");

        var behavior = factory(definition);
        if (behavior is BlockRuntimeBehavior runtimeBehavior)
            runtimeBehavior.BindRuntime(_context.Blocks);
        return behavior;
    }

    private Dictionary<string, BehaviorFactory> BuiltInFactories()
    {
        return new Dictionary<string, BehaviorFactory>
        {
            // Parameterized behaviors extract their state from JSON data
            ["door"] = json => new DoorBehavior(ResolveMaterial(json.GetProperty("material").GetString() ?? "wood")),
            ["trap_door"] = json => new TrapDoorBehavior(ResolveMaterial(json.GetProperty("material").GetString() ?? "wood")),
            ["furnace"] = json => new FurnaceBehavior(json.TryGetProperty("lit", out var lit) && lit.GetBoolean(),
                Texture(json, "top"), Texture(json, "front_off"), Texture(json, "front_on")),
            ["rail"] = json => new RailBehavior(json.TryGetProperty("powered", out var p) && p.GetBoolean(),
                Texture(json, "turn"), Texture(json, "unpowered")),
            ["slab"] = json => new SlabBehavior(json.TryGetProperty("is_double", out var d) && d.GetBoolean(),
                [.. json.GetProperty("variants").EnumerateArray().Select(ResolveFaceTextures)]),
            ["sign"] = json => new SignBehavior(json.TryGetProperty("standing", out var s) && s.GetBoolean()),
            ["pumpkin"] = json => new PumpkinBehavior(Texture(json, "top"), Texture(json, "side"),
                Texture(json, "face"), Texture(json, "item_face")),
            ["piston_base"] = json => new PistonBaseBehavior(
                json.TryGetProperty("sticky", out var st) && st.GetBoolean(),
                ResolveTexture(json.GetProperty("top").GetString()!),
                ResolveTexture(json.GetProperty("side").GetString()!),
                ResolveTexture(json.GetProperty("bottom").GetString()!),
                ResolveTexture(json.GetProperty("extension_side").GetString()!)),
            ["pressure_plate"] = json => new PressurePlateBehavior(Enum.Parse<PressurePlateActiviationRule>(json.GetProperty("activation_rule").GetString() ?? "EVERYTHING", true)),
            ["glass_visual"] = json => new GlassVisualBehavior(json.TryGetProperty("hide_adjacent_faces", out var h) && h.GetBoolean()),
            ["wall_mount"] = json => new WallMountBehavior(json.TryGetProperty("is_ladder", out var l) && l.GetBoolean()),
            ["stairs"] = json =>
            {
                var baseBlock = ResolveBlock(json.GetProperty("base").GetString()!);
                return new StairsBehavior(() => baseBlock);
            },
            ["plant_survival"] = json => new PlantSurvivalBehavior(ResolveBlockArray(json.GetProperty("valid_ground"))),
            ["melt"] = json =>
            {
                var meltReplacement = ResolveBlockOrAir(json.GetProperty("melt_replacement").GetString()!);
                int? brokenReplacement = json.TryGetProperty("broken_replacement", out var broken)
                    ? ResolveBlockOrAir(broken.GetString()!)
                    : null;
                return new MeltBehavior(
                    () => meltReplacement,
                    json.TryGetProperty("subtract_opacity", out var sub) && sub.GetBoolean(),
                    brokenReplacement is { } replacement ? () => replacement : null);
            },
            ["redstone_torch"] = _ => new RedstoneTorchBehavior(new WallMountBehavior(false),
                ResolveBlock("redstone_torch"), ResolveBlock("lit_redstone_torch"), ResolveBlock("redstone_wire")),
            ["bed"] = json => new BedBehavior(Texture(json, "bottom"),
                Texture(json, "foot_top"), Texture(json, "foot_side"), Texture(json, "foot_end"),
                Texture(json, "head_top"), Texture(json, "head_side"), Texture(json, "head_end"), ResolveItem("bed")),
            ["button"] = _ => new ButtonBehavior(),
            ["cactus"] = json => new CactusBehavior(ResolveBlock(json.GetProperty("stem").GetString()!), ResolveBlock(json.GetProperty("soil").GetString()!), json.GetProperty("max_height").GetInt32(),
                Texture(json, "top"), Texture(json, "side"), Texture(json, "bottom")),
            ["cake"] = json => new CakeBehavior(Texture(json, "top"), Texture(json, "side"), Texture(json, "inner"), Texture(json, "bottom")),
            ["chest"] = json => new ChestBehavior(
                ResolveTexture(json.GetProperty("top").GetString()!),
                ResolveTexture(json.GetProperty("side").GetString()!),
                ResolveTexture(json.GetProperty("front").GetString()!),
                ResolveTexture(json.GetProperty("double_front_left").GetString()!),
                ResolveTexture(json.GetProperty("double_front_right").GetString()!),
                ResolveTexture(json.GetProperty("double_back_left").GetString()!),
                ResolveTexture(json.GetProperty("double_back_right").GetString()!)),
            ["cloth_visual"] = json => new ClothVisualBehavior(ResolveTextures(json.GetProperty("textures"))),
            ["crop"] = json => new CropBehavior(ResolveBlock(json.GetProperty("required_soil").GetString()!), ResolveItem(json.GetProperty("mature_crop_item").GetString()!), ResolveItem(json.GetProperty("seeds").GetString()!),
                json.GetProperty("drop_spread").GetSingle(), json.GetProperty("seed_scatter_chance_bound").GetInt32(), json.GetProperty("growth_chance_denominator").GetInt32(),
                ResolveTextures(json.GetProperty("stages"))),
            ["detector_rail"] = _ => new DetectorRailBehavior(),
            ["dispenser"] = json => new DispenserBehavior(ResolveItem(json.GetProperty("arrow").GetString()!), ResolveItem(json.GetProperty("egg").GetString()!), ResolveItem(json.GetProperty("snowball").GetString()!),
                Texture(json, "front"), Texture(json, "top"), Texture(json, "side")),
            ["falling_block"] = json => new FallingBlockBehavior(ResolveBlockArray(json.GetProperty("passable")), json.GetProperty("region_load_check_radius").GetInt32()),
            ["farmland"] = json => new FarmlandBehavior(ResolveBlock(json.GetProperty("revert_block").GetString()!), ResolveBlock(json.GetProperty("crop").GetString()!),
                json.GetProperty("trample_chance_one_in").GetInt32(), json.GetProperty("tick_chance_one_in").GetInt32(), json.GetProperty("water_check_radius").GetInt32(),
                Texture(json, "wet"), Texture(json, "dry"), Texture(json, "side")),
            ["fence"] = _ => new FenceBehavior(),
            ["fire"] = json => new FireBehavior(ResolveBlock(json.GetProperty("portal_base").GetString()!), ResolveBlock(json.GetProperty("portal_fill").GetString()!), ResolveBlock(json.GetProperty("eternal_fuel").GetString()!),
                ResolveBlock(json.GetProperty("explosive").GetString()!), json.GetProperty("max_age").GetInt32(), json.GetProperty("crackle_sound_chance_one_in").GetInt32()),
            ["flowing_fluid"] = json => new FlowingFluidBehavior(ResolveBlockArray(json.GetProperty("passable")), ResolveBlock(json.GetProperty("source_solidified").GetString()!),
                ResolveBlock(json.GetProperty("flow_solidified").GetString()!),
                Texture(json, "still"), Texture(json, "flowing")),
            ["grass_ticker"] = json => new GrassTickerBehavior(ResolveBlock(json.GetProperty("soil").GetString()!), json.GetProperty("die_light_threshold").GetInt32(), json.GetProperty("die_chance_one_in").GetInt32(),
                json.GetProperty("spread_light_threshold").GetInt32()),
            ["grass_visual"] = json => new GrassVisualBehavior(Texture(json, "side"), Texture(json, "snowy_side")),
            ["jukebox"] = json => new JukeboxBehavior(json.GetProperty("drop_spread").GetSingle()),
            ["leaves"] = json => new LeavesBehavior(ResolveBlock(json.GetProperty("trunk").GetString()!), ResolveBlock(json.GetProperty("sapling").GetString()!), ResolveItem(json.GetProperty("harvest_tool").GetString()!),
                ResolveTextures(json.GetProperty("fancy")), ResolveTextures(json.GetProperty("fast"))),
            ["lever"] = _ => new LeverBehavior(),
            ["locked_chest"] = json => new LockedChestBehavior(
                ResolveTexture(json.GetProperty("top").GetString()!),
                ResolveTexture(json.GetProperty("side").GetString()!),
                ResolveTexture(json.GetProperty("front").GetString()!)),
            ["log"] = json => new LogBehavior(ResolveBlock(json.GetProperty("canopy").GetString()!), json.GetProperty("search_radius").GetInt32(),
                Texture(json, "top"), ResolveTextures(json.GetProperty("sides"))),
            ["mushroom"] = json => new MushroomBehavior(ResolveBlockArray(json.GetProperty("valid_ground")), json.GetProperty("spread_chance_one_in").GetInt32(), json.GetProperty("max_brightness").GetInt32()),
            ["noteblock"] = _ => new NoteBlockBehavior(),
            ["piston_extension"] = _ => new PistonExtensionBehavior(),
            ["piston_moving"] = _ => new PistonMovingBehavior(),
            ["portal"] = json => new PortalBehavior(ResolveBlock(json.GetProperty("portal_base").GetString()!)),
            ["redstone_ore"] = json => new RedstoneOreBehavior(ResolveBlock(json.GetProperty("unlit_ore").GetString()!), ResolveBlock(json.GetProperty("lit_ore").GetString()!)),
            ["redstone_wire"] = json => new RedstoneWireBehavior(ResolveBlock(json.GetProperty("wire").GetString()!), ResolveBlockArray(json.GetProperty("conductors")), ResolveBlock(json.GetProperty("repeater").GetString()!),
                ResolveBlock(json.GetProperty("powered_repeater").GetString()!)),
            ["reed"] = json => new ReedBehavior(ResolveBlockArray(json.GetProperty("valid_ground"))),
            ["repeater"] = json => new RepeaterBehavior(Texture(json, "top_off"), Texture(json, "top_on"),
                Texture(json, "torch_off"), Texture(json, "torch_on"), Texture(json, "side"),
                ResolveBlock("repeater"), ResolveBlock("powered_repeater"), ResolveBlock("redstone_wire")),
            ["sapling"] = json => new SaplingBehavior(ResolveTextures(json.GetProperty("textures"))),
            ["scripted_ticker"] = json => new ScriptedTickerBehavior(json.GetProperty("script_hook").GetString()!),
            ["snow"] = json => new SnowBehavior(ResolveItem(json.GetProperty("drop_item").GetString()!), json.GetProperty("drop_spread").GetSingle()),
            ["soul_sand"] = json => new SoulSandBehavior(json.GetProperty("speed_factor").GetDouble()),
            ["sponge_lifecycle"] = json => new SpongeLifecycleBehavior(json.GetProperty("absorb_radius").GetInt32()),
            ["stationary_fluid"] = json => new StationaryFluidBehavior(ResolveBlock(json.GetProperty("ignition_target").GetString()!), ResolveBlock(json.GetProperty("source_solidified").GetString()!),
                ResolveBlock(json.GetProperty("flow_solidified").GetString()!),
                Texture(json, "still"), Texture(json, "flowing")),
            ["tall_grass"] = json => new TallGrassBehavior(ResolveItem(json.GetProperty("seeds").GetString()!), json.GetProperty("seed_drop_chance_one_in").GetInt32(), ResolveTextures(json.GetProperty("textures"))),
            ["tile_entity_lifecycle"] = _ => new TileEntityLifecycleBehavior(),
            ["tnt"] = json => new TntBehavior(ResolveItem(json.GetProperty("igniter").GetString()!),
                Texture(json, "top"), Texture(json, "side"), Texture(json, "bottom")),
            ["web"] = _ => new WebBehavior(),
            ["workbench_interact"] = _ => new WorkbenchInteractBehavior()
        };
    }

    private int ResolveTexture(string name)
    {
        return _context.ResolveTerrainTexture(name);
    }

    private int Texture(JsonElement json, string property)
    {
        return ResolveTexture(json.GetProperty(property).GetString()!);
    }

    private BlockFaceTextures ResolveFaceTextures(JsonElement json)
    {
        return new BlockFaceTextures(Texture(json, "top"), Texture(json, "side"), Texture(json, "bottom"));
    }

    private int[] ResolveTextures(JsonElement array)
    {
        var textures = new int[array.GetArrayLength()];
        var i = 0;
        foreach (var element in array.EnumerateArray()) textures[i++] = ResolveTexture(element.GetString()!);

        return textures;
    }

    private Block ResolveBlock(string name)
    {
        return _context.ResolveBlock(ResourceLocation.Parse(name));
    }

    private int ResolveBlockOrAir(string name)
    {
        var key = ResourceLocation.Parse(name);
        return key.Namespace == Namespace.OmniBlock && key.Path == "air"
            ? 0
            : _context.ResolveBlock(key).Id;
    }

    private Item ResolveItem(string name)
    {
        return _context.ResolveItem(ResourceLocation.Parse(name));
    }

    private Material ResolveMaterial(string name)
    {
        return _context.ResolveMaterial(ResourceLocation.Parse(name));
    }

    private Block[] ResolveBlockArray(JsonElement array)
    {
        var blocks = new Block[array.GetArrayLength()];
        var i = 0;
        foreach (var element in array.EnumerateArray()) blocks[i++] = ResolveBlock(element.GetString()!);

        return blocks;
    }
}