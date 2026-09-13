# Unity MCP 配置指南

> 适用范围：让 Claude Code 直接操作 Unity 编辑器（读场景、改脚本、跑测试、看 Console）
> 涉及组件：`com.coplaydev.unity-mcp` (MCP for Unity) · Claude Code CLI / VSCode 扩展 · uv/uvx
> 首次配置时间：2026-09-05 · 记录机器：Windows 11 + Unity 项目 `prometheus`

---

## 1. 结论速览

配置本身只有三步（Unity 装包 → 注册 MCP server → 重启会话），但在 **VSCode Claude Code 扩展**下踩到一个不明显的坑：插件的"一键配置"会成功，但扩展会话里**一个工具都拿不到**。

根因是 Windows 盘符大小写导致配置被写进了 `~/.claude.json` 里的**另一个 project 条目**。详见 §4。

如果你用的是**终端里的 `claude` CLI**，一键配置直接可用，不会遇到这个问题。

**本项目已把配置固化为项目根的 `.mcp.json`（§5.2），新机器 clone 后无需任何注册命令。**

---

## 2. 前置条件

| 组件 | 要求 | 本机实测值 |
|------|------|-----------|
| Unity 包 | `com.coplaydev.unity-mcp` | 10.1.0（`Packages/manifest.json` 已引用 git URL） |
| Python 服务端 | 由 uvx 自动拉取 | `mcpforunityserver==10.1.0`（版本号取自包的 `package.json`） |
| uv / uvx | 需在 PATH，或在插件里手填路径 | `D:\Apps\Python3.12.8\Scripts\uvx.exe` |
| Claude Code | CLI 或 VSCode 扩展 | 扩展 2.1.261 / PATH 上的 CLI 2.1.239 |

`Packages/manifest.json` 里已有：

```json
"com.coplaydev.unity-mcp": "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main"
```

新机器 clone 项目后 Unity 会自动拉包，**不需要手动装**。

---

## 3. 两种 transport，二选一

插件支持 HTTP 和 stdio 两种，且在 Unity 侧是**互斥**的 —— `BridgeControlService.StartAsync()` 启动一种时会先 `StopAsync` 另一种。**Unity 窗口里选的模式必须和 Claude 侧注册的 transport 一致**，否则连不上。

| | HTTP（推荐，插件默认） | stdio |
|---|---|---|
| Claude 侧配置 | `{"type":"http","url":"http://127.0.0.1:8080/mcp"}` | `uvx --from mcpforunityserver==<ver> mcp-for-unity` |
| 服务端谁启动 | Unity 插件（"Start Local HTTP Server"） | Claude 每个会话自己拉起 |
| 配置可移植性 | **高**，纯 URL，无机器相关路径 | 低，含 uvx 绝对路径 |
| 前置要求 | 用前必须先在 Unity 里把服务起起来 | 无需手动启动 |

本项目采用 **HTTP**，配置随仓库的 `.mcp.json` 分发（见 §5.2）。理由：配置里不含任何机器相关路径，可跨机器直接复用。

---

## 4. 根因：`--scope local` 按路径字符串分区

这是新机器上最容易重复踩的坑，单独说明。

`claude mcp add --scope local` 把配置存进 `~/.claude.json` 的 `projects` 字典，**key 是当前工作目录的字符串原样，不做大小写规范化**。而两类进程给出的 cwd 不一样：

- **Unity 插件 / PowerShell / cmd** → 盘符大写 `D:/Unity Projects/prometheus`
  （Windows 层面强制规范化。实测：`Set-Location 'd:\...'` 后 `(Get-Location).Path` 立即变成 `D:\...`）
- **VSCode 扩展** → 盘符小写 `d:/Unity Projects/prometheus`
  （cwd 来自 VSCode 的 `Uri.fsPath`，在 Windows 上产出小写盘符）

于是 `~/.claude.json` 里出现了只有大小写不同的多个条目：

```
'd:/Unity Projects/Prometheus'    9 字段，空壳
'D:/Unity Projects/prometheus'   27 字段，含会话统计 ← 插件一键写这里
'd:/Unity Projects/prometheus'    9 字段          ← VSCode 扩展读这里
```

**表现**：插件一键提示成功，终端 `claude mcp list` 显示 ✔ Connected，但 VSCode 扩展会话里工具列表为空。不是配置失败，是**配置进了另一个抽屉**。

> 副作用提醒：插件的一键会先对 local/user/project 三个作用域各跑一次 `claude mcp remove`，再只写回大写那条。所以**手工补好小写条目后，不要再点一键**，否则会被清掉、问题复现。

---

## 5. 新机器配置流程

### 5.1 通用步骤（两种客户端都要做）

1. clone 项目，用 Unity 打开，等包管理器拉完 `com.coplaydev.unity-mcp`
2. 安装 uv：`winget install astral-sh.uv`（或 `pip install uv`），确认 `uvx --version` 可用
3. Unity 里 `Ctrl+Shift+M`（macOS `Cmd+Shift+M`）打开 MCP for Unity 窗口
4. 确认 transport 是 **HTTP**，点 **Start Local HTTP Server**，等 Console 出现 `Server ready on http://127.0.0.1:8080`
   - 首次会拉 Python 依赖，可能要一分钟

### 5.2 首选：项目级 `.mcp.json`（本项目已采用）

项目根已提交 `.mcp.json`：

```json
{
  "mcpServers": {
    "UnityMCP": {
      "type": "http",
      "url": "http://127.0.0.1:8080/mcp"
    }
  }
}
```

**新机器上不需要跑任何注册命令** —— clone 下来就有。它按磁盘目录被发现，不走 `projects` 字典，因此完全不受 §4 的盘符大小写影响。

