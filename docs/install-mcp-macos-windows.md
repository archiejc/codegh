# LiveCanvas MCP 安装与配置指南（macOS / Windows）

本文档面向需要在另一台机器上直接 clone `main` 分支并安装 LiveCanvas MCP 的用户，覆盖：

- Host-only 模式：只启动 MCP 主机，验证 MCP 通信
- Live 模式：连接 Rhino 8 + Grasshopper，真正执行 `gh_*` 与 `copilot_apply_plan`
- Codex 配置：将 LiveCanvas 注册为 Codex 可调用的 MCP server

当前说明基于仓库 `main` 分支验证整理。

## 1. 安装前先理解两种模式

### Host-only

适合先验证 MCP 服务本身是否能启动、是否能被客户端识别。

特点：

- 不要求 Rhino 8 正在运行
- 可在 macOS / Windows / Linux 上完成
- 可以通过 `smoke_mcp_stdio.py` 验证 `initialize` 与 `tools/list`

### Live

适合演示真实 Grasshopper 工作流。

特点：

- 需要 Rhino 8
- 需要打开 Grasshopper
- `gh_*` 工具会作用到当前 Rhino / Grasshopper 会话
- `copilot_apply_plan` 也依赖这一模式

如果你的目标是让 Copilot 在演示里真正生成 GH 图并继续自然语言迭代，请直接按 Live 模式安装。

## 2. 通用前提

两种系统都需要先准备：

1. `Git`
2. `.NET SDK 8`
3. `Python 3`
4. 如果要用 Live 模式，再安装 `Rhino 8`

默认 Rhino 路径：

- macOS: `/Applications/Rhino 8.app`
- Windows: `C:\Program Files\Rhino 8`

如果 Rhino 不在默认位置，需要修改 `src/LiveCanvas.RhinoPlugin/LiveCanvas.RhinoPlugin.csproj` 中的 Rhino 引用路径：

- `RhinoCommon.dll`
- `Grasshopper.dll`

## 3. 获取代码

```bash
git clone <your-repo-url>
cd codegh
git checkout main
git pull origin main
```

如果你只是做演示环境准备，建议固定使用 `main`，避免把中间开发分支带进演示机。

## 4. macOS 安装步骤

### 4.1 推荐路径：一键安装并注册到 Codex

如果你要把 LiveCanvas 同时注册给 Codex 和 Claude：

```bash
bash scripts/install-mcp-livecanvas-mac.sh --target both
```

如果只注册给 Codex：

```bash
bash scripts/install-mcp-livecanvas-mac.sh --target codex
```

这个脚本会自动完成：

1. 构建 Rhino 插件
2. 构建 `LiveCanvas.AgentHost`
3. 构建 smoke harness
4. 把 Rhino 插件部署到 Rhino 8 的 `MacPlugIns`
5. 更新 Codex 配置文件 `~/.codex/config.toml`

默认写入的 Codex MCP 配置等价于：

```toml
[mcp_servers.livecanvas]
command = "/bin/bash"
args = [ "/absolute/path/to/codegh/scripts/run-agenthost-mac.sh", "--skip-build" ]
```

脚本结束后：

1. 重启 Codex Desktop
2. 打开 Rhino 8
3. 新建一个 Rhino 文档
4. 打开 Grasshopper
5. 在 Codex 里请求调用 `gh_session_info`

### 4.2 手工安装路径

如果你不想用自动脚本，可以手工执行。

先构建：

```bash
dotnet build LiveCanvas.sln -v minimal
```

或者使用仓库脚本：

```bash
scripts/build-rhino-plugin-mac.sh Debug
```

然后部署 Rhino 插件：

```bash
scripts/deploy-rhino-plugin-mac.sh Debug
```

插件会同步到：

```text
~/Library/Application Support/McNeel/Rhinoceros/8.0/MacPlugIns/LiveCanvas.RhinoPlugin/
~/Library/Application Support/McNeel/Rhinoceros/8.0/MacPlugIns/LiveCanvas.RhinoPlugin.rhp/
```

启动 Rhino 和 Grasshopper：

```bash
scripts/open-rhino-grasshopper-mac.sh
```

### 4.3 如果你的 MCP 客户端不是 Codex

先发布 host：

```bash
dotnet publish src/LiveCanvas.AgentHost/LiveCanvas.AgentHost.csproj -c Release -o ./dist/agenthost
```

然后写入 MCP 配置：

```json
{
  "mcpServers": {
    "livecanvas": {
      "command": "dotnet",
      "args": [
        "/absolute/path/to/codegh/dist/agenthost/LiveCanvas.AgentHost.dll"
      ],
      "env": {
        "LIVECANVAS_BRIDGE_URI": "ws://127.0.0.1:17881/livecanvas/v0"
      }
    }
  }
}
```

