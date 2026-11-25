using System.Numerics;
using Silk.NET.Maths;

namespace Engine.Graphics;

public sealed class Camera2D
{
    public Vector2 Position;
    public float Zoom = 1f;

    private Vector2D<int> _fb;

    public Camera2D(Vector2D<int> framebuffer) => _fb = framebuffer;
    public void SetViewport(Vector2D<int> framebuffer) => _fb = framebuffer;

    // Zoom = сколько пикселей на одну мировую единицу
    public Matrix4x4 View => Matrix4x4.CreateTranslation(-Position.X, -Position.Y, 0f);

    public Matrix4x4 Projection
    {
        get
        {
            float hw = _fb.X / (2f * Zoom);
            float hh = _fb.Y / (2f * Zoom);
            return Matrix4x4.CreateOrthographicOffCenter(-hw, hw, -hh, hh, -1f, 1f);
        }
    }

    public Matrix4x4 ViewProjection => View * Projection;

    /// Эcran → мир. Понадобится для click-to-move на M1.
    public Vector2 ScreenToWorld(Vector2 screen)
    {
        var ndc = new Vector2(
            2f * screen.X / _fb.X - 1f,
            1f - 2f * screen.Y / _fb.Y);      // Y экрана вниз, Y в NDC вверх

        Matrix4x4.Invert(ViewProjection, out var inverse);
        return Vector2.Transform(ndc, inverse);
    }

    /// Зум с якорем под курсором: точка под мышью остаётся на месте.
    public void ZoomAt(Vector2 screen, float factor)
    {
        var before = ScreenToWorld(screen);
        Zoom = Math.Clamp(Zoom * factor, 0.05f, 20f);
        Position += before - ScreenToWorld(screen);
    }
}