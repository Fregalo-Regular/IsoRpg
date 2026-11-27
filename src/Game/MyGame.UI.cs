using System.Numerics;
using Engine.Graphics;
using ImGuiNET;
using Engine.ECS;

namespace Game;

internal partial class MyGame
{
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

        ImGui.Checkbox("Follow", ref _follow);
        ImGui.SliderFloat("Yaw offset", ref _yawOffset, -3.14f, 3.14f);

        ImGui.Checkbox("Colliders", ref _showColliders);
        ImGui.SliderFloat("Max step", ref _maxStep, 0.1f, 1.0f);

        var dl = ImGui.GetBackgroundDrawList();
        var hp = _world.Get<Health>(_player);
        float f = Math.Clamp(hp.Hp / hp.Max, 0, 1);
        float R = 72, m = 20;
        var c = new Vector2(ImGui.GetIO().DisplaySize.X - m - R, m + R);
        dl.AddCircle(c, R + 5, 0xFF404040, 48, 12f);          // внешняя рамка (будущий портрет)
        dl.AddCircle(c, R, 0xAA101010, 48, 16f);             // трек
        dl.PathClear();
        dl.PathArcTo(c, R, -MathF.PI / 2, -MathF.PI / 2 + f * 2 * MathF.PI, 48);
        dl.PathStroke(ImGui.ColorConvertFloat4ToU32(new Vector4(HpColor(f), 1)), 0, 16f);
        dl.AddText(new Vector2(c.X - 14, c.Y - 7), 0xFFFFFFFF, $"{(int)hp.Hp}");
        ImGui.Text($"Kills: {_kills}");

        dl = ImGui.GetBackgroundDrawList();
        ref var ab = ref _world.Get<Abilities>(_player);
        for (int i = 0; i < 3; i++)
        {
            c = AbilityCenter(i);
            var st = i == 0 ? ab.A0 : i == 1 ? ab.A1 : ab.A2;
            dl.AddCircleFilled(c, 28, 0xCC181818, 32);
            dl.AddCircle(c, 28, i == _selectedAbility ? 0xFFE0B040 : 0xFF606060, 32, i == _selectedAbility ? 3f : 1.5f);
            dl.AddText(c + new Vector2(-4, -22), 0xFFFFFFFF, (i + 1).ToString());
            dl.AddText(c + new Vector2(-14, -6), 0xFFFFFFFF, _defs[i].Name);
            dl.AddText(c + new Vector2(-4, 8), 0xFFFFFFFF, st.Charges.ToString());
            if (st.Charges < st.Max)
            {
                dl.PathClear();
                dl.PathArcTo(c, 24, -MathF.PI / 2, -MathF.PI / 2 + (st.Regen / st.Cd) * 2 * MathF.PI, 32);
                dl.PathStroke(0xFF8080FF, 0, 3f);
            }
        }
        ImGui.SliderInt("Max charges", ref _maxCharges, 1, 5);   // тест прокачки

        ImGui.End();

        _imgui.EndFrame(FramebufferSize);
    }

    private Vector2 AbilityCenter(int i)
    {
        var io = ImGui.GetIO();
        float R = 28, gap = 18, total = 3 * (2 * R + gap);
        return new Vector2(io.DisplaySize.X / 2 - total / 2 + R + i * (2 * R + gap), io.DisplaySize.Y - R - 20);
    }

    private int HitAbilityCircle(Vector2 p)
    {
        for (int i = 0; i < 3; i++) if ((p - AbilityCenter(i)).Length() <= 28) return i;
        return -1;
    }
}