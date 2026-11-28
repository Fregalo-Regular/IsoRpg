using System.Numerics;
using Engine.Assets;
using Silk.NET.OpenGL;

namespace Engine.Graphics;

public sealed class ShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly string _name;
    private readonly string _vertexPath;
    private readonly string _fragmentPath;
    private readonly Dictionary<string, int> _locations = new();
    public bool CacheLocations { get; set; } = true;

    public uint Handle { get; private set; }

    public ShaderProgram(GL gl, string vertexPath, string fragmentPath)
    {
        _gl = gl;
        _vertexPath = vertexPath;
        _fragmentPath = fragmentPath;
        _name = Path.GetFileNameWithoutExtension(vertexPath);
        Handle = Build();
    }

    /// Делает эту программу активной. Все SetUniform и Draw работают с активной.
    public void Use() => _gl.UseProgram(Handle);

    public void SetUniform(string name, float value)
    {
        int location = _gl.GetUniformLocation(Handle, name);
        // location < 0 — нормально: компилятор выкидывает неиспользуемые uniform
        if (location >= 0) _gl.Uniform1(location, value);
    }

    public void SetUniform(string name, Vector3 value)
    {
        //int location = _gl.GetUniformLocation(Handle, name);
        int location = Location(name); 
        if (location >= 0) _gl.Uniform3(location, value.X, value.Y, value.Z);
    }

    public unsafe void SetUniform(string name, Matrix4x4 value)
    {
        int location = Location(name); 
        if (location >= 0) _gl.UniformMatrix4(location, 1, false, (float*)&value);
    }

    /// Пересобирает шейдер с диска. При ошибке оставляет старую рабочую версию.
    public bool Reload()
    {
        uint old = Handle;
        try
        {
            Handle = Build();
            _gl.DeleteProgram(old);
            Console.WriteLine($"[Shader] {_name}: перезагружен");
            return true;
        }
        catch (Exception ex)
        {
            Handle = old;
            _locations.Clear();
            Console.WriteLine($"[Shader] {_name}: ОШИБКА, оставлена старая версия\n{ex.Message}");
            return false;
        }
    }

    private uint Build()
    {
        uint vs = Compile(ShaderType.VertexShader, _vertexPath);
        uint fs = 0, program = 0;
        try
        {
            fs = Compile(ShaderType.FragmentShader, _fragmentPath);

            program = _gl.CreateProgram();
            _gl.AttachShader(program, vs);
            _gl.AttachShader(program, fs);
            _gl.LinkProgram(program);

            _gl.GetProgram(program, GLEnum.LinkStatus, out int status);
            if (status != (int)GLEnum.True)
                throw new InvalidOperationException(
                    $"Линковка '{_name}': {_gl.GetProgramInfoLog(program)}");

            return program;
        }
        catch
        {
            if (program != 0) _gl.DeleteProgram(program);
            throw;
        }
        finally
        {
            // после линковки исходники больше не нужны, программа держит их сама
            if (vs != 0) _gl.DeleteShader(vs);
            if (fs != 0) _gl.DeleteShader(fs);
        }
    }

    private uint Compile(ShaderType type, string path)
    {
        string full = AssetPath.Resolve(path);
        string source = File.ReadAllText(AssetPath.Resolve(path));

        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, GLEnum.CompileStatus, out int status);
        if (status == (int)GLEnum.True) return shader;

        string log = _gl.GetShaderInfoLog(shader);
        _gl.DeleteShader(shader);
        throw new InvalidOperationException($"{type} '{full}':\n{log}");
    }

    private int Location(string name)
    {
        if (CacheLocations && _locations.TryGetValue(name, out int cached)) return cached;
        int loc = _gl.GetUniformLocation(Handle, name);
        if (CacheLocations) _locations[name] = loc;
        return loc;
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private void CheckProgram(string name)
    {
        foreach (var n in new[] { "uViewProjection", "uModel", "uJoints[0]", "uSkinned",
                          "uTint", "uUseTexture", "uDebugNormals", "uLightDir" })
        Console.WriteLine($"{n} = {Location(n)}");
    }

    public void SetUniform(string name, int value)
    {
        //CheckProgram(name);
        int location = Location(name);
        if (location >= 0) _gl.Uniform1(location, value);
    }

    public unsafe void SetUniformMatrixArray(string name, Matrix4x4[] matrices)
    {
        int loc = Location(name);
        if (loc < 0) return;
        fixed (Matrix4x4* p = matrices)
            _gl.UniformMatrix4(loc, (uint)matrices.Length, false, (float*)p);
    }

    // Без финализатора намеренно: GL-вызовы можно делать только с того потока,
    // где живёт контекст. Финализатор работает в другом потоке и уронит процесс.
    public void Dispose() => _gl.DeleteProgram(Handle);
}