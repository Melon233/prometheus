using System;
using System.Collections.Generic;
using UnityEngine;

namespace PromeArchTrial.Presentation.Character
{
    /// <summary>
    /// 订阅 YefaCharacterPresenter 的伤害数字入口并生成轻量 TextMesh 演示；生产项目可用对象池实现替换该组件而无需修改模拟层或 Presenter。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterDamageNumberTextPresenter : MonoBehaviour
    {
        /// <summary>发布抽象伤害数字请求的角色表现组件。</summary>
        [SerializeField, Tooltip("伤害数字事件来源。")] private YefaCharacterPresenter sourcePresenter;

        /// <summary>单个演示飘字在场景中的存活秒数。</summary>
        [SerializeField, Min(0.05f), Tooltip("伤害飘字存活秒数。")] private float lifetimeSeconds = 0.8f;

        /// <summary>飘字每秒向上的世界单位速度。</summary>
        [SerializeField, Min(0f), Tooltip("伤害飘字上浮速度。")] private float riseSpeed = 0.8f;

        /// <summary>普通伤害文本颜色。</summary>
        [SerializeField, Tooltip("普通伤害文本颜色。")] private Color normalColor = new Color(1f, 0.32f, 0.2f, 1f);

        /// <summary>暴击伤害文本颜色。</summary>
        [SerializeField, Tooltip("暴击伤害文本颜色。")] private Color criticalColor = new Color(1f, 0.85f, 0.1f, 1f);

        /// <summary>当前仍在逐帧上浮和淡出的演示文本集合。</summary>
        private readonly List<ActiveDamageNumber> activeNumbers = new List<ActiveDamageNumber>();

        /// <summary>记录当前是否已向 sourcePresenter 建立事件订阅。</summary>
        private bool subscribed;

        /// <summary>
        /// 注入伤害数字事件来源；重复调用会安全迁移订阅，不会对同一 Presenter 重复注册。
        /// </summary>
        public void Configure(YefaCharacterPresenter presenter)
        {
            if (presenter == null) throw new ArgumentNullException(nameof(presenter));
            Unsubscribe();
            sourcePresenter = presenter;
            Subscribe();
        }

        /// <summary>组件启用时建立事件订阅。</summary>
        private void OnEnable()
        {
            if (sourcePresenter == null) sourcePresenter = GetComponent<YefaCharacterPresenter>();
            Subscribe();
        }

        /// <summary>组件禁用时移除事件订阅并清理当前演示文本。</summary>
        private void OnDisable()
        {
            Unsubscribe();
            for (int index = activeNumbers.Count - 1; index >= 0; index--) if (activeNumbers[index].GameObject != null) Destroy(activeNumbers[index].GameObject);
            activeNumbers.Clear();
        }

        /// <summary>逐帧推动伤害数字上浮、面向主摄像机并在寿命结束时回收。</summary>
        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            Camera mainCamera = Camera.main;
            for (int index = activeNumbers.Count - 1; index >= 0; index--)
            {
                ActiveDamageNumber activeNumber = activeNumbers[index];
                activeNumber.ElapsedSeconds += deltaTime;
                if (activeNumber.ElapsedSeconds >= lifetimeSeconds)
                {
                    if (activeNumber.GameObject != null) Destroy(activeNumber.GameObject);
                    activeNumbers.RemoveAt(index);
                    continue;
                }
                if (activeNumber.GameObject == null)
                {
                    activeNumbers.RemoveAt(index);
                    continue;
                }
                activeNumber.GameObject.transform.position += Vector3.up * riseSpeed * deltaTime;
                if (mainCamera != null) activeNumber.GameObject.transform.rotation = mainCamera.transform.rotation;
                Color color = activeNumber.TextMesh.color;
                color.a = 1f - Mathf.Clamp01(activeNumber.ElapsedSeconds / lifetimeSeconds);
                activeNumber.TextMesh.color = color;
            }
        }

        /// <summary>订阅当前有效 Presenter。</summary>
        private void Subscribe()
        {
            if (subscribed || sourcePresenter == null || !isActiveAndEnabled) return;
            sourcePresenter.DamageNumberRequested += HandleDamageNumberRequested;
            subscribed = true;
        }

        /// <summary>移除当前 Presenter 订阅。</summary>
        private void Unsubscribe()
        {
            if (!subscribed || sourcePresenter == null) return;
            sourcePresenter.DamageNumberRequested -= HandleDamageNumberRequested;
            subscribed = false;
        }

        /// <summary>把抽象伤害数字请求转换为独立 TextMesh，不访问攻击者、受击者或生命组件。</summary>
        private void HandleDamageNumberRequested(CharacterDamageNumberPresentationRequest request)
        {
            GameObject numberObject = new GameObject($"DamageNumber_{request.Sequence}");
            numberObject.transform.position = request.WorldPosition;
            TextMesh textMesh = numberObject.AddComponent<TextMesh>();
            textMesh.text = request.Amount.ToString();
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.fontSize = request.WasCritical ? 72 : 56;
            textMesh.characterSize = request.WasCritical ? 0.025f : 0.022f;
            textMesh.color = request.WasCritical ? criticalColor : normalColor;
            MeshRenderer meshRenderer = numberObject.GetComponent<MeshRenderer>();
            if (meshRenderer != null) meshRenderer.sortingOrder = 1000;
            activeNumbers.Add(new ActiveDamageNumber(numberObject, textMesh));
        }

        /// <summary>保存一个正在播放的 TextMesh 及其已运行时间。</summary>
        private sealed class ActiveDamageNumber
        {
            /// <summary>获取承载文本的 GameObject。</summary>
            public GameObject GameObject { get; }

            /// <summary>获取文本组件。</summary>
            public TextMesh TextMesh { get; }

            /// <summary>获取或设置已经播放的秒数。</summary>
            public float ElapsedSeconds { get; set; }

            /// <summary>创建一个活动伤害数字记录。</summary>
            public ActiveDamageNumber(GameObject gameObject, TextMesh textMesh)
            {
                GameObject = gameObject;
                TextMesh = textMesh;
                ElapsedSeconds = 0f;
            }
        }
    }
}
