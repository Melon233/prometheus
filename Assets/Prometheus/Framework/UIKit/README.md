# UIKit

UIKit 包含两条并行的界面链路：

- **面板运行时**：Prefab 上的 `UIComponentBinder` → 生成强类型 `PanelBase` → 业务 Panel。这是所有界面最终运行的方式。
- **UI 原型 DSL**：用 C# 声明界面结构，构建器生成 Prefab。这是快速产出可用原型的方式，产物仍然接入上面那条链路。

前半篇描述面板运行时，后半篇描述原型 DSL。

---

## 初始化与 EventSystem

`UIKit.AfterNew` 初始化时会检查 `EventSystem.current`。场景已经存在 EventSystem 时直接沿用；场景未提供时，UIKit 动态创建包含 `EventSystem` 和 `InputSystemUIInputModule` 的 `[UIKit.EventSystem]` 节点，并通过 `DontDestroyOnLoad` 保持跨场景可用。

UIKit 只记录和释放自己创建的事件系统，不接管场景预置的 EventSystem。项目启用新版 Input System，因此运行时事件系统固定使用 `InputSystemUIInputModule`，不创建旧版 `StandaloneInputModule`。

`UIKit` 使用无参构造并在构造时注册 `Core.UI`。面板 Prefab 和世界 UI 资源统一通过 `Core.Asset` 加载，不保存或注入 `IAssetKit`；Core 的固定创建顺序保证 UIKit 存活期间 AssetKit 一定可用。

## 面板生成链路

UI Prefab 根节点使用 `UIComponentBinder` 保存稳定名称和组件引用。选中 Prefab 后执行 `Prometheus/UIKit/Generate Selected Panel`，生成器会覆盖 `Assets/Prometheus/UI/<Panel>` 下的强类型 `PanelBase`，但只在业务 Panel 不存在时创建业务脚本，因此重复生成不会覆盖已经实现的界面逻辑。

生成的 `PanelBase` 负责按 Binder 索引和名称取得组件、注册 Button 点击监听、最终解绑监听并清空 Unity 对象引用。业务 Panel 负责实现生成的抽象 `OnXxxClick` 回调以及 `OnBind`、`OnInitialize`、`OnOpen`、`OnUpdate`、`OnClose`、`OnUnbind` 生命周期。

## Button 规则

Binder 中的普通 Unity `Button` 一律生成点击回调，不根据同节点是否存在 Input System 组件改变按钮语义。Prefab 的 Button `On Click()` 持久化列表必须保持为空，避免 Inspector 监听和生成监听重复执行。

普通按钮不允许挂接 `OnScreenButton`。鼠标和触屏点击直接进入 UIKit 回调；UIPanel 不实现 `IInputReceiver`。

`OnScreenStick` 是唯一例外。它表达连续二维拖拽值而不是离散点击，因此与 `OnScreenStick` 同节点的 Button 不生成点击回调，摇杆继续把移动值写入 `<PrometheusVirtualInput>/move`。

## HUD 约定

HUD 的抽奖、小地图、任务、菜单、引导、活动、角色和背包按钮在各自的点击回调中直接执行界面行为（小地图与背包调用 `Core.UI.OpenPanel`，其余界面尚未实现）。三个头像按钮直接调用 `TeamSystem.SwitchToSlot`。攻击、技能、大招、闪避和跳跃按钮通过 `InputSystem.QueueEntityButtonActions` 为当前上场实体提交一次离散玩法命令，以保证点击发生在任意 Unity 更新时点都不会被下一帧输入重置覆盖。

## 生命周期约定

生成监听只在面板绑定时注册一次，并在最终解绑时移除。UIKit 每帧只向处于打开状态且实例有效的面板分发 `OnUpdate(float dt)`；缓存关闭不会执行该回调。业务 Panel 在 `OnClose` 只释放数据监听。最终 `OnUnbind` 必须清空业务持有的 System 和实体引用，跨模块事件订阅统一通过 `Core.Event` 注册和移除。

