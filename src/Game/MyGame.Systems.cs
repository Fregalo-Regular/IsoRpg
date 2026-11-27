using System.Numerics;
using Engine.Graphics;
using Silk.NET.OpenGL;
using Engine.ECS;

namespace Game;

internal partial class MyGame
{
    private void InputSystem()
    {
        ref var at = ref _world.Get<AttackTarget>(_player);
        _camera.ScreenRay(MouseScreen, out var o, out var d);

        if (_lmbEdge)
        {
            _lmbEdge = false;
            int ui = HitAbilityCircle(MouseScreen);
            int mob = PickMob(o, d);
            if (ui >= 0) { _selectedAbility = ui; _lmbHeld = false; }  // не двигаем/не атакуем
            else if (mob >= 0) { at.Entity = mob; at.Active = true; }
            else
            {
                at.Active = false;
                if (_height.Raycast(o, d, 400f, out var h))
                { ref var mv = ref _world.Get<Movement>(_player); mv.Target = new Vector3(h.X, 0, h.Z); mv.Moving = true; }
            }
        }
        // hold-to-move только когда НЕ атакуем
        if (_lmbHeld && !at.Active && !_imgui.WantMouse && _height.Raycast(o, d, 400f, out var h2))
        { ref var mv = ref _world.Get<Movement>(_player); mv.Target = new Vector3(h2.X, 0, h2.Z); mv.Moving = true; }
    }

    private void MovementSystem(float dt)
    {
        var moves = _world.Set<Movement>();
        var obstacles = _world.Set<ObstacleTag>();
        foreach (int e in moves.Entities())
        {
            if (_world.Has<Health>(e) && _world.Get<Health>(e).Dead) continue;
            ref var mv = ref moves.Get(e);
            ref var tr = ref _world.Get<Transform>(e);
            float radius = _world.Get<Collider>(e).Radius;

            if (mv.Moving)
            {
                var d = mv.Target - tr.Pos; d.Y = 0; float dist = d.Length();
                if (dist < 0.05f) mv.Moving = false;
                else
                {
                    var dir = d / dist;
                    var desired = tr.Pos + dir * MathF.Min(mv.Speed * dt, dist);
                    if (_height.Sample(desired.X, desired.Z) - tr.Pos.Y <= _maxStep)
                    {
                        float lim = (_grid - 1) / 2f * Tile;
                        desired.X = Math.Clamp(desired.X, -lim, lim);
                        desired.Z = Math.Clamp(desired.Z, -lim, lim);
                        foreach (int o in obstacles.Entities())
                        {
                            if (_world.Has<Velocity>(e))
                            {
                                ref var v = ref _world.Get<Velocity>(e);
                                tr.Pos += v.Vel * dt; v.Vel *= MathF.Exp(-6f * dt);
                            }

                            if (o == e) continue;
                            var ot = _world.Get<Transform>(o); var oc = _world.Get<Collider>(o);
                            var d2 = new Vector2(desired.X - ot.Pos.X, desired.Z - ot.Pos.Z);
                            float r = oc.Radius + radius, len = d2.Length();
                            if (len < r && len > 1e-4f) { var n = d2 / len; desired.X = ot.Pos.X + n.X * r; desired.Z = ot.Pos.Z + n.Y * r; }
                        }
                        tr.Pos = desired;
                        tr.Yaw = MathF.Atan2(dir.X, dir.Z);
                        mv.Walk += dt;
                    }
                    else mv.Moving = false;
                }
            }
            float g = _height.Sample(tr.Pos.X, tr.Pos.Z);
            tr.Pos.Y += (g - tr.Pos.Y) * (1f - MathF.Exp(-12f * dt));
        }
    }

    private void CameraSystem(float dt)
    {
        if (!_follow) return;
        var p = _world.Get<Transform>(_player).Pos;
        _camera.Target = Vector3.Lerp(_camera.Target, p, 1f - MathF.Exp(-5f * dt));
    }

    private void RenderSystem()
    {
        foreach (int e in _world.Set<Renderable>().Entities())
        {
            if (_world.Has<Health>(e) && _world.Get<Health>(e).Dead) continue;
            var r = _world.Get<Renderable>(e);
            var tr = _world.Get<Transform>(e);
            var tint = r.Tint;
            if (_world.Has<Combat>(e) && _world.Get<Combat>(e).Flash > 0) tint = new Vector3(2f, 0.6f, 0.5f);

            if (r.Model != null)
            {
                if (r.Model.Skeleton is { } sk)
                {
                    bool moving = _world.Has<Movement>(e) && _world.Get<Movement>(e).Moving;
                    if (moving) sk.Play(r.AnimIndex, _world.Get<Movement>(e).Walk);
                    else sk.UpdateBindPose();
                }
                DrawModel(r.Model, tr.Pos, r.Scale, tr.Yaw, tint);
            }
            else if (r.Mesh != null)
            {
                Gl.Disable(EnableCap.CullFace);
                _shader.SetUniform("uSkinned", 0);
                _shader.SetUniform("uUseTexture", 1);
                _tile.Bind(TextureUnit.Texture0);
                _shader.SetUniform("uModel", Matrix4x4.CreateScale(r.Size) * Matrix4x4.CreateTranslation(tr.Pos));
                _shader.SetUniform("uTint", r.Tint);
                r.Mesh.Draw();
                if (_cull) Gl.Enable(EnableCap.CullFace);
            }
            if (_world.Has<Health>(e) && _world.Get<Health>(e).Dead) continue;
            if (_world.Has<Combat>(e) && _world.Get<Combat>(e).Flash > 0) tint = new Vector3(2f, 0.6f, 0.5f);
        }
    }

