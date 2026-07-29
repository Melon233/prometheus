using System.Collections.Generic;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Component
{
    public interface IAttacker
    { }
    public interface IDefender
    { }
    public class DamageInfo
    {
        public IAttacker attacker;
        public IDefender defender;
        public List<Effect> effects = new();
    }
    public abstract class Effect
    {
        public static int nextUid;
        public int uid;
        public int eid;
        public float curTickTime;
        public float curTime;
        public float duration;
        public List<float> paras = new() { };
        public float tickTime = 1f;
        public float NormalizedTime => curTime / duration;
        public bool IsOver => curTime >= duration;
        public Entity owner;
        public Entity caster;
        public bool isInstant;
        public Effect(Entity caster, Entity owner)
        {
            this.caster = caster;
            this.owner = owner;
            uid = nextUid++;
        }
        public abstract void OnUpdate(float dt);
    }
    public class DamageEffect : Effect
    {
        public float damage;
        public DamageEffect(Entity caster, Entity owner, float damage) : base(caster, owner)
        {
            isInstant = true;
            this.damage = damage; // 100ms
        }
        public override void OnUpdate(float dt)
        {
            owner.TryGetComp(out PropertyComponent propComp);
            owner.TryGetComp(out EventComponent eventComp);
            propComp.OnTakeDamage(damage);
            if (propComp.NoHp) eventComp.Invoke(new DieEvent());//控制死亡，最大化Effect能力边界
            else
            {
                eventComp.Invoke(new AttackedEvent());
                eventComp.Invoke(new HpChangedEvent() { hp = propComp.curHp, maxHp = propComp.propConfig.hp });
            }
        }
    }
    public class RecoverEffect : Effect
    {
        public float recover;
        public RecoverEffect(Entity caster, Entity owner, float recover) : base(caster, owner)
        {
            isInstant = true;
            this.recover = recover; // 100ms
        }
        public override void OnUpdate(float dt)
        {
            owner.TryGetComp(out PropertyComponent propComp);
            owner.TryGetComp(out EventComponent eventComp);
            propComp.OnRecoverHp(recover);
            eventComp.Invoke(new HpChangedEvent() { hp = propComp.curHp, maxHp = propComp.propConfig.hp });
        }
    }
    public class FireDotEffect : Effect
    {
        public float dotDmg = 1f;
        PropertyComponent propComp;
        EventComponent evtComp;
        EffectComponent otherEffectComp;
        EffectComponent selfEffectComp;

        public FireDotEffect(Entity caster, Entity owner) : base(caster, owner)
        {
            duration = 10f;
            tickTime = 1f;
            owner.TryGetComp(out propComp);
            owner.TryGetComp(out evtComp);
            caster.TryGetComp(out otherEffectComp);
            owner.TryGetComp(out selfEffectComp);
        }
        public override void OnUpdate(float dt)
        {
            curTime += dt; // 200ms
            curTickTime += dt;
            if (curTickTime > tickTime)
            {
                curTickTime -= tickTime;
                var dmg = propComp.OnTakeDamage(dotDmg);
                selfEffectComp.toAddEffects.Add(new StiffnessEffect(caster, owner, 3f));
                otherEffectComp.toAddEffects.Add(new RecoverEffect(owner, caster, dmg * 10f));
                if (propComp.NoHp) evtComp.Invoke(new DieEvent());//控制死亡，最大化Effect能力边界
                else
                {
                    evtComp.Invoke(new AttackedEvent());
                    evtComp.Invoke(new HpChangedEvent() { hp = propComp.curHp, maxHp = propComp.propConfig.hp });
                }
            }
        }
    }
    public class StiffnessEffect : Effect
    {
        EventComponent evtComp;
        public StiffnessEffect(Entity caster, Entity owner, float duration) : base(caster, owner)
        {
            base.duration = duration;
            owner.TryGetComp(out evtComp);
            evtComp.Invoke(new StiffnessStartEvent());
        }
        public override void OnUpdate(float dt)
        {
            curTime += dt; // 200ms
            if (IsOver)
                evtComp.Invoke(new StiffnessEndEvent());
        }
    }
    public class EffectComponent : MonoComponent, IDefender
    {
        public List<Effect> toAddEffects = new();
        public XMap<int, Effect> effects = new();
        public List<Effect> toRemoveEffects = new();
    }
}