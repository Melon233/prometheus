# 生命周期与启动链路重设计

> 状态：**P0–P3 全部实现并通过验证（EditMode 231/231；Play 模式完整启动链路、世界切换与副本压栈/弹栈零报错）**。
> 本次重设计**不受既有 ArchSpec 约束**，与之冲突以本文为准；已按第 11 节修订 ArchSpec。
>
> **2026-09-27 修订**：P2（`GameFlow` 状态机、开屏、热更占位、`LoginPanel`）与 P3 中的世界栈（`WorldSession`）已移除，
> 启动改回「入口直接加载模块并进入主世界」的线性链路；三段生命周期、依赖注入、单一变量存储与世界相位保留不变。
> 第 6、7 节已按现状重写，修订原因与影响见第 13 节。

## 0. 触发这次重设计的四件事

1. `ARCH-COMP-004` 逼着系统把私有资源地址提升为跨装配常量。
2. `ARCH-SYS-005` 逼着系统在使用点做服务定位，依赖关系不写在签名上。
3. 系统在开屏时就全量初始化，登录界面被拖在整个玩法初始化之后。
4. 任务与剧情的互投影模型需要一个重入守卫才能不无限递归。

四件事的共同形态是：**为了满足规则而采取了特别的写法**。因此本文同时确立一条元规则（第 10 节），把这种情形本身定为需要审查规则的信号。

## 1. 三段生命周期

现在只有一段：Core 一建，所有东西一起活到进程结束。重设计为三段：

| 段 | 内容 | 创建 | 销毁 |
| --- | --- | --- | --- |
| **App** | `Core` + 全部 Kit（Asset / Event / UI / Gameplay） | 进程启动 | 进程退出 |
| **Session** | 全部玩法 System | Kit 就绪后由 `Entry` 建立 | 进程退出（`GameplayKit.DestroySession` 也可单独销毁） |
| **World** | 场景本身 + 场景内 Entity / POI / NPC | 每次进入场景 | 每次离开场景 |

**关键收益：Kit 与玩法 System 的初始化彼此独立。** Kit 就绪之前不加载任何玩法配置；会话由入口在 Kit 就绪后显式建立。
（原设计借此让登录界面只依赖 App 段，登录界面已随第 13 节的修订移除，但这条分段本身仍然成立。）

**Session 独立于 World**：背包、任务进度、小队必须跨场景存活；换世界时 System 不能重建。所以一个 Session 跨越多个 World。

**销毁会话、保留 App 仍然可表达**：`GameplayKit` 是会话工厂而不是会话本身，同一个 Core 上可以销毁并重建会话。
旧结构里做不到——`Core.Configure` 只能调一次，`GameplayKit` 在 `IsReady` 之后拒绝注册。当前没有调用方使用这项能力。

## 2. `XSystem` 与 `Kit` 同构

`Kit` 有 `AfterNewAsync` / `AfterNew` / `OnUpdate` / `Dispose`，`XSystem` 却只有同步的 `AfterNew`。这个不对称是很多问题的根源：需要异步加载配置的系统无法自给自足，只能让组合根代劳。

重设计后逐项对齐，并补上世界级相位：

```csharp
public abstract class XSystem : IDisposable
{
    public virtual UniTask AfterNewAsync();                       // 并行；只加载自己的配置
    public virtual void AfterNew();                               // 正序；自身初始化
    public virtual UniTask OnWorldEnterAsync(WorldContext world); // 正序；建立世界级内容
    public virtual void OnWorldExit();                            // 逆序；释放世界级内容
    public virtual void BeforeEntityUpdate(float dt);
    public virtual void OnUpdate(float dt);
    public virtual void Dispose();                                // 逆序
}
```

### 2.1 相位规则

| 相位 | 执行 | 允许 | 禁止 |
| --- | --- | --- | --- |
| 构造函数 | 同步，拓扑序 | 保存注入的依赖、登记自己的命名空间 | 加载资源、调用依赖的方法 |
| `AfterNewAsync` | **并行** | 加载自己的配置资产 | 调用其他 System |
| `AfterNew` | 同步，正序 | 调用注入的依赖、校验配置 | —— |
| `OnWorldEnterAsync` | 异步，正序 | 生成世界内容 | —— |
| `OnWorldExit` | 同步，逆序 | 释放引用 | 任何耗时操作 |

