using System.Runtime.CompilerServices;

// 联网实现程序集：ServiceSystem 与各领域 NetworkGateway 均为 internal sealed。
// 单机版组合根（Runtime）当前不引用本程序集；恢复联网时在 Runtime.asmdef 加上本程序集引用，
// 组合根即可凭此声明构造这些实现，其余代码仍只依赖 IPoiGateway / IBagGateway 契约。
[assembly: InternalsVisibleTo("Runtime")]