唯一需要一次的动作：首次启动会话时会提示是否启用项目级 MCP server，点确认。批准状态记在 `~/.claude.json` 的 `enabledMcpjsonServers` 里，这一项仍按大小写敏感的 key 存，所以终端和 VSCode 扩展各会问一次 —— 但**这是明确的提示，不是静默失败**，正是它优于 `--scope local` 的地方。

想跳过提示，可在 `~/.claude/settings.json` 里加：

```json
{ "enableAllProjectMcpServers": true }
```

### 5.3 备选 A：终端 CLI 注册

在 Unity 窗口的客户端列表里选 Claude Code → 点 **Register**。或手动：

```bash
claude mcp add --scope local --transport http UnityMCP http://127.0.0.1:8080/mcp
claude mcp list        # 应显示 UnityMCP ✔ Connected
```

注意这会写进 `~/.claude.json` 的大写盘符 key，VSCode 扩展读不到（§4）。

### 5.4 备选 B：手工补齐 `~/.claude.json`（不建议）

只在无法改动仓库时使用。先照 §5.3 注册，再把生成的条目复制到其它大小写 key 下：

```bash
python -c "
import json,io
p=r'C:/Users/<你>/.claude.json'
d=json.load(io.open(p,encoding='utf-8'))
src=d['projects']['D:/你的/项目路径']['mcpServers']['UnityMCP']
for key in ['d:/你的/项目路径']:          # 补上小写盘符变体
    d['projects'][key]['mcpServers']['UnityMCP']=json.loads(json.dumps(src))
io.open(p,'w',encoding='utf-8').write(json.dumps(d,indent=2,ensure_ascii=False))
"
```

改前先备份 `~/.claude.json`。

---

## 6. 交给 Claude 的提示词

新机器上把下面这段整个粘给 Claude Code，它能自己查完并配好：

```
帮我在这台机器上配置 Unity MCP。项目已经通过 Packages/manifest.json 引用了
com.coplaydev.unity-mcp，采用 HTTP transport（http://127.0.0.1:8080/mcp）。

请按 Docs/UnityMcpSetup.md 执行，重点注意第 4 节描述的坑：

1. 确认 uvx 可用，确认 Unity 包版本（读 Library/PackageCache 下该包的 package.json）
2. 检查 ~/.claude.json 的 projects 字典里，本项目是否存在多个只有盘符大小写不同的 key。
   注意用 python 解析 JSON 来查，不要用行内 grep —— 非空的 mcpServers 是跨行的，
   行内正则会漏掉它，导致误判成"没配置"
3. 确认当前会话实际用的是哪个 key：读 ~/.claude/projects/<项目>/<当前sessionId>.jsonl
   第一行的 cwd 字段
4. 项目根应已有 .mcp.json（不受大小写影响），确认它存在且已在 enabledMcpjsonServers 里获批准；
   若不便动仓库，才退回 claude mcp add，注册后把条目复制到所有大小写变体下，改前备份
5. 配完告诉我需要重启会话，并提醒我先在 Unity 里 Ctrl+Shift+M 启动 HTTP server

不要点插件的一键配置，它会清掉手工补的条目。
```

---

## 7. 验证

重启会话后，工具列表里应出现 `mcp__UnityMCP__*`（50+ 个）。**光看到工具还不够**，要验证到 Unity 编辑器的链路是通的：

```
mcp__UnityMCP__manage_editor(action="telemetry_status")   → success: true（只验到服务端）
mcp__UnityMCP__read_console(action="get", count=3)        → 能读到编辑器真实日志（端到端）
```

`read_console` 返回 `Server ready on http://127.0.0.1:8080` / `Session connected` 就说明全通了。

---

## 8. 排查表

| 症状 | 检查 |
|------|------|
| 工具列表为空 | §4 大小写问题；确认会话 cwd 对应的 key 下有 `mcpServers` |
| `claude mcp list` 显示 Connected 但会话里没工具 | 同上，典型症状 |
| 工具在但调用报连不上 Unity | Unity 编辑器没开，或只开了 Unity Hub；或 HTTP server 没启动 |
| 调用超时 / 握手失败 | Unity 窗口的 transport 与 Claude 侧注册的不一致（§3 互斥） |
| 一键配置后原本好的又坏了 | 一键会先 remove 三个作用域；用 §5.2 的 `.mcp.json` 则不受影响 |
| 端口冲突 | 8080 被占用时，以 Unity 窗口 "HTTP Server Command" 折叠区显示的实际 URL 为准 |

日志位置（Windows）：`%LOCALAPPDATA%\UnityMCP\Logs\unity_mcp_server.log`

---

## 9. 可用工具速查

| 分类 | 工具 |
|------|------|
| 脚本 | `manage_script` `create_script` `apply_text_edits` `script_apply_edits` `validate_script` `find_in_file` |
| 场景 / 对象 | `manage_scene` `manage_gameobject` `find_gameobjects` `manage_components` `manage_prefabs` |
| 资源 | `manage_asset` `manage_material` `manage_texture` `manage_shader` `manage_scriptable_object` `import_model` |
| 编辑器 | `manage_editor`（play/pause/stop、tag/layer、undo/redo）`execute_menu_item` `refresh_unity` `read_console` |
| 其它 | `execute_code`（在编辑器内跑 C#）`run_tests` `manage_build` `manage_profiler` `manage_probuilder` `manage_animation` `manage_vfx` `unity_docs` `unity_reflect` |

改完脚本后按插件建议：先 `refresh_unity`，再 `read_console` 确认没有编译错误。