`AfterNewAsync` 里禁止调用其他 System 是并行安全的全部理由：没有跨系统读取就没有竞态。

**进入异步、退出同步**是有意的不对称：进入世界要加载预制体、生成实体，可以慢、可以显示进度；退出时场景下一刻就被 Unity 销毁，不存在"慢慢退出"，能做的只有释放引用。

## 3. `GameplayKit` 接管系统装配

### 3.1 组合根缩到只剩一句话

旧 `InstallAsync` 混了四件事：加载配置、注册系统、交叉接线、加载场景。重设计后组合根**只回答一个问题**——这一局由哪些 System 组成：

```csharp
public interface IGameplaySystemInstaller
{
    /// 同步、纯构造。资源加载在各 System 的 AfterNewAsync，世界内容在 OnWorldEnterAsync。
    void Install(IGameplaySystemRegistry registry);
}
```

`CreateInitialContent` 整个删除（并入世界相位）。装配的驱动完全由 `GameplayKit` 负责，节奏与 `Core` 驱动 Kit 逐字相同：

```
installer.Install(registry)  →  await WhenAll(每个 System 的 AfterNewAsync)  →  正序 AfterNew()
```

### 3.2 `GameplayKit` 变成会话工厂

System 是 Session 段的，`GameplayKit` 是 App 段的 Kit，两者生命周期不同，所以它不再**是**会话，而是**造**会话：

```
GameplayKit（App 段 Kit）
   └─ 当前会话（Kit 就绪后由 Entry 创建，可单独销毁）
         ├─ 全部 System
         └─ 当前世界（每个场景）
```

- `CreateSessionAsync(installer)` —— `Entry` 在 Kit 就绪、配表加载后调用
- `DestroySession()` —— 逆序 `Dispose` 全部 System；**不驱动世界退出相位**，理由见 8.3
- `EnterWorldAsync(WorldContext)` / `ExitWorld()` —— 当前只由 `Entry` 在启动时进入主世界（原 `WorldSession` 已移除）
- `TryGetSystem<T>` —— **无会话时返回 false**

无会话返回 false 覆盖了会话建立之前的那段时间。场景对象（如 `NpcMarkerMono`）本就可能在会话之外存在，那条"取不到系统就显示无标记"的判断因此继续成立。

`GameplayKit` 不认识场景：驱动世界的一方（当前是 `Entry`）先 `ExitWorld`（如有），再由 `Core.Asset` 加载场景，最后把 `WorldContext` 交给 `EnterWorldAsync`。

## 4. System 之间强制依赖注入

### 4.1 规则反转

| | 旧 `ARCH-SYS-005` | 新规则 |
| --- | --- | --- |
| System 间依赖 | **禁止**构造注入，必须在使用点 `Core.Gameplay.GetSystem` | **强制**构造注入 `I*System` 契约 |
| `Core.Gameplay` | System 内的标准做法 | **System 实现内禁止出现** |

### 4.2 旧规则的代价

三处现场证据，全部是"为了满足规则而采取的特别写法"：

1. `PoiSystem` 里两个专为绕开规则而存在的静态属性，注释直接写着*"按 ARCH-SYS-005 在使用点解析，不长期保存实例"*、*"按 ARCH-SYS-007，跨 System 依赖在使用点解析，不长期保存 TeamSystem 实例"*。
2. 组合根里*"任务系统必须先于 NpcSystem 注册：NpcSystem 在 AfterNew 里订阅它的绑定失效通知"*——注册顺序靠人工注释维护。
3. 架构测试有一整套 `CaptureRegistrationOrder` + `ResolveContractsReferencing` 设施，**用正则扫源码推导依赖关系再校验注册顺序**。这套设施存在的唯一理由，就是依赖没有写在签名上。

### 4.3 收益

