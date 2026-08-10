using UnityEngine;
using Xuan.Prometheus.Asset;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 通过 UIKit 屏幕空间世界锚点对象池生成伤害飘字，并把具体文本和动画参数交给飘字组件初始化。
    /// </summary>
    public class FloatTextKit : MonoSingleton<FloatTextKit>
    {
        private const string ConfigPath = "DmgConf";
        private const string WorldUIAssetAddress = "UI_Dmg";

        private FloatDamageConfig config;

        /// <summary>
        /// 单例创建时加载伤害飘字配置；UIKit 及其屏幕空间世界锚点 Canvas 由 GameCore 独立管理，无需在此缓存场景 Canvas。
        /// </summary>
        protected override void OnAwake()
        {
            LoadConfig();
        }

        /// <summary>
        /// 在指定世界坐标生成一次伤害或治疗飘字，并通过句柄取得池实例上的 FloatDmgComponent 完成表现初始化。
        /// </summary>
        /// <param name="number">需要显示的伤害或治疗数值。</param>
        /// <param name="worldPosition">伤害目标提供的基础世界坐标。</param>
        /// <param name="isHeal">为 true 时使用治疗文本颜色，否则恢复 Prefab 配置的默认伤害颜色。</param>
        public void CastNumberText(float number, Vector3 worldPosition, bool isHeal = false)
        {
            if (!EnsureReady())
                return;

            Vector2 randomPoint = Random.insideUnitCircle * Mathf.Max(0f, config.radius);
            Vector3 spawnWorldPosition = worldPosition + new Vector3(randomPoint.x, config.startHeight, randomPoint.y);
            WorldUIHandle handle = Core.UI.SpawnScreenSpaceWorldUI(WorldUIAssetAddress, spawnWorldPosition, config.lifeTime);
            FloatDmgComponent damageComponent = handle.GetComponent<FloatDmgComponent>();
            if (damageComponent == null)
            {
                Debug.LogError($"[FloatDamageKit] Screen-space world UI asset '{WorldUIAssetAddress}' does not contain FloatDmgComponent on its root object.", handle.Root);
                handle.Release();
                return;
            }

            damageComponent.Initialize(number, handle, config, isHeal);
        }

        /// <summary>
        /// 确保配置与当前 GameCore 的 UIKit 均可用；资源实例、相机、Canvas 和回收状态全部由 UIKit 统一处理。
        /// </summary>
        /// <returns>当前可以安全生成伤害飘字时返回 true。</returns>
        private bool EnsureReady()
        {
            if (config == null)
                LoadConfig();

            if (config == null)
            {
                Debug.LogError($"[FloatDamageKit] 无法通过 AssetKit 加载配置 '{ConfigPath}'。");
                return false;
            }

            if (Core.UI == null)
            {
                Debug.LogError("[FloatDamageKit] UIKit 尚未创建，无法生成屏幕空间伤害飘字。");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 通过 AssetKit 地址加载伤害飘字动画配置，配置中不再承担 Prefab 实例化职责。
        /// </summary>
        private void LoadConfig()
        {
            config = AssetKit.Ins.LoadAssetSync<FloatDamageConfig>(ConfigPath);
        }
    }
}
