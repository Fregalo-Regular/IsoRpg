using System.Numerics;
using Engine.Diagnostics;
using Engine.Graphics;
using Engine.Platform;
using Engine.Scene;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Engine.Assets;
using Engine.ECS;

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
    private float _manScale = 1f, _foxScale = 0.01f, _lightAzimuth = 140f;
    private Vector3 _lightDir = Vector3.Normalize(new Vector3(-0.4f, 1.2f, 0.3f));
    private bool _debugNormals;
    private GltfModel man = null!, fox = null!;
    private float _animTime;
    private bool _play = true;
    private int _animIndex;
    private bool _follow = true;
    private float _yawOffset;
    private Heightfield _height = null!;
    private readonly List<Obstacle> _obstacles = new();
    private Mesh _crate = null!;
    private float _maxStep = 0.45f, _playerRadius = 0.35f;
    private bool _showColliders = true;
    private bool _lmbHeld;
    private World _world = new();
    private int _player, _fox;

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

        _mouse.MouseDown += (_, b) => { if (b == MouseButton.Left) _lmbHeld = true; };
        _mouse.MouseUp += (_, b) => { if (b == MouseButton.Left) _lmbHeld = false; };

        _gpuWorld = new GpuProfiler(Gl);
        _gpuUi = new GpuProfiler(Gl);

        _camera = new IsoCamera(FramebufferSize);
        _imgui = new ImGuiLayer(Gl, Input);
        _shader = new ShaderProgram(Gl, "assets/shaders/basic.vert", "assets/shaders/basic.frag");
        _tile = Texture2D.FromRgba(Gl, 64, 64, MakeTileRgba(64));

        _height = new Heightfield(_grid, Tile, Terrace);
        _crate = MakeCube(Gl);

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

        man = GltfLoader.Load(Gl, "assets/models/CesiumMan.glb");
        fox = GltfLoader.Load(Gl, "assets/models/Fox.glb");

        _player = _world.Create();
        _world.Add<Transform>(_player) = new Transform { Pos = new Vector3(-6, 0, 0) };
        _world.Add<Movement>(_player) = new Movement { Speed = 3f };
        _world.Add<Collider>(_player) = new Collider { Radius = _playerRadius };
        _world.Add<Renderable>(_player) = new Renderable { Model = man, Scale = _manScale, Tint = Vector3.One };
        _world.Add<PlayerTag>(_player);

        _fox = _world.Create();
        _world.Add<Transform>(_fox) = new Transform { Pos = new Vector3(6, 0, 0) };
        _world.Add<Renderable>(_fox) = new Renderable { Model = fox, Scale = _foxScale, Tint = Vector3.One };

        int m = _world.Create();
        _world.Add<Transform>(m) = new Transform { Pos = new Vector3(0,0,10) };
        _world.Add<Renderable>(m) = new Renderable { Model = fox, Scale = _foxScale, Tint = Vector3.One };

        foreach (var (p, r, h) in new[] {
            (new Vector3(-10,0,4), 0.7f, 1.0f),
            (new Vector3(-2,0,-10), 0.7f, 1.2f),
            (new Vector3(8,0,-6), 0.7f, 0.8f) })
        {
            int e = _world.Create();
            _world.Add<Transform>(e) = new Transform { Pos = p };
            _world.Add<Collider>(e) = new Collider { Radius = r };
            _world.Add<Renderable>(e) = new Renderable { Mesh = _crate, Size = new Vector3(r * 2, h, r * 2), Tint = new Vector3(0.8f, 0.6f, 0.35f) };
            _world.Add<ObstacleTag>(e);
        }
        // лиса тоже препятствие:
        _world.Add<Collider>(_fox) = new Collider { Radius = 0.3f };
        _world.Add<Collider>(m) = new Collider {Radius = 0.3f};
        _world.Add<ObstacleTag>(_fox); _world.Add<ObstacleTag>(m);
    }

    private Vector2 MouseScreen => new(_mouse.Position.X, _mouse.Position.Y);

    protected override void OnUpdate(double dt)
    {
        if (_imgui.WantKeyboard) return;
        float f = (float)dt;

        var move = Vector3.Zero;
        if (_kb.IsKeyPressed(Key.W)) { move += _camera.Forward; _follow = false; }
        if (_kb.IsKeyPressed(Key.S)) { move -= _camera.Forward; _follow = false; }
        if (_kb.IsKeyPressed(Key.D)) { move += _camera.Right; _follow = false; }
        if (_kb.IsKeyPressed(Key.A)) { move -= _camera.Right; _follow = false; }
        if (move != Vector3.Zero)
            _camera.Target += Vector3.Normalize(move) * (_camera.ViewHeight * 0.8f) * f;

        if (_kb.IsKeyPressed(Key.Q)) _camera.Yaw -= 1.2f * f;
        if (_kb.IsKeyPressed(Key.E)) _camera.Yaw += 1.2f * f;
        if (_play) _animTime += (float)dt;

        InputSystem();
        MovementSystem((float)dt);
        CameraSystem((float)dt);

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

        _camera.ScreenRay(MouseScreen, out var ro, out var rd);
        Vector3 hit = _height.Raycast(ro, rd, 400f, out var hh) ? hh : _camera.ScreenToGround(MouseScreen);

        int hx = (int)MathF.Round(hit.X / Tile + half);
        int hz = (int)MathF.Round(hit.Z / Tile + half);
        if (hx >= 0 && hx < _grid && hz >= 0 && hz < _grid)
            DrawTile((hx - half) * Tile, Terrace(hx, hz) + 0.01f, (hz - half) * Tile,
                     new Vector3(1.6f, 1.3f, 0.6f));

        Gl.Disable(EnableCap.CullFace);
        _shader.SetUniform("uSkinned", 0);
        _shader.SetUniform("uUseTexture", 1);
        _tile.Bind(TextureUnit.Texture0);
        foreach (var ob in _obstacles)
        {
            if (!_showColliders && ob.H < 0.75f) continue; // лису-«препятствие» не рисуем ящиком
            float gy = _height.Sample(ob.Pos.X, ob.Pos.Z);
            _shader.SetUniform("uModel",
                Matrix4x4.CreateScale(ob.R * 2f, ob.H, ob.R * 2f)
                * Matrix4x4.CreateTranslation(ob.Pos.X, gy, ob.Pos.Z));
            _shader.SetUniform("uTint", new Vector3(0.8f, 0.6f, 0.35f));
            _crate.Draw();
        }
        if (_cull) Gl.Enable(EnableCap.CullFace);

        _shader.SetUniform("uUseTexture", 0);

        RenderSystem();
        _shader.SetUniform("uSkinned", 0);

        _gpuWorld.End();

        _gpuUi.Begin();
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

        if (man.Skeleton is { } s && s.AnimationCount > 0)
        {
            ImGui.Checkbox("Play", ref _play);
            float dur = s.Duration(_animIndex);
            float t = _animTime % dur;
            if (ImGui.SliderFloat("Anim time", ref t, 0f, dur))
            { _play = false; _animTime = t; s.Play(_animIndex, t); }
            ImGui.Text($"duration {dur:F2}s");
        }

        ImGui.Checkbox("Follow", ref _follow);
        ImGui.SliderFloat("Yaw offset", ref _yawOffset, -3.14f, 3.14f);

        ImGui.Checkbox("Colliders", ref _showColliders);
        ImGui.SliderFloat("Max step", ref _maxStep, 0.1f, 1.0f);
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

    private void DrawModel(GltfModel model, Vector3 position, float scale, float yaw = 0f)
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
                    * Matrix4x4.CreateRotationY(yaw)
                    * Matrix4x4.CreateTranslation(position.X, position.Y - lowest, position.Z);
            _shader.SetUniform("uModel", m);
            _shader.SetUniform("uTint", Vector3.One);
            p.Mesh.Draw();
            _drawCalls++;
        }
    }

    private void DrawModel(GltfModel model, Vector3 position, float scale, float yaw, Vector3 tint)
    {
        if (model.Primitives.Count == 0) return;

        if (model.Skeleton is { } sk)
        {
            _shader.SetUniform("uSkinned", 1);
            _shader.SetUniformMatrixArray("uJoints[0]", sk.Palette);
        }
        else _shader.SetUniform("uSkinned", 0);

        var scaleM = Matrix4x4.CreateScale(scale);
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
                  * Matrix4x4.CreateRotationY(yaw)
                  * Matrix4x4.CreateTranslation(position.X, position.Y - lowest, position.Z);
            _shader.SetUniform("uModel", m);
            _shader.SetUniform("uTint", tint);
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

    private float GroundHeight(float x, float z)
    {
        float half = (_grid - 1) / 2f;
        int ix = Math.Clamp((int)MathF.Floor(x / Tile + half + 0.5f), 0, _grid - 1);
        int iz = Math.Clamp((int)MathF.Floor(z / Tile + half + 0.5f), 0, _grid - 1);
        return Terrace(ix, iz);
    }

    private static Mesh MakeCube(GL gl)
    {
        var v = new List<Vertex>(); var idx = new List<ushort>();
        void Face(Vector3 n, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int s = v.Count;
            v.Add(new Vertex { Position = a, Normal = n, Color = Vector3.One, UV = new(0, 0) });
            v.Add(new Vertex { Position = b, Normal = n, Color = Vector3.One, UV = new(1, 0) });
            v.Add(new Vertex { Position = c, Normal = n, Color = Vector3.One, UV = new(1, 1) });
            v.Add(new Vertex { Position = d, Normal = n, Color = Vector3.One, UV = new(0, 1) });
            idx.AddRange(new ushort[] { (ushort)s, (ushort)(s + 1), (ushort)(s + 2), (ushort)(s + 2), (ushort)(s + 3), (ushort)s });
        }
        float h = 0.5f;
        Face(Vector3.UnitY, new(-h, h, h), new(h, h, h), new(h, h, -h), new(-h, h, -h));
        Face(-Vector3.UnitY, new(-h, -h, -h), new(h, -h, -h), new(h, -h, h), new(-h, -h, h));
        Face(Vector3.UnitX, new(h, -h, h), new(h, -h, -h), new(h, h, -h), new(h, h, h));
        Face(-Vector3.UnitX, new(-h, -h, -h), new(-h, -h, h), new(-h, h, h), new(-h, h, -h));
        Face(Vector3.UnitZ, new(-h, -h, h), new(h, -h, h), new(h, h, h), new(-h, h, h));
        Face(-Vector3.UnitZ, new(h, -h, -h), new(-h, -h, -h), new(-h, h, -h), new(h, h, -h));
        return new Mesh(gl, v.ToArray(), idx.ToArray());
    }

    private void InputSystem()
    {
        if (_lmbHeld && !_imgui.WantMouse)
        {
            _camera.ScreenRay(MouseScreen, out var o, out var d);
            if (_height.Raycast(o, d, 400f, out var h))
            {
                ref var mv = ref _world.Get<Movement>(_player);
                mv.Target = new Vector3(h.X, 0, h.Z); mv.Moving = true;
            }
        }
    }

    private void MovementSystem(float dt)
    {
        var moves = _world.Set<Movement>();
        var obstacles = _world.Set<ObstacleTag>();
        foreach (int e in moves.Entities())
        {
            ref var mv = ref moves.Get(e);
            ref var tr = ref _world.Get<Transform>(e);
            float radius = _world.Get<Collider>(e).Radius;

            if (mv.Moving)
            {
                var d = mv.Target - tr.Pos; d.Y = 0; float dist = d.Length();
                if (dist < 0.05f) mv.Moving = false;
                else
                {
                    var dir = d / dist;
                    var desired = tr.Pos + dir * MathF.Min(mv.Speed * dt, dist);
                    if (_height.Sample(desired.X, desired.Z) - tr.Pos.Y <= _maxStep)
                    {
                        float lim = (_grid - 1) / 2f * Tile;
                        desired.X = Math.Clamp(desired.X, -lim, lim);
                        desired.Z = Math.Clamp(desired.Z, -lim, lim);
                        foreach (int o in obstacles.Entities())
                        {
                            if (o == e) continue;
                            var ot = _world.Get<Transform>(o); var oc = _world.Get<Collider>(o);
                            var d2 = new Vector2(desired.X - ot.Pos.X, desired.Z - ot.Pos.Z);
                            float r = oc.Radius + radius, len = d2.Length();
                            if (len < r && len > 1e-4f) { var n = d2 / len; desired.X = ot.Pos.X + n.X * r; desired.Z = ot.Pos.Z + n.Y * r; }
                        }
                        tr.Pos = desired;
                        tr.Yaw = MathF.Atan2(dir.X, dir.Z);
                        mv.Walk += dt;
                    }
                    else mv.Moving = false;
                }
            }
            float g = _height.Sample(tr.Pos.X, tr.Pos.Z);
            tr.Pos.Y += (g - tr.Pos.Y) * (1f - MathF.Exp(-12f * dt));
        }
    }

    private void CameraSystem(float dt)
    {
        if (!_follow) return;
        var p = _world.Get<Transform>(_player).Pos;
        _camera.Target = Vector3.Lerp(_camera.Target, p, 1f - MathF.Exp(-5f * dt));
    }

    private void RenderSystem()
    {
        foreach (int e in _world.Set<Renderable>().Entities())
        {
            var r = _world.Get<Renderable>(e);
            var tr = _world.Get<Transform>(e);
            if (r.Model != null)
            {
                if (r.Model.Skeleton is { } sk)
                {
                    bool moving = _world.Has<Movement>(e) && _world.Get<Movement>(e).Moving;
                    if (moving) sk.Play(0, _world.Get<Movement>(e).Walk);
                    else sk.UpdateBindPose();
                }
                DrawModel(r.Model, tr.Pos, r.Scale, tr.Yaw, r.Tint);
            }
            else if (r.Mesh != null)
            {
                Gl.Disable(EnableCap.CullFace);
                _shader.SetUniform("uSkinned", 0);
                _shader.SetUniform("uUseTexture", 1);
                _tile.Bind(TextureUnit.Texture0);
                _shader.SetUniform("uModel", Matrix4x4.CreateScale(r.Size) * Matrix4x4.CreateTranslation(tr.Pos));
                _shader.SetUniform("uTint", r.Tint);
                r.Mesh.Draw();
                if (_cull) Gl.Enable(EnableCap.CullFace);
            }
        }
    }
}

internal static class Program
{
    private static void Main() => new MyGame().Run("Iso RPG — isometric camera", 1920, 1080);
}