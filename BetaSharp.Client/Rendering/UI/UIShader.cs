using BetaSharp.Client.Rendering.Core;
using Silk.NET.Maths;

namespace BetaSharp.Client.Rendering.UI;

public sealed class UIShader : IDisposable
{
    private readonly Shader _shader;

    public uint ProgramId => _shader.ProgramId;

    public UIShader()
    {
        _shader = new Shader(
            AssetManager.Instance.getAsset("shaders/ui.vert").GetTextContent(),
            AssetManager.Instance.getAsset("shaders/ui.frag").GetTextContent());
        _shader.Bind();
        _shader.SetUniform1("u_Texture", 0);
    }

    public void SetProjection(Matrix4X4<float> proj) => _shader.SetUniformMatrix4("u_Projection", proj);
    public void SetUseTexture(bool use) => _shader.SetUniform1("u_UseTexture", use ? 1 : 0);

    public void Dispose() => _shader.Dispose();
}
