using System;
using NUnit.Framework;
using UnityEngine;

namespace Xuan.Prometheus.World.Tests
{
    /// <summary>
    /// 验证世界目录的配置约束与压栈规则的推导。
    ///
    /// 世界栈本身的行为（压栈记录返回点、弹栈回到原位）依赖场景加载与玩法会话，
    /// 属于 Play 模式验收范围；这里只覆盖不依赖运行时的那部分——它们恰好是配置最容易写错的地方。
    /// </summary>
    public sealed class WorldCatalogTests
    {
        /// <summary>构造一个可用的世界定义。</summary>
        private static WorldDefinition NewWorld(string id, string scene, WorldKind kind)
        {
            return new WorldDefinition().Configure(id, scene, kind, Vector3.zero);
        }

        /// <summary>构造一个目录。</summary>
        private static WorldCatalog NewCatalog(params WorldDefinition[] worlds)
        {
            WorldCatalog catalog = ScriptableObject.CreateInstance<WorldCatalog>();
            catalog.SetDefinitions(worlds);
            return catalog;
        }

        /// <summary>压栈行为由种类推导，不单独配开关：副本与活动压栈，主世界与次级世界不压。</summary>
        [Test]
        public void PushBehaviour_IsDerivedFromKind()
        {
            Assert.That(NewWorld("a", "s", WorldKind.MainWorld).PushesReturnPoint, Is.False);
            Assert.That(NewWorld("b", "s", WorldKind.SubWorld).PushesReturnPoint, Is.False);
            Assert.That(NewWorld("c", "s", WorldKind.Dungeon).PushesReturnPoint, Is.True);
            Assert.That(NewWorld("d", "s", WorldKind.Activity).PushesReturnPoint, Is.True);
        }

        /// <summary>栈底必须唯一确定，因此主世界有且只能有一个。</summary>
        [Test]
        public void Validate_RequiresExactlyOneMainWorld()
        {
            WorldCatalog none = NewCatalog(NewWorld("d", "s", WorldKind.Dungeon));
            Assert.Throws<InvalidOperationException>(() => none.Validate());

            WorldCatalog two = NewCatalog(NewWorld("a", "s1", WorldKind.MainWorld), NewWorld("b", "s2", WorldKind.MainWorld));
            Assert.Throws<InvalidOperationException>(() => two.Validate());

            WorldCatalog one = NewCatalog(NewWorld("a", "s1", WorldKind.MainWorld), NewWorld("d", "s2", WorldKind.Dungeon));
            Assert.DoesNotThrow(() => one.Validate());
            Assert.That(one.RequireMainWorld().WorldId, Is.EqualTo("a"));

            UnityEngine.Object.DestroyImmediate(none);
            UnityEngine.Object.DestroyImmediate(two);
            UnityEngine.Object.DestroyImmediate(one);
        }

        /// <summary>世界标识是跳转的唯一引用键，重复会让跳转目标不确定。</summary>
        [Test]
        public void Validate_RejectsDuplicateWorldId()
        {
            WorldCatalog catalog = NewCatalog(NewWorld("a", "s1", WorldKind.MainWorld), NewWorld("a", "s2", WorldKind.Dungeon));
            Assert.Throws<InvalidOperationException>(() => catalog.Validate());
            UnityEngine.Object.DestroyImmediate(catalog);
        }

        /// <summary>标识或场景地址为空属于配置错误，必须在校验期暴露而不是等到跳转时。</summary>
        [Test]
        public void Validate_RejectsEmptyIdentityOrScene()
        {
            WorldCatalog emptyId = NewCatalog(NewWorld(string.Empty, "s", WorldKind.MainWorld));
            Assert.Throws<InvalidOperationException>(() => emptyId.Validate());

            WorldCatalog emptyScene = NewCatalog(NewWorld("a", string.Empty, WorldKind.MainWorld));
            Assert.Throws<InvalidOperationException>(() => emptyScene.Validate());

            UnityEngine.Object.DestroyImmediate(emptyId);
            UnityEngine.Object.DestroyImmediate(emptyScene);
        }

        /// <summary>按标识查找命中与未命中的两条路径。</summary>
        [Test]
        public void Require_ThrowsForUnknownWorld()
        {
            WorldCatalog catalog = NewCatalog(NewWorld("a", "s1", WorldKind.MainWorld));
            Assert.That(catalog.Require("a").SceneAddress, Is.EqualTo("s1"));
            Assert.That(catalog.Find("missing"), Is.Null);
            Assert.Throws<InvalidOperationException>(() => catalog.Require("missing"));
            UnityEngine.Object.DestroyImmediate(catalog);
        }

        /// <summary>正式目录资产必须能通过全部校验；配置改坏了要在这里立刻发现。</summary>
        [Test]
        public void ShippedCatalog_IsValid()
        {
            WorldCatalog catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<WorldCatalog>("Assets/BundleResources/World/WorldCatalog.asset");
            Assert.That(catalog, Is.Not.Null, "正式世界目录资产缺失。");
            Assert.DoesNotThrow(() => catalog.Validate());
            Assert.That(catalog.RequireMainWorld().SceneAddress, Is.EqualTo("MainWorld"));
        }
    }
}
