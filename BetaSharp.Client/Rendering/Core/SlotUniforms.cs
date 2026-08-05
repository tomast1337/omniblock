using Silk.NET.Maths;

namespace BetaSharp.Client.Rendering.Core;

/// <summary>The state more than one slot's program needs uploaded, and the state none of them may inherit.</summary>
internal static class SlotUniforms
{
    /// <summary>The transforms every slot draws under, in the names the shaders declare them by.</summary>
    public static void UploadTransforms(Shader shader)
    {
        shader.SetUniformMatrix4("modelViewMatrix", GLManager.ModelView.Top);
        shader.SetUniformMatrix4("projectionMatrix", GLManager.Projection.Top);
    }

    /// <summary>The fog and the alpha threshold, which every slot has to carry for itself.</summary>
    public static void UploadFogAndAlpha(Shader shader)
    {
        FogState fog = GLManager.Fog;

        shader.SetUniform1("alphaThreshold", GLManager.EffectiveAlphaThreshold);
        shader.SetUniform1("shadeModel", (int)GLManager.ShadeModel);
        shader.SetUniform1("fogEnabled", GLManager.FogEnabled ? 1 : 0);
        shader.SetUniform1("fogMode", (int)fog.Curve);
        shader.SetUniform1("fogStart", fog.Start);
        shader.SetUniform1("fogEnd", fog.End);
        shader.SetUniform1("fogDensity", fog.Density);
        shader.SetUniform4("fogColor", fog.Color);
    }

    /// <summary>
    ///     How dark the sky channel is right now and where the brightness curve floors out, which a
    ///     shader needs to turn a vertex's light levels into a brightness.
    /// </summary>
    /// <remarks>
    ///     Ambient, like the fog, and for the same reason: it belongs to the world rather than to
    ///     any one draw, and it moves per tick. A draw with no world behind it -- the main menu,
    ///     the inventory -- reads the defaults, under which full block light is still full brightness.
    /// </remarks>
    public static void UploadWorldLight(Shader shader)
    {
        WorldLightState light = GLManager.WorldLight;

        shader.SetUniform1("ambientDarkness", light.AmbientDarkness);
        shader.SetUniform1("luminanceOffset", light.LuminanceOffset);
    }

    /// <summary>The two directional lights, the ambient term, and the matrix normals arrive under.</summary>
    /// <remarks>
    ///     <para>
    ///         The normal matrix is the upper-left of the inverse-transpose of the model-view, and it
    ///         is built here rather than asked of <c>GLManager</c> because nothing else wants it. It
    ///         is only worth computing when something will read it, hence the branch — a 4x4 inverse
    ///         per draw is not free, and an unlit draw's normals go nowhere.
    ///     </para>
    ///     <para>
    ///         An unlit draw still gets <c>lightingEnabled</c> written, so a program left bound from
    ///         a lit draw cannot shade the next one by inheritance.
    ///     </para>
    /// </remarks>
    public static void UploadLighting(Shader shader)
    {
        bool enabled = GLManager.LightingEnabled;
        shader.SetUniform1("lightingEnabled", enabled ? 1 : 0);
        if (!enabled)
        {
            return;
        }

        LightingState lighting = GLManager.Lighting;
        shader.SetUniform3("light0Direction", lighting.Light0Direction);
        shader.SetUniform3("light0Diffuse", lighting.Light0Diffuse);
        shader.SetUniform3("light1Direction", lighting.Light1Direction);
        shader.SetUniform3("light1Diffuse", lighting.Light1Diffuse);
        shader.SetUniform3("ambientLight", lighting.Ambient);

        Matrix3X3<float> normalMatrix = Matrix3X3<float>.Identity;
        if (Matrix4X4.Invert(GLManager.ModelView.Top, out Matrix4X4<float> inverse))
        {
            Matrix4X4<float> transposed = Matrix4X4.Transpose(inverse);
            normalMatrix = new Matrix3X3<float>(
                transposed.M11, transposed.M12, transposed.M13,
                transposed.M21, transposed.M22, transposed.M23,
                transposed.M31, transposed.M32, transposed.M33);
        }

        shader.SetUniformMatrix3("normalMatrix", normalMatrix);
    }
}