HUD 小地图复用 Binder 已生成的 `MiniMapButton` 作为容器，不把运行时地图图层加入稳定绑定表。`HudPanel.OnInitialize` 创建 RawImage、径向虚化材质和 POI 标记层；`OnOpen`/`OnClose` 订阅或解除 `PoiSystem` 的地图事件，世界坐标映射统一调用 `WorldMapDefinition`。大地图使用独立的 `MapPanel`，但同样只读取 `PoiSystem` 的地图和 POI 数据。

---

# UI 原型 DSL

## 它解决什么问题

Prefab 是 YAML，充满 FileID、GUID、锚点和 Prefab override，人和 AI 读改都困难，一处修改产生大量 diff。原型 DSL 把界面结构提升成一段 C# 声明，让「这个界面长什么样」变成可读、可 diff、可 review、可整体重建的代码。

**构建脚本是唯一真源，Prefab 是产物。** 目标 Prefab 每次重建都被整体覆盖，因此不要在 Prefab 上做任何手工修改——会丢。

## 什么时候用，什么时候停止用

原型 DSL 只服务**原型阶段**：结构和交互还在变、美术尚未介入、需要快速出可用界面。

出现下面任一情况就应当**毕业**——把 Prefab 从构建脚本脱钩，交给美术按传统 Unity 流程维护，同时删除或存档对应的原型定义：

- 美术开始逐像素调整视觉
- 出现 LayoutGroup 表达不了的需求（重叠、斜置、非规则定位）
- 需要接入真实美术资源、Spine、粒子、复杂 Animator

毕业后 Prefab 回归传统工作流，`PanelBase` 的代码生成链路不变，业务 Panel 代码一行不用改——契约始终是 Binder 里的名称。

> 毕业目前只是约定，没有工具支撑。脱钩就是删掉原型定义类，Prefab 自然不再被重建。

**不要为了继续用 DSL 而给它加坐标类接口。** 一旦开始想写 `.Offset(-3, 0)`，说明这个界面该毕业了；继续堆下去只会得到一份代码版 YAML，失去全部收益。

## 工作流

```
编写 / 修改原型定义 (.cs)
        ↓
Prometheus/UIKit/Rebuild UI Prototypes
        ↓
构建期校验（结构 + 实例化结果）
        ↓
Prefab 落盘 + 写入 UIComponentBinder
        ↓
（可选）触发 UIPanelCodeGenerator 生成 PanelBase
        ↓
Console 输出带实际尺寸的结构回读报告
```

菜单一次重建工程内全部原型定义，按 `Order` 从小到大执行。也可以在脚本或 `execute_code` 中直接调用：

```csharp
UIPrototypeBuilder.RebuildAll();          // 全量重建，返回汇总报告
UIPrototypeBuilder.Rebuild(typeof(XxxProto));  // 重建单个
```

## 目录约定

| 位置 | 内容 |
| --- | --- |
| `Framework/UIKit/Editor/Prototype/` | 框架：节点树、构建器、主题、校验 |
| `UI/Prototype/Editor/` | 界面定义与第三方组件封装（独立 asmdef，隔离 SuperScrollView 等依赖） |
| `BundleResources/UI/<Name>/Prefabs/` | 生成产物，勿手工修改 |

界面定义放在独立程序集，是为了不让框架反向依赖任何第三方 UI 组件。

## 一个完整例子

