using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;

namespace Xuan.Prometheus
{
    public class EAttackLogic : Logic.Logic, ITriggerHandler
    {
        EAttackComponent eAttackComp;
        EnmityComponent enmityComp; // Add this line to get the EnmityComponent
        PropertyComponent propComp;
        SpineComponent spineComp;
        AttackComponent atkComp;
        EventComponent evtComp;
        EffectComponent effectComp;

        /// <summary>
        /// 缓存敌人战斗组件，并通过 EffectComponent 使用当前单局的效果运行时。
        /// </summary>
        public override void AfterNew()
        {
            Entity.TryGetComp(out eAttackComp);
            Entity.TryGetComp(out propComp);
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out atkComp);
            Entity.TryGetComp(out enmityComp); // Add this line to get the EnmityComponent
            Entity.TryGetComp(out evtComp);
            Entity.TryGetComp(out effectComp);
            eAttackComp.recoveryTimer = new(3f);
            atkComp.atkCollider.handler = this;
            // evtComp.AddListener<AttackedEvent>(evt => eAttackComp.isAttacking = false);
        }

        public override bool CanDisable()
        {
            // return eAttackComp.trackEntry == null || eAttackComp.trackEntry.IsComplete;
            return !CanEnable() && !eAttackComp.isAttacking && !eAttackComp.isRecovery;
        }

        public override bool CanEnable()
        {
            return CheckAttack();
        }

        /// <summary>结束攻击状态并释放互斥 Logic；死亡回收时不覆盖已经开始播放的死亡动画。</summary>
        public override void OnDisable()
        {
            if (!propComp.IsDead && eAttackComp.isAttacking) spineComp.Stop(0, 0f);
            if (atkComp.atkCollider != null && atkComp.atkCollider.cod != null) atkComp.atkCollider.cod.enabled = false;
            eAttackComp.trackEntry = null;
            eAttackComp.isAttacking = false;
            Entity.UnBlockLogic<EnmityLogic>();
            Entity.UnBlockLogic<PatrolLogic>();
            eAttackComp.recoveryTimer.TimeOut();
        }

        /// <summary>回收旧敌人攻击 Logic 时关闭命中盒并解绑代理，防止保留死亡动画期间继续转发碰撞。</summary>
        public override void OnDispose()
        {
            if (atkComp != null && atkComp.atkCollider != null && atkComp.atkCollider.cod != null) atkComp.atkCollider.cod.enabled = false;
            if (atkComp != null && atkComp.atkCollider != null && ReferenceEquals(atkComp.atkCollider.handler, this)) atkComp.atkCollider.handler = null;
            effectComp = null;
        }

        public override void OnEnable()
        {
            Entity.BlockLogic<EnmityLogic>();
            Entity.BlockLogic<PatrolLogic>();
            eAttackComp.recoveryTimer.TimeOut();
            // eAttackComp.trackEntry.Complete += (entry) => spineComp.animationLib.idleExecutor.Execute();
            // eAttackComp.trackEntry.Interrupt += (entry) => entry = null;
            // enemyAttackComp.atkTimer.SetActive(true);
        }

        public void OnTriggerEnter(Collider other)
        {
            if (!Entity.IsActive || other == null) return;
            if (other.CompareTag("Player"))
            {
                if (eAttackComp.targetPropertyComp == null) other.TryGetComponent(out eAttackComp.targetPropertyComp);
                if (eAttackComp.targetPropertyComp == null || eAttackComp.targetPropertyComp.Entity == null || eAttackComp.targetPropertyComp.IsDead || !eAttackComp.targetPropertyComp.Entity.IsActive) return;
                float requestedDamage = propComp.Atk;
                EffectSignal signal = new EffectSignal(EffectSignalType.HitConfirmed, Entity, eAttackComp.targetPropertyComp.Entity, Entity, requestedDamage, requestedDamage, EffectTag.Attack | EffectTag.NormalAttack, "Enemy.NormalAttack", position: other.transform.position);
                effectComp.Runtime.Publish(signal);
            }
        }
        public bool CheckAttack()
        {
            GizmosKit.Instance.DrawWireCircle(Entity.bindGo.transform.position, Vector3.up, eAttackComp.eAttackConfig.attckRadius, Color.yellow); // Debug log to confirm the enemy is chasing the player
            if (enmityComp.target != null && Vector3.Distance(Entity.bindGo.transform.position, enmityComp.target.position) < eAttackComp.eAttackConfig.attckRadius)
            {
                eAttackComp.targetPropertyComp = enmityComp.target.GetComponent<PropertyComponent>();
                return eAttackComp.targetPropertyComp != null && eAttackComp.targetPropertyComp.Entity != null;
            }
            eAttackComp.targetPropertyComp = null; // If no target is found, set the target to null
            return false;
        }
        public override void OnUpdate(float dt)
        {
            if (eAttackComp.isRecovery)
                eAttackComp.recoveryTimer.OnUpdate(dt);
            if (eAttackComp.recoveryTimer.IsTimeOut && !eAttackComp.isAttacking)
            {
                eAttackComp.isRecovery = false;
                if (eAttackComp.targetPropertyComp != null)
                {
                    eAttackComp.isAttacking = true;
                    eAttackComp.trackEntry = spineComp.animationLib.atkExecutor.Execute();
                    eAttackComp.trackEntry.Complete += (entry) =>
                    {
                        spineComp.animationLib.idleExecutor.Execute();
                        eAttackComp.recoveryTimer.Reset();
                        eAttackComp.isAttacking = false;
                        eAttackComp.isRecovery = true;
                    };
                    eAttackComp.trackEntry.Interrupt += (entry) =>
                    {
                        eAttackComp.isAttacking = false;
                    };
                }
            }
        }
    }
}
