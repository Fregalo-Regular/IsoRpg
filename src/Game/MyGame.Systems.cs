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
        //hold-to-move только когда НЕ атакуем
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

            // Knockback: один раз за кадр, независимо от Moving (раньше был внутри цикла obstacles и затирался)
            if (_world.Has<Velocity>(e))
            {
                ref var v = ref _world.Get<Velocity>(e);
                tr.Pos += v.Vel * dt;
                v.Vel *= MathF.Exp(-6f * dt);
            }

            bool locked = _world.Has<AnimState>(e) && _world.Get<AnimState>(e).Lock > 0;
            if (mv.Moving && !locked)
            {
                var d = mv.Target - tr.Pos; d.Y = 0; float dist = d.Length();
                if (dist < 0.05f) mv.Moving = false;
                else
                {
                    float speed = mv.Speed * (e == _player && _shiftHeld ? 0.75f : 1.75f);
                    var dir = d / dist;
                    var desired = tr.Pos + dir * MathF.Min(speed * dt, dist);
                    if (_height.Sample(desired.X, desired.Z) - tr.Pos.Y <= _maxStep)
                    {
                        float lim = (_grid - 1) / 2f * Tile;
                        desired.X = Math.Clamp(desired.X, -lim, lim);
                        desired.Z = Math.Clamp(desired.Z, -lim, lim);
                        foreach (int o in obstacles.Entities())
                        {
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
            bool dead = _world.Has<Health>(e) && _world.Get<Health>(e).Dead;
            if (dead && !_world.Has<AnimState>(e)) continue;   // без death-клипа исчезаем как раньше

            var r = _world.Get<Renderable>(e);
            var tr = _world.Get<Transform>(e);
            var tint = r.Tint;
            if (!dead && _world.Has<Combat>(e) && _world.Get<Combat>(e).Flash > 0) tint = new Vector3(2f, 0.6f, 0.5f);

            if (r.Model != null)
            {
                if (r.Model.Skeleton is { } sk)
                {
                    if (_world.Has<AnimState>(e))
                    {
                        var a = _world.Get<AnimState>(e);
                        sk.Play(a.Clip, a.Time, a.Loop);
                    }
                    else
                    {
                        bool moving = _world.Has<Movement>(e) && _world.Get<Movement>(e).Moving;
                        if (moving) sk.Play(r.AnimIndex, _world.Get<Movement>(e).Walk);
                        else sk.UpdateBindPose();
                    }
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

            if (_world.Has<AnimState>(e))
            {
                var mdl = _world.Get<Renderable>(e).Model;
                int idle = mdl is not null && mdl.Anims.TryGetValue("idle01", out var ix) ? ix : 0;
                _world.Get<AnimState>(e) = new AnimState { Clip = idle, Loop = true };
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

            //радиус будит пачку (урон уже будит через Hit)
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
        if (_world.Has<AnimState>(_player) && _world.Get<AnimState>(_player).Lock > 0) return; // каст/атака держит позу и Yaw
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
        if (_world.Has<AnimState>(_player) && _world.Get<AnimState>(_player).Lock > 0) return;

        var def = _defs[_selectedAbility];
        ref var ab = ref _world.Get<Abilities>(_player);
        ref var st = ref AbilityRef(ref ab, _selectedAbility);
        if (st.Charges <= 0) return;
        st.Charges--;

        var pp = _world.Get<Transform>(_player).Pos;
        var aim = AimPoint(pp);

        // Поворот переживёт каст: MovementSystem стоит под Lock и не перезапишет Yaw.
        if (def.FaceTarget)
        {
            var f = aim - pp; f.Y = 0;
            if (f.LengthSquared() > 1e-6f)
                _world.Get<Transform>(_player).Yaw = MathF.Atan2(f.X, f.Z);
        }

        PlayOneShot(_player, def.Clip);

        float dur = PlayOneShot(_player, def.Clip);

        var dir = aim - pp; dir.Y = 0;
        float yaw = _world.Get<Transform>(_player).Yaw;
        dir = dir.LengthSquared() > 1e-6f
            ? Vector3.Normalize(dir)
            : new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw));

        var action = new PendingAction
        {
            Kind = _selectedAbility + 1,
            At = HitAtFor(def.Clip),
            Damage = def.Power,
            Range = def.Range,
            Dir = dir,
        };

        if (dur <= 0) ExecuteAction(_player, action);          // нет клипа — работаем по-старому
        else _world.Get<PendingAction>(_player) = action;
    }

    private Vector3 AimPoint(Vector3 from)
    {
        var at = _world.Get<AttackTarget>(_player);
        if (at.Active && _world.Has<Health>(at.Entity) && !_world.Get<Health>(at.Entity).Dead)
            return _world.Get<Transform>(at.Entity).Pos;
        _camera.ScreenRay(MouseScreen, out var ro, out var rd);
        return _height.Raycast(ro, rd, out var h) ? h : from + Vector3.UnitZ;
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

    private void AnimSystem(float dt)
    {
        foreach (int e in _world.Set<AnimState>().Entities())
        {
            ref var a = ref _world.Get<AnimState>(e);
            var r = _world.Get<Renderable>(e);
            if (r.Model?.Skeleton is not { } sk) continue;

            a.Time += dt * _animRate;
            if (a.Lock > 0)
            {
                a.Lock = MathF.Max(0, a.Lock - dt);
                if (a.Lock > 0) continue;   // атака/смерть доигрывает до конца
            }

            bool dead = _world.Has<Health>(e) && _world.Get<Health>(e).Dead;
            bool moving = _world.Has<Movement>(e) && _world.Get<Movement>(e).Moving;
            string want = dead ? "death" : moving ? (_shiftHeld ? "walk" : "run01") : "idle01";
            if (!r.Model.Anims.TryGetValue(want, out int clip)) continue;

            if (clip != a.Clip) { a.Clip = clip; a.Time = 0; }
            a.Loop = !dead;
            if (dead) a.Lock = sk.Duration(clip) / _animRate;   // труп держит позу до респауна
        }
    }

    /// Одноразовый клип с локом движения. Возвращает длительность (0 — клипа нет).
    private float PlayOneShot(int e, string clipName)
    {
        if (!_world.Has<AnimState>(e)) return 0f;
        var model = _world.Get<Renderable>(e).Model;
        if (model?.Skeleton is not { } sk) return 0f;
        if (!model.Anims.TryGetValue(clipName, out int clip)) return 0f;

        ref var a = ref _world.Get<AnimState>(e);
        a.Clip = clip; a.Time = 0; a.Loop = false; a.Lock = sk.Duration(clip);
        return a.Lock;
    }

    private void ActionSystem()
    {
        var acts = _world.Set<PendingAction>();
        foreach (int e in acts.Entities().ToArray())
        {
            if (!acts.Has(e)) continue;
            ref var slot = ref acts.Get(e);
            if (slot.Fired) continue;

            bool dead = _world.Has<Health>(e) && _world.Get<Health>(e).Dead;
            var model = _world.Get<Renderable>(e).Model;
            if (dead || model?.Skeleton is not { } sk || !_world.Has<AnimState>(e))
            { slot.Fired = true; continue; }        // смерть отменяет замах

            var a = _world.Get<AnimState>(e);
            float dur = sk.Duration(a.Clip);
            if (dur <= 0 || a.Time < slot.At * dur) continue;

            var action = slot;      // копия: Execute создаёт сущности, ref может протухнуть
            slot.Fired = true;
            ExecuteAction(e, action);
        }
    }
}