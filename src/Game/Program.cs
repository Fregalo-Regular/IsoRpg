using System.Numerics;
using Engine.Diagnostics;
using Engine.Graphics;
using Engine.Platform;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Engine.Assets;
//using Mesh = Engine.Graphics.Mesh;

namespace Game;

internal sealed class MyGame : EngineHost
{
    private ShaderProgram _shader = null!;
    private Mesh _quad = null!;
    private Texture2D _tile = null!;
    private IsoCamera _camera = null!;
    private ImGuiLayer _imgui = null!;
    private IKeyboard _kb = null!;
    private IMouse _mouse = null!;

    private Vector3 _clearColor = new(0.07f, 0.08f, 0.10f);
    private bool _wireframe, _cull = true, _depth = true;
    private TexFilter _filter = TexFilter.Trilinear;
    private int _grid = 32;
    private int _drawCalls;
    private const float Tile = 2f;
    private GpuProfiler _gpuWorld = null!, _gpuUi = null!;
    private double _cpuMs, _cpuMsAvg;
    private float _manScale = 1f, _foxScale = 1f, _lightAzimuth = 140f;
    private Vector3 _lightDir = Vector3.Normalize(new Vector3(-0.4f, 1.2f, 0.3f));
    private bool _debugNormals;
    private GltfModel _man = null!, _fox = null!;
    private float _animTime;
    private bool _play = true;
    private int _animIndex;

    protected override void OnLoad()
    {
        _kb = Input.Keyboards[0];
        _mouse = Input.Mice[0];
        _kb.KeyDown += (_, key, _) => { if (key == Key.Escape) Exit(); };
        _mouse.Scroll += (_, wheel) =>
        {
            if (_imgui.WantMouse) return;
            _camera.ZoomAt(MouseScreen, MathF.Pow(1.15f, -wheel.Y));
        };

        _gpuWorld = new GpuProfiler(Gl);
        _gpuUi   = new GpuProfiler(Gl);

        _camera = new IsoCamera(FramebufferSize);
        _imgui = new ImGuiLayer(Gl, Input);
        _shader = new ShaderProgram(Gl, "assets/shaders/basic.vert", "assets/shaders/basic.frag");
        _tile = Texture2D.FromRgba(Gl, 64, 64, MakeTileRgba(64));

        // Квад лежит в XZ, нормаль +Y (см. вычисление winding выше)
        Vertex[] vertices =
        [
            new() { Position = new(-0.5f, 0f,  0.5f), Color = Vector3.UnitY, UV = new(0f, 0f) },
            new() { Position = new( 0.5f, 0f,  0.5f), Color = Vector3.UnitY, UV = new(1f, 0f) },
            new() { Position = new( 0.5f, 0f, -0.5f), Color = Vector3.UnitY, UV = new(1f, 1f) },
            new() { Position = new(-0.5f, 0f, -0.5f), Color = Vector3.UnitY, UV = new(0f, 1f) },
        ];
        ushort[] indices = [0, 1, 2, 2, 3, 0];
        _quad = new Mesh(Gl, vertices, indices);

        _man = GltfLoader.Load(Gl, "assets/models/CesiumMan.glb");
        _fox = GltfLoader.Load(Gl, "assets/models/Fox.glb");
    }

    private Vector2 MouseScreen => new(_mouse.Position.X, _mouse.Position.Y);

    protected override void OnUpdate(double dt)
    {
        if (_imgui.WantKeyboard) return;
        float f = (float)dt;

        var move = Vector3.Zero;
        if (_kb.IsKeyPressed(Key.W)) move += _camera.Forward;
        if (_kb.IsKeyPressed(Key.S)) move -= _camera.Forward;
        if (_kb.IsKeyPressed(Key.D)) move += _camera.Right;
        if (_kb.IsKeyPressed(Key.A)) move -= _camera.Right;
        if (move != Vector3.Zero)
            _camera.Target += Vector3.Normalize(move) * (_camera.ViewHeight * 0.8f) * f;

        if (_kb.IsKeyPressed(Key.Q)) _camera.Yaw -= 1.2f * f;
        if (_kb.IsKeyPressed(Key.E)) _camera.Yaw += 1.2f * f;
        if (_play) _animTime += (float)dt;
    }