```csharp
[UIPrototype("Assets/BundleResources/UI/Mail/Prefabs/MailPanel.prefab", Order = 10)]
public sealed class MailPanelProto : UIPrototypeDefinition
{
    public override ProtoNode Build()
    {
        return Panel("MailPanel")
            .Bg(UIProtoTheme.Dim, false)
            .Padding(56, 56, 40, 40)
            .Add(
                Column("Frame")
                    .Flex()
                    .Bg(UIProtoTheme.PanelBg)
                    .Padding(36, 36, 28, 28)
                    .Spacing(20f)
                    .Add(
                        Row("Header")
                            .Height(76f)
                            .Spacing(16f)
                            .Add(
                                Label("TitleLabel", "邮件").Width(220f).FontSize(40f),
                                Spacer("HeaderSpacer"),
                                Button("CloseBtn", "X").Width(76f).FontSize(28f)),
                        Row("Body")
                            .Flex()
                            .Add(/* ... */)));
    }
}
```

`[UIPrototype]` 的参数：

| 参数 | 说明 |
| --- | --- |
| `prefabPath` | 生成目标路径（构造参数，必填） |
| `Order` | 重建顺序，被别人引用的列表项 Prefab 必须更小 |
| `GeneratePanelCode` | 生成 Prefab 后是否调用 `UIPanelCodeGenerator`，默认 `false` |
| `DesignWidth` / `DesignHeight` | 构建期计算布局用的设计分辨率，默认 1920×1080 |

## 四条硬约束

这些约束是这套方案能成立的前提，不是可选风格。

**一、禁止手写坐标。** 作者 API 里不存在 `Anchor` / `Pivot` / `Offset` / `anchoredPosition`。所有定位由 `HorizontalLayoutGroup` / `VerticalLayoutGroup` / `GridLayoutGroup` + `LayoutElement` 完成。这条把最容易出错的二维坐标推算整个移除，代价是原型只能做规则布局。

**二、只用占位美术。** 纯色 + Unity 内置 `UISprite`/`Knob`，不引用工程内任何美术资源。这样原型不产生资源引用，也就不涉及打包收集与资源丢失。

**三、语义优先于组件。** 写 `Column` / `Row` / `Label` / `Button`，不写 `RectTransform` / `Image` / `TextMeshProUGUI`。

**四、词汇表即约定。** 需要收紧的约定一律从 API 里删掉对应能力，而不是写在注释里。字重就是例子：工程统一 85W，于是整个 `Weight` 接口被删除，第二种字重在定义里根本写不出来。

## 节点工厂

| 工厂 | 角色 | 子节点排布 |
| --- | --- | --- |
| `Panel(name)` | 面板根，自带 `UIComponentBinder` + `RaycastBlocker` | 纵向 |
| `ItemRow(name)` / `ItemColumn(name)` | 可复用子 Prefab 的根 | 横向 / 纵向 |
| `Column(name)` / `Row(name)` | 容器 | 纵向 / 横向 |
| `Grid(name, columns, cell)` | 固定列数网格 | 网格 |
| `ScrollBox(name)` | 纵向滚动容器，内容由子节点撑高 | 纵向 |
| `Label(name, text)` | 文本 | — |
| `Button(name, text)` | 按钮，自动进 Binder | — |
| `Icon(name, size)` | 图片占位 | — |
| `Bar(name)` | 带填充比例的进度条 | — |
| `Spacer(name)` | 在主轴撑开剩余空间 | — |
| `Divider(name)` | 2px 分隔线 | — |
| `Custom(name, build)` | 扩展节点，实例化逻辑由外部提供 | — |

## 修饰方法