说明：

- `LIVECANVAS_BRIDGE_URI` 可以省略，默认就是 `ws://127.0.0.1:17881/livecanvas/v0`
- 在 macOS 上也可以直接调用可执行文件 `dist/agenthost/LiveCanvas.AgentHost`
- 如果你希望配置跨平台可迁移，推荐始终使用 `dotnet + .dll`

## 5. Windows 安装步骤

Windows 当前没有一键安装脚本，建议按下面顺序手工配置。

### 5.1 构建 Rhino 插件

```powershell
dotnet build .\src\LiveCanvas.RhinoPlugin\LiveCanvas.RhinoPlugin.csproj -c Debug -v minimal
```

插件项目会在 Windows 下编译两个输出目录：

- `.\src\LiveCanvas.RhinoPlugin\bin\Debug\net8.0-windows\`
- `.\src\LiveCanvas.RhinoPlugin\bin\Debug\net7.0-windows\`

建议优先尝试：

```text
.\src\LiveCanvas.RhinoPlugin\bin\Debug\net8.0-windows\LiveCanvas.RhinoPlugin.rhp
```

如果 Rhino 无法加载，再退回尝试：

```text
.\src\LiveCanvas.RhinoPlugin\bin\Debug\net7.0-windows\LiveCanvas.RhinoPlugin.rhp
```

### 5.2 在 Rhino 8 中加载插件

1. 打开 Rhino 8
2. 新建一个 Rhino 文档
3. 打开 Rhino 的插件管理界面
4. 手工加载上一步构建出的 `LiveCanvas.RhinoPlugin.rhp`
5. 确认插件已启用
6. 打开 Grasshopper

### 5.3 发布 MCP Host

```powershell
dotnet publish .\src\LiveCanvas.AgentHost\LiveCanvas.AgentHost.csproj -c Release -o .\dist\agenthost
```

### 5.4 配置 Codex

Codex 的 MCP 配置可直接写在共享配置文件 `~/.codex/config.toml` 中。请加入：

```toml
[mcp_servers.livecanvas]
command = "dotnet"
args = [ "C:\\absolute\\path\\to\\codegh\\dist\\agenthost\\LiveCanvas.AgentHost.dll" ]
```

如果你希望显式指定 bridge，也可以在支持环境变量的客户端里增加：

```json
{
  "mcpServers": {
    "livecanvas": {
      "command": "dotnet",
      "args": [
        "C:\\absolute\\path\\to\\codegh\\dist\\agenthost\\LiveCanvas.AgentHost.dll"
      ],
      "env": {
        "LIVECANVAS_BRIDGE_URI": "ws://127.0.0.1:17881/livecanvas/v0"
      }
    }
  }
}
```

推荐优先使用 `dotnet + .dll`，这样和 macOS 的配置思路一致，也更稳定。

### 5.5 重启客户端

修改 Codex 配置后，关闭并重新打开 Codex Desktop，使新的 MCP 配置生效。

## 6. 验证步骤

### 6.1 Host-only 验证

```bash
python3 ./scripts/smoke_mcp_stdio.py --agent-host dist/agenthost
```

Windows 也可以使用：

```powershell
py -3 .\scripts\smoke_mcp_stdio.py --agent-host dist\agenthost
```

这一步验证的是：

- MCP host 能启动
- `initialize` 能成功
- `tools/list` 能成功

### 6.2 Live Bridge 验证

请先确认：

1. Rhino 8 已打开
2. 当前存在一个 Rhino 文档
3. Grasshopper 已打开

然后执行：

```bash
python3 ./scripts/check_live_bridge.py --agent-host dist/agenthost
```

Windows：

```powershell
py -3 .\scripts\check_live_bridge.py --agent-host dist\agenthost
```

如果你只是想先看主机是否能连通，但不想让脚本因为 Rhino 未启动而退出失败，可以使用：

```bash
python3 ./scripts/check_live_bridge.py --agent-host dist/agenthost --allow-offline
```

### 6.3 在 Codex 中做最终验收

完成上面两步后，在 Codex 里先请求一次：

```text
gh_session_info
```

如果返回当前 Rhino / Grasshopper 会话信息，说明：

- Codex 已成功连接到 `livecanvas`
- MCP stdio 正常
- Rhino bridge 正常

## 7. Copilot Provider 配置

如果你需要使用：

- `copilot_plan`
- `copilot_apply_plan`

则还需要为 `copilot_plan` 提供一个 OpenAI-compatible `POST /chat/completions` 服务，通过环境变量配置：

- `LIVECANVAS_COPILOT_BASE_URL`
- `LIVECANVAS_COPILOT_API_KEY`
- `LIVECANVAS_COPILOT_MODEL`

macOS 示例，可加入 `~/.zshrc`：

```bash
export LIVECANVAS_COPILOT_BASE_URL="https://your-openai-compatible-endpoint"
export LIVECANVAS_COPILOT_API_KEY="your_api_key"
export LIVECANVAS_COPILOT_MODEL="your_model_name"
```

执行：

```bash
source ~/.zshrc
```

Windows PowerShell 示例：

```powershell
setx LIVECANVAS_COPILOT_BASE_URL "https://your-openai-compatible-endpoint"
setx LIVECANVAS_COPILOT_API_KEY "your_api_key"
setx LIVECANVAS_COPILOT_MODEL "your_model_name"
```

然后重新打开终端与 Codex。

说明：

- `copilot_plan` 可以在 Host-only 模式下测试
- `copilot_apply_plan` 需要 Live 模式

## 8. 演示时给 Codex 的配置与验收 Prompt

下面这段 prompt 可以直接贴给 Codex，用来检查当前机器是否已经把 LiveCanvas 配置好，并给出演示前的最终结论。

```text
请帮我检查这台机器上的 LiveCanvas MCP 演示环境是否已经配置完成。仓库根目录是 /absolute/path/to/codegh。