- 依赖写在构造函数签名上，编译器可见、IDE 可跳转。
- **环从"能构造但会死循环"变成"编译不过"**——不能把一个还没造出来的东西传进去。
- 注册顺序不再是需要记住的规则，而是 C# 强制的事实。
- 释放逆序 = 逆拓扑序，依赖者先于被依赖者释放，正确性由构造顺序保证。

### 4.4 立刻暴露的环

改造第一步就撞上一个真实的环：

```
EntitySystem.RemoveRegisteredEntity → ITeamSystem.UnregisterMember
TeamSystem → IInputSystem → IEntitySystem
```

即 `Entity → Team → Input → Entity`。它今天存在且运行正常，因为服务定位把解析推迟到了调用时刻——这正是服务定位掩盖的东西。

**断法：把 `Entity → Team` 这条边反转成事件。** 新增 `EntityRemovedEvent : EntityEvent`，`EntitySystem` 在移除实体时发布，`TeamSystem` 订阅并注销自己的成员。

这条边本来就是"通知已经发生的事实"而不是"请求对方做事"，用事件表达比用调用更准确，且与既有的 `EntityDiedEvent` 是同一个先例。

### 4.5 顺带归位：小队创建

`EntitySystem.CreateInitialTeam(TeamSystem)` 是同一个环的另一条腿：实体容器反过来认识小队。

把小队创建移到 `TeamSystem.OnWorldEnterAsync`——TeamSystem 通过注入的 `IEntitySystem` 创建自己的三个成员、填自己的槽位。一次改动解决四件事：

- `EntitySystem` 不再需要认识 `TeamSystem`
- `TeamSystem.MemberAddresses` 从 `public static` 收回 `private`（"小队成员是谁"本就是 TeamSystem 的领域知识）
- 小队创建变成世界级的——实体本来就是世界级的
- `IGameplaySystemInstaller.CreateInitialContent` 整个删除

### 4.6 服务定位保留给谁

Unity 创建的对象无法构造注入，它们继续用 `Core.Gameplay.TryGetSystem`：

- MonoBehaviour（`PoiMono`、`NpcMarkerMono`、`RegionTriggerMono`）
- `UIPanel`（由 UIKit 从预制体实例化）
- Entity 的 Logic / Component（每个实体运行时创建）
- 端口适配器（`Ports/Runtime/` 下的适配器）

边界因此可测：**扫描 System 实现，源码内不得出现 `Core.Gameplay`。**

**Kit 不在此列。** `Core.Asset`、`Core.Event`、`Core.UI` 允许在任何位置直接引用，包括 System 内部。
Kit 是进程级基础能力而不是会话级玩法依赖：它们在 System 存在之前就已就位、在 System 全部释放之后才消失，
不存在「拿到时还没初始化」的时序风险，注入它们只会给每个构造函数增加三个恒定参数。
被禁的只有 `Core.Gameplay`——因为它背后是一张会变、会成环、需要被看见的依赖图。

### 4.7 依赖图

拓扑序即注册序，且由编译器强制：

```
EntitySystem                                    ServiceSystem
     │                                               │
     ├── InputSystem ─── TeamSystem            PoiGateway   BagGateway
     ├── CameraSystem                               │            │
     └───────────────── PoiSystem ─────────────────┘        BagSystem
                                                
EffectSystem ─── CombatAudioPresentationSystem      WorldMapSystem

NarrativeSystem ─┬─ NpcSystem
QuestSystem ─────┘
```

### 4.8 共享状态的注入

`VariableStore`（第 5 节）不是 System，是会话级共享状态，由组合根创建并注入给需要它的系统。新规则下这与注入一个 System 契约在形式上完全一致——**系统需要的一切都从构造函数进来**，不再有"有些从构造函数、有些从全局入口"的分裂。

## 5. 单一变量存储

### 5.1 现状与证据

任务与剧情各有一个变量存储，通过互相注册为投影互通：

```csharp
questSystem.Variables.AddProjection(narrativeSystem.Variables);
narrativeSystem.Variables.AddProjection(questSystem.Variables);
narrativeSystem.Variables.Changed += questSystem.Invalidate;
```

