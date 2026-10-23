# 截帧模式：Vulkan vs OpenGL

## 核心原则

截帧时尽量选择与游戏实际图形 API 一致、中间翻译层最少的方式。

- **GLES 游戏用 OpenGL 模式**：翻译层最薄，名字和约定都保留。
- **原生 Vulkan 游戏用 Vulkan 模式**。反过来用 OpenGL 模式时，模拟器通常不向游戏提供 Vulkan，游戏会启动失败，或者退回 GLES 渲染路径；这时截到的和真机上不是同一套 shader 和效果。
- **最理想的是真机 + RenderDoc 安卓远程截帧**：完全没有模拟器翻译层。前提是应用可调试，或者设备已 root。

### 模拟器上的注意事项

在 PC 上截模拟器（比如 MuMu 的 `MuMuNxDevice.exe`），截到的是**模拟器翻译后发给主机显卡的调用**，而不是游戏自己发出的调用。模拟器设置里的"渲染模式"决定主机端用 OpenGL、DirectX 还是 Vulkan，和游戏用什么 API 无关。

MuMu 的截帧方式（本机实测；可以直接用 `mumu-renderdoc-capture` skill 自动完成检测、注入启动和恢复）：
- 先在模拟器设置里选渲染模式。OpenGL 模式下，`vms/<实例>/configs/shell_config.json` 中 `renderer.platform = "ngl"`。
- 用 `renderdoccmd capture -d <shell目录> <...>\nx_device\<版本>\shell\MuMuNxDevice.exe --vm <实例名>` 启动并注入**设备进程**（不是主界面）。
- 之后在 qrenderdoc 里 Attach to Running Instance，触发截帧。
- 关机和启动用 `nx_main\MuMuManager.exe control -v 0 shutdown|launch`，比强杀进程安全。

**帧边界不是游戏的帧边界（重要）**：MuMu GL 模式下，截帧里交替出现两个 GL context：游戏渲染线程（如 `ResourceId::74997`）和模拟器显示线程（如 `ResourceId::70`，负责 blit 到窗口和 SwapBuffers）。RenderDoc 按显示线程的 SwapBuffers 切帧，所以一份单帧截帧通常是从游戏帧**中间**开始的：开头那部分 draw（比如游戏帧最先画的地形、草地）在截帧开始前就画完了，只以初始内容（G-buffer、深度、stencil）的形式存在于 rdc 里，没有几何体。
- 截帧机位复刻不受影响（初始内容精确恢复，验证全过）；但离开机位后，这些内容仍按旧机位排布，表现为近处地面不跟视角、遮挡和光照分类错乱。
- 判断方法，按顺序：
  1. 提取前看主 G-buffer 和主深度在范围内是否先 clear 再画。截断的帧里，它们从头到尾都不 clear，第一个 draw 之前就已经有画面。
  2. 提取日志里的 `carried over into the range` 是否列出了 G-buffer 或主深度。
  3. 在 Unity 里点 **Validate completeness**，看结果是不是 `TRUNCATED`。
- 解决：**重新截帧，直到截到完整的帧**。显示线程的 SwapBuffers 和游戏帧之间的相位是随机的，多截几次总会截到。实测 43642 被截断、3558 完整。
  - 注意：qrenderdoc 里把截帧数设为 N，得到的是 N 个**相互独立**的 rdc，每个仍然按 SwapBuffers 切分。RenderDoc 不能把两帧放进同一个 rdc，所以这个设置的作用只是一次多截几份备选，不能拼出完整帧。

尝试过 `RENDERDOC_HOOK_EGL=1`，想在主机上截到游戏原生的 GLES，**结果无效**。原因是 OpenGL 模式下设备进程只加载 `libRenderer.dll` 加 `opengl32.dll`：GLES 指令流在 libRenderer 内部直接翻译成桌面 GL，主机上不存在可以拦截的 EGL/GLES 调用，上下文都是 WGL 创建的。目录里的 `libEGL_nemu.dll` / `libGLESv2_nemu.dll` 这个模式下根本没有加载；即使其他模式会加载它们，RenderDoc 的 EGL 钩子也只认 `libEGL.dll` 这个模块名。所以对 MuMu 来说，OpenGL 模式截到的已经是主机上能得到的最接近 GLES 的数据。

在模拟器内部用 RenderDoc 安卓端远程截帧，目前也不可行：
- MuMu 的安卓系统是 x86_64，ARM 游戏通过 `libnb.so` 转译运行；
- RenderDoc 只提供 arm32 和 arm64 的安卓层库，x86_64 的 EGL/Vulkan loader 加载不了；
- 要走通，需要自己编译 x86_64 版的 RenderDoc，此外还要 root（应用不可调试时）。
- 商业游戏（比如带反作弊的）做 root 加注入有封号风险，不要建议。

## 怎么判断游戏用的是哪个 API

- **Unity GLES 游戏的截帧特征**：
  - GLSL 里有 `hlslcc_mtx4x4unity_MatrixVP`、`in_POSITION0`、`vs_TEXCOORD0`；
  - 有 `glBindAttribLocation`；
  - clip z 在 -1..1。
- **GLES 被模拟器翻译成 Vulkan 的特征（ANGLE 式）**：
  - VS 末尾有 prerotation 函数（`gl_Position = _22(gl_Position)`，用 snorm 打包的 flipXY）；
  - 每个 stage 都有一个 32 字节的 driver uniform block；
  - 开启了 `VK_EXT_depth_clip_control`（`depthNegativeOneToOne = True`）；
  - 离屏 target 的行序是 bottom-up；
  - ETC2 贴图被解压成 RGBA8。
