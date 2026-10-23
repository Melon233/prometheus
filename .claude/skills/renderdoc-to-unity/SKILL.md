---
name: renderdoc-to-unity
description: 把 RenderDoc 截帧（.rdc，Vulkan 或 OpenGL/GLES）复刻成 Unity 里可编辑的真实场景，原始 shader、光照、阴影、雾、云、后处理逐 pass 还原，Scene view / Game view 用同一条自建 SRP 渲染，可自由移动相机、移动/隐藏物体，并能逐 pass 和截帧原图做数值对比。用户提到 RenderDoc、rdc、截帧、frame capture、"把截帧还原/复刻/重建到 Unity"、模拟器（MuMu 等）截帧、分析别的游戏的渲染管线/画面效果、frame replay、shader 还原时都应使用本 skill，即使没有明确说"Unity 场景"。
---

# RenderDoc → Unity 截帧复刻

把一帧截帧变成 Unity 场景，有两条路线：

- **frame（主路线）**：记录整帧的 pass / draw / blit 序列、原始 shader（反编译成 HLSL）、原始 cbuffer 和贴图，在 Unity 里用自建 SRP（`RenderDocPipelineAsset`）按原顺序重放。场景中的物体是真实 GameObject，相机、物体 Transform、分辨率都会改写进原 shader 的常量里。效果最接近原作，并且可以逐 pass 验证。
- **extract（旧路线）**：只重建物体和相机，每个物体挂一份单独的重放材质，不还原延迟光照和后处理。适合只要几何和材质的场景。

本文默认走 frame 路线。

## 前置条件

- **RenderDoc 1.46+**（Windows，默认 `C:\Program Files\RenderDoc`）。会用到：
  - `qrenderdoc.exe`：运行内置 Python；
  - `plugins/spirv/spirv-cross.exe`：SPIR-V → HLSL；
  - `plugins/spirv/glslangValidator.exe`：GLSL → SPIR-V，OpenGL 截帧需要。
- 普通 Python 3.7+，不需要第三方包。
- Unity 2022.3 或 Unity 6，编辑器图形 API 为 D3D11/D3D12（生成的 shader 排除了 gles/vulkan/metal），推荐 Linear 色彩空间。

## 安全和协作约定（务必遵守）

- 只结束**自己启动的** qrenderdoc 进程（按 PID）。用户可能同时开着 RenderDoc 窗口，绝不能按进程名批量结束。`run_in_renderdoc.py` 已经这样实现；自己写探针脚本时也要照做。
- 保存或覆盖 `.unity` 场景、删除资源目录之前，先确认目标是否已存在、是否是用户的文件。当前场景有未保存内容时，先另存或询问。
- `RenderDocScene` 启用时会把 `QualitySettings.renderPipeline` 换成复刻管线，禁用时恢复。要提醒用户：场景打开时保存项目，Quality 设置里会记成复刻管线。
- 截帧里的模型、贴图、shader 属于第三方，只用于分析和验证工具，要向用户说明。

## 工作流

### 1. 选截帧模式（截帧之前）

- 截帧应尽量贴近游戏实际使用的图形 API，中间翻译层越少越好：GLES 游戏用 OpenGL 模式截，原生 Vulkan 游戏用 Vulkan 模式截。
- 在模拟器上，两种模式截到的都是模拟器翻译之后发给主机显卡的调用。GLES 游戏用 Vulkan 模式截，会丢失全部名字，并多出一层翻译痕迹。

判断方法和两种模式的差异，见 `references/capture-modes.md`。在 MuMu 模拟器上截帧（让模拟器进入可截帧状态、截帧、恢复），用 `mumu-renderdoc-capture` skill。

### 2. 概览：选一帧的事件范围

```bash
python scripts/run_in_renderdoc.py survey <capture.rdc> <out_dir>
```

看 `survey.md`：

