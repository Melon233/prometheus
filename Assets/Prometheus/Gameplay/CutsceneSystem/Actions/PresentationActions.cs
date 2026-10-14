using System;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace Xuan.Prometheus.Cutscene
{
    /// <summary>
    /// 播放一个 FMOD 事件，位置取演员或锚点。事件播完自然结束；演出结束或被跳过时以淡出停止。
    /// 跳过时不补播（声音是过程，不是结果）。
    /// </summary>
    [Serializable]
    public sealed class AudioAction : CutsceneAction
    {
        [SerializeField] [Tooltip("FMOD 事件。")]
        private EventReference audioEvent;

        [SerializeField] [Tooltip("可选：声源跟随的演员键；为空时使用锚点位置。")]
        private string attachActor;

        /// <inheritdoc />
        public override CutsceneActionRun Begin(CutsceneContext context)
        {
            Transform source = string.IsNullOrEmpty(attachActor) ? context.Anchor : context.GetActor(attachActor).Root;
            EventInstance instance = RuntimeManager.CreateInstance(audioEvent);
            instance.set3DAttributes(RuntimeUtils.To3DAttributes(source.position));
            instance.start();
            return new Run(instance, source);
        }

        /// <inheritdoc />
        public override void ApplyFinalState(CutsceneContext context)
        {
        }

        /// <inheritdoc />
        public override void Validate(CutsceneDefinition definition, string where, List<string> errors)
        {
            if (audioEvent.IsNull) errors.Add($"{where}：未配置 FMOD 事件。");
            if (!string.IsNullOrEmpty(attachActor)) ValidateActor(definition, attachActor, true, where, errors);
        }

        /// <summary>一个 FMOD 事件实例的运行体。</summary>
        private sealed class Run : CutsceneActionRun
        {
            private EventInstance instance;
            private readonly Transform source;

            public Run(EventInstance instance, Transform source)
            {
                this.instance = instance;
                this.source = source;
            }

            /// <inheritdoc />
            public override bool Tick(float dt)
            {
                // 声源跟随演员移动；事件进入 STOPPED 即视为自然结束。
                instance.set3DAttributes(RuntimeUtils.To3DAttributes(source.position));
                instance.getPlaybackState(out PLAYBACK_STATE state);
                return state == PLAYBACK_STATE.STOPPED;
            }

            /// <inheritdoc />
            public override void Finish()
            {
                instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
                instance.release();
            }
        }
    }

    /// <summary>
    /// 在演员或锚点上生成一个特效 prefab，存在指定时长后销毁；时长为 0 表示持续到演出结束。
    /// 跳过时不生成（特效是过程，不是结果）。
    /// </summary>
    [Serializable]
    public sealed class VfxAction : CutsceneAction
    {
        [SerializeField] [Tooltip("特效 prefab。")]
        private GameObject prefab;

        [SerializeField] [Tooltip("可选：特效挂载并跟随的演员键；为空时放在锚点上。")]
        private string attachActor;

        [SerializeField] [Tooltip("相对挂载点的偏移。")]
        private Vector3 offset;

        [SerializeField] [Tooltip("存在时长（秒）；0 表示持续到演出结束。")]
        private float lifetime = 2f;

        /// <inheritdoc />
        public override CutsceneActionRun Begin(CutsceneContext context)
        {
            // 跟随演员时挂到演员下；挂在锚点时放进临时对象根，保证演出结束时一定被清理。
            bool follow = !string.IsNullOrEmpty(attachActor);
            Transform mount = follow ? context.GetActor(attachActor).Root : context.Anchor;
            GameObject instance = UnityEngine.Object.Instantiate(prefab, mount.TransformPoint(offset), mount.rotation, follow ? mount : context.PropRoot);
            return new Run(instance, lifetime);
        }

        /// <inheritdoc />
        public override void ApplyFinalState(CutsceneContext context)
        {
        }

        /// <inheritdoc />
        public override void Validate(CutsceneDefinition definition, string where, List<string> errors)
        {
            if (prefab == null) errors.Add($"{where}：未配置特效 prefab。");
            if (lifetime < 0f) errors.Add($"{where}：存在时长不能为负。");
            if (!string.IsNullOrEmpty(attachActor)) ValidateActor(definition, attachActor, true, where, errors);
        }

        /// <summary>一个特效实例的运行体。</summary>
        private sealed class Run : CutsceneActionRun
        {
            private readonly GameObject instance;
            private float remaining;

            public Run(GameObject instance, float lifetime)
            {
                this.instance = instance;
                // 0 表示持续到演出结束：用正无穷让它永不自然结束，由演出收尾时 Finish。
                remaining = lifetime > 0f ? lifetime : float.PositiveInfinity;
            }

            /// <inheritdoc />
            public override bool Tick(float dt)
            {
                remaining -= dt;
                return remaining <= 0f;
            }

            /// <inheritdoc />
            public override void Finish() => UnityEngine.Object.Destroy(instance);
        }
    }

    /// <summary>
    /// 显示一句台词。含本 Action 的步骤等待玩家点击推进；配置了选项时改为等待选择。
    /// 选项不影响后续流程，NPC 的回应就是下一步。文本全部是 TbText 的 id。
    /// </summary>
    [Serializable]
    public sealed class DialogAction : CutsceneAction
    {
        [SerializeField] [Tooltip("说话人文本 id；0 表示旁白。")]
        private int speakerTextId;

        [SerializeField] [Tooltip("台词文本 id。")]
        private int lineTextId;

        [SerializeField] [Tooltip("选项文本 id；为空表示点击推进。")]
        private List<int> optionTextIds = new List<int>();

        /// <summary>本句是否带选项（带选项时等待选择而不是点击）。</summary>
        public bool HasOptions => optionTextIds.Count > 0;

        /// <inheritdoc />
        public override CutsceneActionRun Begin(CutsceneContext context)
        {
            string[] options = new string[optionTextIds.Count];
            for (int index = 0; index < options.Length; index++) options[index] = NarrativeText.Get(optionTextIds[index]);
            context.ShowLine(new CutsceneLine(NarrativeText.Get(speakerTextId), NarrativeText.Get(lineTextId), options));
            return null;
        }

        /// <inheritdoc />
        public override void ApplyFinalState(CutsceneContext context)
        {
        }

        /// <inheritdoc />
        public override void Validate(CutsceneDefinition definition, string where, List<string> errors)
        {
            if (lineTextId == NarrativeText.None) errors.Add($"{where}：未配置台词文本 id。");
            if (optionTextIds.Contains(NarrativeText.None)) errors.Add($"{where}：选项文本 id 不能为 0。");
        }
    }
}