四条证据说明这个模型是错的：

1. **`QuestVariables`(222 行) 与 `StoryVariables`(137 行) 约 85% 重复**：`values` 字典、`projections` 列表、`Changed` 事件、`TryResolve`、`IsKnownRoot`、`Capture`、`Restore`、`Clear` 全是同一套。
2. **`QuestVariables.isResolving` 重入守卫**。注释自陈*"环是互投影模型的固有属性而不是接线错误"*。一个必须靠守卫才能不无限递归的模型，守卫本身就是模型错误的证据。
3. **`QuestSystem` 全系 `using Xuan.Prometheus.Narrative`**。所谓"任务与剧情互不认识"只有一半为真：Quest 一直依赖 Narrative 的 `StoryValue` / `StoryExpression` / `IStoryVariableResolver`，只是这个依赖披着"表达式语言"的外衣。铁律 R6 只管住了反方向。
4. **写权限按命名空间划分已经是既有模型**。`QuestValidator` 强制任务触发器只能写 `quest.*`，`QuestVariables.RequireOwnedPath` 拒绝写其他根。既然所有权本就按根段划分，就不需要两个存储来表达它。

### 5.2 目标形态

**一个 `VariableStore`，按路径根段路由到命名空间所有者。**

```csharp
public interface IVariableNamespace
{
    string Root { get; }                                          // "quest" / "flag" / "var" / "bag"
    bool TryResolveDerived(string path, out StoryValue value);    // quest.<id>.status 这类现算值
    bool TryGetDefault(string path, out StoryValue value);        // flag.* → false，quest.* → 0
    bool AllowsWrite { get; }
}

public sealed class VariableStore
{
    public void Register(IVariableNamespace ns);
    public bool TryResolve(string path, out StoryValue value);
    public void Set(string path, StoryValue value);
    public event Action<string> Changed;
    public IReadOnlyDictionary<string, StoryValue> Capture(string root);
    public void Restore(string root, IReadOnlyDictionary<string, StoryValue> snapshot);
}
```

解析顺序：**已存值 → 所有者的推导值 → 所有者的缺省值 → 失败**。与当前两个类各自的顺序一致，只是没有了"查对方的投影"这一步。

`values` 字典由 `VariableStore` 独家持有——路径自带根段、天然唯一。存档按前缀切片，各系统仍各存各的档。

### 5.3 消掉了什么

| 消失的东西 | 原因 |
| --- | --- |
| `isResolving` 重入守卫（两处） | 路由是一次字典查找，图上没有环 |
| `AddProjection` / `RemoveProjection` / `projections` 遍历 | 不再有投影概念 |
| 组合根的 3 行交叉接线 | 各系统构造时自行登记命名空间 |
| `IsKnownRoot` 的递归遍历 | 变成 `namespaces.ContainsKey(root)` |
| "catalog 必须在投影之后注册"的顺序约束 | 构造期登记，`AfterNew` 校验时全部根必然已知 |
| 约 200 行重复的字典/存档/清理代码 | 只剩一份 |

四点真实差异全部保留，各自落在对应的 `IVariableNamespace` 实现里。

### 5.4 落地结果

实现与设计一致，四点真实差异各自落在对应的 `IVariableNamespace` 上，没有新增偏差。实测消掉的东西：

| 消失的东西 | 位置 |
| --- | --- |
| `isResolving` 重入守卫 | `QuestVariables`（`IsKnownRoot` 与 `TryResolve` 各一处） |
| `AddProjection` / `RemoveProjection` / `projections` 遍历 | `QuestVariables` 与 `StoryVariables` 各一套 |
| 组合根的 3 行交叉接线 | `PrometheusSystemInstaller` |
| 两套重复的字典 / `Capture` / `Restore` / `Clear` | 合并为 `VariableStore` 一份 |

一条**没有预料到**的连带结果：搬走表达式语言之后，`QuestSystem/` 下**六个生产文件**的 `using Xuan.Prometheus.Narrative`
全部变成死引用，删掉后仍然编译通过。这是「任务与剧情互不认识」第一次在两个方向上都被编译器证实——
在此之前，铁律 R6 只管住了 Narrative→Quest 一个方向，反方向的依赖一直存在，只是披着「表达式语言」的外衣。

