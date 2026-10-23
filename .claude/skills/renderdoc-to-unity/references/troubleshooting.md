# 排查流程与已知问题

## 目录

1. 排查方法（先读）
2. 按现象索引
3. 已修复问题清单（改代码前先看，避免回退）
4. Unity / MCP 环境的坑

## 1. 排查方法

1. **先跑离线逐 pass 验证**（RenderDoc Frame Replay 窗口 → Validate all passes）。
   - 它不做任何相机/物体改写，等于在 Unity 里原样执行截帧，能把问题和自由相机的改写分开。
   - **只看第一个超标的 pass**，后面的大多是被它带偏的。
2. **离线验证正常、但场景渲染不对时，逐项关掉改写做对照**：
   - 每种 patch kind、`historyOf`、`viewRays` 各关一次，看哪项影响画面；
   - 方法是在内存里临时清空 `cb.patches` 或 `op.viewRays`，渲染后再恢复。
3. **用局部区域的误差统计**，不要只看全图平均：
   - 用户指出的问题常在某个局部，全图平均会把它稀释掉。
4. **定位到具体 draw**：
   - 用探针在 qrenderdoc 里把 target 在该 pass 每个 draw 之后的内容导出成 raw；
   - 在 Unity 里 `stopAfterOp = i` 逐 draw 重放、逐 draw 比较，误差跳变的那个 draw 就是源头；
   - 再把真值增量和我们的增量都画出来对比。
5. **检查 draw 的输入**，按这个顺序：
   - 顶点属性数据（每个 location 的前几个值看起来是否合理）；
   - 打包后的 cbuffer（按布局解码出名字和值）；
   - 贴图（格式、swizzle、sampler）；
   - 渲染状态（blend、cull、depth）；
   - 生成的 HLSL（输入结构的语义、VS 输出与 PS 输入是否对齐）。
6. **探针脚本的写法**：
   - 在 qrenderdoc 里运行，通过环境变量传参（`__file__` 未定义）；
   - 只结束自己启动的进程；
   - 参考 `renderdoc-api.md`。
7. **颜色问题以屏幕实际像素为准**：
   - 截真实窗口：PowerShell 调 `PrintWindow`（flag 2），按像素对比；
   - 不要依赖 MCP 的 scene_view 截图，它读 HDR RT 时颜色不准。

## 2. 按现象索引

| 现象 | 常见原因 | 位置 |
|---|---|---|
| 某个全屏 pass 输出全黑或只有 clear 色 | clip z 在 -1..1，被 D3D 裁掉 | 已处理：`neg_one_to_one` |
| G-buffer 部分面消失或里外反了 | culling 方向 | `invertCulling`（默认 on）、`front_ccw`；GL 需要取反 |
| albedo 平涂、法线贴图失效、UV 像常量 | VS→PS 寄存器错位 | 已处理：`align_ps_inputs` |
| 位置正确但法线/顶点色不对（GL） | 属性 location 用了 regIndex，或缺少 generic 常量 | 已处理：`attribute_locations` |
| instanced 物体全部不见或坍缩（GL） | `gl_InstanceID` 混进 inputSignature，占了 location 0 | 已处理：排除 `gl_*` 和 system value |
| 某个贴图效果丢失或不对（GL） | sampler 被丢弃（object 为 Null），wrap/filter 用了默认值 | 已处理：GL 接受 Null sampler |
| R8 贴图读 `.w`、Alpha8 效果不对 | image view swizzle | 已处理：swizzle 烘焙 |
| 导入报 B10G11R11 不能 Sample | D3D11 不能采样该格式 | 已处理：扩宽为 RGBA half |
| 阴影整片反了 | Unity 镜像了 comparison sampler | 已处理：`rd_cmp` 手算比较 |
| 一帧开头的 G-buffer/深度 pass 就超标，近处大片没被照亮（GL） | 帧内不 clear depth/stencil，靠上一帧的内容；D24S8 位布局或 stencil 没恢复 | 已处理：GL D24S8 转换位序，`raw_depth` 带 stencil |
| 截帧机位完美，离开机位后近处地面/草地不跟视角、角色被挡、光照分类乱 | 模拟器截帧从游戏帧中间开始，开头的 draw 只剩初始内容 | Unity 里 **Validate completeness**（结果为 TRUNCATED）；重新截帧直到完整（capture-modes.md） |
| 双面材质（布料、裙子）的 normal 反了，内外层贴图对调（GL） | Cull/Stencil 的正反面对调了，但 `SV_IsFrontFace` 没跟着对调 | 已处理：`front_ccw` 时 `gl_FrontFacing` 取反 |
| 全部 pass 都"通过"，但某个小物体明显不对 | 平均误差被整帧稀释 | 看 `pixels > 0.1` 这一列（超过 0.2% 会标记） |
| 一串 draw 完全没画、console 报 `unrecognized identifier 'RDPIn'` | PS 不读任何 varying，spirv-cross 不生成 RDPIn | 已处理：包装函数按有无 RDPIn 生成 |
| 4444 贴图颜色通道错乱 | B4G4R4A4 不在 `UNITY_FORMAT`，或位序错 | 已处理：按 PickPixel 判定位序，扩成 RGBA8 |
| 远处或低 mip 的贴图不对 | 贴图走了 PNG，只有 mip 0 | RGB8 已改为原始数据；ETC2 仍然只有 mip 0 |
| 移动相机后，雾或光照固定在屏幕上（浮影、固定的地平线） | 顶点里的视锥射线是 CPU 按原相机写的 | 已处理：`view_rays` |
| 转动相机后，柱子等高对比边缘有残影 | TAA 历史图是截帧机位的画面 | 已处理：`history_of`（离开截帧机位才替换） |
| 截帧机位的场景渲染比离线差 | jitter 被去掉，或历史图被替换 | 已处理：`atCapture` 特判 |
| 画面整体发白或偏暗 | gamma 解码次数不对 | 见 pipeline.md §6 |
| Game view 或 Scene view 上下颠倒 | 目标是否 bottom-up 判断错 | 只有带 `targetTexture` 的相机（含 Scene view）在 D3D 上是 bottom-up |
| 锯齿明显 | 没有 TAA | 提高 `renderScale`（超采样） |
| 阴影块状 | shadow map 分辨率，以及部分缓存自前几帧 | 原作限制 |
| 光柱、云片从侧面看露出是一张片 | CPU 预先朝向原镜头的面片 | 原作限制 |

