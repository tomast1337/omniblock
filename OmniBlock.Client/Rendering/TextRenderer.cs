using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using OmniBlock.Client.Options;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Rendering.UI;
using Silk.NET.OpenGL;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.Rendering;

public class TextRenderer : IDisposable
{
    private const string MonocraftFontPath = "assets/font/Monocraft.ttc";
    private const string UnifontPath = "assets/font/unifont.ttf";
    private const string SevenishPath = "assets/font/sevenish.ttf";

    private const int AtlasSize = 2048;
    private const int AtlasFontSize = 64;
    private const int GlyphPadding = 2;
    private const float DisplayScale = 0.125f;
    private readonly Image<Rgba32> _atlasImage;
    private readonly Dictionary<Rune, GlyphInfo> _glyphCache = [];
    private readonly ILogger<TextRenderer> _logger = Log.Instance.For<TextRenderer>();

    private readonly FontFamily _monoFamily;
    private readonly int _rowHeight;
    private readonly FontFamily _sevenFamily;

    private readonly TextureManager _textureManager;
    private readonly FontFamily _uniFamily;

    private readonly Rune ColorCodeChar = new('§');
    private int _atlasX;
    private int _atlasY;
    private Font _font;
    private TextOptions _textOptions;

    public TextRenderer(GameOptions options, TextureManager textureManager)
    {
        _textureManager = textureManager;

        var monoPath = Path.Combine(AppContext.BaseDirectory, "font", "Monocraft.ttc");
        var uniPath = Path.Combine(AppContext.BaseDirectory, "font", "unifont.ttf");
        var sevenPath = Path.Combine(AppContext.BaseDirectory, "font", "sevenPath.ttf");

        if (!File.Exists(monoPath))
            monoPath = MonocraftFontPath;

        if (!File.Exists(uniPath))
            uniPath = UnifontPath;

        if (!File.Exists(sevenPath))
            sevenPath = SevenishPath;

        if (!File.Exists(monoPath))
            throw new FileNotFoundException("Monocraft font not found", monoPath);

        if (!File.Exists(uniPath))
            throw new FileNotFoundException("Unifont font not found", uniPath);

        if (!File.Exists(sevenPath))
            throw new FileNotFoundException("Sevenish font not found", sevenPath);

        var collection = new FontCollection();
        _monoFamily = collection.AddCollection(monoPath).First();
        _uniFamily = collection.Add(uniPath);
        _sevenFamily = collection.Add(sevenPath);

        _rowHeight = AtlasFontSize + GlyphPadding;
        _atlasImage = new Image<Rgba32>(AtlasSize, AtlasSize);

        fontTextureName = textureManager.Load(_atlasImage);
        fontTextureName.Texture?.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);

        _font = _monoFamily.CreateFont(AtlasFontSize);
        _textOptions = new TextOptions(_font);

        ApplyFontForLanguage();

