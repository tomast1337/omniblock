using OmniBlock.Client.Rendering.Core;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.Rendering;

public class LoadingScreenRenderer(OmniBlock game) : LoadingDisplay
{
    private string _currentStage = string.Empty;
    private bool _ignoreShutdownCheck;
    private long _lastUpdateMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private string _titleText = string.Empty;

    public void BeginLoadingPersistent(string message)
    {
        _ignoreShutdownCheck = true;
        UpdateLoadingTitle(_titleText);
    }

    public void SetStage(string message)
    {
        if (!game.Running && !_ignoreShutdownCheck)
        {
            throw new OmniBlockShutdownException();
        }

        if (game.Running)
        {
            _lastUpdateMs = 0L;
            _currentStage = message;
            SetProgress(-1);
            _lastUpdateMs = 0L;
        }
    }

    public void SetProgress(int progress)
    {
        if (!game.Running && !_ignoreShutdownCheck)
        {
            throw new OmniBlockShutdownException();
        }

        if (!game.Running) return;

        var currentTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (currentTimeMs - _lastUpdateMs < 20L) return;

        _lastUpdateMs = currentTimeMs;
        ScaledResolution resolution = new(game.Options, game.DisplayWidth, game.DisplayHeight);
        var width = resolution.ScaledWidth;
        var height = resolution.ScaledHeight;

        RenderSystem.Projection.LoadIdentity();
        RenderSystem.Projection.Ortho(0.0, width, height, 0.0, 100.0, 300.0);
        RenderSystem.ModelView.LoadIdentity();
        RenderSystem.ModelView.Translate(0.0f, 0.0f, -200.0f);

        void DrawContents()
        {
            var tessellator = Tessellator.instance;
            var backgroundHandle = game.TextureManager.GetTextureId("/gui/background.png");
            game.TextureManager.BindTexture(backgroundHandle);

            var textureScale = 32.0f;
            tessellator.startDrawingQuads();
            tessellator.setColorOpaque_I(0x404040);
            tessellator.addVertexWithUV(0.0, height, 0.0, 0.0, height / textureScale);
            tessellator.addVertexWithUV(width, height, 0.0, width / textureScale, height / textureScale);
            tessellator.addVertexWithUV(width, 0.0, 0.0, width / textureScale, 0.0);
            tessellator.addVertexWithUV(0.0, 0.0, 0.0, 0.0, 0.0);
            tessellator.draw(ProgramSlot.Textured);

            if (progress >= 0)
            {
                const int progressBarWidth = 100;
                const int progressBarHeight = 2;
                var x = width / 2 - progressBarWidth / 2;
                var y = height / 2 + 16;

                RenderSystem.TextureEnabled = false;
                tessellator.startDrawingQuads();
                tessellator.setColorOpaque_I(0x808080);
                tessellator.addVertex(x, y, 0.0);
                tessellator.addVertex(x, y + progressBarHeight, 0.0);
                tessellator.addVertex(x + progressBarWidth, y + progressBarHeight, 0.0);
                tessellator.addVertex(x + progressBarWidth, y, 0.0);

                tessellator.setColorOpaque_I(0x80FF80);
                tessellator.addVertex(x, y, 0.0);
                tessellator.addVertex(x, y + progressBarHeight, 0.0);
                tessellator.addVertex(x + progress, y + progressBarHeight, 0.0);
                tessellator.addVertex(x + progress, y, 0.0);
                tessellator.draw(ProgramSlot.Basic);
                RenderSystem.TextureEnabled = true;
            }

            var titleX = (width - game.TextRenderer.GetStringWidth(_titleText)) / 2;
            var titleY = height / 2 - 4 - 16;
            game.TextRenderer.DrawStringWithShadow(_titleText, titleX, titleY, Color.White);

            var stageX = (width - game.TextRenderer.GetStringWidth(_currentStage)) / 2;
            var stageY = height / 2 - 4 + 8;
            game.TextRenderer.DrawStringWithShadow(_currentStage, stageX, stageY, Color.White);
        }

        game.WebGpuRenderer.RenderLoadingFrame(DrawContents);

        Display.update();
        Thread.Yield();
    }

    public void BeginLoading(string message)
    {
        _ignoreShutdownCheck = false;
        UpdateLoadingTitle(message);
    }

    public void UpdateLoadingTitle(string message)
    {
        if (!game.Running && !_ignoreShutdownCheck)
        {
            throw new OmniBlockShutdownException();
        }

        if (game.Running)
        {
            _titleText = message;

            ScaledResolution resolution = new(game.Options, game.DisplayWidth, game.DisplayHeight);

            RenderSystem.Projection.LoadIdentity();
            RenderSystem.Projection.Ortho(0.0, resolution.ScaledWidth, resolution.ScaledHeight, 0.0, 100.0, 300.0);
            RenderSystem.ModelView.LoadIdentity();
            RenderSystem.ModelView.Translate(0.0f, 0.0f, -200.0f);
        }
    }
}