| 方法 | 作用 |
| --- | --- |
| `Padding(all)` / `Padding(h, v)` / `Padding(l, r, t, b)` | 内边距 |
| `Spacing(v)` | 子节点间距 |
| `Size(w, h)` / `Width(w)` / `Height(h)` | 固定尺寸 |
| `Flex(weight = 1)` | 在父容器主轴瓜分剩余空间 |
| `Bg(color, sliced = true)` | 底图颜色 |
| `Text(s)` / `FontSize(f)` / `TextColor(c)` | 文本内容与样式 |
| `Align(ProtoAlign)` / `VAlign(ProtoVAlign)` | 内容对齐。文本节点上控制文字对齐，容器节点上控制子节点排布；未调用的方向保持左上角 |
| `Truncate()` | 允许省略号截断，并跳过文本溢出校验 |
| `Fill(ratio)` | 进度条填充比例 |
| `Clickable()` | 把容器整体变成可点击区域（卡片、列表行、菜单格子），并自动登记绑定 |
| `Bind(type = null)` | 登记进 `UIComponentBinder` |
| `BindText()` | 绑定按钮内部文本，登记为 `<节点名>Text` |
| `BindBackground()` | 绑定容器底图，登记为 `<节点名>Bg` |
| `Add(...)` | 追加子节点 |
| `With(Action<GameObject>)` | 扩展出口，仅供第三方组件封装使用 |

节点名不能使用 `Bg`、`Text`、`Fill`、`Viewport`、`Content`——这些是构建器自己创建的实现节点名，同级重名会让按路径定位的绑定静默指向错误对象，因此构建期直接拦截。

## 布局规则

主轴由**父容器方向**决定：`Row` 的主轴是水平，`Column` 的主轴是垂直。

| 父容器 | `Width()` | `Height()` | `Flex()` |
| --- | --- | --- | --- |
| `Row` | 主轴固定宽度 | 交叉轴固定高度 | 瓜分剩余**宽度** |
| `Column` | 交叉轴固定宽度 | 主轴固定高度 | 瓜分剩余**高度** |

**交叉轴未显式指定尺寸时自动填满父容器。** 所以 `Column` 里的子节点默认横向铺满，`Row` 里的子节点默认纵向铺满。

`Label` 既不设 `Height()` 也不 `Flex()` 时，高度由文本内容决定。

`Grid` 的子节点尺寸完全由 `cell` 决定，`Width`/`Height`/`Flex` 对它们无效。

### 长度不可控的文本

不要用 `Flex()` 装长文本——`Flex()` 的语义是「占满剩余空间」，对文本等于**设了高度上限**，超出即被截断。正确做法是放进 `ScrollBox`：

```csharp
ScrollBox("DetailBodyScroll")
    .Flex()
    .Add(Label("DetailBody", longText).VAlign(ProtoVAlign.Top))
```

`ScrollBox` 内部装配 `ScrollRect` + `Viewport(RectMask2D)` + `Content(VerticalLayoutGroup + ContentSizeFitter)`，子节点挂到 Content 上由内容撑高，超过视口即滚动。

固定高度且内容长度不可控的位置（列表项标题等），显式声明 `.Truncate()`。

## 绑定与代码生成

节点调用 `.Bind()` 后会按**深度优先先序**写入 Prefab 根的 `UIComponentBinder`。`Button()` 工厂自动绑定，无需显式声明。

默认绑定组件按角色推断：

| 角色 | 组件 |
| --- | --- |
| `Button` | `UnityEngine.UI.Button` |
| `Label` | `TextMeshProUGUI` |
| `Icon` / `Bar` | `Image` |
| 其他 | `RectTransform` |

需要别的类型时传入：`.Bind(typeof(LoopListView2))`。

节点名称即绑定名称，也就是生成到 `PanelBase` 的字段名，因此**节点名必须全局唯一**。

### 该绑定什么

绑定表不是越全越好——每条绑定都会变成 `PanelBase` 的一个字段和一次运行时校验。判断标准只有一条：

> **内容或状态会在运行时改变的节点才绑定，写死的装饰和标题一律不绑。**

对照着看：

| 绑 | 不绑 |
| --- | --- |
| 玩家昵称、签名、UID、等级、数值 | 「冒险等阶」「世界等级」这类字段名 |
| 会变的按钮标题（`未读 12`、当前排序方式） | 「关闭」「全部领取」这类固定按钮标题 |
| 运行时换图的 Icon（头像、道具图标、元素标识） | 纯装饰色块、分隔线 |
| 需要显示隐藏的红点、星级、装备者行 | 布局容器、占位 Spacer |
| 进度条（好感度、经验） | 网格里静态排布的格子背景 |

