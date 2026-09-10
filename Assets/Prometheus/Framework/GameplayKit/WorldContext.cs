using UnityEngine;
using UnityEngine.SceneManagement;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 一次世界进入的只读上下文，由驱动世界切换的组合根构造并逐个传给 System 的世界相位。
    ///
    /// 携带什么有一条明确的判据：**只装那些「由流程解析、System 自己查不到」的事实**。
    /// 出生位姿满足这条——同一个世界，首次进入用配置的出生点，从副本弹回时用记录的返回点，
    /// 只有持有世界栈的流程知道该用哪个。
    /// 副本难度、奖励倍率、返回目标之类**不**满足：它们由关心的 System 按世界标识自行查配置，
    /// 放进来只会让这个类型随场景种类不断膨胀，最终变回一个集中式启动参数对象。
    /// </summary>
    public readonly struct WorldContext
    {
        /// <summary>创建一次世界进入的上下文。</summary>
        /// <param name="worldId">世界稳定标识；System 用它查自己关心的世界级配置。</param>
        /// <param name="sceneAddress">本次加载使用的 YooAsset 场景地址。</param>
        /// <param name="scene">已经加载完成的 Unity 场景句柄。</param>
        /// <param name="spawnPosition">本次进入的出生坐标。</param>
        /// <param name="spawnRotation">本次进入的出生朝向。</param>
        public WorldContext(string worldId, string sceneAddress, Scene scene, Vector3 spawnPosition, Quaternion spawnRotation)
        {
            WorldId = worldId;
            SceneAddress = sceneAddress;
            Scene = scene;
            SpawnPosition = spawnPosition;
            SpawnRotation = spawnRotation;
        }

        /// <summary>获取世界稳定标识。</summary>
        public string WorldId { get; }

        /// <summary>获取本次加载使用的 YooAsset 场景地址。</summary>
        public string SceneAddress { get; }

        /// <summary>获取已经加载完成的 Unity 场景句柄。</summary>
        public Scene Scene { get; }

        /// <summary>获取本次进入的出生坐标；首次进入为配置值，弹回时为记录的返回点。</summary>
        public Vector3 SpawnPosition { get; }

        /// <summary>获取本次进入的出生朝向。</summary>
        public Quaternion SpawnRotation { get; }
    }
}