    protected override void OnResize(Vector2D<int> size) => _camera?.SetViewport(size);

    protected override void OnRender(double dt)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

        Gl.ClearColor(_clearColor.X, _clearColor.Y, _clearColor.Z, 1f);
        Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // Объявляем своё состояние каждый кадр — ImGui оставил контекст в своём виде
        if (_depth) Gl.Enable(EnableCap.DepthTest); else Gl.Disable(EnableCap.DepthTest);
        Gl.DepthFunc(DepthFunction.Less);
        if (_cull) Gl.Enable(EnableCap.CullFace); else Gl.Disable(EnableCap.CullFace);
        Gl.CullFace(TriangleFace.Back);
        Gl.FrontFace(FrontFaceDirection.Ccw);
        Gl.PolygonMode(TriangleFace.FrontAndBack, _wireframe ? PolygonMode.Line : PolygonMode.Fill);

        _gpuWorld.Begin();
        _tile.SetFilter(_filter);
        _tile.Bind(TextureUnit.Texture0);
        _shader.Use();

        _tile.SetFilter(_filter);
        _tile.Bind(TextureUnit.Texture0);

        _shader.Use();
        _shader.SetUniform("uDebugNormals", _debugNormals ? 1 : 0);
        _shader.SetUniform("uTexture", 0);
        _shader.SetUniform("uUvScale", 1f);
        _shader.SetUniform("uLightDir", _lightDir);
        _shader.SetUniform("uViewProjection", _camera.ViewProjection);

        _shader.SetUniform("uUseTexture", 1);
        _shader.SetUniform("uSkinned", 0);

        _drawCalls = 0;
        float half = (_grid - 1) / 2f;
        for (int z = 0; z < _grid; z++)
        for (int x = 0; x < _grid; x++)
        {
            DrawTile((x - half) * Tile, Terrace(x, z), (z - half) * Tile,
                     (x + z) % 2 == 0 ? Vector3.One : new Vector3(0.72f, 0.76f, 0.85f));
        }

        // Курсор: тайл под мышью. Это уже почти click-to-move.
        var hit = _camera.ScreenToGround(MouseScreen);
        int hx = (int)MathF.Round(hit.X / Tile + half);
        int hz = (int)MathF.Round(hit.Z / Tile + half);
        if (hx >= 0 && hx < _grid && hz >= 0 && hz < _grid)
            DrawTile((hx - half) * Tile, Terrace(hx, hz) + 0.01f, (hz - half) * Tile,
                     new Vector3(1.6f, 1.3f, 0.6f));

        _shader.SetUniform("uUseTexture", 0);
        if (_man.Skeleton is { } sk && sk.AnimationCount > 0)
        {
            if (_play) sk.Play(_animIndex, _animTime);
            // если на паузе — поза уже установлена скрабом ниже
        }
        DrawModel(_man, new Vector3(-6f, 0f, 0f), _manScale);
        DrawModel(_fox, new Vector3( 6f, 0f, 0f), _foxScale);
        _shader.SetUniform("uSkinned", 0);

        _gpuWorld.End();

        _gpuUi.Begin();
        //DrawDebugUi(dt, hit);
        _gpuUi.End();

        _cpuMs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        _cpuMsAvg += (_cpuMs - _cpuMsAvg) * (1.0 - Math.Exp(-10.0 * dt));   // dt-независимое сглаживание

