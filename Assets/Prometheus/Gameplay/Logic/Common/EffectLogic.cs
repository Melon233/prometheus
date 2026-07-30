using System;
using System.Linq;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    public class EffectLogic : Logic
    {
        EffectComponent effectComp;
        public override void AfterNew()
        {
            Entity.TryGetComp(out effectComp);
        }

        public override bool CanEnable()
        {
            return true;
        }

        public override bool CanDisable()
        {
            return false;
        }

        public override void OnEnable()
        {
        }

        public override void OnDisable()
        {
        }

        public override void OnUpdate(float dt)
        {
            foreach (var effect in effectComp.toAddEffects)
            {
                if (effectComp.effects.HasKey(effect.GetType()))
                    effect.OnStack();
                else
                {
                    effectComp.effects.Add(effect.GetType(), effect);
                }
            }
            foreach (var effect in effectComp.toRemoveEffects)
            {
                effectComp.effects.Remove(effect.GetType());
                effect.OnRemove();
            }
            effectComp.toAddEffects.Clear();
            effectComp.toRemoveEffects.Clear();
            foreach (var effect in effectComp.effects)
            {
                effect.OnUpdate(dt);
            }
        }

        public override void OnDispose()
        {
            effectComp.effects.Dispose();
            effectComp.toAddEffects.Clear();
            effectComp.toRemoveEffects.Clear();
        }
    }
}