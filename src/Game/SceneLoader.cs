using System.Numerics;
using System.Text.Json;
using Engine.ECS;
using Engine.Assets;
using Engine.Graphics;
using Silk.NET.OpenGL;

namespace Game;

public sealed class SceneFile 
{ 
    public PlayerDef Player { get; set; } = null!; 
    public List<MobDef> Mobs { get; set; } = new(); 
    public List<PropDef> Props { get; set; } = new(); 
}
public sealed class PlayerDef 
{ 
    public string Model { get; set; } = ""; 
    public float[] Pos { get; set; } = new float[3]; 
    public float Scale { get; set; } = 1; 
    public float Hp { get; set; } = 100; 
}
public sealed class MobDef 
{ 
    public string Model { get; set; } = ""; 
    public float[] Pos { get; set; } = new float[3]; 
    public float Scale { get; set; } = 0.01f; 
    public float Hp { get; set; } = 60; 
    public int Pack { get; set; } 
    public float Aggro { get; set; } = 9;
    public float Knockback { get; set; } = 0f;
}
public sealed class PropDef 
{ 
    public float[] Pos { get; set; } = new float[3]; 
    public float Radius { get; set; } = 0.7f; 
    public float Height { get; set; } = 1; 
}

public static class SceneLoader
{
    private static Vector3 V(float[] a) => new(a[0], a[1], a[2]);
    
    public static int Load(World w, GL gl, string path,
        Dictionary<string, GltfModel> cache, AbilityDef[] defs)
    {
        var s = JsonSerializer.Deserialize<SceneFile>(File.ReadAllText(AssetPath.Resolve(path)),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var crate = MakeCube(gl);
        int player = SpawnPlayer(w, gl, s.Player, cache, defs);
        foreach (var m in s.Mobs) SpawnMob(w, gl, m, cache);
        foreach (var p in s.Props) SpawnProp(w, p, crate);
        return player;
    }

    private static GltfModel Model(GL gl, Dictionary<string, GltfModel> c, string path)
    {
        if (!c.TryGetValue(path, out var m)) { m = GltfLoader.Load(gl, path); c[path] = m; }
        return m;
    }

    private static int SpawnPlayer(World w, GL gl, PlayerDef d,
        Dictionary<string, GltfModel> c, AbilityDef[] defs)
    {
        int e = w.Create();
        w.Add<Transform>(e) = new Transform { Pos = V(d.Pos) };
        w.Add<Movement>(e) = new Movement { Speed = 3f };
        w.Add<Collider>(e) = new Collider { Radius = 0.35f };
        w.Add<Health>(e) = new Health { Hp = d.Hp, Max = d.Hp };
        w.Add<Combat>(e) = new Combat { Damage = 25, Range = 1.8f, Knockback = 1f };
        w.Add<Velocity>(e);
        w.Add<AttackTarget>(e);
        w.Add<Abilities>(e) = new Abilities
        {
            A0 = new AbilityState { Charges = 1, Max = 1, Cd = defs[0].Cd },
            A1 = new AbilityState { Charges = 1, Max = 1, Cd = defs[1].Cd },
            A2 = new AbilityState { Charges = 1, Max = 1, Cd = defs[2].Cd },
        };
        w.Add<Renderable>(e) = new Renderable { Model = Model(gl, c, d.Model), Scale = d.Scale, Tint = Vector3.One, AnimIndex = 0 };
        w.Add<PlayerTag>(e);
        var model = Model(gl, c, d.Model);
        w.Add<Renderable>(e) = new Renderable { Model = model, Scale = d.Scale, Tint = Vector3.One, AnimIndex = 0 };
        w.Add<AnimState>(e) = new AnimState { Clip = model.Anims.TryGetValue("idle01", out var idle) ? idle : 0, Loop = true };
        w.Add<PendingAction>(e) = new PendingAction { Fired = true };
        w.Add<PlayerTag>(e);
        return e;
    }

    private static void SpawnMob(World w, GL gl, MobDef d, Dictionary<string, GltfModel> c)
    {
        int e = w.Create();
        w.Add<Transform>(e) = new Transform { Pos = V(d.Pos) };
        w.Add<Collider>(e) = new Collider { Radius = 0.6f };
        w.Add<ObstacleTag>(e);
        w.Add<Health>(e) = new Health { Hp = d.Hp, Max = d.Hp };
        w.Add<Combat>(e) = new Combat { Damage = 8, Range = 1.2f, Knockback = d.Knockback };
        w.Add<AI>(e) = new AI { Aggro = d.Aggro };
        w.Add<Movement>(e) = new Movement { Speed = 2.5f };
        w.Add<Velocity>(e);
        w.Add<MobTag>(e);
        if (d.Pack != 0) w.Add<Pack>(e) = new Pack { Id = d.Pack };
        w.Add<Renderable>(e) = new Renderable { Model = Model(gl, c, d.Model), Scale = d.Scale, Tint = Vector3.One, AnimIndex = 2 };
    }

    private static void SpawnProp(World w, PropDef d, Mesh crate)
    {
        int e = w.Create();
        w.Add<Transform>(e) = new Transform { Pos = V(d.Pos) };
        w.Add<Collider>(e) = new Collider { Radius = d.Radius };
        w.Add<ObstacleTag>(e);
        w.Add<Renderable>(e) = new Renderable { Mesh = crate, Size = new Vector3(d.Radius * 2, d.Height, d.Radius * 2), Tint = new Vector3(0.8f, 0.6f, 0.35f) };
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
            idx.AddRange(new ushort[] { (ushort)s, (ushort)(s+1), (ushort)(s+2), (ushort)(s+2), (ushort)(s+3), (ushort)s });
        }
        float h = 0.5f;
        Face(Vector3.UnitY,  new(-h,h,h),  new(h,h,h),  new(h,h,-h), new(-h,h,-h));
        Face(-Vector3.UnitY, new(-h,-h,-h),new(h,-h,-h),new(h,-h,h), new(-h,-h,h));
        Face(Vector3.UnitX,  new(h,-h,h),  new(h,-h,-h),new(h,h,-h), new(h,h,h));
        Face(-Vector3.UnitX, new(-h,-h,-h),new(-h,-h,h),new(-h,h,h), new(-h,h,-h));
        Face(Vector3.UnitZ,  new(-h,-h,h), new(h,-h,h), new(h,h,h),  new(-h,h,h));
        Face(-Vector3.UnitZ, new(h,-h,-h), new(-h,-h,-h),new(-h,h,-h),new(h,h,-h));
        return new Mesh(gl, v.ToArray(), idx.ToArray());
    }
}