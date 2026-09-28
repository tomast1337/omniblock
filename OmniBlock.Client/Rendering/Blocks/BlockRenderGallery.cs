using System.Text.Json;
using OmniBlock.Registries;
using OmniBlock.Client.DynamicTexture;
using Sprite = OmniBlock.Client.Rendering.Core.Textures.DynamicTexture;

namespace OmniBlock.Client.Rendering.Blocks;

/// <summary>
/// Restricted E2E fixture: client-only block replicas in an isolated flat world. Uses the real
/// chunk mesher, lighting and renderer, but never saves, ticks or places these on the server.
/// This characterizes rendering, not placement validity or block-entity animation.
/// </summary>
internal sealed class BlockRenderGallery : IDisposable
{
    private readonly OmniBlock game;
    internal const int PageSize = 12;
    internal const int CaptureWidth = 1280;
    internal const int CaptureHeight = 720;
    private const int StudioMinX = -24;
    private const int StudioMaxX = 38;
    private const int StudioMinZ = -12;
    private const int StudioBackZ = 13;
    internal sealed record Sample(string Id, int Meta);
    private readonly bool _previousHideGui;
    private readonly Sample[] _samples;
    private readonly IReadOnlyDictionary<int, byte[]>? _previousAnimationFrames;
    private int _pageIndex;
    private bool _backdropBuilt;
    private Sample[] _page = [];
    private readonly HashSet<(int X, int Y, int Z)> _occupied = [];
    public int PageCount => (_samples.Length + PageSize - 1) / PageSize;

    public BlockRenderGallery(OmniBlock game)
    {
        this.game = game;
        _previousHideGui = game.Options.HideGUI;
        _samples = BuildSamples(game.Content.Blocks);
        _previousAnimationFrames = game.TextureManager.TerrainAnimationFramesForTest;
        Sprite[] animations = [new WaterSprite(game.Content.Blocks), new WaterSideSprite(),
            new LavaSprite(game.Content.Blocks), new LavaSideSprite(), new NetherPortalSprite(game.Content.Blocks),
            new FireSprite("fire_layer_0", "custom_fire_e_w.png"), new FireSprite("fire_layer_1", "custom_fire_n_s.png")];
        var frames = new Dictionary<int, byte[]>();
        foreach (var animation in animations)
        {
            animation.RandomForTest = new Random(7419 + animation.Sprite);
            animation.Setup(game);
            for (var tick = 0; tick < 64; tick++) animation.tick();
            frames.Add(animation.Sprite, animation.Pixels.ToArray());
        }
        game.TextureManager.TerrainAnimationFramesForTest = frames;
    }

    internal static Sample[] BuildSamples(RuntimeBlockRegistry blocks)
    {
        List<Sample> result = [];
        foreach (var key in blocks.Keys.OrderBy(key => key.ToString(), StringComparer.Ordinal))
        {
            var id = key.ToString();
            // Every catalog entry appears, even renderless technical blocks. Additional samples
            // deliberately characterize important variants rather than every invalid metadata bit.
            var count = id switch
            {
                "omniblock:wool" => 16,
                "omniblock:wooden_stairs" or "omniblock:cobblestone_stairs" => 4,
                "omniblock:slab" or "omniblock:double_slab" => 4,
                "omniblock:log" or "omniblock:leaves" => 3,
                "omniblock:torch" => 6,
                "omniblock:snow" => 8,
                _ => 1
            };
            for (var meta = 0; meta < count; meta++) result.Add(new Sample(id, meta));
        }
        return result.ToArray();
    }