    private void CooldownSystem(float dt)
    {
        foreach (int e in _world.Set<Combat>().Entities())
        {
            ref var c = ref _world.Get<Combat>(e);
            c.Cd = MathF.Max(0, c.Cd - dt);
            c.Flash = MathF.Max(0, c.Flash - dt);
        }
        foreach (int e in _world.Set<Health>().Entities())
        {
            ref var h = ref _world.Get<Health>(e);
            if (!h.Dead) continue;
            h.Respawn -= dt;
            if (h.Respawn <= 0)
            {
                h.Dead = false; h.Hp = h.Max;
                _world.Get<Transform>(e).Pos = _world.Has<PlayerTag>(e) ? new Vector3(-6, 0, 0) : new Vector3(10, 0, 8);
                _world.Get<Velocity>(e).Vel = Vector3.Zero;
                if (_world.Has<AI>(e)) _world.Get<AI>(e).Angry = false;      // ← забывался
                if (_world.Has<Movement>(e)) _world.Get<Movement>(e).Moving = false;
            }
        }
        ref var ab = ref _world.Get<Abilities>(_player);
        for (int i = 0; i < 3; i++)
        {
            ref var st = ref AbilityRef(ref ab, i); 
            st.Max = _maxCharges;                       // позже — от уровня способности
            if (st.Charges < st.Max)
            {
                st.Regen += dt;
                if (st.Regen >= st.Cd) { st.Regen -= st.Cd; st.Charges++; }
            }
            else st.Regen = 0;
        }
    }

    private void NumberSystem(float dt)
    {
        var nums = _world.Set<Number>();
        var ids = nums.Entities().ToArray();
        foreach (int e in ids)
        {
            if (!nums.Has(e)) continue;
            ref var n = ref nums.Get(e);
            n.T += dt;
            if (n.T > 0.8f) _world.Destroy(e);
        }
    }

    private void AISystem()
    {
        var pt = _world.Get<Transform>(_player).Pos;
        bool playerDead = _world.Get<Health>(_player).Dead;

        foreach (int m in _world.Set<MobTag>().Entities())
        {
            if (_world.Get<Health>(m).Dead) continue;
            ref var mv = ref _world.Get<Movement>(m);
            ref var c = ref _world.Get<Combat>(m);
            var ai = _world.Get<AI>(m);

            var to = pt - _world.Get<Transform>(m).Pos; to.Y = 0;
            float dist = to.Length();

            // радиус будит пачку (урон уже будит через Hit)
            if (!playerDead && dist < ai.Aggro && !ai.Angry) Aggro(m);
            bool angry = _world.Get<AI>(m).Angry;

            if (!playerDead && angry)
            {
                if (dist > c.Range) { mv.Target = pt; mv.Moving = true; }
                else
                {
                    mv.Moving = false;
                    if (c.Cd <= 0) { c.Cd = 1f; Hit(_player, c.Damage, _world.Get<Transform>(m).Pos); }
                }
            }
            else mv.Moving = false;
        }
    }

    private void SeparationSystem()
    {
        var ids = _world.Set<MobTag>().Entities().ToArray();
        for (int i = 0; i < ids.Length; i++)
            for (int j = i + 1; j < ids.Length; j++)
            {
                int a = ids[i], b = ids[j];
                if (_world.Get<Health>(a).Dead || _world.Get<Health>(b).Dead) continue;
                ref var pa = ref _world.Get<Transform>(a);
                ref var pb = ref _world.Get<Transform>(b);
                var d = new Vector2(pb.Pos.X - pa.Pos.X, pb.Pos.Z - pa.Pos.Z);
                float r = _world.Get<Collider>(a).Radius + _world.Get<Collider>(b).Radius;
                float len = d.Length();
                if (len < r)
                {
                    var n = len > 1e-4f ? d / len : new Vector2(1, 0);
                    float push = (r - len) * 0.5f;
                    pa.Pos.X -= n.X * push; pa.Pos.Z -= n.Y * push;
                    pb.Pos.X += n.X * push; pb.Pos.Z += n.Y * push;
                }
            }
    }

