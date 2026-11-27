using System.Numerics;
using Engine.Diagnostics;
using Engine.Graphics;
using Engine.Platform;
using Engine.Scene;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Engine.Assets;
using Engine.ECS;

namespace Game;

internal partial class MyGame : EngineHost
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
    private float _lightAzimuth = 140f;
    private Vector3 _lightDir = Vector3.Normalize(new Vector3(-0.4f, 1.2f, 0.3f));
    private bool _debugNormals;
    private float _animTime;
    private bool _play = true;
    private bool _follow = true;
    private float _yawOffset;
    private Heightfield _height = null!;
    private float _maxStep = 0.45f, _playerRadius = 0.35f;
    private bool _showColliders = true;
    private bool _lmbHeld;
    private World _world = new();
    private int _player;
    private Mesh _ring = null!;
    private bool _lmbEdge = false;
    private int _foxClip = 2;
    private static readonly (string Name, float Cd, float Range)[] _defs =
    { ("Bolt", 2f, 12), ("Nova", 5f, 4), ("Heal", 6f, 0) };
    private int _selectedAbility, _maxCharges = 1;
    private bool _rmbEdge;
    private readonly Dictionary<string, GltfModel> _modelCache = new();
    private bool _attackQueued; private int _kills;
    public struct Number { public float Val, T; public Vector3 Color; }
    //private Vector3 ScreenRayDir() { _camera.ScreenRay(MouseScreen, out var o, out var d); return d; }

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
        _ring = MakeRing(Gl);

        _height = new Heightfield(_grid, Tile, Terrace);

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

        _player = _world.Create();
        _world.Add<Transform>(_player) = new Transform { Pos = new Vector3(-6, 0, 0) };
        _world.Add<Movement>(_player) = new Movement { Speed = 3f };
        _world.Add<Collider>(_player) = new Collider { Radius = _playerRadius };
        _world.Add<PlayerTag>(_player);

        _world.Add<Health>(_player) = new Health { Hp = 100, Max = 100 };
        _world.Add<Combat>(_player) = new Combat { Damage = 25, Range = 1.8f };
        _world.Add<Velocity>(_player);
        _kb.KeyDown += (_, k, _) => { if (k == Key.Space) _attackQueued = true; };

        foreach (int m in _world.Set<MobTag>().Entities())
        {
            _world.Add<Health>(m) = new Health { Hp = 60, Max = 60 };
            _world.Add<Combat>(m) = new Combat { Damage = 8, Range = 1.2f };
            _world.Add<AI>(m) = new AI { Aggro = 9f };
            _world.Add<Movement>(m) = new Movement { Speed = 2.5f };
            _world.Add<Velocity>(m);
        }

        _world.Add<AttackTarget>(_player);
        _mouse.MouseDown += (_, b) => { if (b == MouseButton.Left) { _lmbHeld = true; _lmbEdge = true; } };
        _mouse.MouseUp   += (_, b) => { if (b == MouseButton.Left) { _lmbHeld = false; _world.Get<AttackTarget>(_player).Active = false; } };

        foreach (int e in _world.Set<MobTag>().Entities())
            _world.Get<Renderable>(e).AnimIndex = _foxClip;

        _world.Add<Abilities>(_player) = new Abilities
        {
            A0 = new AbilityState { Charges = 1, Max = 1, Cd = _defs[0].Cd },
            A1 = new AbilityState { Charges = 1, Max = 1, Cd = _defs[1].Cd },
            A2 = new AbilityState { Charges = 1, Max = 1, Cd = _defs[2].Cd },
        };
        _mouse.MouseDown += (_, b) => { if (b == MouseButton.Right) _rmbEdge = true; };
        _kb.KeyDown += (_, k, _) =>
        {
            if (k == Key.Number1) _selectedAbility = 0;
            if (k == Key.Number2) _selectedAbility = 1;
            if (k == Key.Number3) _selectedAbility = 2;
        };

        _player = SceneLoader.Load(_world, Gl, "assets/scene.json", _modelCache, _defs);
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
        AutoAttackSystem();
        CastSystem();
        AISystem();
        MovementSystem((float)dt);
        SeparationSystem();
        ProjectileSystem((float)dt);
        EffectSystem((float)dt);
        CooldownSystem((float)dt);
        NumberSystem((float)dt);
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

        var at = _world.Get<AttackTarget>(_player);
        if (at.Active && !_world.Get<Health>(at.Entity).Dead)
            DrawFloorRing(_world.Get<Transform>(at.Entity).Pos, _world.Get<Collider>(at.Entity).Radius * 2.4f, 1f, new Vector3(1, 1, 1));

        Gl.Disable(EnableCap.CullFace);

        foreach (int e in _world.Set<MobTag>().Entities())
        {
            var h = _world.Get<Health>(e); if (h.Dead) continue;
            float f = Math.Clamp(h.Hp / h.Max, 0, 1);
            DrawFloorRing(_world.Get<Transform>(e).Pos, _world.Get<Collider>(e).Radius * 2, f, HpColor(f));
        }

        foreach (int e in _world.Set<Effect>().Entities())
        {
            var f = _world.Get<Effect>(e);
            float r = f.Radius * (f.T / f.Dur);
            DrawFloorRing(_world.Get<Transform>(e).Pos, r, 1f, new Vector3(0.3f, 0.6f, 1f));
        }

        _shader.SetUniform("uSkinned", 0);
        _shader.SetUniform("uUseTexture", 1);
        _tile.Bind(TextureUnit.Texture0);

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
        _imgui.Dispose(); _tile.Dispose(); _quad.Dispose(); _shader.Dispose(); _gpuWorld.Dispose(); _gpuUi.Dispose(); _ring.Dispose();
    }

    private int PickMob(Vector3 o, Vector3 d)
    {
        int best = -1; float bestT = float.MaxValue;
        foreach (int m in _world.Set<MobTag>().Entities())
        {
            if (_world.Get<Health>(m).Dead) continue;
            var c = _world.Get<Transform>(m).Pos + Vector3.UnitY * 0.5f;
            float r = _world.Get<Collider>(m).Radius * 1.5f;
            float t = Vector3.Dot(c - o, d); if (t < 0) continue;
            if (((o + d * t) - c).Length() < r && t < bestT) { bestT = t; best = m; }
        }
        return best;
    }

    private static ref AbilityState AbilityRef(ref Abilities ab, int i)
    {
        if (i == 0) return ref ab.A0;
        if (i == 1) return ref ab.A1;
        return ref ab.A2;
    }

}

internal static class Program
{
    private static void Main() => new MyGame().Run("Iso RPG — isometric camera", 1920, 1080);
}