### 5.5 表达式语言独立成库

`StoryValue`、`StoryExpression*`、`VariableStore`、`IVariableNamespace` 从 `NarrativeSystem/` 移到 `Gameplay/Expression/`，命名空间改为 `Xuan.Prometheus.Expression`。这样 `QuestSystem` 依赖的是共享表达式库而不是 `Xuan.Prometheus.Narrative`，"任务与剧情互不认识"在**两个方向上**才第一次都成立。

**迁移风险已核实**：`[SerializeReference]` 只用在 `StoryNode` / `EffectOperation` / `QuestAction` 三族子类上，它们全部留在原命名空间；`StoryValue` 是 struct，不可能被 `SerializeReference` 引用。因此这次移动不触及 `ARCH-UNITY-002` 的资产损坏风险，Gameplay 是单一装配，`asm:` 字段也不变。

## 6. 完整流程

> 本节按 2026-09-27 修订后的现状重写。原 P2 的 `GameFlow` 状态机、开屏、热更占位与登录界面已移除，见第 13 节。

```
Entry.Start ─ Kit 初始化 ─ Config.Load ─ CreateSession ─ LoadScene(MainWorld) ─ EnterWorld ─ 打开 HUD
```

启动是 `Entry.BootAsync` 里的一条线性链路，每一步都依赖上一步的结果，因此顺序即契约：

| 步骤 | 段 | 做法 | 关键约束 |
| --- | --- | --- | --- |
| **启动** | App | `Entry.unity` 建 Core，注册全部 Kit | 保持 Player Build 唯一入口 |
| **Kit 初始化** | App | `UniTask.WhenAll(core.CreateAfterNewTasks())` 后执行同步 `core.AfterNew()` | 资源包在这一步就绪 |
| **配表** | App | `Core.Config.Load()` | 资源包就绪之后、任何玩法 System 构造之前（`ARCH-CONFIG-001`） |
| **模块资源配置** | Session | `Core.Gameplay.CreateSessionAsync(new PrometheusSystemInstaller())`，各 System 的 `AfterNewAsync` 并行 | |
| **主世界** | World | 按 `WorldCatalog.RequireMainWorld()` 加载场景，再 `Core.Gameplay.EnterWorldAsync(WorldContext)` | 场景与出生位姿由世界目录决定，入口不写死 |
| **HUD** | World | 世界定义 `ShowsHud` 为真时 `Core.UI.OpenPanel<HudPanel>()` | 必须在世界相位之后，HUD 要读场景内容 |

### 6.0 实现要点

**链路可以被中止**：`Entry` 持有一个生命周期令牌，在 `Entry.OnDestroy` 中**先于** `Core.Dispose` 取消。
链路里的每个 await 都用 `AttachExternalCancellation` 挂上这个令牌——Kit 初始化、会话建立、场景加载本身都不可取消，
取消只让链路停止等待，不在 Core 释放之后继续访问 Kit。链路以 `BootAsync(...).Forget()` 启动，
中止时以取消异常结束，UniTask 默认不上报这类异常。

**没有局外相机**：从启动到会话建立之间场景里没有相机，`CameraSystem` 在会话建立时创建玩法相机。
这段时间没有任何界面需要渲染，因此不再需要一台只负责清屏的相机。

**热更**：当前两种播放模式（编辑器模拟 / 内置离线）没有远端内容，`AssetKit` 不再预留下载阶段与进度广播。
接入热更时需要在 `AssetKit.Initialize` 的「清单就绪」与「可加载」之间加入下载，并同时设计对应的启动界面——
开屏与热更界面运行在资源包就绪之前，不能使用 `UIPanel`（原 6.1 的结论仍然成立）。

## 7. 世界会话与场景种类

### 7.1 切换世界的三步

```
逆序 OnWorldExit()  →  LoadSceneAsync(address)  →  正序 OnWorldEnterAsync(context)
```

