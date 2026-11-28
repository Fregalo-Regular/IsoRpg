using System.Numerics;
using Silk.NET.OpenGL;
using Engine.Assets;
using Engine.Graphics;

namespace Game;

internal partial class MyGame
{
    private void DrawModel(GltfModel model, Vector3 position, float scale, float yaw = 0f, Vector3? tint = null)
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
            var basis = model.Skeleton is { } s ? s.BindBasis : p.Transform;
            lowest = MathF.Min(lowest, LowestY(p.BoundsMin, p.BoundsMax, basis * scaleM));
        }

        var col = tint ?? Vector3.One;
        foreach (var p in model.Primitives)
        {
            if (p.Texture != null) { p.Texture.Bind(TextureUnit.Texture0); _shader.SetUniform("uUseTexture", 1); }
            else _shader.SetUniform("uUseTexture", 0);

            var m = p.Transform * scaleM
                  * Matrix4x4.CreateRotationY(yaw)
                  * Matrix4x4.CreateTranslation(position.X, position.Y - lowest, position.Z);
            _shader.SetUniform("uModel", m);
            _shader.SetUniform("uTint", col);
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

    private static Mesh MakeRing(GL gl, int seg = 48)
    {
        var v = new List<Vertex>(); var idx = new List<ushort>();
        for (int i = 0; i <= seg; i++)
        {
            float t = (float)i / seg, a = t * MathF.PI * 2;
            float c = MathF.Cos(a), s = MathF.Sin(a);
            v.Add(new Vertex { Position = new Vector3(c, 0, s), Normal = Vector3.UnitY, Color = Vector3.One, UV = new Vector2(t, 1) });
            v.Add(new Vertex { Position = new Vector3(c * 0.8f, 0, s * 0.8f), Normal = Vector3.UnitY, Color = Vector3.One, UV = new Vector2(t, 0) });
        }
        for (int i = 0; i < seg; i++)
        { int o = i * 2; idx.AddRange(new ushort[] { (ushort)o, (ushort)(o + 1), (ushort)(o + 2), (ushort)(o + 2), (ushort)(o + 1), (ushort)(o + 3) }); }
        return new Mesh(gl, v.ToArray(), idx.ToArray());
    }

    private static Vector3 HpColor(float f) => f > 0.5f
        ? Vector3.Lerp(new Vector3(0.9f,0.7f,0.2f), new Vector3(0.2f,0.8f,0.3f), (f-0.5f)*2)
        : Vector3.Lerp(new Vector3(0.8f,0.2f,0.2f), new Vector3(0.9f,0.7f,0.2f), f*2);

    private void DrawFloorRing(Vector3 pos, float radius, float frac, Vector3 col)
    {
        _shader.SetUniform("uIsRing", 1);
        _shader.SetUniform("uUseTexture", 0);
        _shader.SetUniform("uSkinned", 0);
        Gl.Disable(EnableCap.CullFace);

        // трек
        _shader.SetUniform("uModel", Matrix4x4.CreateScale(radius, 1, radius) * Matrix4x4.CreateTranslation(pos.X, pos.Y + 0.02f, pos.Z));
        _shader.SetUniform("uRingFrac", 1f);
        _shader.SetUniform("uTint", new Vector3(0.08f, 0.08f, 0.08f));
        _ring.Draw();

        // заполнение — чуть выше трека, чтобы прошло depth-тест
        _shader.SetUniform("uModel", Matrix4x4.CreateScale(radius, 1, radius) * Matrix4x4.CreateTranslation(pos.X, pos.Y + 0.04f, pos.Z));
        _shader.SetUniform("uRingFrac", frac);
        _shader.SetUniform("uTint", col);
        _ring.Draw();

        if (_cull) Gl.Enable(EnableCap.CullFace);
        _shader.SetUniform("uIsRing", 0);
    }
}