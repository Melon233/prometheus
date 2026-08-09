using System;
using UnityEngine;

namespace PromeArchTrial.Presentation.Character
{
    /// <summary>
    /// 为独立验收场景生成可交互的表现快照；它只用于展示所有 Yefa 动画与血条飘字入口，不参与正式客户端或服务器 gameplay。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterPresentationAcceptanceDriver : MonoBehaviour
    {
        /// <summary>独立表现验收使用的三十赫兹 Tick 间隔。</summary>
        private const float SimulationTickSeconds = 1f / 30f;

        /// <summary>验收抛物线从起跳到落地的总秒数。</summary>
        private const float JumpDurationSeconds = 1.2f;

        /// <summary>验收抛物线的最大世界高度。</summary>
        private const float JumpHeight = 1.5f;

        /// <summary>接收验收快照的纯表现组件。</summary>
        [SerializeField, Tooltip("独立验收场景中的 Yefa Presenter。")] private YefaCharacterPresenter presenter;

        /// <summary>血条和飘字验收使用的角色最大生命值。</summary>
        [SerializeField, Min(1), Tooltip("表现验收的最大生命值。")] private int maximumHealth = 3000;

        // 下列字段构成独立验收驱动器的本地演示状态，不会写回正式 gameplay 或网络状态。
        private Vector3 groundedPosition;
        private CharacterFacingDirection facing = CharacterFacingDirection.Right;
        private CharacterLocomotionPresentationState locomotion = CharacterLocomotionPresentationState.Idle;
        private CharacterActionPresentationState action = CharacterActionPresentationState.None;
        private uint simulationTick;
        private uint actionSequence;
        private uint damageEventSequence;
        private float simulationTickAccumulator;
        private float actionElapsedSeconds;
        private float actionDurationSeconds;
        private float jumpElapsedSeconds;
        private int health;
        private int comboIndex;
        private int latestDamageAmount;
        private bool latestDamageWasCritical;
        private bool jumping;

        /// <summary>
        /// 注入要验收的 Presenter，并以其当前位置与满生命值初始化演示状态。
        /// </summary>
        public void Configure(YefaCharacterPresenter targetPresenter)
        {
            presenter = targetPresenter != null ? targetPresenter : throw new ArgumentNullException(nameof(targetPresenter));
            groundedPosition = presenter.transform.position;
            health = maximumHealth;
            ApplyCurrentSnapshot();
        }

        /// <summary>
        /// 由测试工具或外部验收脚本直接触发一个动作，不需要伪造键盘输入。
        /// </summary>
        public void TriggerAction(CharacterActionPresentationState targetAction, float durationSeconds)
        {
            if (durationSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(durationSeconds), "动作演示时长必须大于零。");
            action = targetAction;
            actionSequence++;
            actionElapsedSeconds = 0f;
            actionDurationSeconds = durationSeconds;
        }

        /// <summary>
        /// 施加一次仅用于验收血条与飘字的伤害表现，不执行命中、减伤或其他 gameplay 规则。
        /// </summary>
        public void InflictDemoDamage(int amount, bool wasCritical)
        {
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "演示伤害必须大于零。");
            health = Mathf.Max(0, health - amount);
            damageEventSequence++;
            latestDamageAmount = amount;
            latestDamageWasCritical = wasCritical;
        }

        /// <summary>恢复满生命并返回待机状态，便于重复验收全部动作。</summary>
        public void ResetCharacter()
        {
            health = maximumHealth;
            groundedPosition = Vector3.zero;
            locomotion = CharacterLocomotionPresentationState.Idle;
            action = CharacterActionPresentationState.None;
            actionElapsedSeconds = 0f;
            actionDurationSeconds = 0f;
            jumpElapsedSeconds = 0f;
            jumping = false;
            comboIndex = 0;
            ApplyCurrentSnapshot();
        }

        /// <summary>运行时补全 Presenter 并初始化演示状态。</summary>
        private void Awake()
        {
            if (presenter == null) presenter = GetComponent<YefaCharacterPresenter>();
            if (presenter == null) throw new InvalidOperationException($"{nameof(CharacterPresentationAcceptanceDriver)} requires a {nameof(YefaCharacterPresenter)}.");
            health = maximumHealth;
            groundedPosition = presenter.transform.position;
        }

        /// <summary>每帧读取验收快捷键、推进演示状态并生成新的只读表现快照。</summary>
        private void Update()
        {
            AdvanceSimulationTick(Time.deltaTime);
            HandleAcceptanceInput();
            AdvanceAction(Time.deltaTime);
            AdvanceMovementAndJump(Time.deltaTime);
            ApplyCurrentSnapshot();
        }