第一步是关键：`AssetKit.LoadSceneAsync` 用 `LoadSceneMode.Single`，Unity 会直接销毁旧场景全部 GameObject，而 `EntitySystem` 持有的 Entity 包着这些 GameObject。必须抢在销毁之前让系统主动释放，否则加载第二个场景的那一刻就是一片空引用。

现在不出事，只因为场景一辈子只加载一次且在任何 Entity 创建之前——靠巧合成立的时序，不是设计。

### 7.2 世界目录

> 本节按 2026-09-27 修订后的现状重写。原 P3 的世界栈（`WorldSession`：平级切换、压栈进副本、弹栈返回）已移除，
> 当前只有启动时进入主世界这一个入口，见第 13 节。

`WorldDefinition` 与 `WorldCatalog`（ScriptableObject，按地址 `WorldCatalog` 加载，位于 `Gameplay/WorldCatalog/`）描述：
世界标识、场景地址、种类、默认出生位姿、HUD 档位。种类为 `MainWorld` / `SubWorld` / `Dungeon` / `Activity`。

`Entry` 在启动时加载目录并校验：标识非空且唯一、场景地址非空、**有且只有一个主世界**；
随后用 `RequireMainWorld()` 取出主世界，按它的场景地址加载场景、按它的出生位姿构造 `WorldContext`。

**跳转按 worldId 而不是场景地址**这条规则保留：将来重新引入世界切换时，仍然按 `WorldId` 引用世界。

#### 出生位姿为什么走 `WorldContext`

`WorldContext` 只装那些「由流程解析、System 自己查不到」的事实。出生位姿满足这条：
首次进入用配置的出生点，将来从副本弹回时用记录的返回点，只有驱动世界切换的一方知道该用哪个。
副本难度、奖励倍率之类**不**满足：关心它们的 System 按 `WorldId` 自行查配置即可。

配套改动：`TeamSystem` 不持有出生坐标常量，改用 `world.SpawnPosition` / `world.SpawnRotation`。

### 7.3 进入世界失败

场景地址错误、资源缺失属于配置错误，直接抛出，不做可恢复处理。

## 8. P0 实施中暴露的三个缺口

前两个不是重构引入的 bug，而是**「一辈子只加载一次场景」的假设留下的地雷**，在第一次真正切换世界时同时引爆。
它们正是 P0 存在的理由——不做这一期，做第一个副本时才会撞上。第三个是我自己在这一期写出来的，
一并记下来，因为它牵出了一条必须成立的约束。

### 8.1 `AssetKit` 拒绝加载第二个场景

`LoadSceneAsync` 开头写着「已经持有场景就抛异常」：

```csharp
if (activeSceneHandle != null && activeSceneHandle.IsValid)
    throw new InvalidOperationException($"AssetKit already owns loaded scene '{activeSceneHandle.SceneName}'.");
```

这条在只有一个场景时永远不会触发，因此从未被发现。改为**先释放旧句柄再加载**：场景以 `LoadSceneMode.Single`
加载，旧场景本来就会被 Unity 卸载，继续持有它的句柄只会让旧场景的资源包永远无法回收。
世界切换的时序由驱动世界的一方保证：各 System 先走完 `OnWorldExit`，才轮到这里卸载场景。

### 8.2 相机的跟随节点随角色一起被销毁

`CameraSystem` 的相机本体挂在 `PersistentRoot` 下，是会话级的；但 `BindFollowTarget` 会把跟随参考节点
**挂到角色身下**，而角色随场景销毁。切换世界后再收到小队变更事件，就会踩到一个已销毁的 GameObject：

```
MissingReferenceException ... CameraSystem.BindFollowTarget → OnActiveTeamMemberChanged
```

修法是把既有的 `DetachFollowTarget()`（本就写着"把参考节点收回系统根对象"）接到 `OnWorldExit`。
逆序退出保证了正确性：`CameraSystem` 在 `EntitySystem` 销毁角色之前收回节点。

这一条是 `ARCH-LIFE-003` 的典型样本：**一个对象属于哪一段，看的不是谁创建了它，而是它被挂在谁身下。**