        DrawDebugUi(dt, hit);
    }

    private void DrawTile(float x, float y, float z, Vector3 tint)
    {
        _shader.SetUniform("uModel",
            Matrix4x4.CreateScale(Tile, 1f, Tile) * Matrix4x4.CreateTranslation(x, y, z));
        _shader.SetUniform("uTint", tint);
        _quad.Draw();
        _drawCalls++;
    }

    /// Ступенчатая пирамида в центре — чтобы было видно работу depth buffer.
    private float Terrace(int x, int z)
    {
        int d = Math.Max(Math.Abs(x - _grid / 2), Math.Abs(z - _grid / 2));
        return Math.Max(0, 3 - d) * 0.4f;
    }

    private void DrawDebugUi(double dt, Vector3 hit)
    {
        _imgui.BeginFrame(dt, WindowSize, FramebufferSize);

        ImGui.Begin("Debug");
        ImGui.Text($"FPS: {ImGui.GetIO().Framerate:F1}   Draw calls: {_drawCalls}");
        ImGui.Checkbox("Wireframe", ref _wireframe);
        ImGui.SameLine(); ImGui.Checkbox("Cull back", ref _cull);
        ImGui.SameLine(); ImGui.Checkbox("Depth test", ref _depth);

        ImGui.Separator();
        float yawDeg = _camera.Yaw * 180f / MathF.PI;
        float pitchDeg = _camera.Pitch * 180f / MathF.PI;
        if (ImGui.SliderFloat("Yaw", ref yawDeg, 0f, 360f)) _camera.Yaw = yawDeg * MathF.PI / 180f;
        if (ImGui.SliderFloat("Pitch", ref pitchDeg, 10f, 80f)) _camera.Pitch = pitchDeg * MathF.PI / 180f;
        if (ImGui.Button("True iso 45/35.26")) { _camera.Yaw = MathF.PI / 4f; _camera.Pitch = MathF.Atan(1f / MathF.Sqrt(2f)); }
        ImGui.SameLine();
        if (ImGui.Button("D2 45/26.57")) { _camera.Yaw = MathF.PI / 4f; _camera.Pitch = MathF.Atan(0.5f); }
        bool vs = VSync;
        if (ImGui.Checkbox("VSync", ref vs)) VSync = vs;
        ImGui.Text($"CPU submit: {_cpuMsAvg:F2} ms");
        ImGui.Text($"GPU world:  {_gpuWorld.LastMs:F2} ms  ({_drawCalls} draws)");
        ImGui.Text($"GPU imgui:  {_gpuUi.LastMs:F2} ms");
        if (ImGui.Button("Toggle uniform cache")) _shader.CacheLocations = !_shader.CacheLocations;
        ImGui.Checkbox("Debug normals", ref _debugNormals);
        _shader.SetUniform("uDebugNormals", _debugNormals ? 1 : 0);

        ImGui.Separator();
        ImGui.SliderFloat("Man scale", ref _manScale, 0.01f, 5f);
        ImGui.InputFloat("Man scale exact", ref _manScale);
        ImGui.SliderFloat("Fox scale", ref _foxScale, 0.01f, 5f);
        ImGui.InputFloat("Fox scale exact", ref _foxScale);
        if (ImGui.SliderFloat("Light yaw", ref _lightAzimuth, 0f, 360f))
        {
            float a = _lightAzimuth * MathF.PI / 180f;
            _lightDir = Vector3.Normalize(new Vector3(MathF.Cos(a), 1.2f, MathF.Sin(a)));
        }

        ImGui.SliderFloat("View height", ref _camera.ViewHeight, 4f, 60f);
        ImGui.SliderInt("Grid", ref _grid, 8, 64);
        ImGui.Text($"Ground hit: {hit}");
        if (ImGui.Button("Reset camera")) { _camera.Target = Vector3.Zero; _camera.ViewHeight = 600f; }

        ImGui.Separator();
        if (ImGui.RadioButton("Nearest", _filter == TexFilter.Nearest)) _filter = TexFilter.Nearest;
        ImGui.SameLine();
        if (ImGui.RadioButton("Linear", _filter == TexFilter.Linear)) _filter = TexFilter.Linear;
        ImGui.SameLine();
        if (ImGui.RadioButton("Trilinear", _filter == TexFilter.Trilinear)) _filter = TexFilter.Trilinear;

        
        if (_man.Skeleton is { } s && s.AnimationCount > 0)
        {
            ImGui.Checkbox("Play", ref _play);
            float dur = s.Duration(_animIndex);
            float t = _animTime % dur;
            if (ImGui.SliderFloat("Anim time", ref t, 0f, dur))
            { _play = false; _animTime = t; s.Play(_animIndex, t); }
            ImGui.Text($"duration {dur:F2}s");
        }
        ImGui.End();

        _imgui.EndFrame(FramebufferSize);
    }

    private static byte[] MakeTileRgba(int size)
    {
        var data = new byte[size * size * 4];
        var rng = new Random(12345);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int i = (y * size + x) * 4;
            bool border = x < 2 || y < 2 || x >= size - 2 || y >= size - 2;
            float v = (border ? 0.18f : 0.42f) + (float)rng.NextDouble() * 0.06f
                    + ((x + y) % 8 == 0 ? 0.04f : 0f);
            data[i + 0] = (byte)(Math.Clamp(v * 0.95f, 0f, 1f) * 255f);
            data[i + 1] = (byte)(Math.Clamp(v * 1.00f, 0f, 1f) * 255f);
            data[i + 2] = (byte)(Math.Clamp(v * 1.10f, 0f, 1f) * 255f);
            data[i + 3] = 255;
        }
        return data;
    }

    protected override void OnShutdown()
    {
        _imgui.Dispose(); _tile.Dispose(); _quad.Dispose(); _shader.Dispose(); _gpuWorld.Dispose(); _gpuUi.Dispose();
    }

    private void DrawModel(GltfModel model, Vector3 position, float scale)
    {
        if (model.Primitives.Count == 0) return;

        if (model.Skeleton is { } sk)
        {
            _shader.SetUniform("uSkinned", 1);
            _shader.SetUniformMatrixArray("uJoints[0]", sk.Palette);
        }
        else
        {
            _shader.SetUniform("uSkinned", 0);
        }

        var scaleM = Matrix4x4.CreateScale(scale);

        // Для скиннинг-меша Transform = Identity, и сырые габариты лежат в пространстве меша
        // (у CesiumMan — Z-up). Ориентацию даёт палитра. В bind pose она одинакова у всех
        // костей, поэтому Palette[0] работает как матрица меш→мир.
        float lowest = float.MaxValue;
        foreach (var p in model.Primitives)
        {
            var basis = model.Skeleton is { } s ? s.Palette[0] : p.Transform;
            lowest = MathF.Min(lowest, LowestY(p.BoundsMin, p.BoundsMax, basis * scaleM));
        }

        foreach (var p in model.Primitives)
        {
            if (p.Texture != null) { p.Texture.Bind(TextureUnit.Texture0); _shader.SetUniform("uUseTexture", 1); }
            else _shader.SetUniform("uUseTexture", 0);

            var m = p.Transform * scaleM
                  * Matrix4x4.CreateTranslation(position.X, position.Y - lowest, position.Z);
            _shader.SetUniform("uModel", m);
            _shader.SetUniform("uTint", Vector3.One);
            p.Mesh.Draw();
            _drawCalls++;
        }

    }

    /// Нижняя точка AABB после преобразования.
    /// ВАЖНО: трансформируем все 8 углов, а не только min и max.
    private static float LowestY(Vector3 min, Vector3 max, Matrix4x4 m)
    {
        float lowest = float.MaxValue;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? min.X : max.X,
                (i & 2) == 0 ? min.Y : max.Y,
                (i & 4) == 0 ? min.Z : max.Z);
            lowest = MathF.Min(lowest, Vector3.Transform(corner, m).Y);
        }
        return lowest;
    }
}

internal static class Program
{
    private static void Main() => new MyGame().Run("Iso RPG — isometric camera", 1920, 1080);
}