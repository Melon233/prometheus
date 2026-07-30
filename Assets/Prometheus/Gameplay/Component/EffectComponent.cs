using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Logic;
namespace Xuan.Prometheus.Component
{
    // public interface IAttacker
    // { }
    // public interface IDefender
    // { }
    // public class DamageInfo
    // {
    //     public IAttacker attacker;
    //     public IDefender defender;
    //     public List<Effect> effects = new();
    // }
    public enum DurationType
    {
        Instant,
        Normal,
        Permanent
    }
    public abstract class Effect
    {
        public static int nextUid;
        public int uid;
        public int eid;
        public float curTickTime;
        public float curTime;
        public float duration;
        public int maxStacks;
        public int curStacks = 1;
        public List<float> paras = new() { };
        public float tickTime = 1f;
        public float NormalizedTime => curTime / duration;
        public bool IsOver => curTime > duration;
        public Entity owner;
        public Entity caster;
        public DurationType durationType = DurationType.Normal;
        public EffectComponent ownerEffectComp;
        public Effect()
        {
            uid = nextUid++;
        }
        public virtual void OnStack() { }
        public virtual void OnUpdate(float dt) { }
        public virtual void OnRemove() { }
    }
    public class DamageEffect : Effect, IAdd<float>
    {
        public DamageEffect() : base()
        {
            durationType = DurationType.Instant;
        }

        public void OnAdd(float dmg)
        {
            owner.TryGetComp(out PropertyComponent propComp);
            owner.TryGetComp(out EventComponent eventComp);
            var actualDmg = propComp.OnTakeDamage(dmg);
            if (propComp.NoHp) eventComp.Invoke(new DieEvent());//控制死亡，最大化Effect能力边界
            else
            {
                eventComp.Invoke(new AttackedEvent());
                eventComp.Invoke(new HpChangedEvent() { newHp = propComp.Hp + actualDmg, maxHp = propComp.propConfig.hp });
            }
            ownerEffectComp.toRemoveEffects.Add(this);
        }
    }
    public class RecoverEffect : Effect, IAdd<float>
    {
        public RecoverEffect() : base()
        {
            durationType = DurationType.Instant;
        }

        public void OnAdd(float recover)
        {
            owner.TryGetComp(out PropertyComponent propComp);
            owner.TryGetComp(out EventComponent eventComp);
            propComp.OnRecoverHp(recover);
            eventComp.Invoke(new HpChangedEvent() { oldHp = propComp.Hp - recover, newHp = propComp.Hp, maxHp = propComp.propConfig.hp });
            ownerEffectComp.toRemoveEffects.Add(this);
        }
    }
    public class FireDotEffect : Effect, IAdd
    {
        public float dotDmg = 1f;
        PropertyComponent propComp;
        EventComponent evtComp;
        EffectComponent casterEffectComp;

        public FireDotEffect() : base()
        {
            duration = 10f;
            tickTime = 1f;
        }

        public void OnAdd()
        {
            owner.TryGetComp(out propComp);
            owner.TryGetComp(out evtComp);
            caster.TryGetComp(out casterEffectComp);
        }

        public override void OnUpdate(float dt)
        {
            curTime += dt;
            if (IsOver)
            {
                ownerEffectComp.toRemoveEffects.Add(this);
                return;
            }
            curTickTime += dt;
            if (curTickTime > tickTime)
            {
                curTickTime -= tickTime;
                var dmg = propComp.OnTakeDamage(dotDmg);
                ownerEffectComp.AddEffect<StiffnessEffect, float>(caster, owner, 2f);
                casterEffectComp.AddEffect<RecoverEffect, float>(owner, caster, dmg * 10f);
                if (propComp.NoHp) evtComp.Invoke(new DieEvent());//控制死亡，最大化Effect能力边界
                else
                {
                    evtComp.Invoke(new AttackedEvent());
                    evtComp.Invoke(new HpChangedEvent() { oldHp = propComp.Hp + dmg, newHp = propComp.Hp, maxHp = propComp.propConfig.hp });
                }
            }
        }
    }
    public class StiffnessEffect : Effect, IAdd<float>
    {
        EventComponent evtComp;
        public StiffnessEffect() : base()
        {
        }

