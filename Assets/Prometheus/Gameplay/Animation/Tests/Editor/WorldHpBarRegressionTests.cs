using System;
using System.Collections;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Xuan.Prometheus.Asset;
using Xuan.Prometheus.Component;
using YooAsset;

namespace Xuan.Prometheus.Animation.Tests
{
    /// <summary>验证世界血条重新显示前的同步定位，以及临时隐藏与最终解绑之间互不混淆的生命周期。</summary>
    public sealed class WorldHpBarRegressionTests
    {
        /// <summary>访问 UIKit 私有运行态字段所需的反射标记，仅用于绕过 EditMode 禁止 DontDestroyOnLoad 的测试环境限制。</summary>
        private const BindingFlags PrivateInstanceField = BindingFlags.Instance | BindingFlags.NonPublic;

        private IUIKit previousUIKit;
        private TestAssetKit assetKit;
        private UIKit uiKit;
        private GameObject followTargetObject;

        /// <summary>创建不依赖 YooAsset 和 DontDestroyOnLoad 的最小世界 UI 根节点，并保存全局 UI 入口以便测试后恢复。</summary>
        [SetUp]
        public void SetUp()
        {
            previousUIKit = Core.UI;
            assetKit = new TestAssetKit();
            uiKit = new UIKit(assetKit);
            InitializeWorldUiForEditMode(uiKit);
            followTargetObject = new GameObject("WorldHpBarRegressionTests.FollowTarget");
        }

        /// <summary>释放测试创建的 UIKit 根节点、目标对象和全局 UI 替换，避免跨用例状态泄漏。</summary>
        [TearDown]
        public void TearDown()
        {
            uiKit?.Dispose();
            uiKit = null;
            assetKit?.Dispose();
            assetKit = null;
            if (followTargetObject != null) UnityEngine.Object.DestroyImmediate(followTargetObject);
            followTargetObject = null;
            Core.UI = previousUIKit;
            previousUIKit = null;
        }

        /// <summary>验证隐藏的跟随 UI 能在重新显示前立即定位到目标新坐标，不会先显示一帧旧位置。</summary>
        [Test]
        public void RefreshTransform_UpdatesHiddenWorldUiBeforeItIsShown()
        {
            Vector3 worldOffset = new Vector3(0f, 2f, 0f);
            followTargetObject.transform.position = new Vector3(1f, 2f, 3f);
            WorldUIHandle handle = uiKit.SpawnWorldUI("Tests.WorldHpBar", followTargetObject.transform, worldOffset);
            try
            {
                handle.Root.SetActive(false);
                followTargetObject.transform.position = new Vector3(8f, 5f, -4f);
                Vector3 expectedPosition = followTargetObject.transform.position + worldOffset;
                Assert.That(Vector3.Distance(handle.Root.transform.position, expectedPosition), Is.GreaterThan(0.001f), "刷新前测试实例必须仍保存旧渲染坐标。");
                handle.RefreshTransform();
                Assert.That(Vector3.Distance(handle.Root.transform.position, expectedPosition), Is.LessThan(0.0001f), "显示血条前必须同步到当前角色头顶坐标。");
                handle.Root.SetActive(true);
                Assert.That(Vector3.Distance(handle.Root.transform.position, expectedPosition), Is.LessThan(0.0001f), "重新显示不得改变已经同步完成的位置。");
            }
            finally
            {
                handle.Release();
            }
        }