- **帧边界**：一份截帧可能包含两帧（Vulkan 模式常见）。用 Present 事件确定帧边界。
- **范围选择**：从帧开头（天空/云 LUT 等预计算 pass）选到最终场景图写完为止，通常不包括 UI pass（1920x1080 之类的 UI 渲染）。
- **完整性（模拟器截帧必查）**：模拟器上 RenderDoc 按模拟器显示线程的 SwapBuffers 切帧，游戏帧可能从截帧开始前就画了一半。检查主 G-buffer 和主深度在范围内、第一个 draw 之前有没有 clear：没有 clear 并且第一个 draw 之前里面已经有画面，就是被截断了。截断的帧在截帧机位依然精确，但**不支持自由相机**，需要重截（见 `references/capture-modes.md`）。

### 3. 提取

```bash
python scripts/run_in_renderdoc.py frame <capture.rdc> <out_dir> --events <first>-<last>
```

这一步做三件事：

1. 在 qrenderdoc 里运行 `rd_frame.py`，导出 `frame.json`、mesh、贴图、shader 源，以及每个 pass 结束时的真值（`validate/*.raw`）。
2. 自动运行 `make_replay_shaders.py`，生成 `shaders/RDF_*.shader`。
3. 自动运行 `make_camera_bindings.py`：识别相机、物体、屏幕尺寸常量、视锥射线、TAA 历史图，结果写回 `frame.json`。

日志中要检查：
- `families failed` 应为 0；
- `camera view-projection found in N draws` 中 N 应该占到大多数场景 draw；
- `objects: X (Y movable)`；
- `per-vertex view rays`；
- `temporal history`；
- `carried over into the range`：列出本帧会渲染、但第一次使用时读的是截帧前内容的屏幕尺寸 target。TAA history、缓存的 shadow map 属于正常；如果出现主 G-buffer 或主深度，说明帧被截断了。

这两个后处理脚本可以单独重跑，修改它们后不必重新提取：

```bash
python scripts/make_replay_shaders.py <out_dir>
python scripts/make_camera_bindings.py <out_dir>
```

### 4. 导入 Unity

1. 把 `unity/` 下的 `Runtime/` 和 `Editor/` 复制到项目的 `Assets/<某目录>/`。它们各有 asmdef。
2. 导入有两种方式：
   - 菜单：**Tools > RenderDoc To Unity > Import Frame Replay...**，选择提取目录；
   - 脚本：`RenderDocFrameImporter.Import(extractDir, "Assets/<目标目录>")`。

导入大帧要 1 到 2 分钟，用 MCP 调用时常会超时，但 Unity 仍在执行；稍后检查 `RenderDocScene.Active` 即可。

导入后会生成：
- **`RenderDocScene_<名字>`**：带 `RenderDocScene` 组件，启用时切换到复刻管线。
- **`Objects/ObjectNNN`**：每个物体一个 GameObject，带 `RenderDocObject`。子节点 `Draw*` 的 MeshRenderer 只用于点选和包围盒。物体由复刻管线按截帧顺序重放 draw 画出来，不是靠 MeshRenderer 渲染的；Transform 会改写原 cbuffer 里的物体矩阵。在 Inspector 里可以看到这个物体参与的全部 draw：G-buffer、阴影等，每个 draw 的 eid、mesh、材质/shader、贴图和渲染目标，还能"重放到这个 draw"。选中物体时，Scene view 会叠加它的 mesh 线框，用来观察拓扑。开关在 Inspector 里，也可以用菜单 Tools > RenderDoc To Unity > Selection Wireframe；默认只显示没被遮挡的边：先把所有复刻物体只写深度画一遍，再对线框做深度测试。勾选 "X-ray" 后显示全部边。
- **`Capture Replay (validation)`**：`RenderDocFramePlayer`，按截帧原样离线重放，用于逐 pass 验证。
- **Main Camera**：被放到截帧机位和 FOV。

### 5. 验证（必须做，不要凭肉眼）

打开 **Window > Analysis > RenderDoc Frame Replay**：

- **Validate all passes**：离线精确重放，每个 pass 结束时和截帧真值比较。标准是平均误差小于 0.02，健康结果大多是 0.0000。从**第一个**超标的 pass 开始排查，后面的偏差通常都是被它带偏的。
  每个 pass 还会统计误差超过 0.1 的像素占比：超过 0.2% 也算不一致。只占画面一小块的物体出错（比如角色的某件衣服）时，平均误差会被稀释，要靠这一项才能发现。