### 8.3 会话销毁不能顺手退出世界

修完 8.2 之后立刻冒出第三个问题：退出 Play 模式时 `Core.Dispose()` 中途抛异常，`Core.Current` 没被清空，
于是**后续每一次 EditMode 测试都因为「A Core instance is already active」而失败**——一个 Play 会话污染了整个测试套件。

根因是我最初把 `DestroySession()` 写成「先 `ExitWorld` 再逆序 `Dispose`」。看起来对称，实际上错了：

**`OnWorldExit` 的前提是「场景还活着」，而会话销毁有两种来路，只有一种满足这个前提。**

| 来路 | 场景状态 | 谁负责退出世界 |
| --- | --- | --- |
| 主动销毁会话（当前无调用方，原为登出） | 仍然存活 | 调用方先显式 `Core.Gameplay.ExitWorld()` |
| 进程 / Play 模式结束 | Unity 已在销毁场景对象 | **没有人**——此时碰它们必然抛异常 |

因此 `DestroySession()` 不再调用 `ExitWorldInternal()`，代价是一条必须成立的约束：

> **每个 System 的 `Dispose` 必须自足，不得依赖 `OnWorldExit` 先跑过一遍。**

四个实现了世界相位的系统都已满足：`EntitySystem.Dispose` 自己释放全部实体，`PoiSystem.Dispose` 自己
取消令牌并清空集合，`TeamSystem.Dispose` 自己清槽位与租约，`CameraSystem.Dispose` 自己销毁运行时对象。
`OnWorldExit` 做的是**归还**（把东西交回世界之外的持有者），`Dispose` 做的是**销毁**——两者本就不是同一件事。

这一条同样是 `ARCH-META-001` 的样本：一个「对称好看」的实现逼出了一次抛异常的关机路径，
说明对称的是形式而不是语义。

**后续（Unity 6.6 关闭域重载后）**：以前每次进入 Play 模式都会重载域，残留的 `Core.Current` 会被顺带清掉，所以这个问题只污染 EditMode 测试。关闭域重载后，同样的残留会让下一次进入 Play 直接失败。现在有两层保障：

1. `Core.Dispose` 把静态入口复位和已释放标记放进 `finally`。Kit 释放抛出的异常照常上报，但 `Current` 一定会被清空。
2. `Core` 的静态入口标注了 `[AutoStaticsCleanup]`，由引擎在进出播放模式时复位，见 `Framework/CoreKit/README.md`「关闭域重载时的静态状态」。生成代码会先对残留实例调用 `Dispose` 再置空。有了第 1 条的已释放标记，这次调用是空操作，不会把同一个异常再抛一次。

## 9. 分期

| 期 | 内容 | 状态 |
| --- | --- | --- |
| **P0** | `XSystem` 相位对齐（2）+ `GameplayKit` 会话化（3）+ 强制依赖注入（4）+ 世界钩子与 `WorldSession`（7.1） | **已实现并验证** |
| **P1** | 单一变量存储（5）+ 表达式库独立 | **已实现并验证** |
| **P2** | `GameFlow` + 开屏 + 热更占位 + `LoginPanel` | 已实现，**2026-09-27 移除** |
| **P3** | 世界目录 + 世界栈（7.2） | 世界目录保留；世界栈 **2026-09-27 移除** |

## 10. 元规则

> **`ARCH-META-001`**：新增或修改逻辑时，如果因为某条既有规则而不得不采取**特别的写法**——额外的重入或空值守卫、只为让别人读到而提升的可见性、只为绕开规则而存在的中间层或静态属性、一段解释"为什么必须按这个顺序写"的注释、一套用来校验本可由编译器保证的事情的测试设施——必须先评估该规则是否应当修改，并记录结论。不得默默用特别写法绕过。

这条规则不是事后总结，它有四个当场验证的样本：

