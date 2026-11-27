using System.Numerics;

namespace Engine.Scene;

public sealed class Heightfield
{
    private readonly float[,] _h;
    public int Size { get; }
    public float Tile { get; }
    private readonly float _half;

    public Heightfield(int size, float tile, Func<int, int, float> f)
    {
        Size = size; Tile = tile; _half = (size - 1) / 2f;
        _h = new float[size, size];
        for (int x = 0; x < size; x++)
            for (int z = 0; z < size; z++)
                _h[x, z] = f(x, z);
    }

    public int Index(float w) => (int)MathF.Floor(w / Tile + _half + 0.5f);

    public float Sample(float x, float z)
    {
        int ix = Math.Clamp(Index(x), 0, Size - 1);
        int iz = Math.Clamp(Index(z), 0, Size - 1);
        return _h[ix, iz];
    }

    /// Рейкаст по heightfield: марш с шагом + бисекция уточнения.
    public bool Raycast(Vector3 o, Vector3 d, float maxDist, out Vector3 hit)
    {
        hit = Vector3.Zero;
        if (MathF.Abs(d.Y) < 1e-6f) return false;

        float y = 0;
        for (int i = 0; i < 8; i++)
        {
            float t = (y - o.Y) / d.Y;
            var p = o + d * t;
            float g = Sample(p.X, p.Z);
            if (MathF.Abs(g - y) < 1e-4f) { hit = p; hit.Y = g; return true; }
            y = g;   // подняли плоскость до высоты тайла — пересчитали XZ
        }
        return false;
    }
    public bool Raycast(Vector3 o, Vector3 d, out Vector3 hit)
        => Raycast(o, d, 400f, out hit);

    public Vector3 Raycast(Vector3 o, Vector3 d)
    => Raycast(o, d, 400f, out var h) ? h : Vector3.Zero;
}

public sealed class Obstacle
{
    public Vector3 Pos;
    public float R;      // радиус коллизии
    public float H;      // высота ящика (визуал)
}