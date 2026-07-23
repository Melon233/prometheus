using System;
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
                effectComp.effects.Add(effect.uid, effect);
            }
            foreach (var effect in effectComp.effects)
            {
                effect.OnUpdate(dt);
                if (effect.IsOver || effect.isInstant) effectComp.toRemoveEffects.Add(effect);
            }
            foreach (var effect in effectComp.toRemoveEffects)
            {
                effectComp.effects.Remove(effect.uid);
            }
            effectComp.toAddEffects.Clear();
            effectComp.toRemoveEffects.Clear();
        }

        public override void OnDispose()
        {
            effectComp.effects.Dispose();
            effectComp.toAddEffects.Clear();
            effectComp.toRemoveEffects.Clear();
        }
    }
}