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
        public float tickTime = 2f;
        public float NormalizedTime => curTime / duration;
        public bool IsOver => curTime >= duration;
        public Entity owner;
        public bool isInstant;
        public Effect(Entity owner)
        {
            this.owner = owner;
            uid = nextUid++;
        }
        public abstract void OnUpdate(float dt);
    }
    public class DamageEffect : Effect
    {
        public float damage = 10f;
        public DamageEffect(Entity owner) : base(owner)
        {
            isInstant = true;
        }
        public override void OnUpdate(float dt)
        {
            owner.TryGetComp(out PropertyComponent propComp);
            owner.TryGetComp(out EventComponent eventComp);
            propComp.OnTakeDamage(damage);
            if (propComp.NoHp) eventComp.Invoke(EventName.Die);//控制死亡，最大化Effect能力边界
            else eventComp.Invoke(EventName.Attacked);
        }
    }
    public class FireDotEffect : Effect
    {
        public float dotDmg = 5f;
        public FireDotEffect(Entity owner) : base(owner)
        {
            duration = 10f;
            tickTime = 1f;
        }
        public override void OnUpdate(float dt)
        {
            if (IsOver) return;
            curTime += dt; // 200ms
            curTickTime += dt;
            if (curTickTime > tickTime)
            {
                curTickTime -= tickTime;
                owner.TryGetComp(out PropertyComponent propComp);
                owner.TryGetComp(out EventComponent eventComp);
                propComp.OnTakeDamage(dotDmg);
                if (propComp.NoHp) eventComp.Invoke(EventName.Die);//控制死亡，最大化Effect能力边界
                else eventComp.Invoke(EventName.Attacked);
            }
        }
    }
    public class EffectComponent : MonoComponent, IDefender
    {
        public List<Effect> toAddEffects = new();
        public XMap<int, Effect> effects = new();
        public List<Effect> toRemoveEffects = new();
    }
}