- **Validate completeness**：把所有屏幕尺寸 target 先 clear 再重放一遍，和正常重放做对比。
  - `COMPLETE`：完整，可以自由相机。
  - `COMPLETE (temporal inputs)`：个别 pass 读了上一帧的数据（比如上一帧的深度），影响只在局部，同样可以自由相机。
  - `TRUNCATED`：截帧开始前画的内容一直影响到了最终画面，自由相机下这部分会停留在旧画面，需要重截。
  要支持自由相机的话，这一步必须做。
- **Validate scene render**：用 Main Camera 在截帧机位，按截帧分辨率走复刻管线渲染，再和最终画面比较。健康结果约 0.003–0.015，开启超采样后边缘会多一些差异。
- **Main Camera / Scene View to capture view**：把相机放回截帧机位。

排查方法和已知问题清单见 `references/troubleshooting.md`。

### 6. 使用

- Scene view 和 Game view 直接可以用，二者走同一条管线，显示一致。
- 物体可以移动、旋转、缩放、禁用、删除，G-buffer、shadow caster、instancing 实例槽位都会同步。
- `RenderDocReplayPipeline.asset` 的 `renderScale`（默认 2）是超采样倍数。原游戏的 TAA 抖动在自由视角下无法使用，超采样用来替代抗锯齿，半分辨率的雾、云、阴影也会一起变清晰。

## 已知限制（告诉用户）

- **只有截帧里画过的物体**：游戏已经剔除了视野外的物体。
- **阴影覆盖范围有限**：shadow map 有一部分从前几帧缓存，离截帧机位太远会缺阴影。
- **面向原镜头的东西不会转向**：CPU 预先朝向原镜头的面片（光柱、云片、billboard）和预烘焙的远景大角度看会露出是一张片。
- **离开截帧机位后，TAA 历史帧用当前帧代替**：没有时域残影，但也没有 TAA 抗锯齿。
- **极少数物体不能移动**：顶点已经在世界空间的物体（比如静态合批）能正常渲染，但不会跟随 Transform。
- **新加的普通 Unity 物体不会被复刻管线渲染**。

## 进一步阅读

| 文件 | 何时读 |
|---|---|
| `references/capture-modes.md` | 截帧前选模式；判断游戏用哪个 API；Vulkan 和 OpenGL 截帧的差异，以及工具分别如何处理 |
| `references/pipeline.md` | 想了解或修改提取和重放的原理：frame.json 结构、相机/物体改写的种类、Unity 端的类 |
| `references/troubleshooting.md` | 验证不过、画面不对、残影、颜色发白、方向颠倒等问题的排查流程和已知问题清单 |
| `references/renderdoc-api.md` | 写 qrenderdoc 探针脚本、改提取脚本时，RenderDoc 1.46 Python API 的用法和坑 |

## 文件

```
scripts/run_in_renderdoc.py     启动器（普通 Python）：survey / frame / extract
scripts/rd_inside.py            qrenderdoc 内入口；survey、extract；frame 模式转给 rd_frame
scripts/rd_frame.py             frame 模式提取（Vulkan + OpenGL）
scripts/glsl_globals.py         OpenGL：GLSL 改写（uniform 打包 std140、location）与 std140 布局，提取和生成两端共用
scripts/make_replay_shaders.py  shader 生成：GLSL→SPIR-V→HLSL、渲染状态、坐标约定、PS 输入对齐、比较采样模拟
scripts/make_camera_bindings.py 相机/物体/屏幕常量/视锥射线/TAA 历史识别
unity/Runtime/                  RenderDocFrame(数据) RenderDocFrameRenderer(重放核心) RenderDocScene/RenderDocObject(场景)
                                RenderDocPipelineAsset(SRP) RenderDocFramePlayer(离线验证) + Present/WriteDepth/Display shader
unity/Editor/                   RenderDocFrameImporter(导入、建场景) RenderDocFrameWindow(验证窗口) RenderDocObjectEditor(物体的 draw 清单) RenderDocWireframe + RenderDocWire.shader(选中物体的线框)
                                RenderDocSceneImporter(extract 路线)
```
