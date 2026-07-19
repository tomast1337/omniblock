using BetaSharp.Client.Options;
using BetaSharp.Client.Rendering.Core;
using Silk.NET.Maths;

namespace BetaSharp.Client.Rendering.UI;

public sealed class UIShader : IDisposable
{
    private readonly ShaderOptionSet _options;
    private Shader _shader;

    public uint ProgramId => _shader.ProgramId;

    public UIShader(GameOptions gameOptions)
    {
        _options = gameOptions.ShaderOptions.GetOrCreate("ui");
        _shader = Build();
        _options.Changed += Rebuild;
    }

    public void SetProjection(Matrix4X4<float> proj) => _shader.SetUniformMatrix4("u_Projection", proj);
    public void SetUseTexture(bool use) => _shader.SetUniform1("u_UseTexture", use ? 1 : 0);

    private Shader Build()
    {
        string rawVert = AssetManager.Instance.getAsset("shaders/ui.vert").GetTextContent();
        string rawFrag = AssetManager.Instance.getAsset("shaders/ui.frag").GetTextContent();

        _options.Parse(rawVert);
        _options.Parse(rawFrag);

        Shader shader = new(_options.Inject(rawVert), _options.Inject(rawFrag));
        shader.Bind();
        shader.SetUniform1("u_Texture", 0);
        return shader;
    }

    private void Rebuild()
    {
        Shader newShader = Build();
        _shader.Dispose();
        _shader = newShader;
    }

    public void Dispose()
    {
        _options.Changed -= Rebuild;
        _shader.Dispose();
    }
}
