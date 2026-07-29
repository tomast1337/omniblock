using BetaSharp.Client.Options;
using Microsoft.Extensions.Logging;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace BetaSharp.Client.Rendering.Core;

public class Shader : IDisposable
{
    private readonly ILogger<Shader> _logger = Log.Instance.For<Shader>();
    private uint _id;
    private readonly Dictionary<string, int> _uniformLocations = [];

    public uint ProgramId => _id;

    private int _fogModeLoc;
    private int _fogLoc;
    private int _fogColorLoc;
    private int _timeLoc;

    private readonly string? _vertexShaderPath;
    private readonly string _fragmentShaderPath;
    private readonly ShaderOptionSet _options;

    private Action<Shader>? _changed;

    /// <summary>Fires after every (re)build, including once synchronously on subscribe, so subscribers can refresh their own cached uniform locations.</summary>
    public event Action<Shader>? Changed
    {
        add
        {
            _changed += value;
            value?.Invoke(this);
        }
        remove => _changed -= value;
    }

    public Shader(ShaderOptionSet optionSet, string? vertexShaderPath, string fragmentShaderPath)
    {
        _options = optionSet;
        _vertexShaderPath = vertexShaderPath;
        _fragmentShaderPath = fragmentShaderPath;

        BuildShader();

        optionSet.Changed += OnOptionsChanged;
    }

    private void OnOptionsChanged(ShaderOptionSet _) => BuildShader();

    private void BuildShader()
    {
        IGL gl = GLManager.GL;

        uint vertexShader = gl.CreateShader(ShaderType.VertexShader);
        if (_vertexShaderPath == null)
        {
            string vertexShaderSource = AssetManager.Instance.getAsset("shaders/quad.vert").GetTextContent();
            gl.ShaderSource(vertexShader, vertexShaderSource);
        }
        else
        {
            string vertexShaderSource = AssetManager.Instance.getAsset(_vertexShaderPath).GetTextContent();
            _options.Parse(vertexShaderSource);
            gl.ShaderSource(vertexShader, _options.Inject(vertexShaderSource));
        }
        gl.CompileShader(vertexShader);
        CheckShaderCompilation(vertexShader, "Vertex");

        string fragmentShaderSource = AssetManager.Instance.getAsset(_fragmentShaderPath).GetTextContent();
        uint fragmentShader = gl.CreateShader(ShaderType.FragmentShader);
        _options.Parse(fragmentShaderSource);
        gl.ShaderSource(fragmentShader, _options.Inject(fragmentShaderSource));
        gl.CompileShader(fragmentShader);
        CheckShaderCompilation(fragmentShader, "Fragment");

        uint newId = gl.CreateProgram();
        gl.AttachShader(newId, vertexShader);
        gl.AttachShader(newId, fragmentShader);
        gl.LinkProgram(newId);
        CheckProgramLinking(newId);

        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);

        if (_id != 0)
        {
            gl.DeleteProgram(_id);
        }

        _id = newId;
        _uniformLocations.Clear();

        _fogModeLoc = GetUniformLocationNoCache("fogMode");
        _fogLoc = GetUniformLocationNoCache("fog");
        _fogColorLoc = GetUniformLocationNoCache("fogColor");
        _timeLoc = GetUniformLocationNoCache("time");

        _changed?.Invoke(this);
    }

    public void Bind()
    {
        GLManager.GL.UseProgram(_id);
    }

    public void SetCommonUniforms(CommonShaderInfo info)
    {
        GLManager.GL.Uniform1(_fogModeLoc, info.FogMode);
        GLManager.GL.Uniform3(_fogLoc, info.FogStart, info.FogEnd, info.FogDensity);
        GLManager.GL.Uniform4(_fogColorLoc, info.FogColor.X, info.FogColor.Y, info.FogColor.Z, info.FogColor.W);
        GLManager.GL.Uniform3(_timeLoc, info.Time, info.DeltaTime, info.DayTime);
    }

    public void SetUniform3(string name, Vector3D<float> vec)
    {
        int location = GetUniformLocation(name);
        GLManager.GL.Uniform3(location, vec.X, vec.Y, vec.Z);
    }

    public void SetUniform1(string name, float value)
    {
        int location = GetUniformLocation(name);
        GLManager.GL.Uniform1(location, value);
    }

    public void SetUniform1(string name, int value)
    {
        int location = GetUniformLocation(name);
        GLManager.GL.Uniform1(location, value);
    }

    public void SetUniform2(string name, float x, float y)
    {
        int location = GetUniformLocation(name);
        GLManager.GL.Uniform2(location, x, y);
    }

    public void SetUniformMatrix4(string name, Matrix4X4<float> matrix)
    {
        int location = GetUniformLocation(name);
        unsafe
        {
            GLManager.GL.UniformMatrix4(location, 1, false, (float*)&matrix);
        }
    }

    public void SetUniformMatrix4(string name, float[] matrix)
    {
        int location = GetUniformLocation(name);
        unsafe
        {
            fixed (float* mat = matrix)
            {
                GLManager.GL.UniformMatrix4(location, 1, false, mat);
            }
        }
    }

    public void SetUniform4(string name, Vector4D<float> vec)
    {
        int location = GetUniformLocation(name);
        GLManager.GL.Uniform4(location, vec.X, vec.Y, vec.Z, vec.W);
    }

    private int GetUniformLocationNoCache(string name) => GLManager.GL.GetUniformLocation(_id, name);

    public int GetUniformLocation(string name)
    {
        if (_uniformLocations.TryGetValue(name, out int location))
        {
            return location;
        }

        location = GLManager.GL.GetUniformLocation(_id, name);
        _uniformLocations[name] = location;

        if (location == -1)
        {
            _logger.LogWarning($"Warning: Uniform '{name}' not found in shader");
        }

        return location;
    }

    private static void CheckShaderCompilation(uint shader, string type)
    {
        IGL gl = GLManager.GL;
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int success);
        if (success == 0)
        {
            string infoLog = gl.GetShaderInfoLog(shader);
            throw new Exception($"{type} shader compilation failed:\n{infoLog}");
        }
    }

    private static void CheckProgramLinking(uint program)
    {
        IGL gl = GLManager.GL;
        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int success);
        if (success == 0)
        {
            string infoLog = gl.GetProgramInfoLog(program);
            throw new Exception($"Shader program linking failed:\n{infoLog}");
        }
    }

    public void Dispose()
    {
        _options.Changed -= OnOptionsChanged;
        GLManager.GL.DeleteProgram(_id);
        GC.SuppressFinalize(this);
    }
}
