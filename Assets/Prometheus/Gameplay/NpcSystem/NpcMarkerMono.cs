using UnityEngine;
using Xuan.Prometheus.Quest;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus.Npc
{
    /// <summary>
    /// NPC 头顶标记的表现层：订阅标记变化，把当前标记映射到一组图标对象的显隐。
    ///
    /// 它**不保存任何状态**，也不判断该显示什么——标记值由任务系统与对话内容一次解析同时产出，
    /// 本组件只负责把那个值画出来。这正是「头顶挂问号、点进去是闲聊」这类不一致不可能发生的原因。
    ///
    /// 图标对象由美术在预制体上摆好并逐个指到下面的字段；没有指定的档位就什么都不显示。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcMarkerMono : MonoBehaviour
    {
        [SerializeField] [Tooltip("承载本 NPC 的 POI 组件；留空则在同一对象上查找。")]
        private PoiMono poi;

        [SerializeField] [Tooltip("可接主线或传说任务时显示的图标（金色感叹号）。")]
        private GameObject questAvailableIcon;

        [SerializeField] [Tooltip("可接世界任务时显示的图标（蓝色感叹号）。")]
        private GameObject worldQuestAvailableIcon;

        [SerializeField] [Tooltip("任务进行中但当前步骤不指向本 NPC 时显示的图标（灰色）。")]
        private GameObject questInProgressIcon;

        [SerializeField] [Tooltip("可交付或可推进时显示的图标（问号）。")]
        private GameObject questTurnInIcon;

        /// <summary>保存已订阅的 NPC 系统，供释放时精确退订。</summary>
        private INpcSystem subscribedNpcSystem;

        /// <summary>缓存本组件对应的 NPC 标识，避免每次通知都重新取配置。</summary>
        private string npcId;

        /// <summary>解析所属 NPC、订阅标记变化并立即刷新一次。</summary>
        private void OnEnable()
        {
            if (poi == null) poi = GetComponent<PoiMono>();
            npcId = poi != null && poi.Config != null && poi.Config.Npc != null ? poi.Config.Npc.NpcId : null;
            if (string.IsNullOrEmpty(npcId))
            {
                Apply(QuestMarker.None);
                return;
            }
            // 场景对象可以在没有 Core 的情况下存在——例如策划直接打开 MainWorld 按 Play 看效果。
            // 此时「没有任务状态」就是正确答案，因此显示无标记而不是抛异常。
            // 正式启动链路里组合根先注册 GameplayKit 再加载场景，这条分支不会被走到。
            if (Core.Gameplay == null || !Core.Gameplay.TryGetSystem(out subscribedNpcSystem))
            {
                subscribedNpcSystem = null;
                Apply(QuestMarker.None);
                return;
            }
            subscribedNpcSystem.MarkerChanged += OnMarkerChanged;
            Apply(subscribedNpcSystem.GetMarker(npcId));
        }

        /// <summary>退订标记变化，避免被回收的表现对象继续被事件持有。</summary>
        private void OnDisable()
        {
            if (subscribedNpcSystem != null) subscribedNpcSystem.MarkerChanged -= OnMarkerChanged;
            subscribedNpcSystem = null;
        }

        /// <summary>只在通知涉及本 NPC 时重新读取标记。</summary>
        private void OnMarkerChanged(string changedNpcId)
        {
            if (!string.Equals(changedNpcId, npcId, System.StringComparison.Ordinal)) return;
            Apply(subscribedNpcSystem.GetMarker(npcId));
        }

        /// <summary>把标记映射成图标显隐；同时只会有一个档位可见。</summary>
        /// <param name="marker">当前标记。</param>
        private void Apply(QuestMarker marker)
        {
            SetVisible(questAvailableIcon, marker == QuestMarker.QuestAvailable);
            SetVisible(worldQuestAvailableIcon, marker == QuestMarker.WorldQuestAvailable);
            SetVisible(questInProgressIcon, marker == QuestMarker.QuestInProgress);
            SetVisible(questTurnInIcon, marker == QuestMarker.QuestTurnIn);
        }

        /// <summary>切换一个图标对象的显隐；未配置的档位直接跳过。</summary>
        private static void SetVisible(GameObject icon, bool visible)
        {
            if (icon == null || icon.activeSelf == visible) return;
            icon.SetActive(visible);
        }
    }
}