两个辅助声明覆盖了不能直接绑的情况：

- `BindText()` —— 绑定**按钮内部的文本**，登记为 `<节点名>Text`。按钮本身绑的是 `Button`，标题会变的按钮才需要它。
- `BindBackground()` —— 绑定**容器的底图**，登记为 `<节点名>Bg`。底色随数据变化的区域用它，例如背包详情头部按品质换色。两者都与链式调用顺序无关。

条目 Prefab（`ItemRow` / `ItemColumn` / `GridItemColumn`）**不携带 `UIComponentBinder`**，其中的 `.Bind()` 不会产生绑定。条目内容由条目自己的 MonoBehaviour 负责，通过 `With()` 在原型里挂上；由于 Prefab 每次重建都会重生成，该脚本**不能依赖序列化字段**，应在运行时按节点名解析引用（参考 `BagItemMono`）。

`[UIPrototype(GeneratePanelCode = true)]` 会在 Prefab 落盘后自动调用 `UIPanelCodeGenerator`，产出强类型 `PanelBase` 与首次业务 Panel 模板，接入上半篇的面板运行时链路。`PanelBase` 每次重建都会被覆盖，业务 Panel 只在不存在时创建，因此**改了绑定表就要回业务 Panel 补上新增的 `OnXxxClick`**，否则抽象方法未实现会编译失败。

## 三级验证

按成本从低到高，前两级都是自动的。

**第一级 · 结构树静态校验**（实例化前）

- 节点名全局唯一
- 容器不能没有子节点
- `Grid` 列数至少为 1
- 层级深度不超过 8
- 根节点必须由 `Panel()` / `ItemRow()` / `ItemColumn()` 创建

**第二级 · 实例化结果校验**（布局计算后）

- 任何 `RectTransform` 尺寸为零即失败（`ScrollRect` 的 Content 由运行时撑开，豁免）
- 文本放不下即失败，除非声明了 `Truncate()`
- `Button` 的 persistent `onClick` 列表必须为空
- 字体材质必须已作为子资产落盘

**第三级 · 结构回读报告**

每次重建向 Console 输出带实际尺寸的层级树与绑定表：

```
MailPanel                  [ 1920x1080]  Column
  Bg                       [ 1920x1080]  Image
  Frame                    [ 1808x1000]  Column
    Header                 [   1736x76]  Row
      TitleLabel           [    220x76]  Text
      HeaderSpacer         [    884x76]
      CloseBtn             [     76x76]  Button Image
bindings (9):
  [0] BackBtn : Button
  ...
```

**读这份报告比截图更可靠也更便宜**，「元素跑到屏外」「Grid 只有一列」「底图尺寸为零」这类问题在这里一眼可见。截图只用于确认整体观感，不做像素级调整。

## 循环列表与循环网格

条目数量会持续增长的地方**不要用 `Grid` 或 `ScrollBox` 一次性铺开**，必须走 SuperScrollView 的循环视图。`ScrollProto` 已经把它们封装成语义节点：

| 工厂 | 底层组件 | 用途 |
| --- | --- | --- |
| `ScrollProto.VList(name, itemPath, …)` | `LoopListView2` 纵向 | 邮件列表这类纵向长列表 |
| `ScrollProto.HList(name, itemPath, …)` | `LoopListView2` 横向 | 角色页签这类横向长列表 |
| `ScrollProto.GridView(name, itemPath, columns, itemSize, itemPadding, …)` | `LoopGridView` | 背包这类上千格的网格 |

配套的条目根节点工厂：`ListItemRow` / `ListItemColumn`（挂 `LoopListViewItem2`）、`GridItemColumn`（挂 `LoopGridViewItem`）。