目标：
1. 确认当前仓库位于 main 分支并且工作区没有影响演示的改动。
2. 确认 LiveCanvas MCP 已注册到 Codex。
3. 如果是 macOS，请检查 ~/.codex/config.toml 中是否存在：
   [mcp_servers.livecanvas]
   command = "/bin/bash"
   args = ["/absolute/path/to/codegh/scripts/run-agenthost-mac.sh", "--skip-build"]
4. 如果是 Windows，请检查 Codex 配置中是否存在：
   [mcp_servers.livecanvas]
   command = "dotnet"
   args = ["C:\\absolute\\path\\to\\codegh\\dist\\agenthost\\LiveCanvas.AgentHost.dll"]
5. 检查 dist/agenthost 是否存在；如果不存在，请构建或发布 LiveCanvas.AgentHost。
6. 用 scripts/smoke_mcp_stdio.py 验证 MCP stdio。
7. 如果 Rhino 和 Grasshopper 已打开，再用 scripts/check_live_bridge.py 验证 live bridge。
8. 最后告诉我：
   - 当前是 Host-only ready 还是 Live ready
   - 还缺哪一步
   - 下一句我应该输入什么来开始演示

请不要修改与 LiveCanvas 配置无关的文件；如果要修改 Codex 配置，先告诉我你准备修改哪个文件以及原因。
```

如果你已经确认环境安装完成，演示启动时也可以直接贴下面这段 prompt：

```text
请使用 livecanvas MCP server 开始一次演示前检查：
1. 先调用 gh_session_info，确认 Rhino 和 Grasshopper 会话在线。
2. 如果会话在线，请告诉我当前可以开始执行建模任务。
3. 如果会话不在线，请明确指出是 Rhino 未打开、Grasshopper 未打开，还是 MCP bridge 未连通。
4. 在确认在线后，等待我给你建模指令。
```

## 9. 常见问题

### `Bridge unavailable`

优先检查：

1. Rhino 8 是否已打开
2. 当前是否已有 Rhino 文档
3. Grasshopper 是否已打开
4. 插件是否已正确加载
5. `LIVECANVAS_BRIDGE_URI` 是否与默认监听地址一致

### 插件构建失败，提示找不到 Rhino 程序集

通常是 Rhino 8 没装在默认路径，或者安装路径不同。请修改 `src/LiveCanvas.RhinoPlugin/LiveCanvas.RhinoPlugin.csproj` 中的 `HintPath`。

### MCP 客户端无法启动 host

优先切换为：

- `command = "dotnet"`
- `args = [ "<absolute-path>/LiveCanvas.AgentHost.dll" ]`

这通常比直接指向 `.exe` 或裸可执行文件更稳。

### `copilot_plan` 不可用

大多是因为没有配置：

- `LIVECANVAS_COPILOT_BASE_URL`
- `LIVECANVAS_COPILOT_API_KEY`
- `LIVECANVAS_COPILOT_MODEL`

### 我只想验证 MCP，不想马上开 Rhino

只执行 Host-only 验证即可：

```bash
python3 ./scripts/smoke_mcp_stdio.py --agent-host dist/agenthost
```

等需要真正演示 GH 组件时，再进入 Live 验证。