| 特别的写法 | 被牵连的规则 | 结论 |
| --- | --- | --- |
| 三个资源地址常量提升为 `public` / `internal` | `ARCH-COMP-004` | 规则错，改规则 |
| `PoiSystem` 两个"在使用点解析"的静态属性 | `ARCH-SYS-005` | 规则错，反转规则 |
| 用正则扫源码校验注册顺序的测试设施 | `ARCH-SYS-005` | 同上，设施随规则一起废除 |
| `QuestVariables.isResolving` 重入守卫 | 互投影模型 | 模型错，换模型 |

四个样本里没有一个是"代码写得不好"，全部是规则或模型的问题。这正是需要这条元规则的理由：**特别的写法是证据，不只是瑕疵。**

## 11. 对 ArchSpec 的修订

- **新增 `ARCH-META-001`**：正文见第 10 节。放在规范开头，因为它管所有其他条。
- **反转 `ARCH-SYS-005`**：System 之间必须构造注入 `I*System` 契约；System 实现内禁止 `Core.Gameplay`。服务定位仅供 Unity 创建的对象与端口适配器使用。
- **修订 `ARCH-COMP-004`**：删去"每个资源地址由使用它的系统以常量持有"及其举例；其余保留。
- **新增 `ARCH-COMP-006`（资源自持）**：资源地址由使用它的系统以 `private` 常量持有，并在自己的 `AfterNewAsync` 中加载。禁止代加载，禁止为让别人代加载而提升可见性。
- **修订 `ARCH-COMP-003`**：`IGameplaySystemInstaller` 只回答"由哪些 System 组成"；注册顺序由构造依赖强制，不再是需要人工维护的约定。
- **修订 `ARCH-SYS-007`**：`IGameplayKit` 仍禁止公开玩法领域概念；会话与世界的生命周期是框架级组合概念，允许公开。
- **新增 `ARCH-LIFE-002`（相位隔离）**：`AfterNewAsync` 内禁止调用其他 System。
- **新增 `ARCH-LIFE-003`（三段生命周期）**：App / Session / World 的划分与各自的创建销毁时机。
- **删除注册顺序校验测试**：其职责已由构造注入交给编译器。

## 12. 已决策与非目标

| 决策 | 结论 |
| --- | --- |
| 登录 | 已移除登录界面；单机版启动直接进入主世界 |
| 剧情场景 | 原地演出为主，独立场景后置 |
| 副本权威 | 纯本地，不新增 Gateway |
| 进入世界失败 | 直接抛出，不做可恢复处理 |

非目标：不实现 `HostPlayMode` 与真实下载器；不做真实账号与服务器存档（单机版不做任何存档，每次进入游戏都重新开始）；不做副本服务器建房；不拆分 System 级 asmdef。

## 13. 2026-09-27 修订：移除启动流程，改为单机直进

**动机**：项目先做单机版。开屏、热更占位与本地假登录在单机版里没有承载任何实际能力，
却让入口与 `AssetKit` 多了一台状态机、一套进度契约与一台局外相机；世界栈目前也只有验收脚本在用。

**移除**：

- `Bootstrap/GameFlow.cs`（`GameFlowStage` 状态机）、`Bootstrap/BootScreen.cs`（开屏与热更界面）、`Bootstrap/OuterCamera.cs`（局外相机）
- `Bootstrap/WorldSession.cs`（世界栈）
- `UI/LoginPanel` 及其预制体 `BundleResources/UI/Login`
- `AssetKit` 的启动进度（`AssetBootProgress` / `AssetBootPhase` / `BootProgressChanged`）与空的 `DownloadContent` 占位阶段
- ArchSpec 的 `ARCH-BOOT-001` / `ARCH-BOOT-002`

**保留**：三段生命周期、`GameplayKit` 会话工厂与世界相位、构造注入、单一变量存储、世界目录与 `WorldContext`。
这些与启动界面无关，System 侧的实现因此一行未改。

**现在的入口**：`Entry.BootAsync` 线性执行第 6 节的步骤。将来要恢复登录或世界切换，在 `Entry` 与 `GameplayKit` 之间
重新引入编排者即可——`CreateSessionAsync` / `DestroySession` / `EnterWorldAsync` / `ExitWorld` 这几个接缝都还在。
