using BetaSharp.Client.Options;
using BetaSharp.Client.Rendering.Core;
using Silk.NET.Maths;

namespace BetaSharp.Client.Rendering.UI;

public sealed class UIShader : IDisposable
{
    private readonly Shader _shader;

    public uint ProgramId => _shader.ProgramId;

    public UIShader(GameOptions gameOptions)
    {
        _shader = new Shader(gameOptions.ShaderOptions.GetOrCreate("ui"), "shaders/ui.vert", "shaders/ui.frag");
        _shader.Changed += OnShaderBuilt;
    }

    private static void OnShaderBuilt(Shader shader)
    {
        shader.Bind();
        shader.SetCommonUniforms(GameRenderer.ShaderInfo);
        shader.SetUniform1("u_Texture", 0);
    }

    public void SetProjection(Matrix4X4<float> proj) => _shader.SetUniformMatrix4("u_Projection", proj);
    public void SetUseTexture(bool use) => _shader.SetUniform1("u_UseTexture", use ? 1 : 0);
    public void SetTextureId(int id) => _shader.SetUniform1("u_TextureId", id);

    public void Dispose()
    {
        _shader.Dispose();
    }
}
