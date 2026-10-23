# 原理与数据格式

## 目录

1. 总体流程
2. frame.json
3. shader 生成（make_replay_shaders.py）
4. 相机、物体和屏幕的改写（make_camera_bindings.py）
5. Unity 端
6. 坐标、行序与颜色约定

## 1. 总体流程

```
qrenderdoc (rd_frame.py)          普通 Python                               Unity
──────────────────────────        ───────────────────────────────          ─────────────────────────────────
ops: pass / draw / blit     ──▶   make_replay_shaders.py                   RenderDocFrameImporter
资源描述 + 初始内容                  GLSL→SPIR-V→HLSL→RDF_*.shader      ──▶   RenderDocFrame (asset)
mesh (按 location 的原始属性)      make_camera_bindings.py                    RenderDocScene + RenderDocObject
cbuffer 原始字节                     相机/物体/屏幕/射线/历史 → patches          RenderDocPipelineAsset (SRP)
每个 pass 的真值 validate/*.raw                                               RenderDocFramePlayer (离线验证)
```

核心思路是"原样重放加精确改写"：
- draw 顺序、渲染状态、cbuffer 字节、贴图都保持截帧原样；
- 只把和视角、物体、分辨率有关的值替换掉，替换的位置预先从字节里识别好（patch 列表）。

## 2. frame.json

| 字段 | 内容 |
|---|---|
| `api`, `event_range`, `final` | API、提取的事件范围、最终图像资源 id |
| `rows_bottom_up` | target 内存行 0 是图像底部（GL 或模拟 GL 的截帧） |
| `resources[]` | `id, format, unity_format, width, height, mips, kind(2d/cube/2darray/3d), rt, is_depth, init{encoding: raw/raw_depth/png, files}`；`init` 是本帧第一次被读取时的内容 |
| `meshes[]` | `id, file, vertex_count, raw[{location, offset}], index_offset, index_count, base_vertex`。`.bin` 里每个 location 存一段 float4 数组，后面跟局部索引（u32） |
| `ops[]` | 见下 |
| `camera` | `view`, `proj`, `view_proj`，均为行主序 16 个 float，列向量约定 clip = P·V·x |
| `objects[]` | `model`（16 个 float）, `ops`（相关 op 下标）, `movable` |
| `screen_levels[]` | 屏幕相关 buffer 的级别：`(level, width, height)`，0 为全分辨率 |

**op 种类**：
- `pass`：`attachments, clears[{color, depth, stencil}], loads{color, depth, stencil}, colors, depth, end_eid, validate{target, file}`。
- `draw`：
  - `family, state, mesh, colors, depth, instances, first_instance, base_vertex, viewport, scissor`；
  - `textures[{name, res, sampler{filter, mip, u, v, w, compare}, swizzle, history_of}]`；
  - `vs_cb / ps_cb[{block, data(base64), patches[...]}]`；
  - `mvp, instance_mvps, instance_samples, position_location`；
  - `objects[]`、`view_rays[{location, depth, ndc[{x,y}], w}]`。
- `blit`：`src, dst`。

**shaders/families.json**：`id, lang(spirv/glsl), vs_spv, ps_spv, vs_attribs, vs_blocks, ps_blocks, spec_constants, states[]`。
- `states` 是完整渲染状态：cull、front_ccw、深度、模板、逐 RT 的 blend、`neg_one_to_one`、`depth_clamp`。
- OpenGL 下 `vs_blocks` 里的 `$Globals` 记作 `_RDGlobals`。

## 3. shader 生成

每个 (family, state) 生成一个 `RenderDocFrame/famN_kM`：

- **cbuffer 改名**：按 block 序号改为 `rdv_N` / `rdp_N`，保留原 packoffset。运行时用 `cmd.SetGlobalConstantBuffer` 绑定原始字节。不要用 MaterialPropertyBlock，它对 `CommandBuffer.DrawMesh` 不是所有后端都生效。
- **sampler 改名**：`__X_sampler` 改成 `sampler_X`（Unity 内联 sampler 的命名规则）。VS 用到的贴图加 `rdvt` 前缀，因为 VS 和 PS 是两个独立的 SPIR-V 模块，id 会撞名。
- **顶点输入**：location k 读 `TEXCOORDk`，导入器把属性 k 写进 mesh 的 UV 通道 k。整数输入只对 TEXCOORD 语义做类型转换。`SV_InstanceID` / `SV_VertexID` 换成 `rd_InstanceIndex` / `rd_BaseVertex` uniform。
- **clip 空间**：`o.gl_Position.y = -y`（Vulkan/GL 的 NDC 到 D3D，保持 target 内存行不变）；`neg_one_to_one` 时再加 `z = (z + w) / 2`。
- **ZTest**：写成镜像（Less 写成 Greater），因为 Unity 在 reversed-Z 平台上会自动镜像 ZTest，镜像两次正好还原，存的仍是原始深度值。
- **Cull 和模板**：`front_ccw` 时交换 Front/Back。
- **PS 输入对齐**：按 VS 输出结构的顺序重排 RDPIn，PS 没有读的槽位补占位成员。D3D11 按声明顺序分配寄存器来链接 VS 和 PS，不对齐会导致数据整体错位。
- **比较采样模拟**：Unity 在 reversed-Z 平台会把 comparison sampler 镜像成 GreaterEqual，而重放存的是原始深度。所以把 `SampleCmp*` 换成 `rd_cmp`，用 GatherRed 加双线性权重手算，比较函数取自截帧。