        /// <summary>验证临时隐藏只解绑事件但保留属性目标，最终回收才彻底清除目标引用。</summary>
        [Test]
        public void HpBar_TemporaryDisablePreservesPropertyUntilFinalUninitialize()
        {
            GameObject hpBarObject = new GameObject("WorldHpBarRegressionTests.HpBar", typeof(RectTransform), typeof(CanvasGroup));
            GameObject hpImageObject = new GameObject("Hp", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            GameObject chaserImageObject = new GameObject("Chaser", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            GameObject propertyObject = new GameObject("WorldHpBarRegressionTests.Property");
            try
            {
                hpImageObject.transform.SetParent(hpBarObject.transform, false);
                chaserImageObject.transform.SetParent(hpBarObject.transform, false);
                HpBar hpBar = hpBarObject.AddComponent<HpBar>();
                PropertyComponent propertyComponent = propertyObject.AddComponent<PropertyComponent>();
                hpBar.hpImg = hpImageObject.GetComponent<UnityEngine.UI.Image>();
                hpBar.chaserImg = chaserImageObject.GetComponent<UnityEngine.UI.Image>();
                hpBar.canvasGroup = hpBarObject.GetComponent<CanvasGroup>();
                hpBar.Initialize(propertyComponent, Color.red, Color.white);
                MethodInfo onDisableMethod = typeof(HpBar).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(onDisableMethod, Is.Not.Null);
                onDisableMethod.Invoke(hpBar, null);
                Assert.That(hpBar.propComp, Is.SameAs(propertyComponent), "临时隐藏必须保留属性目标，供重新启用后恢复事件绑定。");
                hpBar.Uninitialize();
                Assert.That(hpBar.propComp, Is.Null, "最终回收必须释放属性目标引用。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(propertyObject);
                UnityEngine.Object.DestroyImmediate(hpBarObject);
            }
        }

        /// <summary>只创建世界 UI 测试所需的 Canvas 与缓存根节点，并把 UIKit 标记为已初始化。</summary>
        private static void InitializeWorldUiForEditMode(UIKit targetUIKit)
        {
            GameObject worldRootObject = new GameObject("[WorldHpBarRegressionTests.UIKit.World]", typeof(RectTransform), typeof(Canvas));
            RectTransform worldCanvasRoot = worldRootObject.GetComponent<RectTransform>();
            GameObject worldCacheObject = new GameObject("Cache", typeof(RectTransform));
            RectTransform worldCacheRoot = worldCacheObject.GetComponent<RectTransform>();
            worldCacheRoot.SetParent(worldCanvasRoot, false);
            worldCacheObject.SetActive(false);
            SetPrivateField(targetUIKit, "worldRootObject", worldRootObject);
            SetPrivateField(targetUIKit, "worldCanvasRoot", worldCanvasRoot);
            SetPrivateField(targetUIKit, "worldCacheRoot", worldCacheRoot);
            SetPrivateField(targetUIKit, "worldCanvas", worldRootObject.GetComponent<Canvas>());
            SetPrivateField(targetUIKit, "isInitialized", true);
        }

        /// <summary>向指定 UIKit 私有字段写入测试运行态，并在实现字段变更时抛出可定位错误。</summary>
        private static void SetPrivateField<TValue>(UIKit targetUIKit, string fieldName, TValue value)
        {
            FieldInfo field = typeof(UIKit).GetField(fieldName, PrivateInstanceField);
            if (field == null) throw new MissingFieldException(typeof(UIKit).FullName, fieldName);
            field.SetValue(targetUIKit, value);
        }

        /// <summary>为 UIKit 世界 UI 测试提供只创建 RectTransform 实例的最小资源实现。</summary>
        private sealed class TestAssetKit : IAssetKit
        {
            /// <summary>测试资源实现始终处于可用状态。</summary>
            public bool IsReady => true;

            /// <summary>测试资源无需异步初始化。</summary>
            public IEnumerator Initialize(string packageName = AssetKit.DefaultPackageName)
            {
                yield break;
            }

            /// <summary>当前测试不支持同步加载普通资源。</summary>
            public TAsset LoadAssetSync<TAsset>(string location) where TAsset : UnityEngine.Object
            {
                throw new NotSupportedException();
            }

            /// <summary>当前测试不支持异步加载普通资源。</summary>
            public IEnumerator LoadAssetAsync<TAsset>(string location, Action<TAsset> onCompleted, Action<string> onFailed = null, uint priority = 0) where TAsset : UnityEngine.Object
            {
                throw new NotSupportedException();
            }

            /// <summary>在场景根节点创建一个 RectTransform 世界 UI 实例。</summary>
            public GameObject InstantiateSync(string location, bool isActive = true)
            {
                return InstantiateSync(location, null, false, isActive);
            }

            /// <summary>在指定父节点下创建一个 RectTransform 世界 UI 实例。</summary>
            public GameObject InstantiateSync(string location, Transform parent, bool worldPositionStays = false, bool isActive = true)
            {
                GameObject instance = new GameObject(location, typeof(RectTransform));
                instance.transform.SetParent(parent, worldPositionStays);
                instance.SetActive(isActive);
                return instance;
            }

            /// <summary>在指定世界姿态创建一个 RectTransform 世界 UI 实例。</summary>
            public GameObject InstantiateSync(string location, Vector3 position, Quaternion rotation, Transform parent = null, bool isActive = true)
            {
                GameObject instance = InstantiateSync(location, parent, false, isActive);
                instance.transform.SetPositionAndRotation(position, rotation);
                return instance;
            }

            /// <summary>当前测试不支持异步实例化。</summary>
            public IEnumerator InstantiateAsync(string location, Action<GameObject> onCompleted, Transform parent = null, bool worldPositionStays = false, bool isActive = true, Action<string> onFailed = null, uint priority = 0)
            {
                throw new NotSupportedException();
            }

            /// <summary>测试资源实现始终已经完成异步初始化。</summary>
            public UniTask WaitUntilReadyAsync()
            {
                return UniTask.CompletedTask;
            }

            /// <summary>当前世界 UI 测试不异步加载场景。</summary>
            public UniTask<Scene> LoadSceneAsync(string location)
            {
                throw new NotSupportedException();
            }

            /// <summary>测试实现没有需要释放的资源句柄。</summary>
            public bool ReleaseAsset(string location)
            {
                return false;
            }

            /// <summary>测试实现没有需要批量释放的资源句柄。</summary>
            public void ReleaseAllAssets()
            {
            }

            /// <summary>测试实现没有 YooAsset 卸载操作。</summary>
            public UnloadUnusedAssetsOperation UnloadUnusedAssetsAsync()
            {
                return null;
            }

            /// <summary>测试实现不持有外部资源。</summary>
            public void Dispose()
            {
            }
        }
    }
}
