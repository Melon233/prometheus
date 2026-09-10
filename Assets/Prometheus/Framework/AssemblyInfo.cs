using System.Runtime.CompilerServices;

// 仅向项目内测试程序集和唯一组合根开放实现类型，使生产代码只能依赖公开的 Kit 与 System 接口。
// 组合根（Runtime）是唯一被允许认识具体实现的装配：它必须能构造 internal sealed 的 System。
[assembly: InternalsVisibleTo("Prometheus.Ai.EditorTests")]
[assembly: InternalsVisibleTo("Prometheus.Animation.EditorTests")]
[assembly: InternalsVisibleTo("Prometheus.Effects.EditorTests")]
[assembly: InternalsVisibleTo("Prometheus.GameplayKit.EditorTests")]
[assembly: InternalsVisibleTo("Prometheus.Narrative.EditorTests")]
[assembly: InternalsVisibleTo("Prometheus.Growth.EditorTests")]
[assembly: InternalsVisibleTo("Prometheus.EntitySystem.EditorTests")]
[assembly: InternalsVisibleTo("Prometheus.Input.EditorTests")]
[assembly: InternalsVisibleTo("Prometheus.Npc.EditorTests")]
[assembly: InternalsVisibleTo("Prometheus.Quest.EditorTests")]
[assembly: InternalsVisibleTo("Prometheus.UIKit.Editor.Tests")]
[assembly: InternalsVisibleTo("Prometheus.World.PlayModeTests")]
[assembly: InternalsVisibleTo("Prometheus.Narrative.Editor")]
[assembly: InternalsVisibleTo("Prometheus.Animation.Editor")]
[assembly: InternalsVisibleTo("Prometheus.Fmod.Editor")]
[assembly: InternalsVisibleTo("Prometheus.UIKit.Editor")]
[assembly: InternalsVisibleTo("Runtime")]
