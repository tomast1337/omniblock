using BetaSharp.Client.Options;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Util;
using Silk.NET.OpenGL.Legacy;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.IO;

namespace BetaSharp.Client.Rendering;

public class TextRenderer
{
    private readonly int[] _charWidth = new int[256];
    public int fontTextureName = 0;
    private readonly int _fontDisplayLists;

    // Buffer to hold Display List IDs before sending them to OpenGL
    private readonly uint[] _listBuffer = new uint[1024];

    public TextRenderer(GameOptions options, TextureManager textureManager)
    {
        Image<Rgba32> fontImage;
        try
        {
            var asset = AssetManager.Instance.getAsset("font/default.png");
            using var stream = new MemoryStream(asset.getBinaryContent());
            fontImage = Image.Load<Rgba32>(stream);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to load font", ex);
        }

        int imgWidth = fontImage.Width;
        int imgHeight = fontImage.Height;
        int[] pixels = new int[imgWidth * imgHeight];

        fontImage.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < accessor.Width; x++)
                {
                    var p = row[x];
                    pixels[y * imgWidth + x] = (p.A << 24) | (p.R << 16) | (p.G << 8) | p.B;
                }
            }
        });

        for (int charIndex = 0; charIndex < 256; ++charIndex)
        {
            int col = charIndex % 16;
            int row = charIndex / 16;
            int widthInPixels = 0;

            for (int bit = 7; bit >= 0; --bit)
            {
                int xOffset = col * 8 + bit;
                bool columnIsEmpty = true;

                for (int yOffset = 0; yOffset < 8 && columnIsEmpty; ++yOffset)
                {
                    int pixelIndex = (row * 8 + yOffset) * imgWidth + xOffset;
                    int alpha = pixels[pixelIndex] & 255;
                    
                    if (alpha > 0)
                    {
                        columnIsEmpty = false;
                    }
                }

                if (!columnIsEmpty)
                {
                    widthInPixels = bit;
                    break;
                }
            }

            if (charIndex == 32)
            {
                widthInPixels = 2;
            }

            _charWidth[charIndex] = widthInPixels + 2;
        }

        fontTextureName = textureManager.Load(fontImage);
        _fontDisplayLists = GLAllocation.generateDisplayLists(288);
        Tessellator tessellator = Tessellator.instance;

        for (int charIndex = 0; charIndex < 256; ++charIndex)
        {
            GLManager.GL.NewList((uint)(_fontDisplayLists + charIndex), GLEnum.Compile);
            tessellator.startDrawingQuads();
            
            int u = (charIndex % 16) * 8;
            int v = (charIndex / 16) * 8;
            
            float quadSize = 7.99F; 
            float uvOffset = 0.0F;

            tessellator.addVertexWithUV(0.0D, quadSize, 0.0D, (u / 128.0F) + uvOffset, ((v + quadSize) / 128.0F) + uvOffset);
            tessellator.addVertexWithUV(quadSize, quadSize, 0.0D, ((u + quadSize) / 128.0F) + uvOffset, ((v + quadSize) / 128.0F) + uvOffset);
            tessellator.addVertexWithUV(quadSize, 0.0D, 0.0D, ((u + quadSize) / 128.0F) + uvOffset, (v / 128.0F) + uvOffset);
            tessellator.addVertexWithUV(0.0D, 0.0D, 0.0D, (u / 128.0F) + uvOffset, (v / 128.0F) + uvOffset);
            tessellator.draw();

            GLManager.GL.Translate(_charWidth[charIndex], 0.0F, 0.0F);
            GLManager.GL.EndList();
        }

        for (int colorIndex = 0; colorIndex < 32; ++colorIndex)
        {
            int baseColorOffset = (colorIndex >> 3 & 1) * 85;
            int r = (colorIndex >> 2 & 1) * 170 + baseColorOffset;
            int g = (colorIndex >> 1 & 1) * 170 + baseColorOffset;
            int b = (colorIndex >> 0 & 1) * 170 + baseColorOffset;
            
            if (colorIndex == 6)
            {
                r += 85;
            }

            bool isShadow = colorIndex >= 16;
            if (isShadow)
            {
                r /= 4;
                g /= 4;
                b /= 4;
            }

            GLManager.GL.NewList((uint)(_fontDisplayLists + 256 + colorIndex), GLEnum.Compile);
            GLManager.GL.Color3(r / 255.0F, g / 255.0F, b / 255.0F);
            GLManager.GL.EndList();
        }
    }

    public void DrawStringWithShadow(ReadOnlySpan<char> text, int x, int y, uint color)
    {
        RenderString(text, x + 1, y + 1, color, true);
        DrawString(text, x, y, color);
    }

    public void DrawString(ReadOnlySpan<char> text, int x, int y, uint color)
    {
        RenderString(text, x, y, color, false);
    }

    public unsafe void RenderString(ReadOnlySpan<char> text, int x, int y, uint color, bool darken)
    {
        if (text.IsEmpty) return;

        uint alpha = color & 0xFF000000;
        if (darken)
        {
            color = (color & 0xFCFCFC) >> 2;
            color |= alpha;
        }

        GLManager.GL.BindTexture(GLEnum.Texture2D, (uint)fontTextureName);
        float a = (color >> 24 & 255) / 255.0F;
        float r = (color >> 16 & 255) / 255.0F;
        float g = (color >> 8 & 255) / 255.0F;
        float b = (color & 255) / 255.0F;
        
        if (a == 0.0F) a = 1.0F;

        GLManager.GL.Color4(r, g, b, a);

        int bufferPos = 0;

        GLManager.GL.PushMatrix();
        GLManager.GL.Translate((float)x, (float)y, 0.0F);

        for (int i = 0; i < text.Length; ++i)
        {
            for (; text.Length > i + 1 && text[i] == 167; i += 2)
            {
                int colorCode = "0123456789abcdef".IndexOf(char.ToLower(text[i + 1]));
                if (colorCode < 0 || colorCode > 15)
                {
                    colorCode = 15;
                }

                _listBuffer[bufferPos++] = (uint)(_fontDisplayLists + 256 + colorCode + (darken ? 16 : 0));

                if (bufferPos >= 1024)
                {
                    CallLists(bufferPos);
                    bufferPos = 0;
                }
            }

            if (i < text.Length)
            {
                int charIndex = ChatAllowedCharacters.allowedCharacters.IndexOf(text[i]);
                if (charIndex >= 0)
                {
                    _listBuffer[bufferPos++] = (uint)(_fontDisplayLists + charIndex + 32);
                }
            }

            if (bufferPos >= 1024)
            {
                CallLists(bufferPos);
                bufferPos = 0;
            }
        }

        if (bufferPos > 0)
        {
            CallLists(bufferPos);
        }
        
        GLManager.GL.PopMatrix();

        void CallLists(int count)
        {
            fixed (uint* ptr = _listBuffer)
            {
                GLManager.GL.CallLists((uint)count, GLEnum.UnsignedInt, ptr);
            }
        }
    }

    public int GetStringWidth(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty) return 0;

        int totalWidth = 0;

        for (int i = 0; i < text.Length; ++i)
        {
            if (text[i] == 167)
            {
                ++i;
            }
            else
            {
                int charIndex = ChatAllowedCharacters.allowedCharacters.IndexOf(text[i]);
                if (charIndex >= 0)
                {
                    totalWidth += _charWidth[charIndex + 32];
                }
            }
        }

        return totalWidth;
    }

    private int GetStringFitLength(ReadOnlySpan<char> text, int maxWidth)
    {
        int width = 0;
        int lastSpaceIndex = -1;
        int i = 0;
        for (; i < text.Length; ++i)
        {
            if (text[i] == 167)
            {
                ++i;
                continue;
            }
            if (text[i] == ' ')
            {
                lastSpaceIndex = i;
            }

            int charIndex = ChatAllowedCharacters.allowedCharacters.IndexOf(text[i]);
            if (charIndex >= 0)
            {
                width += _charWidth[charIndex + 32];
            }

            if (width > maxWidth)
            {
                if (lastSpaceIndex > 0)
                {
                    return lastSpaceIndex;
                }
                return Math.Max(1, i);
            }
        }
        return text.Length;
    }

    private void ProcessWrappedText(ReadOnlySpan<char> text, int x, int y, int maxWidth, uint color, bool draw, ref int outHeight)
    {
        if (text.IsEmpty) return;

        int totalHeight = 0;
        int currentY = y;

        while (text.Length > 0)
        {
            int newlineIndex = text.IndexOf('\n');
            ReadOnlySpan<char> line;
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
                int fitLength = GetStringFitLength(line, maxWidth);
                ReadOnlySpan<char> subline = line.Slice(0, Math.Min(fitLength, line.Length));
                
                while(subline.Length > 0 && subline[subline.Length - 1] == ' ')
                {
                    subline = subline.Slice(0, subline.Length - 1);
                }

                if (subline.Length > 0 || fitLength > 0)
                {
                    if (draw && subline.Length > 0)
                    {
                        DrawString(subline, x, currentY, color);
                    }
                    currentY += 8;
                    totalHeight += 8;
                }

                line = line.Slice(Math.Min(fitLength, line.Length));
                while(line.Length > 0 && line[0] == ' ')
                {
                    line = line.Slice(1);
                }
            }
        }
        
        if (totalHeight < 8) totalHeight = 8;
        outHeight = totalHeight;
    }

    public void DrawStringWrapped(ReadOnlySpan<char> text, int x, int y, int maxWidth, uint color)
    {
        int dummyHeight = 0;
        ProcessWrappedText(text, x, y, maxWidth, color, true, ref dummyHeight);
    }

    public int GetStringHeight(ReadOnlySpan<char> text, int maxWidth)
    {
        int height = 0;
        ProcessWrappedText(text, 0, 0, maxWidth, 0, false, ref height);
        return height;
    }
}
