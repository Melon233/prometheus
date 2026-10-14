using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Xuan.Prometheus.Cutscene
{
    /// <summary>一个演员的静态定义；Action 通过 <see cref="Key"/> 引用演员，而不是直接引用场景对象。</summary>
    [Serializable]
    public sealed class CutsceneActorDef
    {
        [SerializeField] [Tooltip("演出内唯一的演员键，Action 用它引用演员。")]
        private string key;

        [SerializeField] [Tooltip("演员来源。")]
        private CutsceneActorSource source = CutsceneActorSource.Scene;

        [SerializeField] [FormerlySerializedAs("sceneMarkerId")] [Tooltip("Scene 来源：场景中 WorldAnchor 的 id，演员即该锚点对象。")]
        private string anchorId;

        [SerializeField] [Tooltip("Spawn 来源：要生成的 prefab，Spine 骨骼放在其根或子节点上。")]
        private GameObject spawnPrefab;

        [SerializeField] [Tooltip("Spawn 来源：相对演出锚点的生成位置。")]
        private Vector3 spawnPosition;

        [SerializeField] [Tooltip("Spawn 来源：生成时是否面朝右。")]
        private bool spawnFaceRight = true;

        /// <summary>演出内唯一的演员键。</summary>
        public string Key => key;

        /// <summary>演员来源。</summary>
        public CutsceneActorSource Source => source;

        /// <summary>Scene 来源绑定的世界锚点 id。</summary>
        public string AnchorId => anchorId;

        /// <summary>Spawn 来源生成的 prefab。</summary>
        public GameObject SpawnPrefab => spawnPrefab;

        /// <summary>Spawn 来源相对锚点的生成位置。</summary>
        public Vector3 SpawnPosition => spawnPosition;

        /// <summary>Spawn 来源生成时的朝向。</summary>
        public bool SpawnFaceRight => spawnFaceRight;
    }
}