        Translations.LanguageChanged += ReloadForLanguage;
    }

    private TextureHandle? fontTextureName { get; }
    internal uint FontTextureId => fontTextureName != null ? (uint)fontTextureName.Id : 0;

    private bool UseUnifontPrimary =>
        Translations.Instance.CurrentLanguage?.Unifont ?? false;

    private bool UseSevenishPrimary =>
        Translations.Instance.CurrentLanguage?.Sevenish ?? false;

    public void Dispose() => Translations.LanguageChanged -= ReloadForLanguage;

    private void LoadClassicFontIntoAtlas(Image<Rgba32> classicFontImage)
    {
        var scale = AtlasFontSize / 8;
        var imgWidth = classicFontImage.Width;
        var imgHeight = classicFontImage.Height;

        var pixels = new Rgba32[imgWidth * imgHeight];
        classicFontImage.CopyPixelDataTo(pixels);

        for (var charIndex = 32; charIndex < 127; ++charIndex)
        {
            var col = charIndex % 16;
            var row = charIndex / 16;
            var lastSolidPixel = -1;

            for (var bit = 7; bit >= 0; --bit)
            {
                var xOffset = col * 8 + bit;
                var columnIsEmpty = true;

                for (var yOffset = 0; yOffset < 8 && columnIsEmpty; ++yOffset)
                {
                    var pixelIndex = (row * 8 + yOffset) * imgWidth + xOffset;
                    if (pixels[pixelIndex].A > 0)
                    {
                        columnIsEmpty = false;
                    }
                }

                if (!columnIsEmpty)
                {
                    lastSolidPixel = bit;
                    break;
                }
            }

            var advancePixels = lastSolidPixel + 2;
            if (charIndex == 32) advancePixels = 4;

            var cellW = 8 * scale;
            var cellH = 8 * scale;

            if (_atlasX + cellW > AtlasSize)
            {
                _atlasX = 0;
                _atlasY += _rowHeight;
            }

            using (var glyphImage = classicFontImage.Clone(ctx => ctx
                       .Crop(new Rectangle(col * 8, row * 8, 8, 8))
                       .Resize(cellW, cellH, KnownResamplers.NearestNeighbor)))
            {
                glyphImage.ProcessPixelRows(_atlasImage, (srcAccessor, dstAccessor) =>
                {
                    for (var y = 0; y < cellH; y++)
                    {
                        var srcRow = srcAccessor.GetRowSpan(y);
                        var dstRow = dstAccessor.GetRowSpan(_atlasY + y);
                        srcRow.Slice(0, cellW).CopyTo(dstRow.Slice(_atlasX, cellW));
                    }
                });
            }

            var u0 = (float)_atlasX / AtlasSize;
            var v0 = (float)_atlasY / AtlasSize;
            var u1 = (float)(_atlasX + cellW) / AtlasSize;
            var v1 = (float)(_atlasY + cellH) / AtlasSize;

            var c = (Rune)charIndex;

            float advanceWidth = advancePixels * scale;
            _glyphCache[c] = new GlyphInfo(advanceWidth, u0, v0, u1, v1, cellW, cellH);

            _atlasX += cellW;
        }

        UploadAtlasSubImage(0, 0, AtlasSize, _atlasY + _rowHeight);
    }

    private void ApplyFontForLanguage()
    {
        ClearAtlasRegion(0, 0, AtlasSize, AtlasSize);
        _atlasX = 0;
        _atlasY = 0;
        _glyphCache.Clear();

        if (UseUnifontPrimary)
        {
            _font = _uniFamily.CreateFont(AtlasFontSize);
            _textOptions = new TextOptions(_font);
        }
        else if (UseSevenishPrimary)
        {
            _font = _sevenFamily.CreateFont(AtlasFontSize);
            _textOptions = new TextOptions(_font);
        }
        else
        {
            _font = _monoFamily.CreateFont(AtlasFontSize);
            _textOptions = new TextOptions(_font)
            {
                FallbackFontFamilies = [_uniFamily, _sevenFamily]
            };

            try
            {
                var asset = AssetManager.Instance.GetAsset("font/default.png");
                using var stream = new MemoryStream(asset.GetBinaryContent());
                using var classicFontImage = Image.Load<Rgba32>(stream);
                LoadClassicFontIntoAtlas(classicFontImage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load classic font. Falling back entirely to TrueType.");
            }
        }
    }

    public void ReloadForLanguage() => ApplyFontForLanguage();

    private static void ClearAtlasRegion(Image<Rgba32> image, int x, int y, int w, int h) => image.Mutate(ctx => ctx.Fill(SixLabors.ImageSharp.Color.Transparent, new Rectangle(x, y, w, h)));

    private void ClearAtlasRegion(int x, int y, int w, int h) => ClearAtlasRegion(_atlasImage, x, y, w, h);

    public static FontRectangle MeasureRune(Rune rune, TextOptions options)
    {
        Span<char> buffer = stackalloc char[2];
        var len = rune.EncodeToUtf16(buffer);

        return TextMeasurer.MeasureAdvance(buffer.Slice(0, len), options);
    }

    private GlyphInfo GetOrCreateGlyph(Rune c)
    {
        if (_glyphCache.TryGetValue(c, out var info))
            return info;

        if (Rune.IsControl(c))
            c = new Rune('?');

        var advanceRect = MeasureRune(c, _textOptions);

        var advanceWidth = advanceRect.Width;
        var cellW = Math.Max(1, (int)Math.Ceiling(advanceRect.Width) + GlyphPadding);
        var cellH = _rowHeight;

        if (_atlasX + cellW > AtlasSize)
        {
            _atlasX = 0;
            _atlasY += _rowHeight;
        }

        if (_atlasY + cellH > AtlasSize)
        {
            _glyphCache.Clear();
            ClearAtlasRegion(0, 0, AtlasSize, AtlasSize);
            _atlasX = 0;
            _atlasY = 0;

            if (!UseUnifontPrimary)
            {
                var asset = AssetManager.Instance.GetAsset("font/default.png");
                using var stream = new MemoryStream(asset.GetBinaryContent());
                using var classicFontImage = Image.Load<Rgba32>(stream);
                LoadClassicFontIntoAtlas(classicFontImage);
            }
        }

        using (var glyphImage = new Image<Rgba32>(cellW, cellH))
        {
            ClearAtlasRegion(glyphImage, 0, 0, cellW, cellH);

            var drawX = 1f;
            var drawY = 1f;

            glyphImage.Mutate(ctx =>
            {
                RichTextOptions options = new(_font)
                {
                    Origin = new PointF(drawX, drawY),
                    FallbackFontFamilies = _textOptions.FallbackFontFamilies
                };

                ctx.DrawText(
                    options,
                    c.ToString(),
                    SixLabors.ImageSharp.Color.White);
            });

            glyphImage.ProcessPixelRows(_atlasImage, (srcAccessor, dstAccessor) =>
            {
                for (var gy = 0; gy < cellH; gy++)
                {
                    var srcRow = srcAccessor.GetRowSpan(gy);
                    var dstRow = dstAccessor.GetRowSpan(_atlasY + gy);
                    srcRow.Slice(0, cellW).CopyTo(dstRow.Slice(_atlasX, cellW));
                }
            });
        }

        var u0 = (float)_atlasX / AtlasSize;
        var v0 = (float)_atlasY / AtlasSize;
        var u1 = (float)(_atlasX + cellW) / AtlasSize;
        var v1 = (float)(_atlasY + cellH) / AtlasSize;

        UploadAtlasSubImage(_atlasX, _atlasY, cellW, cellH);

        info = new GlyphInfo(advanceWidth, u0, v0, u1, v1, cellW, cellH);
        _glyphCache[c] = info;
        _atlasX += cellW;
        return info;
    }

    private unsafe void UploadAtlasSubImage(int x, int y, int width, int height)
    {
        if (fontTextureName?.Texture == null) return;

        var bufferSize = width * height * 4;

        var region = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            var idx = 0;
            _atlasImage.ProcessPixelRows(accessor =>
            {
                for (var row = y; row < y + height; row++)
                {
                    var pixelRow = accessor.GetRowSpan(row);
                    for (var col = x; col < x + width; col++)
                    {
                        var p = pixelRow[col];
                        region[idx++] = p.R;
                        region[idx++] = p.G;
                        region[idx++] = p.B;
                        region[idx++] = p.A;
                    }
                }
            });

            fixed (byte* ptr = region)
            {
                fontTextureName.Texture!.UploadSubImage(x, y, width, height, ptr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading atlas sub image");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(region);
        }
    }

    private static ReadOnlySpan<Rune> ToRunes(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        // fast path: ASCII only (no allocation of an enumerator, just an array)
        var ascii = true;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] > 127)
            {
                ascii = false;
                break;
            }
        }

        if (ascii)
        {
            var runes = new Rune[text.Length];
            for (var i = 0; i < text.Length; i++)
                runes[i] = new Rune(text[i]);

            return runes;
        }

        return text.EnumerateRunes().ToArray();
    }

    public void DrawStringWithShadow(string text, float x, float y, Color color, HorizontalAlignment align = HorizontalAlignment.Left, UIBatchRenderer? batch = null, float scale = 1f, float cos = 1f, float sin = 0f, float pivotX = 0f, float pivotY = 0f) =>
        DrawStringWithShadow(ToRunes(text), x, y, color, align, batch, scale, cos, sin, pivotX, pivotY);

    public void DrawStringWithShadow(ReadOnlySpan<Rune> text, float x, float y, Color color, HorizontalAlignment align = HorizontalAlignment.Left, UIBatchRenderer? batch = null, float scale = 1f, float cos = 1f, float sin = 0f, float pivotX = 0f,
        float pivotY = 0f)
    {
        RenderString(text, x + 1, y + 1, color, true, align, batch, scale, cos, sin, pivotX, pivotY);
        DrawString(text, x, y, color, align, batch, scale, cos, sin, pivotX, pivotY);
    }

    public void DrawString(string text, float x, float y, Color color, HorizontalAlignment align = HorizontalAlignment.Left, UIBatchRenderer? batch = null, float scale = 1f, float cos = 1f, float sin = 0f, float pivotX = 0f, float pivotY = 0f) =>
        DrawString(ToRunes(text), x, y, color, align, batch, scale, cos, sin, pivotX, pivotY);

    public void DrawString(ReadOnlySpan<Rune> text, float x, float y, Color color, HorizontalAlignment align = HorizontalAlignment.Left, UIBatchRenderer? batch = null, float scale = 1f, float cos = 1f, float sin = 0f, float pivotX = 0f,
        float pivotY = 0f) => RenderString(text, x, y, color, false, align, batch, scale, cos, sin, pivotX, pivotY);

    public void RenderString(ReadOnlySpan<Rune> text, float x, float y, Color color, bool darken, HorizontalAlignment align, UIBatchRenderer? batch = null, float scale = 1f, float cos = 1f, float sin = 0f, float pivotX = 0f, float pivotY = 0f)
    {
        if (text.IsEmpty) return;

        if (darken)
            color = color.Darken();

        var currentX = x;
        var currentY = y;

        var width = GetStringWidth(text);
        if (align == HorizontalAlignment.Center) currentX -= width * scale / 2f;
        else if (align == HorizontalAlignment.Right) currentX -= width * scale;

        if (batch != null)
        {
            batch.SetTexture((uint)fontTextureName!.Id);
            var currentRgba = (uint)color;
            var isRotated = sin != 0f;

            for (var i = 0; i < text.Length; ++i)
            {
                for (; text.Length > i + 1 && text[i] == ColorCodeChar; i += 2)
                {
                    if (TryHexToDec(text[i + 1], out var colorCode))
                        currentRgba = (uint)Color.FromColorCode(colorCode, (byte)color.A, darken);
                }

                if (i < text.Length)
                {
                    var glyph = GetOrCreateGlyph(text[i]);
                    if (glyph.Width > 0 && glyph.Height > 0)
                    {
                        var w = glyph.Width * DisplayScale * scale;
                        var h = glyph.Height * DisplayScale * scale;

                        if (!isRotated)
                        {
                            batch.AddQuad(currentX, currentY, currentX + w, currentY + h, glyph.U0, glyph.V0, glyph.U1, glyph.V1, currentRgba);
                        }
                        else
                        {
                            float lx0 = currentX, ly0 = currentY;
                            float lx1 = currentX, ly1 = currentY + h;
                            float lx2 = currentX + w, ly2 = currentY + h;
                            float lx3 = currentX + w, ly3 = currentY;
                            batch.AddQuadCorners(
                                pivotX + lx0 * cos - ly0 * sin, pivotY + lx0 * sin + ly0 * cos,
                                pivotX + lx1 * cos - ly1 * sin, pivotY + lx1 * sin + ly1 * cos,
                                pivotX + lx2 * cos - ly2 * sin, pivotY + lx2 * sin + ly2 * cos,
                                pivotX + lx3 * cos - ly3 * sin, pivotY + lx3 * sin + ly3 * cos,
                                glyph.U0, glyph.V0, glyph.U1, glyph.V1, currentRgba);
                        }
                    }

                    var advance = glyph.AdvanceWidth * DisplayScale * scale;
                    currentX = isRotated ? currentX + advance : MathF.Floor(currentX + advance);
                }
            }

            return;
        }

        fontTextureName?.Bind();

        var tessellator = Tessellator.instance;
        tessellator.startDrawingQuads();
        tessellator.setColorRGBA(color);

        for (var i = 0; i < text.Length; ++i)
        {
            for (; text.Length > i + 1 && text[i] == ColorCodeChar; i += 2)
            {
                if (TryHexToDec(text[i + 1], out var colorCode))
                {
                    tessellator.setColorRGBA(Color.FromColorCode(colorCode, (byte)color.A, darken));
                }
            }

            if (i < text.Length)
            {
                var glyph = GetOrCreateGlyph(text[i]);
                if (glyph.Width > 0 && glyph.Height > 0)
                {
                    var w = glyph.Width * DisplayScale;
                    var h = glyph.Height * DisplayScale;
                    tessellator.addVertexWithUV(currentX + 0, currentY + h, 0, glyph.U0, glyph.V1);
                    tessellator.addVertexWithUV(currentX + w, currentY + h, 0, glyph.U1, glyph.V1);
                    tessellator.addVertexWithUV(currentX + w, currentY + 0, 0, glyph.U1, glyph.V0);
                    tessellator.addVertexWithUV(currentX + 0, currentY + 0, 0, glyph.U0, glyph.V0);
                }

                currentX = MathF.Floor(currentX + glyph.AdvanceWidth * DisplayScale);
            }
        }

        tessellator.draw(ProgramSlot.Textured);
    }

    private static int HexToDec(char c)
    {
        int v = c;
        if (c <= '9') v -= '0';
        else if (c <= 'F') v += 10 - 'A';
        else if (c <= 'f') v += 10 - 'a';
        else return 15;
        return v <= 0 ? 0 : v;
    }

    private static bool TryHexToDec(Rune c, out int result)
    {
        var v = c.Value;

        if (v >= '0' && v <= '9')
        {
            result = v - '0';
            return true;
        }

        if (v >= 'A' && v <= 'F')
        {
            result = v - 'A' + 10;
            return true;
        }

        if (v >= 'a' && v <= 'f')
        {
            result = v - 'a' + 10;
            return true;
        }

        result = 0;
        return false;
    }

    public int GetStringWidth(ReadOnlySpan<Rune> text)
    {
        if (text.IsEmpty) return 0;
        float total = 0;
        for (var i = 0; i < text.Length; ++i)
        {
            if (text[i] == ColorCodeChar)
                ++i;
            else
                total += GetOrCreateGlyph(text[i]).AdvanceWidth * DisplayScale;
        }

        return (int)Math.Ceiling(total);
    }

    public int GetStringWidth(string text) => GetStringWidth(ToRunes(text));

    private int GetStringFitLength(ReadOnlySpan<Rune> text, int maxWidth)
    {
        float width = 0;
        var lastSpaceIndex = -1;

        for (var i = 0; i < text.Length; i++)
        {
            var r = text[i];

            if (r == ColorCodeChar)
            {
                i++; // skip next rune as part of color code sequence
                continue;
            }

            if (r == new Rune(' '))
                lastSpaceIndex = i;

            width += GetOrCreateGlyph(r).AdvanceWidth * DisplayScale;

            if (width > maxWidth)
            {
                if (lastSpaceIndex >= 0)
                    return lastSpaceIndex;

                return Math.Max(1, i);
            }
        }

        return text.Length;
    }

    private void ProcessWrappedText(ReadOnlySpan<Rune> text, int x, int y, int maxWidth, Color color, bool draw, ref int outHeight, HorizontalAlignment align, UIBatchRenderer? batch = null)
    {
        if (text.IsEmpty) return;

        var totalHeight = 0;
        var currentY = y;
        var lineHeight = (int)((AtlasFontSize + GlyphPadding) * DisplayScale);
        while (text.Length > 0)
        {
            var newlineIndex = text.IndexOf(new Rune('\n'));
            ReadOnlySpan<Rune> line;
            if (newlineIndex >= 0)
            {
                line = text.Slice(0, newlineIndex);
                text = text.Slice(newlineIndex + 1);
            }
            else
            {
                line = text;
                text = [];
            }

            while (line.Length > 0)
            {
                var fitLength = GetStringFitLength(line, maxWidth);
                var subline = line.Slice(0, Math.Min(fitLength, line.Length));

                while (subline.Length > 0 && subline[^1] == new Rune(' '))
                    subline = subline.Slice(0, subline.Length - 1);

                if (subline.Length > 0 || fitLength > 0)
                {
                    if (draw && subline.Length > 0)
                        DrawString(subline, x, currentY, color, align, batch);
                    currentY += lineHeight;
                    totalHeight += lineHeight;
                }

                line = line.Slice(Math.Min(fitLength, line.Length));
                while (line.Length > 0 && line[0] == new Rune(' '))
                    line = line.Slice(1);
            }
        }

        if (totalHeight < lineHeight) totalHeight = lineHeight;
        outHeight = totalHeight;
    }

    public void DrawStringWrapped(string text, int x, int y, int maxWidth, Color color, HorizontalAlignment align = HorizontalAlignment.Left, UIBatchRenderer? batch = null) => DrawStringWrapped(ToRunes(text), x, y, maxWidth, color, align, batch);

    public void DrawStringWrapped(ReadOnlySpan<Rune> text, int x, int y, int maxWidth, Color color, HorizontalAlignment align = HorizontalAlignment.Left, UIBatchRenderer? batch = null)
    {
        var dummyHeight = 0;
        ProcessWrappedText(text, x, y, maxWidth, color, true, ref dummyHeight, align, batch);
    }

    public int GetStringHeight(ReadOnlySpan<Rune> text, int maxWidth)
    {
        var height = 0;
        ProcessWrappedText(text, 0, 0, maxWidth, Color.Black, false, ref height, HorizontalAlignment.Left);
        return height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct GlyphInfo(float advanceWidth, float u0, float v0, float u1, float v1, float width, float height)
    {
        public readonly float AdvanceWidth = advanceWidth;
        public readonly float U0 = u0, V0 = v0, U1 = u1, V1 = v1;
        public readonly float Width = width, Height = height;
    }
}