- **原生 Vulkan 游戏的特征**：SPIR-V 由 DXC/glslang 编译，没有上面那些翻译层痕迹，clip z 在 0..1。
- **真机 logcat**：Unity 启动时打印 `GfxDevice: creating device client`，后面跟着 OpenGL ES 或 Vulkan。

## 同一个 GLES 游戏，两种模式截帧对比（实测：MuMu，Unity GLES3 游戏）

| | Vulkan 模式 | OpenGL 模式 |
|---|---|---|
| 翻译路线 | GLES → 模拟器（ANGLE 式）→ 主机 Vulkan，换了一整套 API 模型 | GLES → 模拟器 → 主机 GL，基本一对一转发 |
| 一份截帧包含 | 两帧（两个 Present） | 一帧 |
| shader | SPIR-V，名字全部剥离（`uniforms90`、`res61`、`_input0`） | GLSL 源码，Unity 名字保留（加了 `_u` 前缀） |
| 全局 uniform | 默认 block 被打包成匿名 UBO | 普通 uniform（`$Globals`，没有 buffer） |
| pass | 有显式 render pass，load op 和 clear 值都写明 | 没有 pass，要按 FBO 切换和 `glClear` 自己合成 |
| attribute location | 就是 shader 里声明的 location | 由 `glBindAttribLocation` 决定；反射给的 `regIndex` 只是按名字排的序号 |
| 未启用的属性 | 翻译层已经展开成 buffer | VAO 不启用，读 GL 的 generic 常量（`glVertexAttrib*`），比如白色的 COLOR0 |
| 采样参数 | 独立的 sampler 对象 | 挂在纹理对象上；descriptor 里 sampler 的 object 为 Null，但参数有效 |
| 比较采样 | sampler 上有明确的 compare | GL 总会报一个 compareFunction，要看 shader 是否声明了 `sampler*Shadow` |
| 贴图 | 全是解压后的 RGBA8 | 保留 ETC2，另有 RGB8（三通道） |
| clip 和行序 | `depthNegativeOneToOne` 加 bottom-up 行序（在模拟 GL） | 原生 GL：clip z 在 -1..1，左下原点 |
| 正面判断 | 在帧缓冲坐标（y 向下）里判断 | 在窗口坐标（y 向上）里判断，相对重放的内存行序要取反 |
| 相机/物体识别 | 只能靠数值特征（透视签名、MVP 拟合、样本匹配） | 同样可用，名字可以帮助人工排查 |
| 复刻最终精度（本工具） | 离线 0.0010，场景渲染 0.0042 | 离线 0.0031，场景渲染 0.0036 |

## 工具对两种模式的处理

`rd_frame.py` 根据 `props.pipelineType` 自动分支，不需要额外参数。

### Vulkan

- pass 来自 `vkCmdBeginRenderPass`。load op 从 action 名字里解析，clear 值从结构化数据的 `RenderPassBegin.pClearValues` 读取，attachment 来自 `pNext.pAttachments`。attachment 是 image view，要沿 `parentResources` 找到 image。
- `depthNegativeOneToOne` 从 `GetVulkanPipelineState().viewportScissor` 读。为 True 时 shader 里加上 `z = (z + w) / 2`，并把 `rows_bottom_up` 设为 True。
- 名字被剥离：贴图名形如 `_60`（来自 `res60`），cbuffer 按 block 序号重命名为 `rdv_N` / `rdp_N`。

### OpenGL

- **pass 合成**：绑定的 FBO（颜色 target 集合加深度）一变化就新开一个 pass，load 现有内容；遇到 `glClear` 也新开一个 pass，clear 值从调用名 `glClear(Color = <r,g,b,a>, Depth = <d>, Stencil = <s>)` 解析。
- **shader**：GLSL 源码存为 `fam*_vs.glsl`。`glsl_globals.rewrite` 做以下改写：
  - `#version` 改为 450，去掉 `gl_PerVertex` 重声明；
  - 散落的 uniform 收进 `layout(std140) uniform _RDGlobals`，按声明顺序；
  - vertex 输入使用 `glBindAttribLocation` 的 location；
  - varying 按名字排序给 location，VS 和 PS 用同一份；
  - `gl_InstanceID` / `gl_VertexID` 换成 `gl_InstanceIndex` / `gl_VertexIndex`。
  
  改写后用 glslangValidator 编成 SPIR-V（`-V --auto-map-bindings --auto-map-locations`），之后走 spirv-cross。
- **`$Globals` 取值**：`GetCBufferVariableContents` 返回名字和值（数组以 members 形式返回），按 `glsl_globals.std140_layout` 打包。这份布局和 glslang 实际生成的 packoffset 已经逐项核对过。
- **attribute**：location 用 `glBindAttribLocation` 的结果（来自结构化数据里的 chunk，按 program 区分）。
  - shader 声明了、但 VAO 没有启用的属性，取 `GetGLPipelineState().vertexInput.attributes[loc].genericValue`，填满所有顶点。
  - 内置变量（`gl_InstanceID` 等）也会出现在 `inputSignature` 里，必须排除，否则会占用 location 0，把 POSITION 覆盖成常量。
- **采样**：descriptor 的 sampler 在 object 为 Null 时也要采用。mip 过滤为 Linear 时对应 Unity 的 Trilinear。比较函数只在 GLSL 声明了 `sampler*Shadow` 时保留。
- **贴图**：
  - R8G8B8 读取每级 mip 的原始数据，补 alpha 扩成 RGBA8；
  - ETC2 走 PNG（只有 mip 0）；
  - GL 下 PNG 的行序和原始数据相反，而 `Texture2D.LoadImage` 又会翻转一次，两次抵消，方向是对的。
- **正面判断**：`front_ccw = not rs.frontCCW`。原因是 GL 在 y 向上的窗口坐标里判断，而重放保持内存行序；配合 Unity 渲染到 RT 时的 `invertCulling`，结果正确。
