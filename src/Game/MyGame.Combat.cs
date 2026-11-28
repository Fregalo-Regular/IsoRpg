using System.Numerics;
using Engine.ECS;

namespace Game;

internal partial class MyGame
{
    private void Hit(int target, float dmg, Vector3 from, float knockback)
    {
        ref var h = ref _world.Get<Health>(target);
        ref var c = ref _world.Get<Combat>(target);
        var t = _world.Get<Transform>(target);

        h.Hp -= dmg;
        c.Flash = 0.15f;

        if (knockback > 0f)
        {
            var kb = t.Pos - from; kb.Y = 0;
            if (kb.LengthSquared() > 1e-8f)
                _world.Get<Velocity>(target).Vel += Vector3.Normalize(kb) * knockback;
        }

        SpawnNumber(t.Pos + Vector3.UnitY * 1.4f, dmg);
        if (_world.Has<AI>(target)) Aggro(target);
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
        if (_world.Has<AnimState>(attacker) && _world.Get<AnimState>(attacker).Lock > 0) return;

        const string clip = "slash01";
        float dur = PlayOneShot(attacker, clip);
        if (dur <= 0) { c.Cd = 0.45f; MeleeHit(attacker, c.Damage, c.Range); return; }

        c.Cd = MathF.Max(0.45f, dur);
        if (_world.Has<PendingAction>(attacker))
            _world.Get<PendingAction>(attacker) = new PendingAction
            { Kind = 0, At = HitAtFor(clip), Damage = c.Damage, Range = c.Range };
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

    private void ExecuteAction(int e, PendingAction a)
{
    switch (a.Kind)
    {
        case 0: MeleeHit(e, a.Damage, a.Range); break;
        case 1: SpawnBolt(e, a.Dir, a.Damage); break;
        case 2: Nova(e, a.Damage, a.Range); break;
        case 3: Heal(e, a.Damage); break;
    }
}

    private void MeleeHit(int attacker, float dmg, float range)
    {
        var ap = _world.Get<Transform>(attacker);
        var fwd = new Vector3(MathF.Sin(ap.Yaw), 0, MathF.Cos(ap.Yaw));
        float kb = _world.Get<Combat>(attacker).Knockback;
        foreach (int m in _world.Set<MobTag>().Entities())
        {
            if (_world.Get<Health>(m).Dead) continue;
            var d = _world.Get<Transform>(m).Pos - ap.Pos; d.Y = 0;
            float len = d.Length();
            if (len < range && (len < 1e-4f || Vector3.Dot(d / len, fwd) > 0.3f))
                Hit(m, dmg, ap.Pos, kb);
        }
    }

    private void SpawnBolt(int caster, Vector3 dir, float dmg)
    {
        var from = _world.Get<Transform>(caster).Pos;
        int e = _world.Create();
        _world.Add<Transform>(e) = new Transform { Pos = from + Vector3.UnitY + dir * 0.6f };
        _world.Add<Velocity>(e) = new Velocity { Vel = dir * 14f };
        _world.Add<Projectile>(e) = new Projectile
        { Damage = dmg, Radius = 0.5f, Life = 2f, Knockback = _world.Get<Combat>(caster).Knockback };
        _world.Add<Renderable>(e) = new Renderable { Mesh = _quad, Size = new Vector3(0.3f), Tint = new Vector3(2f, 0.8f, 0.2f) };
    }

    private void Nova(int caster, float dmg, float range)
    {
        var pp = _world.Get<Transform>(caster).Pos;
        float kb = _world.Get<Combat>(caster).Knockback;
        foreach (int m in _world.Set<MobTag>().Entities())
            if (!_world.Get<Health>(m).Dead && (_world.Get<Transform>(m).Pos - pp).Length() < range)
                Hit(m, dmg, pp, kb);
        int e = _world.Create();
        _world.Add<Transform>(e) = new Transform { Pos = pp };
        _world.Add<Effect>(e) = new Effect { Dur = 0.4f, Radius = range };
    }

    private void Heal(int e, float amount)
    {
        ref var h = ref _world.Get<Health>(e);
        h.Hp = MathF.Min(h.Max, h.Hp + amount);
        SpawnNumber(_world.Get<Transform>(e).Pos + Vector3.UnitY * 1.6f, amount, new Vector3(0.3f, 1f, 0.4f));
    }
}