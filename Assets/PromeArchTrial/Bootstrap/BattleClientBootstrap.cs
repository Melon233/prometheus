using System;
using PromeArchTrial.Core.Networking;
using PromeArchTrial.Game.Unity;
using PromeArchTrial.Game.Unity.Config;
using PromeArchTrial.Presentation.Character;
using UnityEngine;

namespace PromeArchTrial.Bootstrap
{
    /// <summary>
    /// 作为客户端演示的唯一组合根，加载 Luban 客户端数据、实例化净化 Yefa 表现，并装配网络预测会话与单向表现桥接器。
    /// </summary>
    [AddComponentMenu("PromeArchTrial/Battle Client Bootstrap")]
    public sealed class BattleClientBootstrap : MonoBehaviour
    {
        /// <summary>服务器监听地址；本地验收默认连接 IPv4 回环地址。</summary>
        [SerializeField, Tooltip("战斗服务器主机名或 IP 地址。")] private string serverHost = "127.0.0.1";

        /// <summary>服务器 TCP 监听端口。</summary>
        [SerializeField, Tooltip("战斗服务器 TCP 端口。")] private int serverPort = BattleProtocol.DefaultPort;

        /// <summary>直接选择的 Luban Character 表主键，不经过根配置间接寻址。</summary>
        [SerializeField, Tooltip("Luban Character 表中的角色主键。")] private int characterId = 1001;

        /// <summary>Player 构建可直接实例化的净化角色表现 prefab；编辑器可按 Luban 路径补全。</summary>
        [SerializeField, Tooltip("由生成器创建的纯表现角色 prefab。")] private GameObject characterPresentationPrefab;

        /// <summary>防止 Unity 生命周期或手工调用造成重复组合。</summary>
        private bool initialized;

        /// <summary>加载经过 Luban ref 解析的角色配置，并建立服务器权威客户端的全部运行时对象。</summary>
        private void Awake()
        {
            if (initialized) return;
            initialized = true;
            try
            {
                CharacterLubanClientConfig clientConfig = CharacterLubanClientConfigLoader.LoadFromStreamingAssets(characterId);
                GameObject presentationPrefab = ResolvePresentationPrefab(clientConfig.PresentationConfig.PrefabAssetPath);
                Transform runtimeRoot = new GameObject("PromeArchTrial Character Runtime").transform;
                runtimeRoot.SetParent(transform, false);
                GameObject characterObject = Instantiate(presentationPrefab, runtimeRoot);
                characterObject.name = $"Character {characterId} - Yefa Presentation";
                YefaCharacterPresenter characterPresenter = characterObject.GetComponent<YefaCharacterPresenter>();
                if (characterPresenter == null) throw new InvalidOperationException($"Configured character prefab '{presentationPrefab.name}' does not contain {nameof(YefaCharacterPresenter)}.");
                characterPresenter.ConfigureAnimationBindings(clientConfig.PresentationConfig.NormalAttackBindings);
                CreateGround(runtimeRoot);
                EnsureCamera(runtimeRoot);
                EnsureLight(runtimeRoot);
                ClientBattleSession session = runtimeRoot.gameObject.AddComponent<ClientBattleSession>();
                session.Configure(serverHost, serverPort, characterId, clientConfig.RuntimeConfig);
                CharacterClientPresenterBridge bridge = runtimeRoot.gameObject.AddComponent<CharacterClientPresenterBridge>();
                bridge.Configure(session, characterPresenter, clientConfig.PresentationConfig);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[PromeArchTrial] Failed to compose Luban character client: {exception}", this);
                enabled = false;
            }
        }

        /// <summary>约束 Inspector 中的连接和角色输入，避免运行时才发现明显非法值。</summary>
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(serverHost)) serverHost = "127.0.0.1";
            serverPort = Mathf.Clamp(serverPort, 1, 65535);
            characterId = Mathf.Max(1, characterId);
        }

        /// <summary>优先使用场景序列化的 prefab，并在 Unity Editor 中允许按 Luban 路径直接解析以简化演示场景生成。</summary>
        private GameObject ResolvePresentationPrefab(string configuredAssetPath)
        {
            if (characterPresentationPrefab != null) return characterPresentationPrefab;
#if UNITY_EDITOR
            characterPresentationPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(configuredAssetPath);
            if (characterPresentationPrefab != null) return characterPresentationPrefab;
#endif
            throw new InvalidOperationException($"Character presentation prefab is not assigned and cannot be resolved from '{configuredAssetPath}'. Regenerate the demo scene or assign the prefab before building the player.");
        }

        /// <summary>创建无碰撞体的地面视觉参照；所有碰撞和落地仍由纯 C# 固定 Tick 模拟决定。</summary>
        private static void CreateGround(Transform parent)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Character Simulation Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.localPosition = new Vector3(0f, -0.12f, 2f);
            ground.transform.localScale = new Vector3(16f, 0.1f, 16f);
            Collider collider = ground.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Renderer groundRenderer = ground.GetComponent<Renderer>();
            if (groundRenderer != null) groundRenderer.material.color = new Color(0.18f, 0.2f, 0.24f, 1f);
        }

        /// <summary>仅在场景没有主相机时创建观察 XZ 位移和 Y 轴跳跃的演示相机。</summary>
        private static void EnsureCamera(Transform parent)
        {
            if (Camera.main != null) return;
            GameObject cameraObject = new GameObject("PromeArchTrial Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.position = new Vector3(0f, 3.3f, -10f);
            cameraObject.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -0.12f, 1f), Vector3.up);
            Camera cameraComponent = cameraObject.AddComponent<Camera>();
            cameraComponent.clearFlags = CameraClearFlags.SolidColor;
            cameraComponent.backgroundColor = new Color(0.06f, 0.07f, 0.1f, 1f);
            cameraObject.AddComponent<AudioListener>();
        }

        /// <summary>仅在场景没有光源时创建基础方向光，保证角色、血条和地面可见。</summary>
        private static void EnsureLight(Transform parent)
        {
            if (FindFirstObjectByType<Light>() != null) return;
            GameObject lightObject = new GameObject("PromeArchTrial Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light lightComponent = lightObject.AddComponent<Light>();
            lightComponent.type = LightType.Directional;
            lightComponent.intensity = 1.2f;
        }
    }
}
