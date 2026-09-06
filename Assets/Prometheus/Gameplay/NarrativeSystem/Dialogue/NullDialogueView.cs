using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 不呈现任何内容的对话视图。
    /// 仅用于在尚未注入正式视图时构造上下文以读写存档；一旦真的被用于演绎就立即报错，
    /// 避免剧情在无人可见的情况下静默跑完。
    /// </summary>
    public sealed class NullDialogueView : IDialogueView
    {
        /// <summary>获取全局共享实例。</summary>
        public static readonly NullDialogueView Instance = new NullDialogueView();

        /// <summary>限制外部创建，使该视图只以共享实例形式存在。</summary>
        private NullDialogueView()
        {
        }

        /// <inheritdoc />
        public bool AutoPlay { get; set; }

        /// <inheritdoc />
        public void BeginLine(DialogueLine line)
        {
            throw Fail();
        }

        /// <inheritdoc />
        public UniTask ShowLineAsync(DialogueLine line, CancellationToken cancellationToken)
        {
            throw Fail();
        }

        /// <inheritdoc />
        public UniTask WaitForAdvanceAsync(AdvancePolicy policy, CancellationToken cancellationToken)
        {
            throw Fail();
        }

        /// <inheritdoc />
        public void EndLine()
        {
        }

        /// <inheritdoc />
        public UniTask<int> ShowChoicesAsync(DialogueChoiceRequest request, CancellationToken cancellationToken)
        {
            throw Fail();
        }

        /// <inheritdoc />
        public void Hide()
        {
        }

        /// <summary>构造一条指明缺少视图的错误。</summary>
        private static InvalidOperationException Fail()
        {
            return new InvalidOperationException("No dialogue view is attached to the narrative system. Call INarrativeSystem.SetView before playing a story.");
        }
    }
}
