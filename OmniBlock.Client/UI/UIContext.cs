using OmniBlock.Client.Input;
using OmniBlock.Client.Options;
using OmniBlock.Client.Rendering;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Rendering.UI;
using OmniBlock.Registries;
using OmniBlock.Client.UI.Screens;
using Silk.NET.Maths;

namespace OmniBlock.Client.UI;

public sealed class UIContext(
    GameOptions options,
    TextRenderer textRenderer,
    UIBatchRenderer batchRenderer,
    TextureManager textureManager,
    TextureHandle terrainTexture,
    TextureHandle itemsTexture,
    Action playClickSound,
    Func<Vector2D<int>> displaySize,
    Func<Vector2D<int>> inputDisplaySize,
    IControllerState controllerState,
    VirtualCursor virtualCursor,
    Timer timer,
    IScreenNavigator navigator,
    Func<bool> hasWorld,
    Func<Vector2D<int>> mouseOffset,
    Func<Vector2D<int>>? renderTargetSize,
    ContentRuntime content
)
{
    public GameOptions Options => options;
    public TextRenderer TextRenderer => textRenderer;
    public UIBatchRenderer UiBatchRenderer => batchRenderer;
    public TextureManager TextureManager => textureManager;
    public TextureHandle TerrainTexture => terrainTexture;
    public TextureHandle ItemsTexture => itemsTexture;
    public Action PlayClickSound => playClickSound;
    public VirtualCursor VirtualCursor => virtualCursor;
    public Timer Timer => timer;
    public IScreenNavigator Navigator => navigator;
    public bool HasWorld => hasWorld();

    public int DisplayWidth => displaySize().X;
    public int DisplayHeight => displaySize().Y;

    public Func<Vector2D<int>> DisplaySize => displaySize;

    /// <summary>Pixel offset to subtract from raw mouse event coordinates.</summary>
    public Vector2D<int> MouseOffset => mouseOffset?.Invoke() ?? Vector2D<int>.Zero;

    /// <summary>
    ///     The display dimensions used for input scaling. Normally equals <see cref="DisplayWidth" />/
    ///     <see cref="DisplayHeight" />,
    ///     but in debug viewport mode returns the viewport size so that click coordinates match the render coordinate space.
    /// </summary>
    public Vector2D<int> InputDisplaySize => inputDisplaySize?.Invoke() ?? displaySize();

    /// <summary>
    ///     Pixel size of the framebuffer the UI is currently drawing into. This is the window's
    ///     framebuffer normally, but the smaller offscreen FBO while the F3 overlay hosts the game in
    ///     an ImGui viewport. Scissor rectangles are relative to the bound draw buffer, so they must be
    ///     expressed in this space rather than in window pixels.
    /// </summary>
    public Vector2D<int> RenderTargetSize => renderTargetSize?.Invoke()
                                             ?? new Vector2D<int>(Display.getFramebufferWidth(), Display.getFramebufferHeight());

    public IControllerState ControllerState => controllerState;
    public ContentRuntime Content => content;
}
