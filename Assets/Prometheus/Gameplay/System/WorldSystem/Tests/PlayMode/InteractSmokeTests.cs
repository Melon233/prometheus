using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus.World.Tests
{
    /// <summary>交互感应系统的 PlayMode 冒烟测试：验证触发进入/离开维护附近交互物列表，以及类型到操作的映射。</summary>
    public sealed class InteractSmokeTests
    {
        /// <summary>仅组合交互感应链路的最小测试实体，避免依赖 GameplayKit 全量启动。</summary>
        private sealed class InteractTestEntity : Entity
        {
            public InteractTestEntity(GameObject bindGo)
            {
                this.bindGo = bindGo;
                AddComp<InteractComponent>();
                AddLogic<InteractLogic>();
            }
        }

        /// <summary>验证交互逻辑在触发进入时加入、重复进入去重、触发离开时移除附近交互物配置。</summary>
        [UnityTest]
        public IEnumerator InteractLogic_SensesPoiTrigger_AddsAndRemovesConfig()
        {
            GameObject poiGo = new GameObject("TestPoi");
            PoiMono mono = poiGo.AddComponent<PoiMono>();
            mono.Config = new PoiConfig { Id = "Mond_Chest_Smoke", Region = "Mond", PoiType = PoiType.Chest };
            Collider poiCollider = poiGo.AddComponent<SphereCollider>();

            GameObject playerGo = new GameObject("TestPlayer");
            InteractTestEntity player = new InteractTestEntity(playerGo);
            Assert.That(player.TryGetLogic(out InteractLogic interactLogic), Is.True);
            Assert.That(player.TryGetComp(out InteractComponent interactComponent), Is.True);
            interactLogic.AfterNew();

            interactLogic.OnTriggerEnter(null, poiCollider);
            List<PoiConfig> buffer = new List<PoiConfig>();
            interactComponent.CopyNearby(buffer);
            Assert.That(buffer.Count, Is.EqualTo(1), "触发进入后应加入一个交互物。");
            Assert.That(buffer[0].Id, Is.EqualTo("Mond_Chest_Smoke"));

            interactLogic.OnTriggerEnter(null, poiCollider);
            interactComponent.CopyNearby(buffer);
            Assert.That(buffer.Count, Is.EqualTo(1), "重复触发进入不应产生重复项。");

            interactLogic.OnTriggerExit(null, poiCollider);
            interactComponent.CopyNearby(buffer);
            Assert.That(buffer.Count, Is.Zero, "触发离开后应移除交互物。");

            Object.DestroyImmediate(poiGo);
            Object.DestroyImmediate(playerGo);
            yield return null;
        }

        /// <summary>验证每个 POI 类型映射到正确的交互操作。</summary>
        [Test]
        public void GetInteractOp_MapsEachTypeToCorrectOperation()
        {
            Assert.That(WorldSystem.GetInteractOp(PoiType.TeleAnchor), Is.EqualTo(PoiOp.Unlock));
            Assert.That(WorldSystem.GetInteractOp(PoiType.Statue), Is.EqualTo(PoiOp.Unlock));
            Assert.That(WorldSystem.GetInteractOp(PoiType.Dungeon), Is.EqualTo(PoiOp.Unlock));
            Assert.That(WorldSystem.GetInteractOp(PoiType.Chest), Is.EqualTo(PoiOp.OpenChest));
            Assert.That(WorldSystem.GetInteractOp(PoiType.SpiritCore), Is.EqualTo(PoiOp.CollectCore));
            Assert.That(WorldSystem.GetInteractOp(PoiType.Gathering), Is.EqualTo(PoiOp.Gather));
            Assert.That(WorldSystem.GetInteractOp(PoiType.MapBoss), Is.EqualTo(PoiOp.Defeat));
        }
    }
}
