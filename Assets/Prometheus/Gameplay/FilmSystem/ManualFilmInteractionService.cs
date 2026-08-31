using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Xuan.Prometheus.Input;

namespace Xuan.Prometheus.Film
{
    /// <summary>默认的可手动驱动交互服务，供没有正式对话 UI 时联调和测试阶段二流程。</summary>
    public sealed class ManualFilmInteractionService : IFilmInteractionService, IFilmFlowService
    {
        private readonly Dictionary<string, PendingInteraction> pending = new Dictionary<string, PendingInteraction>(StringComparer.Ordinal);
        private readonly Dictionary<string, UniTaskCompletionSource<FilmInteractionResult>> pendingEvents = new Dictionary<string, UniTaskCompletionSource<FilmInteractionResult>>(StringComparer.Ordinal);

        /// <summary>对话 Marker 到达时触发，外部可据此显示自定义对话 UI。</summary>
        public event Action<FilmDialogueRequest> DialogueRequested;

        /// <summary>QTE Marker 到达时触发，外部可据此显示自定义 QTE UI。</summary>
        public event Action<FilmQteRequest> QteRequested;

        /// <summary>外部事件等待 Marker 到达时触发，便于手动测试或临时系统观察。</summary>
        public event Action<FilmEventRequest> EventRequested;

        /// <summary>异步创建一个等待外部完成的对话请求。</summary>
        public async UniTask<FilmInteractionResult> ShowDialogueAsync(FilmDialogueRequest request, CancellationToken cancellationToken)
        {
            PendingInteraction interaction = CreatePending(request.InstanceId, request.InteractionId, false, default);
            DialogueRequested?.Invoke(request);
            try
            {
                return await interaction.Completion.Task.AttachExternalCancellation(cancellationToken);
            }
            finally
            {
                pending.Remove(CreateKey(request.InstanceId, request.InteractionId));
            }
        }

        /// <summary>异步创建一个等待外部完成或输入命中的 QTE 请求。</summary>
        public async UniTask<FilmInteractionResult> RunQteAsync(FilmQteRequest request, CancellationToken cancellationToken)
        {
            PendingInteraction interaction = CreatePending(request.InstanceId, request.InteractionId, true, request.SuccessActions);
            QteRequested?.Invoke(request);
            try
            {
                return await interaction.Completion.Task.AttachExternalCancellation(cancellationToken);
            }
            finally
            {
                pending.Remove(CreateKey(request.InstanceId, request.InteractionId));
            }
        }

        /// <summary>把演出接管的输入转为当前 QTE 的成功结果；普通对话期间输入会被忽略。</summary>
        public void ReceiveInput(in InputFrame frame, InputActionMask actions)
        {
            foreach (PendingInteraction interaction in pending.Values)
            {
                if (!interaction.IsQte || (interaction.SuccessActions & actions) == 0 || !HasPressed(frame, interaction.SuccessActions)) continue;
                interaction.Completion.TrySetResult(new FilmInteractionResult(true));
                return;
            }
        }

        /// <summary>手动完成指定实例的对话请求，成功值由调用方决定。</summary>
        public bool CompleteDialogue(int instanceId, string interactionId, bool succeeded, string value = null)
        {
            return Complete(instanceId, interactionId, false, new FilmInteractionResult(succeeded, value));
        }

        /// <summary>手动完成指定实例的 QTE 请求，成功值由调用方决定。</summary>
        public bool CompleteQte(int instanceId, string interactionId, bool succeeded, string value = null)
        {
            return Complete(instanceId, interactionId, true, new FilmInteractionResult(succeeded, value));
        }

        /// <summary>异步等待指定外部事件，并在取消或完成后移除等待记录。</summary>
        public async UniTask<FilmInteractionResult> WaitForEventAsync(FilmEventRequest request, CancellationToken cancellationToken)
        {
            string key = CreateKey(request.InstanceId, request.EventId);
            if (pendingEvents.ContainsKey(key)) throw new InvalidOperationException($"Film event '{key}' is already pending.");
            UniTaskCompletionSource<FilmInteractionResult> completion = new UniTaskCompletionSource<FilmInteractionResult>();
            pendingEvents.Add(key, completion);
            EventRequested?.Invoke(request);
            try
            {
                return await completion.Task.AttachExternalCancellation(cancellationToken);
            }
            finally
            {
                pendingEvents.Remove(key);
            }
        }

