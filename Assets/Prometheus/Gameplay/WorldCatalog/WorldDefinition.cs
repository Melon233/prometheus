using System;
using UnityEngine;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// 一个世界的种类。
    ///
    /// 种类决定的是**世界之间怎么串**，不是世界里有什么：
    /// 主世界与次级世界是平级的，互相替换；副本与活动压在它们之上，结束后弹回来源。
    /// 剧情不在此列——已决策以主世界原地演出为主，剧情不进栈。
    /// </summary>
    public enum WorldKind
    {
        /// <summary>主世界；永远位于世界栈的栈底。</summary>
        MainWorld = 0,

        /// <summary>次级开放世界；与主世界平级，互相替换而不压栈。</summary>
        SubWorld = 1,

        /// <summary>副本；压在来源世界之上，结束后弹回。</summary>
        Dungeon = 2,

        /// <summary>活动场景；压栈规则与副本一致，区分开只为让配置与统计能分辨来源。</summary>
        Activity = 3
    }

    /// <summary>
    /// 一个世界的静态定义。
    ///
    /// 它描述「这个世界是什么」，不描述「世界里有什么」——后者属于场景本身与各 System 的世界相位。
    /// 命名为 World 而不是 Scene，是因为场景只是它的一个属性：同一个场景资源可以被多个世界复用
    /// （不同出生点、不同 HUD 档位、不同压栈行为）。
    /// </summary>
    [Serializable]
    public sealed class WorldDefinition
    {
        [SerializeField] [Tooltip("世界稳定标识；世界之间的跳转一律按它引用，不按场景地址。")]
        private string worldId;

        [SerializeField] [Tooltip("场景资源的 YooAsset 地址。")]
        private string sceneAddress;

        [SerializeField] [Tooltip("世界种类；决定进入时是替换当前世界还是压栈。")]
        private WorldKind kind = WorldKind.MainWorld;

        [SerializeField] [Tooltip("默认出生坐标；从来源世界弹回时改用记录的返回点。")]
        private Vector3 spawnPosition;

        [SerializeField] [Tooltip("默认出生朝向的 Y 轴角度。")]
        private float spawnYaw;

        [SerializeField] [Tooltip("进入该世界时是否显示主 HUD；剧情场景与纯演出场景关掉它。")]
        private bool showsHud = true;

        /// <summary>创建一个空定义，供 Unity 序列化使用。</summary>
        public WorldDefinition()
        {
        }

        /// <summary>获取世界稳定标识。</summary>
        public string WorldId => worldId;

        /// <summary>获取场景资源的 YooAsset 地址。</summary>
        public string SceneAddress => sceneAddress;

        /// <summary>获取世界种类。</summary>
        public WorldKind Kind => kind;

        /// <summary>获取默认出生坐标。</summary>
        public Vector3 SpawnPosition => spawnPosition;

        /// <summary>获取默认出生朝向。</summary>
        public Quaternion SpawnRotation => Quaternion.Euler(0f, spawnYaw, 0f);

        /// <summary>获取进入该世界时是否显示主 HUD。</summary>
        public bool ShowsHud => showsHud;

        /// <summary>
        /// 进入该世界时是否压栈。
        ///
        /// 副本与活动压栈，主世界与次级世界互相替换。这条由种类推导而不是单独配一个开关：
        /// 两者一旦能各配各的，就会出现「压栈的主世界」这类没有意义却能配出来的组合。
        /// </summary>
        public bool PushesReturnPoint => kind == WorldKind.Dungeon || kind == WorldKind.Activity;

        /// <summary>配置一个世界定义；供编辑器工具与测试使用。</summary>
        /// <param name="id">世界稳定标识。</param>
        /// <param name="scene">场景资源地址。</param>
        /// <param name="worldKind">世界种类。</param>
        /// <param name="spawn">默认出生坐标。</param>
        /// <param name="yaw">默认出生朝向的 Y 轴角度。</param>
        /// <param name="hud">是否显示主 HUD。</param>
        public WorldDefinition Configure(string id, string scene, WorldKind worldKind = WorldKind.MainWorld, Vector3 spawn = default, float yaw = 0f, bool hud = true)
        {
            worldId = id;
            sceneAddress = scene;
            kind = worldKind;
            spawnPosition = spawn;
            spawnYaw = yaw;
            showsHud = hud;
            return this;
        }

        /// <summary>校验定义完整；配置错误在注册期就暴露，不留到跳转时才炸。</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(worldId)) throw new InvalidOperationException("World definition requires a non-empty world id.");
            if (string.IsNullOrWhiteSpace(sceneAddress)) throw new InvalidOperationException($"World '{worldId}' requires a non-empty scene address.");
        }
    }
}