    public void ShowPage(int page)
    {
        if (page < 0 || page >= PageCount) throw new ArgumentOutOfRangeException(nameof(page));
        var world = game.World ?? throw new InvalidOperationException("Gallery requires a client world.");
        for (var x = StudioMinX; x <= StudioMaxX; x++)
        for (var z = StudioMinZ; z <= StudioBackZ; z++)
            if (!world.BlockHost.HasChunk(x >> 4, z >> 4))
                throw new InvalidOperationException("Gallery chunks have not arrived.");
        if (!_backdropBuilt)
        {
            // A real meshed studio backdrop prevents asynchronously arriving terrain/LOD behind
            // the samples from polluting a screenshot diff. It is never part of the server world.
            var backdrop = world.Content.Blocks.Get("omniblock:wool").Id;
            for (var x = StudioMinX; x <= StudioMaxX; x++)
            {
                for (var z = StudioMinZ; z <= StudioBackZ; z++) Put(x, 98, z, backdrop, 15);
                for (var y = 99; y <= 120; y++) Put(x, y, StudioBackZ, backdrop, 15);
            }
            _backdropBuilt = true;
        }
        foreach (var pos in _occupied) Put(pos.X, pos.Y, pos.Z, 0, 0);
        _occupied.Clear();
        _page = _samples.Skip(page * PageSize).Take(PageSize).ToArray();
        _pageIndex = page;
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        for (var slot = 0; slot < _page.Length; slot++)
        {
            var (x, z) = Position(slot);
            // A neutral pedestal also makes missing/transparent geometry obvious in the diff.
            Place(x, 99, z, stone, 0);
            Place(x, 100, z, world.Content.Blocks.Get(_page[slot].Id).Id, _page[slot].Meta);
        }
        // Studio lighting is explicit, not whatever light happened to arrive above the flat
        // world's surface. Gameplay lighting has separate tests; here dark textures must remain
        // readable and identical regardless of server packet timing.
        for (var x = -1; x <= 16; x++)
        for (var z = -1; z <= 12; z++)
        for (var y = 98; y <= 102; y++)
        {
            world.Lighting.SetLight(global::OmniBlock.LightType.Sky, x, y, z, 15);
            world.Lighting.SetLight(global::OmniBlock.LightType.Block, x, y, z, 15);
        }
        game.Options.HideGUI = true;

        void Place(int x, int y, int z, int id, int meta)
        {
            Put(x, y, z, id, meta);
            _occupied.Add((x, y, z));
        }
        void Put(int x, int y, int z, int id, int meta)
        {
            world.Writer.SetBlockWithoutNotifyingNeighbors(x, y, z, id, meta, false);
            world.Broadcaster.BlockUpdateEvent(x, y, z);
        }
    }

    public string ArtifactName => $"block-gallery-{_pageIndex:D3}.json";

    public string Manifest()
    {
        var camera = game.Camera ?? throw new InvalidOperationException("Gallery requires a camera.");
        return JsonSerializer.Serialize(new
        {
            schema = 1, fixture = "block-gallery-v1", backdrop = "black-wool-wide-studio",
            lighting = "studio-sky15-block15", animation = "seed7419-frame64", page = _pageIndex, pages = PageCount,
            catalogCount = game.Content.Blocks.Count, sampleCount = _samples.Length,
            viewport = new { width = CaptureWidth, height = CaptureHeight },
            camera = new { camera.X, camera.Y, camera.Z, camera.Yaw, camera.Pitch },
            fov = game.Options.Fov, gamma = game.Options.Gamma,
            pack = game.TexturePackList.SelectedTexturePack.TexturePackFileName,
            samples = _page.Select((sample, slot) => new { sample.Id, sample.Meta, slot,
                x = Position(slot).X, y = 100, z = Position(slot).Z }),
            scope = "chunk geometry; not placement, inventory or animated block entities"
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    internal static (int X, int Z) Position(int slot) => (1 + slot % 4 * 4, 1 + slot / 4 * 4);

    public bool IsReady()
    {
        var world = game.World;
        if (world == null || _page.Length == 0) return false;
        for (var x = StudioMinX; x <= StudioMaxX; x += 8)
        {
            for (var z = StudioMinZ; z <= StudioBackZ; z += 4)
                if (game.WorldRenderer?.ChunkRenderer.IsMeshCurrent(x, 98, z) != true) return false;
            if (game.WorldRenderer?.ChunkRenderer.IsMeshCurrent(x, 115, StudioBackZ) != true) return false;
        }
        return _page.Select((sample, slot) => (sample, pos: Position(slot))).All(entry =>
            world.Reader.GetBlockId(entry.pos.X, 100, entry.pos.Z) == world.Content.Blocks.Get(entry.sample.Id).Id &&
            world.Reader.GetBlockMeta(entry.pos.X, 100, entry.pos.Z) == entry.sample.Meta &&
            game.WorldRenderer?.ChunkRenderer.IsMeshCurrent(entry.pos.X, 100, entry.pos.Z) == true);
    }

    public void PrepareFrame()
    {
        if (game.World is not { } world) return;
        world.SetTime(6000);
        world.Environment.SetRainGradient(0);
        world.Environment.SetThunderGradient(0);
        world.Environment.UpdateSkyBrightness();
        game.GameRenderer.CameraController.PinWorldBrightness(1);
        game.ParticleManager.clearEffects(world); // particles are not part of the block model contract
    }

    public void Dispose()
    {
        game.Options.HideGUI = _previousHideGui;
        game.TextureManager.TerrainAnimationFramesForTest = _previousAnimationFrames;
    }
}
