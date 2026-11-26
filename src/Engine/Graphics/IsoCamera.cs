using System.Numerics;
using Silk.NET.Maths;

namespace Engine.Graphics;

public sealed class IsoCamera
{
    public Vector3 Target;                                   // точка на земле, куда смотрим
    public float Yaw = MathF.PI / 4f;                        // 45°
    public float Pitch = MathF.Atan(1f / MathF.Sqrt(2f));    // 35.264°
    public float ViewHeight = 20f;                          // сколько мировых единиц видно по вертикали

    private const float Distance = 100f;                    // для ортокамеры не влияет на размер, только на near/far
    private Vector2D<int> _fb;


    public IsoCamera(Vector2D<int> fb) => _fb = fb;
    public void SetViewport(Vector2D<int> fb) => _fb = fb;

    /// Направление от цели к камере. При 45°/35.264° это (1,1,1)/√3.
    public Vector3 Direction => new(
        MathF.Cos(Pitch) * MathF.Sin(Yaw),
        MathF.Sin(Pitch),
        MathF.Cos(Pitch) * MathF.Cos(Yaw));

    /// «Вверх по экрану» и «вправо по экрану», спроецированные на землю.
    public Vector3 Forward => new(-MathF.Sin(Yaw), 0f, -MathF.Cos(Yaw));
    public Vector3 Right   => new( MathF.Cos(Yaw), 0f, -MathF.Sin(Yaw));

    public Matrix4x4 View =>
        Matrix4x4.CreateLookAt(Target + Direction * Distance, Target, Vector3.UnitY);

    public Matrix4x4 Projection
    {
        get
        {
            float hh = ViewHeight * 0.5f;
            float hw = hh * _fb.X / (float)Math.Max(1, _fb.Y);
            return Matrix4x4.CreateOrthographicOffCenter(-hw, hw, -hh, hh, 0.1f, 400f);
        }
    }

    public Matrix4x4 ViewProjection => View * Projection;

    /// Луч из пикселя экрана до плоскости Y=0. Основа click-to-move.
    public Vector3 ScreenToGround(Vector2 screen)
    {
        var ndc = new Vector2(2f * screen.X / _fb.X - 1f,
                              1f - 2f * screen.Y / _fb.Y);

        Matrix4x4.Invert(ViewProjection, out var inv);
        var p0 = Vector3.Transform(new Vector3(ndc.X, ndc.Y, -1f), inv);   // ближняя плоскость
        var p1 = Vector3.Transform(new Vector3(ndc.X, ndc.Y,  1f), inv);   // дальняя

        var dir = Vector3.Normalize(p1 - p0);
        if (MathF.Abs(dir.Y) < 1e-6f) return Target;
        return p0 + dir * (-p0.Y / dir.Y);
    }

    public void ZoomAt(Vector2 screen, float factor)
    {
        var anchor = ScreenToGround(screen);
        ViewHeight = Math.Clamp(ViewHeight * factor, 4f, 60f);
        Target += anchor - ScreenToGround(screen);
        Target.Y = 0f;
    }

    public void ScreenRay(Vector2 screen, out Vector3 origin, out Vector3 dir)
    {
        var ndc = new Vector2(2f * screen.X / _fb.X - 1f, 1f - 2f * screen.Y / _fb.Y);
        Matrix4x4.Invert(ViewProjection, out var inv);
        var p0 = Vector3.Transform(new Vector3(ndc.X, ndc.Y, -1f), inv);
        var p1 = Vector3.Transform(new Vector3(ndc.X, ndc.Y, 1f), inv);

        dir = Vector3.Normalize(p1 - p0);      // для орто это ось взгляда (параллельные лучи)
        if (dir.Y > 0) { dir = -dir; origin = p1; }  // начало — верхний конец, луч всегда вниз
        else origin = p0;
    }

    public Vector2 WorldToScreen(Vector3 w)
    {
        var clip = Vector4.Transform(new Vector4(w, 1f), ViewProjection); // row-vector: v * M
        var ndc = new Vector3(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W);
        return new Vector2(
            (ndc.X * 0.5f + 0.5f) * _fb.X,
            (1f - (ndc.Y * 0.5f + 0.5f)) * _fb.Y);   // Y экрана вниз, NDC вверх
    }
}