## 3. 已修复问题清单（改代码前先看，避免回退）

**提取（rd_frame.py）**
- GL 的 pass 合成：FBO 变化时 load；clear 值从 `glClear(...)` 名字解析。pass 的"首次读取"事件要用 eid-1（draw 之前的状态）。
- GL 的 `$Globals` 打包布局必须和 shader 改写用同一份 `glsl_globals.std140_layout`。
- GL 的属性：`glBindAttribLocation` 加 generic 常量；排除内置变量；generic 常量也要计入 mesh 去重 key。
- GL 的 sampler：接受 object 为 Null 的；compare 只在 `sampler*Shadow` 声明时保留；记录 mip 过滤。
- R8G8B8：原始数据扩成 RGBA8，含全部 mip。cube 退回 PNG。
- 样本匹配：平面网格的 4x4 拟合是奇异的，要保存位置到 clip 的样本，供绑定脚本匹配。
- Vulkan 的 attachment 是 view，要沿 parentResources 找 image（`parentResources[0]` 是 device）。
- 深度初始内容：先读后写的深度（比如缓存的 shadow map）导出为 `raw_depth`，在 Unity 里用 SV_Depth 写回。
- GL 的 D24S8 `GetTextureData` 是 `UNSIGNED_INT_24_8`（高 24 位深度、低 8 位 stencil）；导出时转成 Vulkan 顺序（低 24 位深度、高字节 stencil）。
- 有的 GL 游戏整帧不 clear depth/stencil（例如延迟光照按上一帧留下的 stencil 分类）：D24S8 的 `raw_depth` 导入为 RG32（g = stencil），WriteDepth 先写深度并清 stencil，再按 8 个 bit plane 写回 stencil。
- B4G4R4A4 等 4444 格式：原始数据扩成 RGBA8，含全部 mip；位序每种格式判定一次，只采信 24 种排列里结果唯一的贴图（全 0 的贴图判不出来）。实测 MuMu GL：r=4、g=8、b=0、a=12。
- 3D 贴图：`GetTextureData` 若只返回一个 depth slice，按 slice 拼成整个体积。

**shader（make_replay_shaders.py / glsl_globals.py）**
- cbuffer 名字按 block 序号映射；GL 下 `$Globals` 对应 `_RDGlobals`。
- VS 贴图加 `rdvt` 前缀，对应的 sampler 名为 `samplerrdvt_X`（Unity 规则是 "sampler" 加贴图名）。
- 不要把 SV_InstanceID 当成 TEXCOORD 来转成 float。
- `bilinear` 不能叫 `linear`（HLSL 关键字）。
- PS 不读任何 varying 时没有 `RDPIn`：包装函数写成 `rd_frag()`。shader 编译失败不会让提取报错，导入后要查 console。
- heredoc 会吞掉 Python 字符串里的 `\b`、`\\`：生成补丁时要么用 Write 工具写文件，要么用 Edit 工具。

**相机绑定（make_camera_bindings.py）**
- instancing 的大 cbuffer 里残留着其他批次的矩阵：draw 关联物体时以拟合或样本为准，引用只作 fallback。
- 透视签名会有误报（参数块）：用"最常见且 V 不是单位阵"来选相机。
- 屏幕常量按各级尺寸取整方式匹配（例如 285 = ceil(1138/4)）。

**Unity**
- `SetGlobalConstantBuffer` 能用；MaterialPropertyBlock 的 constant buffer 不可靠。
- 清深度时 reversed-Z 平台传 1-d（`convertClearDepth`）。
- `RenderTexture.shadowSamplingMode` 不存在，要在 descriptor 上设。
- 导入后要 `SaveAssets` 再 `LoadAssetAtPath`，否则引用的是假 null 对象。
- 超采样的平均要在 linear 空间进行。

## 4. Unity / MCP 环境的坑

- **长操作会让 MCP 调用超时**（导入、首次编译 shader），但 Unity 仍在执行。之后再查询状态即可，不要重复导入。
- **改了 asmdef 内的脚本后要等域重载**，再调用新 API。用 `System.Type.GetType("...,Assembly")` 或检查字段是否存在，确认新程序集已经加载。
- **Unity 6 的 API 变化**：
  - `Object.GetInstanceID()` 已过时并报错，用对象本身做字典 key；
  - `FindFirstObjectByType` 已过时，用 `FindAnyObjectByType`。
- **`execute_code` 的 CodeDom 只支持 C# 6**：没有 out var、局部函数、元组。lambda 参数不能和外层变量重名。
- **`EditorSceneManager.NewScene`、`SaveScene` 不会弹框**：覆盖前要自己确认目标文件。
- **ScriptableObject 新增字段**：旧 asset 缺字段时，会用字段初始值。
