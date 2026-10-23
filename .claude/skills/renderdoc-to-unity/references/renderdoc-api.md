# RenderDoc 1.46 Python API 笔记

写探针脚本或修改 `rd_frame.py` / `rd_inside.py` 时参考。脚本运行在 qrenderdoc 内置的 Python 中。

## 运行方式

- 用 `qrenderdoc.exe --python script.py` 运行，此时 `__file__` 未定义，参数通过环境变量传入（`run_in_renderdoc.py` 用 `RD2U_ARGS` 指向一个临时 JSON 文件）。
- 脚本结束时调用 `os._exit(0)`，否则 qrenderdoc 窗口会留着不退出。
- 启动器只结束自己 `Popen` 出来的 PID，绝不按进程名结束 RenderDoc。

最小探针模板：

```python
import os
out = open(os.environ["RD_PROBE_OUT"], "w", encoding="utf-8")
try:
    import renderdoc as rd
    cap = rd.OpenCaptureFile(); cap.OpenFile(os.environ["RD_CAP"], "", None)
    _, ctl = cap.OpenCapture(rd.ReplayOptions(), None)
    ...
except Exception:
    import traceback; out.write(traceback.format_exc())
out.close(); os._exit(0)
```

## 常用调用

- **遍历 action**：`ctl.GetRootActions()` 返回树，用 `a.children` 递归。
  - `a.flags` 包括 `Drawcall`、`Clear`、`ClearColor`、`ClearDepthStencil`、`BeginPass`、`EndPass`、`Copy`、`Resolve`、`Present`；
  - 名字用 `a.GetName(sf)` 取。
- **设置事件**：`ctl.SetFrameEvent(eid, force)`，状态为执行完该事件之后。需要"draw 之前"的内容时，用 eid-1。
- **管线状态**：`pipe = ctl.GetPipelineState()`。
  - `GetOutputTargets()` 返回 Descriptor，资源在 `.resource`；
  - `GetDepthTarget()`；
  - `GetReadOnlyResources(stage, onlyUsed)` 返回 UsedDescriptor：`.descriptor.resource`、`.access.index`（反射资源序号）、`.sampler`（SamplerDescriptor）；
  - `GetSamplers(stage, True)`；
  - `GetConstantBlock(stage, index, 0).descriptor`：`resource`、`byteOffset`、`byteSize`；
  - `GetVertexInputs()`：`name, vertexBuffer, byteOffset, format, perInstance, genericEnabled, genericValue, used`；
  - `GetVBuffers()`、`GetIBuffer()`、`GetViewport(0)`、`GetScissor(0)`、`GetColorBlends()`、`GetDepthTestState()`、`GetRasterState()`、`GetStencilFaces()`；
  - `GetPrimitiveTopology()`、`GetShaderReflection(stage)`、`GetGraphicsPipelineObject()`、`GetShaderEntryPoint(stage)`。
- **API 专用状态**：
  - `ctl.GetVulkanPipelineState()`：`viewportScissor.depthNegativeOneToOne`、`rasterizer.depthClampEnable`。
  - `ctl.GetGLPipelineState()`：
    - `vertexProcessing.clipNegativeOneToOne`、`clipOriginLowerLeft`；
    - `vertexInput.attributes[i]`：`enabled`、`genericValue`、`vertexBufferSlot`；
    - `rasterizer.state.depthClamp`、`frontCCW`；
    - `vertexShader.programResourceId`；
    - `framebuffer.framebufferSRGB`。
- **cbuffer 取值**：

  ```python
  ctl.GetCBufferVariableContents(pipe.GetGraphicsPipelineObject(), refl.resourceId, stage,
                                 pipe.GetShaderEntryPoint(stage), blockIndex, buffer, offset, size)
  ```

  - 有 buffer 的 block（`bufferBacked`）：直接用 `GetBufferData(desc.resource, desc.byteOffset, size)` 读原始字节最可靠。传 Null buffer 会读出全 0。
  - 无 buffer 的 block（GL 的 `$Globals`、push constants）：传 `rd.ResourceId.Null(), 0, 0`。返回 ShaderVariable 列表：`name, rows, columns, value.f32v / u32v`；数组和结构体通过 `members` 展开。
- **反射**：`refl.constantBlocks[i]`（`name, byteSize, bufferBacked, compileConstants, variables[].byteOffset/type`）、`refl.readOnlyResources[i].name`、`refl.inputSignature[].varName/regIndex/systemValue`、`refl.encoding`（SPIRV / GLSL）、`refl.rawBytes`、`refl.debugInfo.files[0].contents`（GLSL 源码）。
- **post-VS**：`ctl.GetPostVSData(instance, 0, rd.MeshDataStage.VSOut)` 返回 `vertexResourceId, vertexByteOffset, vertexByteStride, indexResourceId, indexByteStride, numIndices, baseVertex, format`。
- **贴图**：
  - `ctl.GetTextures()` 返回 TextureDescription：`format.Name(), width, height, mips, arraysize, cubemap, type, msSamp, creationFlags`；
  - `ctl.GetTextureData(rid, sub)` 返回原始字节，`sub = rd.Subresource()`，设置 `mip`、`slice`、`sample`；
  - `ctl.SaveTexture(rd.TextureSave(...), path)` 存为 PNG 等格式（压缩格式会被解码）。
- **结构化数据**：`sf = ctl.GetStructuredFile()`，`sf.chunks[a.events[-1].chunkIndex]`。
  - 子节点用 `GetChild(i)` / `NumChildren()` 访问，名字在 `.name`；
  - 取值用 `AsInt()`、`AsFloat()`、`AsString()`、`AsResourceId()`，按 `type.basetype` 选择。
  - 可以遍历全部 chunk，比如 GL 的 `glBindAttribLocation`（子节点 `program, index, name`）。

## 坑

- **TextureCategory 不可靠**：Vulkan 或模拟器截帧里，普通贴图也会带 ColorTarget 标志。判断是否是 RT，要看它在本帧是否作为 target 被写过。
- **attachment 是 view**：要沿 `GetResources()` 的 `parentResources` 找到 image，而 `parentResources[0]` 是 device。
- **GL 的 inputSignature**：
  - `regIndex` 按名字排序，不是 attribute location；
  - 会混入 `gl_InstanceID` 等内置变量（看 `systemValue`）。
- **GL 的 sampler**：`ud.sampler.object` 为 Null，但 `addressU`、`filter` 等参数是有效的。
- **GL 的 compareFunction 一直有值**：要看 shader 是否声明了 shadow sampler。
- **GL 的 PNG 行序**：`SaveTexture` 存出的 PNG 行序和 `GetTextureData` 的原始数据相反。
- **大帧提取**：逐 instance 读 post-VS 比较慢，约 1 到 2 分钟。