        /// <summary>以三十赫兹累积演示 tick，使快照字段与正式双端模拟的节拍一致。</summary>
        private void AdvanceSimulationTick(float deltaTime)
        {
            simulationTickAccumulator += deltaTime;
            while (simulationTickAccumulator >= SimulationTickSeconds)
            {
                simulationTick++;
                simulationTickAccumulator -= SimulationTickSeconds;
            }
        }

        /// <summary>把键盘与鼠标操作转换为验收动作，不把这些输入规则带入正式 Presenter。</summary>
        private void HandleAcceptanceInput()
        {
            if (Input.GetKeyDown(KeyCode.J)) ResetCharacter();
            if (Input.GetKeyDown(KeyCode.H)) InflictDemoDamage(Input.GetKey(KeyCode.LeftShift) ? 600 : 250, Input.GetKey(KeyCode.LeftShift));
            if (action == CharacterActionPresentationState.Death) return;
            if (Input.GetKeyDown(KeyCode.K)) TriggerAction(CharacterActionPresentationState.Death, 1.2f);
            else if (!jumping && action == CharacterActionPresentationState.None && Input.GetKeyDown(KeyCode.Space)) BeginJump();
            else if (action == CharacterActionPresentationState.None && Input.GetMouseButtonDown(1)) TriggerAction(Input.GetKey(KeyCode.S) ? CharacterActionPresentationState.DodgeBackward : CharacterActionPresentationState.DodgeForward, 0.55f);
            else if (action == CharacterActionPresentationState.None && Input.GetMouseButtonDown(0)) TriggerNextComboAttack();
            else if (action == CharacterActionPresentationState.None && Input.GetKeyDown(KeyCode.Q)) TriggerAction(CharacterActionPresentationState.HeavyAttack, 0.9f);
            else if (action == CharacterActionPresentationState.None && Input.GetKeyDown(KeyCode.F)) TriggerAction(CharacterActionPresentationState.BranchAttack, 0.85f);
            else if (action == CharacterActionPresentationState.None && Input.GetKeyDown(KeyCode.T)) TriggerAction(CharacterActionPresentationState.HitReaction, 0.55f);
            else if (action == CharacterActionPresentationState.None && Input.GetKeyDown(KeyCode.E)) TriggerAction(CharacterActionPresentationState.Skill, 1.1f);
            else if (action == CharacterActionPresentationState.None && Input.GetKeyDown(KeyCode.R)) TriggerAction(CharacterActionPresentationState.Ultimate, 1.1f);
        }

        /// <summary>按顺序触发四段普通攻击，第四段后回到第一段。</summary>
        private void TriggerNextComboAttack()
        {
            CharacterActionPresentationState comboAction = (CharacterActionPresentationState)((int)CharacterActionPresentationState.Attack1 + comboIndex);
            comboIndex = (comboIndex + 1) % 4;
            TriggerAction(comboAction, 0.7f);
        }

        /// <summary>开始一段由起跳、上升、下落和落地四阶段组成的可视化跳跃。</summary>
        private void BeginJump()
        {
            jumping = true;
            jumpElapsedSeconds = 0f;
            TriggerAction(CharacterActionPresentationState.JumpStart, 0.18f);
        }

        /// <summary>推进独占动作时间，并在非死亡动作达到明确时长后回到 None。</summary>
        private void AdvanceAction(float deltaTime)
        {
            if (action == CharacterActionPresentationState.None) return;
            actionElapsedSeconds += deltaTime;
            if (action == CharacterActionPresentationState.Death)
            {
                locomotion = CharacterLocomotionPresentationState.Dead;
                return;
            }
            if (actionElapsedSeconds < actionDurationSeconds) return;
            action = CharacterActionPresentationState.None;
            actionElapsedSeconds = 0f;
            actionDurationSeconds = 0f;
        }

        /// <summary>推进地面八向移动和跳跃抛物线，并选择对应持续运动动画状态。</summary>
        private void AdvanceMovementAndJump(float deltaTime)
        {
            Vector2 movementInput = ReadMovementInput();
            bool movementBlocked = action != CharacterActionPresentationState.None && action != CharacterActionPresentationState.JumpStart;
            float movementSpeed = ResolveMovementSpeed();
            if (!movementBlocked && movementInput.sqrMagnitude > 0f)
            {
                Vector2 normalizedInput = movementInput.normalized;
                groundedPosition += new Vector3(normalizedInput.x, 0f, normalizedInput.y) * movementSpeed * deltaTime;
                if (Mathf.Abs(normalizedInput.x) > 0.01f) facing = normalizedInput.x < 0f ? CharacterFacingDirection.Left : CharacterFacingDirection.Right;
            }
            if (jumping)
            {
                AdvanceJump(deltaTime);
                return;
            }
            if (action == CharacterActionPresentationState.Death) locomotion = CharacterLocomotionPresentationState.Dead;
            else if (movementInput.sqrMagnitude <= 0f) locomotion = CharacterLocomotionPresentationState.Idle;
            else if (Input.GetKey(KeyCode.LeftControl)) locomotion = CharacterLocomotionPresentationState.Walk;
            else if (Input.GetKey(KeyCode.LeftShift)) locomotion = CharacterLocomotionPresentationState.Sprint;
            else locomotion = CharacterLocomotionPresentationState.Run;
        }