## 4. 相机、物体和屏幕的改写

`make_camera_bindings.py` 扫描所有 cbuffer 中按 16 字节对齐的 64 字节窗口，列主序和行主序都试。

### 相机

- **识别 VP**："透视签名"指 w 行 xyz 为单位向量、z 行与之平行。具备这个签名、出现在最多 draw 里、并且分解出的 V 不是单位阵的矩阵就是相机 VP。
- **分解**：VP = P·V，P 允许离轴（jitter），V 是刚体变换。

### patch 种类

| kind | 运行时写入的值 |
|---|---|
| `view_proj`, `inv_view_proj` | P'·V' 及其逆 |
| `view`, `inv_view` | V' 及其逆 |
| `world_to_camera`, `camera_to_world` | F·V'（F 为 z 翻转，Unity 的 worldToCamera）及其逆 |
| `view_proj_rot`, `inv_view_proj_rot` | 只含旋转的 VP，供天空使用 |
| `proj`, `inv_proj` | P' 及其逆 |
| `model_view_proj` | P'·V'·M（M 为物体当前矩阵） |
| `object_to_world`, `world_to_object` | 物体当前矩阵及其逆 |
| `camera_pos` | 匹配相机位置的 float3 |
| `screen` | 等于某级屏幕 buffer 的宽、高、1/宽、1/高的 float |

### 物体识别

1. **拟合**：每个 draw、每个 instance 用 post-VS 输出拟合 MVP（最小二乘）。M = VP⁻¹·MVP 为仿射矩阵即为物体，按 M 分组。
2. **样本匹配**：平面网格和拟合失败的 instance 保存了几组"位置 → clip"样本，在 VS cbuffer 里找仿射矩阵 W，使 VP·W·p 等于样本的 clip。
3. **关联 draw**：任何 draw（包括 shadow caster）的 cbuffer 中出现物体的 M 或 M⁻¹，就把它关联到该物体。draw 自己拟合出来的物体优先；instancing 数组里可能残留其他批次的矩阵，只作为 fallback 使用。

### 其他识别

- **视锥射线（view_rays）**：全屏 draw（不超过 4 个顶点）里，所有顶点视空间深度相同、投影后落在全屏三角形角上（±1、3）的属性，就是 CPU 写入的相机射线。每次渲染按新相机重算，写进每个相机自己的 mesh 副本。
- **TAA 历史（history_of）**：本帧从未写入、尺寸格式等于屏幕的贴图，并且同一个 draw 还读了一张同格式、本帧写入的当前颜色 target。离开截帧机位时，用当前颜色代替历史图。

## 5. Unity 端

| 类 | 作用 |
|---|---|
| `RenderDocFrame` | ScriptableObject：资源、mesh、材质、op、物体、相机 |
| `RenderDocFrameRenderer` | 重放核心：按屏幕尺寸分配 target（屏幕相关级别按截帧的取整方式缩放），恢复初始内容，录制 pass/draw/blit，应用 patch、射线和历史替换 |
| `RenderDocScene` | 场景根：收集 `RenderDocObject`、按相机缓存 renderer、计算 View（V' = `cam.worldToCameraMatrix`，P' 保留截帧的近远平面）、Present |
| `RenderDocObject` | `ObjectMatrix = transform.localToWorld · initial⁻¹ · capturedModel`；禁用或删除时视为隐藏（矩阵写 0，所有物体都隐藏的 draw 直接跳过）；带 `[SelectionBase]` |
| `RenderDocPipelineAsset` / `RenderDocPipeline` | SRP：Game 和 SceneView 相机走 `scene.Render`，然后画 gizmos；`renderScale` 为超采样倍数 |
| `RenderDocFramePlayer` | 离线、按截帧原样重放（不做任何 patch），供验证窗口使用 |
| `RenderDocFrameImporter` | 构建 asset、材质、mesh、贴图（含 swizzle 烘焙、B10G11R11 扩宽、RGB8）、op、场景层级 |
| `RenderDocFrameWindow` | 逐 pass 验证、场景渲染验证、相机对齐 |

**截帧机位特判**：相机位姿、FOV、宽高比和分辨率都等于截帧时（`View.atCapture`），使用截帧原始投影（含 jitter）和真实 TAA 历史帧，复现精度最高。

## 6. 坐标、行序与颜色约定

- **行序**：重放 target 与截帧保持相同的内存行序。`rows_bottom_up` 表示内存行 0 是图像底部。
- **Present**：先判断两件事：
  - 源图是否 bottom-up；
  - 目标是否 bottom-up（D3D 上带 `targetTexture` 的相机是，包括 Scene view；Game view 和 back buffer 不是）。
  
  两者不一致就翻转。
- **颜色**：8-bit UNORM 的最终图存的是 gamma 编码值。
  - 目标存 linear 值时先解码：sRGB 目标、无目标（back buffer 或 Game view）、HDR float 目标（Scene view）都属于这种情况；
  - 目标是普通 UNORM 时，解码、平均之后再编码回去；
  - 超采样的平均在 linear 空间进行。
- **编辑器 GUI 预览**：IMGUI 在 linear 工程里会对输出再编码一次。所以用 `Hidden/RenderDocToUnity/Display` 先解码 UNORM target，不要直接用 `GUI.DrawTexture`。
