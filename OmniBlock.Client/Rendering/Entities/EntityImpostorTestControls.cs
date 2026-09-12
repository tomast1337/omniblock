using OmniBlock.Client.Resource.Pack;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>Only reachable through the restricted E2E host. No user saves or cache files are edited.</summary>
internal sealed class EntityImpostorTestControls(OmniBlock game)
{
    private TexturePack? _original;
    private TexturePack? _red, _green;
    public bool Apply(string action)
    {
        var prototype = game.WorldRenderer?.EntityImpostors;
        if (prototype == null) return false;
        switch (action)
        {
            case "clear-memory": prototype.ClearMemoryForTest(); return true;
            case "dispose-session": prototype.RecreateGpuForTest(); return true;
            case "hold-readback": prototype.HoldReadbackForTest = true; return true;
            case "release-readback": prototype.HoldReadbackForTest = false; return true;
            case "hold-capture": prototype.HoldCaptureForTest = true; return true;
            case "release-capture": prototype.HoldCaptureForTest = false; return true;
            case "reload": game.TextureManager.Reload(); return true;
            case "pack-red":
            case "pack-green":
            case "pack-original":
                _original ??= game.TexturePackList.SelectedTexturePack;
                // The fixture overlay leaves all other resources on the ordinary fallback path.
                var pack = action == "pack-red" ? _red ??= new TintPack(true) :
                    action == "pack-green" ? _green ??= new TintPack(false) : _original;
                game.TexturePackList.setTexturePack(pack);
                game.TextureManager.Reload(); return true;
            default: return false;
        }
    }

    private sealed class TintPack : TexturePack
    {
        private readonly byte[] _skin;
        public TintPack(bool red)
        {
            TexturePackFileName = red ? "Impostor E2E red" : "Impostor E2E green";
            using var stream = base.GetResourceAsStream("/mob/cow.png")!;
            using var image = Image.Load<Rgba32>(stream);
            for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
            {
                var p = image[x, y];
                image[x, y] = red ? new Rgba32(p.R, (byte)(p.G / 3), (byte)(p.B / 3), p.A) : new Rgba32((byte)(p.R / 3), p.G, (byte)(p.B / 3), p.A);
            }
            using var output = new MemoryStream(); image.SaveAsPng(output); _skin = output.ToArray();
        }
        public override Stream? GetResourceAsStream(string path) => path == "/mob/cow.png" ? new MemoryStream(_skin, false) : base.GetResourceAsStream(path);
    }
}
