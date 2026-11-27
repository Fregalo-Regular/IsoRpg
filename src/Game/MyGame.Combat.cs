using System.Numerics;
using Engine.ECS;

namespace Game;

internal partial class MyGame
{
    private void Hit(int target, float dmg, Vector3 from)
    {
        ref var h = ref _world.Get<Health>(target);
        ref var c = ref _world.Get<Combat>(target);
        var t = _world.Get<Transform>(target);

        h.Hp -= dmg;
        c.Flash = 0.15f;

        var kb = t.Pos - from; kb.Y = 0;
        if (kb.Length() > 1e-4f)
            _world.Get<Velocity>(target).Vel += Vector3.Normalize(kb) * 6f;

        SpawnNumber(t.Pos + Vector3.UnitY * 1.4f, dmg);

        if (_world.Has<AI>(target)) Aggro(target);          // урон будит моба и пачку

        if (h.Hp <= 0)
        {
            h.Dead = true; h.Respawn = 3f;
            if (_world.Has<MobTag>(target)) _kills++;
        }
    }

    private void Aggro(int m)
    {
        _world.Get<AI>(m).Angry = true;
        int pack = _world.Has<Pack>(m) ? _world.Get<Pack>(m).Id : 0;
        if (pack == 0) return;
        foreach (int o in _world.Set<MobTag>().Entities())
            if (o != m && _world.Has<Pack>(o) && _world.Get<Pack>(o).Id == pack && !_world.Get<Health>(o).Dead)
                _world.Get<AI>(o).Angry = true;
    }

    private void PerformAttack(int attacker)
    {
        ref var c = ref _world.Get<Combat>(attacker);
        if (c.Cd > 0) return;
        c.Cd = 0.45f;
        var ap = _world.Get<Transform>(attacker);
        var fwd = new Vector3(MathF.Sin(ap.Yaw), 0, MathF.Cos(ap.Yaw));
        foreach (int m in _world.Set<MobTag>().Entities())
        {
            if (_world.Get<Health>(m).Dead) continue;
            var d = _world.Get<Transform>(m).Pos - ap.Pos; d.Y = 0;
            if (d.Length() < c.Range && Vector3.Dot(Vector3.Normalize(d), fwd) > 0.3f)
                Hit(m, c.Damage, ap.Pos);
        }
    }

    private void SpawnNumber(Vector3 pos, float val)
        => SpawnNumber(pos, val, new Vector3(1f, 0.9f, 0.4f));   // жёлтый по умолчанию

    private void SpawnNumber(Vector3 pos, float val, Vector3 color)
    {
        int e = _world.Create();
        _world.Add<Transform>(e) = new Transform { Pos = pos };
        _world.Add<Number>(e) = new Number { Val = val, Color = color };
        _world.Add<NumberTag>(e);
    }
}