        /// <summary>手动发布一个外部事件并唤醒等待该事件的演出实例。</summary>
        public bool CompleteEvent(int instanceId, string eventId, bool succeeded = true, string value = null)
        {
            string key = CreateKey(instanceId, eventId);
            return pendingEvents.TryGetValue(key, out UniTaskCompletionSource<FilmInteractionResult> completion) && completion.TrySetResult(new FilmInteractionResult(succeeded, value));
        }

        /// <summary>创建一个带实例和交互类型校验的待完成请求。</summary>
        private PendingInteraction CreatePending(int instanceId, string interactionId, bool isQte, InputActionMask successActions)
        {
            string key = CreateKey(instanceId, interactionId);
            if (pending.ContainsKey(key)) throw new InvalidOperationException($"Film interaction '{key}' is already pending.");
            PendingInteraction interaction = new PendingInteraction(isQte, successActions);
            pending.Add(key, interaction);
            return interaction;
        }

        /// <summary>按实例、交互 ID 和交互类型完成一个待处理请求。</summary>
        private bool Complete(int instanceId, string interactionId, bool isQte, FilmInteractionResult result)
        {
            string key = CreateKey(instanceId, interactionId);
            if (!pending.TryGetValue(key, out PendingInteraction interaction) || interaction.IsQte != isQte) return false;
            return interaction.Completion.TrySetResult(result);
        }

        /// <summary>生成不会与其他演出实例串线的待处理请求键。</summary>
        private static string CreateKey(int instanceId, string interactionId)
        {
            return $"{instanceId}:{interactionId}";
        }

        /// <summary>判断输入快照中指定动作是否包含本帧按下事件。</summary>
        private static bool HasPressed(in InputFrame frame, InputActionMask actions)
        {
            return (actions & InputActionMask.Submit) != 0 && frame.Submit.PressedThisFrame || (actions & InputActionMask.Cancel) != 0 && frame.Cancel.PressedThisFrame || (actions & InputActionMask.Attack) != 0 && frame.Attack.PressedThisFrame || (actions & InputActionMask.Skill) != 0 && frame.Skill.PressedThisFrame || (actions & InputActionMask.Ultimate) != 0 && frame.Ultimate.PressedThisFrame || (actions & InputActionMask.Dodge) != 0 && frame.Dodge.PressedThisFrame || (actions & InputActionMask.Jump) != 0 && frame.Jump.PressedThisFrame || (actions & InputActionMask.SpecialAttack) != 0 && frame.SpecialAttack.PressedThisFrame || (actions & InputActionMask.ToggleSprint) != 0 && frame.ToggleSprint.PressedThisFrame || (actions & InputActionMask.ToggleWalk) != 0 && frame.ToggleWalk.PressedThisFrame || (actions & InputActionMask.SelectTeamMember1) != 0 && frame.SelectTeamMember1.PressedThisFrame || (actions & InputActionMask.SelectTeamMember2) != 0 && frame.SelectTeamMember2.PressedThisFrame || (actions & InputActionMask.SelectTeamMember3) != 0 && frame.SelectTeamMember3.PressedThisFrame || (actions & InputActionMask.OpenLottery) != 0 && frame.OpenLottery.PressedThisFrame || (actions & InputActionMask.OpenMiniMap) != 0 && frame.OpenMiniMap.PressedThisFrame || (actions & InputActionMask.OpenQuest) != 0 && frame.OpenQuest.PressedThisFrame || (actions & InputActionMask.OpenMenu) != 0 && frame.OpenMenu.PressedThisFrame || (actions & InputActionMask.OpenGuide) != 0 && frame.OpenGuide.PressedThisFrame || (actions & InputActionMask.OpenEvent) != 0 && frame.OpenEvent.PressedThisFrame || (actions & InputActionMask.OpenCharacter) != 0 && frame.OpenCharacter.PressedThisFrame || (actions & InputActionMask.OpenBag) != 0 && frame.OpenBag.PressedThisFrame;
        }

        /// <summary>保存一个等待完成的交互任务及其 QTE 输入过滤条件。</summary>
        private sealed class PendingInteraction
        {
            internal PendingInteraction(bool isQte, InputActionMask successActions)
            {
                IsQte = isQte;
                SuccessActions = successActions;
            }

            /// <summary>获取该请求是否属于 QTE。</summary>
            internal bool IsQte { get; }

            /// <summary>获取 QTE 成功动作过滤条件。</summary>
            internal InputActionMask SuccessActions { get; }

            /// <summary>保存外部完成结果的异步源。</summary>
            internal UniTaskCompletionSource<FilmInteractionResult> Completion { get; } = new UniTaskCompletionSource<FilmInteractionResult>();
        }
    }
}
