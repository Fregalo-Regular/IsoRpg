using System.Numerics;
using SharpGLTF.Schema2;

namespace Engine.Assets;

public sealed class Skeleton
{
    private sealed class Track
    {
        public int Node;
        public string Path = "";
        public float[] Keys = Array.Empty<float>();
        public Vector3[] V3 = Array.Empty<Vector3>();
        public Quaternion[] Q = Array.Empty<Quaternion>();
        public bool Step;
    }

    private readonly Node[] _nodes;
    private readonly int[] _parent;
    private readonly Dictionary<Node, int> _index = new();

    private readonly Vector3[] _t, _s, _bindT, _bindS;
    private readonly Quaternion[] _r, _bindR;
    private readonly Matrix4x4[] _world;
    private readonly bool[] _done;

    private readonly Node[] _joints;
    private readonly Matrix4x4[] _ibm;
    private readonly Animation[] _anims;
    private readonly Track[][] _tracks;

    public Matrix4x4[] Palette { get; }
    public int AnimationCount => _anims.Length;
    public float Duration(int i) => _anims[i].Duration;

    internal Skeleton(ModelRoot model, Skin skin)
    {
        
        _nodes = model.LogicalNodes.ToArray();
        for (int i = 0; i < _nodes.Length; i++) _index[_nodes[i]] = i;

        _parent = new int[_nodes.Length];
        for (int i = 0; i < _nodes.Length; i++)
            _parent[i] = _nodes[i].VisualParent is { } p ? _index[p] : -1;

        int n = _nodes.Length;
        _t = new Vector3[n]; _r = new Quaternion[n]; _s = new Vector3[n];
        _bindT = new Vector3[n]; _bindR = new Quaternion[n]; _bindS = new Vector3[n];
        _world = new Matrix4x4[n]; _done = new bool[n];

        for (int i = 0; i < n; i++)
        {
            Matrix4x4.Decompose(_nodes[i].LocalMatrix, out _s[i], out _r[i], out _t[i]);
            _bindT[i] = _t[i]; _bindR[i] = _r[i]; _bindS[i] = _s[i];
        }

        _joints = skin.Joints.ToArray();
        _ibm = Matrix4x64Safe(skin);
        Palette = new Matrix4x4[_joints.Length];

        _anims = model.LogicalAnimations.ToArray();
        _tracks = new Track[_anims.Length][];
        for (int a = 0; a < _anims.Length; a++)
            _tracks[a] = BuildTracks(_anims[a]);

        UpdateBindPose();
    }

    private static Matrix4x4[] Matrix4x64Safe(Skin skin)
    {
        var r = new Matrix4x4[skin.JointsCount];
        for (int j = 0; j < r.Length; j++) r[j] = skin.InverseBindMatrices[j];
        return r;
    }

    private Track[] BuildTracks(Animation anim)
    {
        var list = new List<Track>();
        foreach (var ch in anim.Channels)
        {
            var path = ch.TargetNodePath.ToString();
            var tr = new Track { Node = _index[ch.TargetNode], Path = path };

            if (path == "rotation")
            {
                var s = ch.GetRotationSampler();
                tr.Step = s.InterpolationMode == AnimationInterpolationMode.STEP;
                var kv = s.GetLinearKeys().ToArray();
                tr.Keys = kv.Select(x => x.Key).ToArray();
                tr.Q = kv.Select(x => x.Value).ToArray();
                if (s.InterpolationMode == AnimationInterpolationMode.CUBICSPLINE)
                    Console.WriteLine("[anim] CUBICSPLINE rotation: пока линейное приближение");
            }
            else if (path == "translation" || path == "scale")
            {
                var s = path == "translation" ? ch.GetTranslationSampler() : ch.GetScaleSampler();
                tr.Step = s.InterpolationMode == AnimationInterpolationMode.STEP;
                var kv = s.GetLinearKeys().ToArray();
                tr.Keys = kv.Select(x => x.Key).ToArray();
                tr.V3 = kv.Select(x => x.Value).ToArray();
                if (s.InterpolationMode == AnimationInterpolationMode.CUBICSPLINE)
                    Console.WriteLine("[anim] CUBICSPLINE vec3: пока линейное приближение");
            }
            else continue;

            list.Add(tr);
        }
        return list.ToArray();
    }

    private void RestoreBind()
    {
        Array.Copy(_bindT, _t, _t.Length);
        Array.Copy(_bindR, _r, _r.Length);
        Array.Copy(_bindS, _s, _s.Length);
    }

    private Matrix4x4 World(int i)
    {
        if (_done[i]) return _world[i];
        var local = Matrix4x4.CreateScale(_s[i])
          * Matrix4x4.CreateFromQuaternion(_r[i])
          * Matrix4x4.CreateTranslation(_t[i]);
        _world[i] = _parent[i] < 0 ? local : local * World(_parent[i]);
        _done[i] = true;
        return _world[i];
    }

    public void UpdatePalette()
    {
        Array.Clear(_done);
        for (int j = 0; j < _joints.Length; j++)
            Palette[j] = _ibm[j] * World(_index[_joints[j]]);
    }

    public void UpdateBindPose() { RestoreBind(); UpdatePalette(); }

    public void Play(int animIndex, float time)
    {
        RestoreBind();
        var anim = _anims[animIndex];
        float t = anim.Duration > 0 ? time % anim.Duration : 0;

        foreach (var tr in _tracks[animIndex])
        {
            if (tr.Path == "rotation") _r[tr.Node] = SampleQ(tr, t);
            else if (tr.Path == "translation") _t[tr.Node] = SampleV(tr, t);
            else if (tr.Path == "scale") _s[tr.Node] = SampleV(tr, t);
        }
        UpdatePalette();
    }

    private static int Segment(float[] k, float t)
    {
        int i = 0;
        while (i < k.Length - 2 && k[i + 1] < t) i++;
        return i;
    }

    private static Vector3 SampleV(Track tr, float t)
    {
        var k = tr.Keys; var v = tr.V3;
        if (k.Length == 0) return Vector3.Zero;
        if (k.Length == 1 || t <= k[0]) return v[0];
        if (t >= k[k.Length - 1]) return v[v.Length - 1];
        int i = Segment(k, t);
        return tr.Step ? v[i] : Vector3.Lerp(v[i], v[i + 1], (t - k[i]) / (k[i + 1] - k[i]));
    }

    private static Quaternion SampleQ(Track tr, float t)
    {
        var k = tr.Keys; var v = tr.Q;
        if (k.Length == 0) return Quaternion.Identity;
        if (k.Length == 1 || t <= k[0]) return v[0];
        if (t >= k[k.Length - 1]) return v[v.Length - 1];
        int i = Segment(k, t);
        return tr.Step ? v[i] : Quaternion.Slerp(v[i], v[i + 1], (t - k[i]) / (k[i + 1] - k[i]));
    }

    public float BindVsLibrary()
    {
        RestoreBind();
        Array.Clear(_done);
        float worst = 0f;
        for (int i = 0; i < _nodes.Length; i++)
        {
            var mine = World(i);
            var lib = _nodes[i].WorldMatrix;
            worst = MathF.Max(worst, (Vector3.Transform(Vector3.Zero, mine) - Vector3.Transform(Vector3.Zero, lib)).Length());
            worst = MathF.Max(worst, (Vector3.Transform(Vector3.UnitZ, mine) - Vector3.Transform(Vector3.UnitZ, lib)).Length());
        }
        //Console.WriteLine(String.Format("worst: {0}", worst.ToString()));
        return worst;
    }
}