using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Xuan.Prometheus.Narrative.Tests
{
    /// <summary>
    /// 完全同步的对话视图伪实现。
    /// 所有等待立即完成，因此整棵剧情树可以在 EditMode 中不依赖 Unity 播放循环地跑完。
    /// </summary>
    public sealed class FakeDialogueView : IDialogueView
    {
        /// <summary>按调用顺序记录视图收到的全部事件，供断言执行顺序。</summary>
        public List<string> Log { get; } = new List<string>();

        /// <summary>按顺序记录展示过的台词。</summary>
        public List<DialogueLine> Lines { get; } = new List<DialogueLine>();

        /// <summary>按顺序记录每个节拍实际生效的推进策略。</summary>
        public List<AdvancePolicy> Policies { get; } = new List<AdvancePolicy>();

        /// <summary>按顺序记录展示过的选项请求。</summary>
        public List<DialogueChoiceRequest> ChoiceRequests { get; } = new List<DialogueChoiceRequest>();

        /// <summary>选项选择策略；默认选择第一个未锁定项。</summary>
        public Func<DialogueChoiceRequest, int> ChoiceSelector { get; set; }

        /// <summary>进入台词时触发的钩子，用于在演绎过程中注入跳过一类的外部操作。</summary>
        public Action<DialogueLine> BeginLineHook { get; set; }

        /// <inheritdoc />
        public bool AutoPlay { get; set; }

        /// <inheritdoc />
        public void BeginLine(DialogueLine line)
        {
            Log.Add($"begin:{line.Path}");
            Lines.Add(line);
            BeginLineHook?.Invoke(line);
        }

        /// <inheritdoc />
        public UniTask ShowLineAsync(DialogueLine line, CancellationToken cancellationToken)
        {
            Log.Add($"show:{line.Path}");
            return UniTask.CompletedTask;
        }

        /// <inheritdoc />
        public UniTask WaitForAdvanceAsync(AdvancePolicy policy, CancellationToken cancellationToken)
        {
            Log.Add($"advance:{policy.Kind}");
            Policies.Add(policy);
            return UniTask.CompletedTask;
        }

        /// <inheritdoc />
        public void EndLine()
        {
            Log.Add("end");
        }

        /// <inheritdoc />
        public UniTask<int> ShowChoicesAsync(DialogueChoiceRequest request, CancellationToken cancellationToken)
        {
            Log.Add($"choices:{request.Path}:{request.Choices.Count}");
            ChoiceRequests.Add(request);
            int selected = ChoiceSelector != null ? ChoiceSelector(request) : SelectFirstUnlocked(request);
            Log.Add($"chose:{selected}");
            return UniTask.FromResult(selected);
        }

        /// <inheritdoc />
        public void Hide()
        {
            Log.Add("hide");
        }

        /// <summary>默认选择策略：返回第一个未锁定选项的原始下标。</summary>
        private static int SelectFirstUnlocked(DialogueChoiceRequest request)
        {
            for (int index = 0; index < request.Choices.Count; index++)
            {
                if (!request.Choices[index].IsLocked) return request.Choices[index].OptionIndex;
            }
            throw new InvalidOperationException("Fake dialogue view received a choice request with no selectable option.");
        }
    }

    /// <summary>记录自身被演绎与被落终态次数的探针动作，用于断言组合子的控制流。</summary>
    public sealed class ProbeAction : StoryAction
    {
        private readonly List<string> log;
        private readonly string label;

        /// <summary>创建一个把执行痕迹写入共享日志的探针。</summary>
        public ProbeAction(List<string> log, string label)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            this.label = label;
        }

        /// <summary>获取该探针被演绎的次数。</summary>
        public int PlayCount { get; private set; }

        /// <summary>获取该探针被落终态的次数。</summary>
        public int SettleCount { get; private set; }

        /// <inheritdoc />
        public override UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            // 与真实叶子一致地响应取消，使中止与跳过两条路径在同步测试中同样可被观测。
            cancellationToken.ThrowIfCancellationRequested();
            PlayCount++;
            log.Add($"play:{label}");
            return UniTask.CompletedTask;
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            SettleCount++;
            log.Add($"settle:{label}");
        }
    }

    /// <summary>剧情测试共用的构造与同步等待工具。</summary>
    public static class NarrativeTestKit
    {
        /// <summary>创建一个载入了少量测试文案的文本表。</summary>
        public static TextMap CreateTextMap(params string[] keys)
        {
            TextMap textMap = new TextMap();
            List<KeyValuePair<string, string>> entries = new List<KeyValuePair<string, string>>();
            entries.Add(new KeyValuePair<string, string>("actor.a.name", "甲"));
            entries.Add(new KeyValuePair<string, string>("actor.b.name", "乙"));
            if (keys != null)
            {
                for (int index = 0; index < keys.Length; index++) entries.Add(new KeyValuePair<string, string>(keys[index], $"文案-{keys[index]}"));
            }
            textMap.Load("zh-CN", entries);
            return textMap;
        }

        /// <summary>创建一个使用伪视图的剧情上下文。</summary>
        public static StoryContext CreateContext(FakeDialogueView view, params string[] textKeys)
        {
            return new StoryContext(CreateTextMap(textKeys), view);
        }

        /// <summary>
        /// 同步取出一个应当立即完成的 UniTask 结果。
        /// 测试中的全部等待都由伪视图立即完成，因此剧情树不会真正挂起，无需 Unity 播放循环。
        /// </summary>
        public static TResult AwaitSync<TResult>(UniTask<TResult> task)
        {
            Assert.That(task.Status.IsCompleted(), Is.True, "Story task did not complete synchronously; the test fixture must avoid actions that yield.");
            return task.GetAwaiter().GetResult();
        }

        /// <summary>同步等待一个应当立即完成的 UniTask。</summary>
        public static void AwaitSync(UniTask task)
        {
            Assert.That(task.Status.IsCompleted(), Is.True, "Story task did not complete synchronously; the test fixture must avoid actions that yield.");
            task.GetAwaiter().GetResult();
        }
    }
}
