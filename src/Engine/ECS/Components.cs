using System.Numerics;
using Engine.Assets;
using Engine.Graphics;

namespace Engine.ECS;

public struct Transform { public Vector3 Pos; public float Yaw; }
public struct Velocity { public Vector3 Vel; }
public struct Movement { public Vector3 Target; public float Speed; public bool Moving; public float Walk; }
public struct Collider { public float Radius; }
public struct Health { public float Hp, Max; public bool Dead; public float Respawn; }
public struct Renderable
{
    public GltfModel? Model;   // для персонажей
    public Mesh? Mesh;         // для ящиков
    public float Scale;
    public Vector3 Size;       // негабаритный масштаб меша
    public Vector3 Tint;
    public int AnimIndex;
}
public struct PlayerTag { }
public struct MobTag { }
public struct ObstacleTag { }
public struct Combat { public float Cd, Flash, Damage, Range; }
public struct AI { public float Aggro; }
public struct Number { public float Val, T; }
public struct NumberTag { }
public struct AttackTarget { public int Entity; public bool Active; }
public struct AbilityState { public int Charges, Max; public float Regen, Cd; }
public struct Abilities { public AbilityState A0, A1, A2; }
public struct Projectile { public float Damage, Radius, Life; }
public struct Effect { public float T, Dur, Radius; }   // расширяющееся кольцо новы