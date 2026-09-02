using System.Runtime.CompilerServices;

// 允许 NetworkKit 的专用编辑器测试程序集验证程序集内部传输边界，同时不扩大运行时公开 API。
[assembly: InternalsVisibleTo("Prometheus.NetworkKit.EditorTests")]