        /// <summary>推进跳跃高度与三段空中持续状态；落地结束由时间而不是 Spine hit_end 事件决定。</summary>
        private void AdvanceJump(float deltaTime)
        {
            jumpElapsedSeconds += deltaTime;
            float normalizedJumpTime = Mathf.Clamp01(jumpElapsedSeconds / JumpDurationSeconds);
            groundedPosition.y = 4f * JumpHeight * normalizedJumpTime * (1f - normalizedJumpTime);
            if (jumpElapsedSeconds < 0.58f) locomotion = CharacterLocomotionPresentationState.Rising;
            else if (jumpElapsedSeconds < 1.03f) locomotion = CharacterLocomotionPresentationState.Falling;
            else locomotion = CharacterLocomotionPresentationState.Landing;
            if (jumpElapsedSeconds < JumpDurationSeconds) return;
            groundedPosition.y = 0f;
            jumpElapsedSeconds = 0f;
            jumping = false;
            locomotion = CharacterLocomotionPresentationState.Idle;
        }

        /// <summary>读取 WASD 并返回 XZ 平面的二维输入。</summary>
        private static Vector2 ReadMovementInput()
        {
            float horizontal = 0f;
            float vertical = 0f;
            if (Input.GetKey(KeyCode.A)) horizontal -= 1f;
            if (Input.GetKey(KeyCode.D)) horizontal += 1f;
            if (Input.GetKey(KeyCode.S)) vertical -= 1f;
            if (Input.GetKey(KeyCode.W)) vertical += 1f;
            return new Vector2(horizontal, vertical);
        }

        /// <summary>根据验收按键返回与旧 Yefa 配置一致的走、跑、冲刺速度。</summary>
        private static float ResolveMovementSpeed()
        {
            if (Input.GetKey(KeyCode.LeftControl)) return 2f;
            if (Input.GetKey(KeyCode.LeftShift)) return 5f;
            return 3f;
        }

        /// <summary>把当前演示状态组装为独立快照并立即应用到 Presenter。</summary>
        private void ApplyCurrentSnapshot()
        {
            if (presenter == null) return;
            int actionTick = action == CharacterActionPresentationState.None ? 0 : Mathf.Max(0, Mathf.FloorToInt(actionElapsedSeconds / SimulationTickSeconds));
            int actionDurationTicks = action == CharacterActionPresentationState.None ? 0 : Mathf.Max(1, Mathf.CeilToInt(actionDurationSeconds / SimulationTickSeconds));
            float normalizedActionTime = action == CharacterActionPresentationState.None ? 0f : Mathf.Clamp01(actionElapsedSeconds / actionDurationSeconds);
            CharacterPresentationSnapshot snapshot = new CharacterPresentationSnapshot(simulationTick, groundedPosition, facing, locomotion, action, actionSequence, actionTick, actionDurationTicks, normalizedActionTime, health, maximumHealth, damageEventSequence, latestDamageAmount, latestDamageWasCritical);
            presenter.ApplySnapshot(snapshot);
        }

        /// <summary>绘制独立验收场景的操作说明和当前表现状态。</summary>
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16f, 16f, 690f, 185f), GUI.skin.box);
            GUILayout.Label("Yefa Pure Presentation Acceptance");
            GUILayout.Label("WASD move | Ctrl walk | Shift sprint | Space jump | RMB dodge | LMB attack combo");
            GUILayout.Label("Q heavy | F branch | T hit reaction | E skill | R ultimate | K death | J reset | H damage | Shift+H critical damage");
            GUILayout.Label($"Tick {simulationTick} | Locomotion {locomotion} | Action {action} | Spine track0 {presenter?.ActiveAnimationName ?? "--"}");
            GUILayout.Label($"HP {health}/{maximumHealth} | DamageSequence {damageEventSequence} | Position {groundedPosition}");
            GUILayout.EndArea();
        }
    }
}
