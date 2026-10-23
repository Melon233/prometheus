---
name: mumu-renderdoc-capture
description: 用 RenderDoc 注入的方式启动 MuMu 模拟器，让它进入可截帧状态，并指导用户截帧、检查截帧是否完整、截完后恢复正常启动。用户提到"模拟器截帧"、"让 MuMu 进入截帧状态"、"用 RenderDoc 截手游/安卓游戏"、"启动截帧模拟器"、"截帧输出到某目录"、"一次截几帧"、"恢复模拟器正常启动"时都应使用本 skill；截好后要复刻到 Unity，接着用 renderdoc-to-unity。
---

# MuMu 模拟器 + RenderDoc 截帧

用 `renderdoccmd capture` 启动 MuMu 的**设备进程**（`MuMuNxDevice.exe`，不是主界面），让 RenderDoc 注入它。之后在 qrenderdoc 里连接这个进程就能截帧。所有操作都由 `scripts/mumu_capture.ps1` 完成，每个动作输出一个 JSON。

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/mumu_capture.ps1 -Action detect
```

| 动作 | 作用 |
|---|---|
| `detect` | 查找 MuMu 和 RenderDoc 的安装目录、实例、渲染模式、是否已在运行、是否已注入（只读） |
| `launch` | 如果实例正在运行，先关闭（没注入时需要 `-Force`），再用 RenderDoc 注入启动，等到 RenderDoc 的连接端口开始监听 |
| `restore` | 关闭实例，再用 MuMuManager 正常启动（不注入） |
| `captures` | 列出 `-OutDir` 和 RenderDoc 默认目录（`%TEMP%\RenderDoc`）里最新的 .rdc（只读） |

常用参数：`-Index`（实例序号，默认 0）、`-OutDir`（截帧输出目录，默认 `D:\Rdc`）、`-Name`（文件名前缀，默认 `MuMuNxDevice`）、`-MuMuDir` / `-RenderDocDir`（手动指定安装目录）。

## 流程

### 1. 检测

运行 `detect`，按返回结果处理：

- `error: mumu_not_found` 或 `renderdoc_not_found`：**问用户**安装目录。MuMu 目录下应该有 `nx_main\MuMuManager.exe`，RenderDoc 目录下应该有 `renderdoccmd.exe`。拿到后通过 `-MuMuDir` / `-RenderDocDir` 传入，再检测一遍。脚本会依次查找卸载注册表、`.rdc` 文件关联和各盘常见目录，都找不到才会报错。
- `error: instance_N_not_found`：`instances` 字段列出了已有的实例，问用户要截哪个。目录名 `MuMuPlayer-<版本>-<序号>` 末尾的数字就是 `-Index`。
- 输出目录用户没指定时，问一句（默认 `D:\Rdc`）。截帧文件会命名为 `<OutDir>\<Name>_frame<帧号>.rdc`。
- `renderer_api`：模拟器的渲染模式决定了截到的是哪种 API。GLES 游戏用 OpenGL 模式（`ngl`）截，原生 Vulkan 游戏用 Vulkan 模式截（判断方法见 renderdoc-to-unity 的 `references/capture-modes.md`）。模式不合适时，**让用户在 MuMu 设置里改**，不要直接改配置文件。

### 2. 启动

- `injected: true`：已经是可截帧状态，不用重启，直接进入第 3 步。不过输出目录是启动时就定下来的，用户要换输出目录时，必须用 `launch -Force` 重启。
- `running: true` 且 `injected: false`：`launch` 会**关闭正在运行的模拟器**，用户的游戏进度会中断。先征得用户同意，再加 `-Force` 运行。
- 实例没有运行：直接 `launch`。

`launch` 成功后返回 `result: launched_injected`，`injected_port` 一般是 38920。安卓系统还需要一些时间启动（`android_ready` 会变成 true），提醒用户进游戏、走到要截的画面。

### 3. 告诉用户怎么截帧

- **qrenderdoc**：选 File → Attach to Running Instance，选 `localhost`，再选 `MuMuNxDevice`，然后点 **Trigger Capture**。
  - 连接窗口里的 **Frames** 设为 N，会连续截 N 帧，但得到的是 **N 个独立的 rdc**，不是一个文件里装 N 帧。用来一次多截几份备选很方便。
  - **Delay** 可以延迟几秒再截。
- **快捷键**：模拟器窗口在前台时按 **F12** 或 **PrintScreen**。

截完后用 `captures` 找到新文件。如果文件没有出现在 `OutDir`，而是在 `%TEMP%\RenderDoc`，说明模拟器不是通过本脚本启动的，或者是从 qrenderdoc 里另存的。

### 4. 检查截帧是否完整（要自由相机复刻时必做）

模拟器上，RenderDoc 按**模拟器显示线程**的 SwapBuffers 切帧，和游戏帧不对齐。一份截帧可能从游戏帧的中间开始：开头那部分（比如地形、草地）在截帧开始前就画完了，rdc 里只有它们的结果，没有几何体。这样的截帧在截帧机位能精确还原，但**离开机位就会出错**。

快速判断：在事件列表里找主 G-buffer 和主深度，看它们在第一个 draw 之前有没有 `glClear`。有 clear 就是完整的；没有 clear，并且第一个 draw 之前里面已经有画面，就是截断了。截断的话就**重新截**，两者的相位是随机的，多截几次就能截到完整的帧。更准确的检查（提取日志里的 `carried over`，Unity 里的 Validate completeness）见 renderdoc-to-unity。

### 5. 结束后恢复

用户不再截帧时，提议运行 `restore`，让模拟器回到不注入的正常状态，因为注入会占用资源。restore 同样会关闭模拟器，先征得用户同意。

## 注意事项

- 只通过 `MuMuManager control` 关闭和启动实例，不要强杀进程。不要动用户自己开的 qrenderdoc 和 renderdoccmd 进程。
- 模拟器内部用 RenderDoc 安卓端截帧在 MuMu 上行不通（x86_64 + ARM 转译，RenderDoc 没有对应的层库）。`RENDERDOC_HOOK_EGL` 在 MuMu 的 OpenGL 模式下也无效。详见 renderdoc-to-unity 的 `references/capture-modes.md`。
- 截帧里的游戏资源属于第三方，只用于分析和学习。
- 已验证的环境：MuMu 安卓设备 15.0（`nx_device\15.0`），OpenGL 模式，RenderDoc 1.46。其他版本的目录结构可能不同，`detect` 找不到时问用户。