```csharp
ScrollProto.GridView("BagGrid", ItemPrefabPath, 8,
        new Vector2(150f, 160f), new Vector2(10f, 10f),
        examples: 5, decorateExample: TintByQuality)
    .Flex()
```

要点：

- 被引用的条目 Prefab 必须由 **`Order` 更小**的原型定义先行生成。
- `GridView` 会把固定列数、条目尺寸与间距一并写进 Prefab。`LoopGridView` 未配置固定列数时会以 0 列计算行数而抛除零异常，写进 Prefab 可以避免依赖运行时补传。
- 循环视图的 `Content` 在 Prefab 里是空的，长度由组件在运行时按数据量撑开。

### 示例条目

循环视图在编辑器里是空的，看不出实际效果。`examples` 参数会往 `Content` 里放入若干条目实例，`decorateExample` 可以逐个差异化（背包用它演示五种品质底色）。

实例统一命名为 `<条目名>Example<序号>`。**它们只是编辑期示例，没有任何自动清理逻辑**——接入真实数据前按名字搜出来手工删除，否则会和组件自己管理的条目池同时存在。

## 扩展第三方组件

第三方组件通过 `Custom()` + `With()` 封装成语义节点，装配逻辑集中在封装类里，界面定义只看到一个节点。参考 `UI/Prototype/Editor/ScrollProto.cs`：

```csharp
public static ProtoNode VList(string name, string itemPrefabPath, …)
{
    return UIPrototypeDefinition
        .Custom(name, go => BuildList(go, name, itemPrefabPath, …))
        .Bind(typeof(LoopListView2));
}
```

`With()` 是给这类封装用的出口，**界面定义本身不应直接调用**——直接操作 GameObject 就绕过了全部约束和校验。唯一的例外是给条目 Prefab 挂它自己的业务 MonoBehaviour，那同样应该封装在工厂里而不是写在界面定义中。

## 主题与字体

`UIProtoTheme` 集中定义占位配色与字体，界面定义只引用语义名（`PanelBg`、`TextPrimary`、`Accent`…），不写字面色值。

字体统一使用 `Assets/BundleResources/Font/HYWenHei-85W SDF.asset`，路径写在 `UIProtoTheme.FontAssetPath`，换字体改这一处。工程约定只用这一种字重，视觉层次靠字号和颜色区分。

## 已知陷阱

**`ForceRebuildLayoutImmediate` 不会穿过没有布局组件的节点。** uGUI 只在节点自身带 `ILayoutController` 时才向下递归，而滚动视图的 `Viewport` 只挂 `RectMask2D`，递归就断在那里——`Content` 的 `ContentSizeFitter` 不生效，放进 `Content` 的示例条目尺寸为零。

构建器不去猜链路在哪断，而是自外向内把**每一个带布局组或 `ContentSizeFitter` 的节点**都重建一遍（`RebuildLayoutRoots`）。重建是幂等的，重复覆盖主链上的节点只有构建期开销。因此新增插入实现层的封装节点时不需要改动构建器。

**运行时同理**：业务代码动态修改滚动内容后，只标脏不够，需要对 `Content` 再调一次 `ForceRebuildLayoutImmediate`——运行时没有上面那道兜底。

**不要用脚本生成 TMP 字体资产。** `TMP_FontAsset.CreateFontAsset()` 连带创建的图集和材质都只是内存对象，若不按「图集先落盘 → 材质指向已落盘图集 → 材质落盘」的顺序 `AddObjectToAsset`，Prefab 会引用到一个不存在于任何资产文件的材质，**域重载后文字全部不渲染且不报错**。正常路径是 `Window > TextMeshPro > Font Asset Creator`。构建器已针对这种情况加了拦截。

**动态图集的运行时代价。** 当前字体使用动态图集，首次遇到新字会实时烘焙，长文本首次显示可能有帧尖峰。上线前需要预热常用字表或改用静态图集 + fallback。