    private void AutoAttackSystem()
    {
        ref var at = ref _world.Get<AttackTarget>(_player);
        if (!at.Active || !_lmbHeld || _world.Get<Health>(_player).Dead) return;
        int t = at.Entity;
        if (!_world.Has<Health>(t) || _world.Get<Health>(t).Dead) { at.Active = false; return; }

        ref var mv = ref _world.Get<Movement>(_player);
        ref var pt = ref _world.Get<Transform>(_player);
        var d = _world.Get<Transform>(t).Pos - pt.Pos; d.Y = 0;
        pt.Yaw = MathF.Atan2(d.X, d.Z);                       // всегда лицом к цели
        if (d.Length() > _world.Get<Combat>(_player).Range) { mv.Target = _world.Get<Transform>(t).Pos; mv.Moving = true; }
        else { mv.Moving = false; PerformAttack(_player); }
    }

    private void CastSystem()
    {
        if (!_rmbEdge) return; _rmbEdge = false;
        if (_world.Get<Health>(_player).Dead) return;
        var def = _defs[_selectedAbility];
        ref var ab = ref _world.Get<Abilities>(_player);
        ref var st = ref AbilityRef(ref ab, _selectedAbility);
        if (st.Charges <= 0) return;
        st.Charges--;
        var pp = _world.Get<Transform>(_player).Pos;

        if (_selectedAbility == 0)
        {
            var at = _world.Get<AttackTarget>(_player);
            _camera.ScreenRay(MouseScreen, out var ro, out var rd);
            Vector3 aim = at.Active
                ? _world.Get<Transform>(at.Entity).Pos
                : (_height.Raycast(ro, rd, out var h) ? h : pp + Vector3.UnitZ);
            var dir = aim - pp; dir.Y = 0; dir = Vector3.Normalize(dir);
            int e = _world.Create();
            _world.Add<Transform>(e) = new Transform { Pos = pp + Vector3.UnitY + dir * 0.6f };
            _world.Add<Velocity>(e) = new Velocity { Vel = dir * 14f };
            _world.Add<Projectile>(e) = new Projectile { Damage = 40, Radius = 0.5f, Life = 2f };
            Vertex[] vertices =
            [
            new() { Position = new(-0.5f, 0f,  0.5f), Color = Vector3.UnitY, UV = new(0f, 0f) },
                    new() { Position = new( 0.5f, 0f,  0.5f), Color = Vector3.UnitY, UV = new(1f, 0f) },
                    new() { Position = new( 0.5f, 0f, -0.5f), Color = Vector3.UnitY, UV = new(1f, 1f) },
                    new() { Position = new(-0.5f, 0f, -0.5f), Color = Vector3.UnitY, UV = new(0f, 1f) },
                ];
                    ushort[] indices = [0, 1, 2, 2, 3, 0];
                    _world.Add<Renderable>(e) = new Renderable { Mesh = new Mesh(Gl, vertices, indices), Size = new Vector3(0.3f, 0.3f, 0.3f), Tint = new Vector3(2f, 0.8f, 0.2f) };
                }
        else if (_selectedAbility == 1)
        {
            foreach (int m in _world.Set<MobTag>().Entities())
                if (!_world.Get<Health>(m).Dead && (_world.Get<Transform>(m).Pos - pp).Length() < def.Range)
                    Hit(m, 30, pp);
            int e = _world.Create();
            _world.Add<Transform>(e) = new Transform { Pos = pp };
            _world.Add<Effect>(e) = new Effect { Dur = 0.4f, Radius = def.Range };
        }
        else
        {
            ref var h = ref _world.Get<Health>(_player);
            h.Hp = MathF.Min(h.Max, h.Hp + 40);
            SpawnNumber(pp + Vector3.UnitY * 1.6f, 40, new Vector3(0.3f, 1f, 0.4f));
        }
    }

    private void ProjectileSystem(float dt)
    {
        var ids = _world.Set<Projectile>().Entities().ToArray();
        foreach (int e in ids)
        {
            if (!_world.Has<Projectile>(e)) continue;
            ref var p = ref _world.Get<Projectile>(e);
            ref var tr = ref _world.Get<Transform>(e);
            tr.Pos += _world.Get<Velocity>(e).Vel * dt;
            p.Life -= dt;
            bool dead = p.Life <= 0;
            foreach (int m in _world.Set<MobTag>().Entities())
            {
                if (_world.Get<Health>(m).Dead) continue;
                if ((_world.Get<Transform>(m).Pos + Vector3.UnitY * 0.5f - tr.Pos).Length() < p.Radius + _world.Get<Collider>(m).Radius)
                { Hit(m, p.Damage, tr.Pos); dead = true; break; }
            }
            if (dead) _world.Destroy(e);
        }
    }

    private void EffectSystem(float dt)
    {
        var ids = _world.Set<Effect>().Entities().ToArray();
        foreach (int e in ids)
        { ref var f = ref _world.Get<Effect>(e); f.T += dt; if (f.T > f.Dur) _world.Destroy(e); }
    }
}