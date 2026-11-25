using System.Numerics;
using SharpGLTF.Schema2;

namespace Engine.Assets;

public sealed class Skeleton
{
    private readonly Node[] _joints;
    private readonly Matrix4x4[] _ibm;
    public Matrix4x4[] Palette { get; }

    internal Skeleton(Skin skin)
    {
        int n = skin.JointsCount;
        _joints = new Node[n];
        _ibm = new Matrix4x4[n];
        Palette = new Matrix4x4[n];

        for (int i = 0; i < n; i++)
        {
            _joints[i] = skin.Joints[i];
            _ibm[i] = skin.InverseBindMatrices[i];   // индексатор сверь через F12
        }
    }

    /// Bind pose: global == inverse(IBM), значит палитра ОБЯЗАНА быть единичной.
    public void UpdateBindPose()
    {
        for (int i = 0; i < _joints.Length; i++)
            Palette[i] = _ibm[i] * _joints[i].WorldMatrix;
    }

    /// Максимальное геометрическое отклонение палитры от Identity, в метрах.
    public float MaxDeviationFromIdentity()
    {
        float worst = 0f;
        foreach (var m in Palette)
        {
            worst = MathF.Max(worst, Vector3.Transform(Vector3.Zero,  m).Length());
            worst = MathF.Max(worst, (Vector3.Transform(Vector3.UnitX, m) - Vector3.UnitX).Length());
            worst = MathF.Max(worst, (Vector3.Transform(Vector3.UnitY, m) - Vector3.UnitY).Length());
            worst = MathF.Max(worst, (Vector3.Transform(Vector3.UnitZ, m) - Vector3.UnitZ).Length());
        }
        return worst;
    }
}