        public void OnAdd(float duration)
        {
            this.duration = duration;
            owner.TryGetComp(out evtComp);
            evtComp.Invoke(new StiffnessStartEvent());
        }
        public override void OnStack()
        {
            curTime = 0f;
        }
        public override void OnUpdate(float dt)
        {
            curTime += dt;
            if (IsOver)
            {
                evtComp.Invoke(new StiffnessEndEvent());
                ownerEffectComp.toRemoveEffects.Add(this);
            }
        }
    }
    public class YefaCoreTalentEffect : Effect, IAdd
    {
        EventComponent evtComp;
        public YefaCoreTalentEffect() : base()
        {
            durationType = DurationType.Permanent;
        }
        public void OnAdd()
        {
            owner.TryGetComp(out evtComp);
            owner.TryGetComp(out ownerEffectComp);
            evtComp.AddListener<HitEvent>(OnHit);
        }
        private void OnHit(HitEvent @event)
        {
            ownerEffectComp.AddEffect<CombatFlowEffect>(caster, owner);
        }

        public override void OnRemove()
        {
            evtComp.RemoveListener<HitEvent>(OnHit);
        }
    }
    public class CombatFlowEffect : Effect, IAdd
    {
        PropertyComponent propComp;
        float atkBoost = 1f;
        float atkSpeedBoost = 1f;
        float moveSpeedBoost = 0.2f;
        float critRateBoost = 0.2f;
        float critDmgBoost = 0.2f;
        public CombatFlowEffect() : base()
        {
            duration = 3f;
        }
        public void OnAdd()
        {
            owner.TryGetComp(out propComp);
            propComp.atkBoost += atkBoost;
            propComp.atkSpeedBoost += atkSpeedBoost;
            propComp.moveSpeedBoost += moveSpeedBoost;
            propComp.critRateBoost += critRateBoost;
            propComp.critDmgBoost += critDmgBoost;
            Debug.Log($"CombatFlowEffect: {curStacks}");
        }
        public override void OnStack()
        {
            curTime = 0f;
            if (curStacks == maxStacks) return;
            curStacks++;
            propComp.atkBoost += atkBoost;
            propComp.atkSpeedBoost += atkSpeedBoost;
            propComp.moveSpeedBoost += moveSpeedBoost;
            propComp.critRateBoost += critRateBoost;
            propComp.critDmgBoost += critDmgBoost;
            Debug.Log($"CombatFlowEffect: {curStacks}");
        }
        public override void OnUpdate(float dt)
        {
            curTime += dt;
            if (IsOver)
            {
                ownerEffectComp.toRemoveEffects.Add(this);
                return;
            }
        }
        public override void OnRemove()
        {
            propComp.atkBoost -= atkBoost * curStacks;
            propComp.atkSpeedBoost -= atkSpeedBoost * curStacks;
            propComp.moveSpeedBoost -= moveSpeedBoost * curStacks;
            propComp.critRateBoost -= critRateBoost * curStacks;
            propComp.critDmgBoost -= critDmgBoost * curStacks;
            Debug.Log($"Lose stacks: {curStacks}");
        }
    }
    public interface IAdd { void OnAdd(); }
    public interface IAdd<P1> { void OnAdd(P1 p1); }
    public interface IAdd<P1, P2> { void OnAdd(P1 p1, P2 p2); }

    public class EffectComponent : MonoComponent
    {
        public List<Effect> toAddEffects = new();
        public List<Effect> toRemoveEffects = new();
        public XMap<Type, Effect> effects = new();
        public void AddEffect<TEffect>(Entity caster, Entity owner) where TEffect : Effect, IAdd, new()
        {
            if (effects.TryGet(typeof(TEffect), out var e))
            {
                e.OnStack();
                return;
            }
            TEffect effect = new()
            {
                owner = owner,
                caster = caster,
                ownerEffectComp = this
            };
            toAddEffects.Add(effect);
            effect.OnAdd();
        }
        public void AddEffect<TEffect, P1>(Entity caster, Entity owner, P1 p1) where TEffect : Effect, IAdd<P1>, new()
        {
            if (effects.TryGet(typeof(TEffect), out var e))
            {
                e.OnStack();
                return;
            }
            TEffect effect = new()
            {
                owner = owner,
                caster = caster,
                ownerEffectComp = this
            };
            toAddEffects.Add(effect);
            effect.OnAdd(p1);
        }
        public void AddEffect<TEffect, P1, P2>(Entity caster, Entity owner, P1 p1, P2 p2) where TEffect : Effect, IAdd<P1, P2>, new()
        {
            if (effects.TryGet(typeof(TEffect), out var e))
            {
                e.OnStack();
                return;
            }
            TEffect effect = new()
            {
                owner = owner,
                caster = caster,
                ownerEffectComp = this
            };
            toAddEffects.Add(effect);
            effect.OnAdd(p1, p2);
        }
    }
}