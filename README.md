# 创世兵魂恢复工程

本项目尝试使用已经保存下来的《创世兵魂》Unity、Flash 缓存和游戏配置资源，
恢复一个可以通过现代浏览器运行、并逐步支持玩家联网对战的版本。

项目坚持复用现有游戏资源和恢复出的程序结构，不使用另外制作的仿制大厅或灰盒地图
替代原游戏内容。目前可从原版风格大厅进入自由模式、频道和训练模式，并在 WebGL 中
运行恢复出的金字塔地图。

## 当前进度

- 已恢复 33 个原客户端场景和大厅 UI。
- 已整理 7 组地图资源，并生成可运行的 WebGL 地图场景。
- 已接通大厅中的“训练模式”按钮与金字塔地图。
- 已恢复第一人称控制器、HUD、雷达、计分板、生命值和近战武器模型。
- 金字塔地图已接入恢复出的手枪模型、枪口火焰与原版射击/换弹音效。
- 已实现手枪、匕首切换，弹匣、换弹、命中、死亡、3 秒复活和 3 分钟回合。
- 已修复 WebGL 场景切换时的 `abort()`、重复按钮监听和加载画面空引用。
- 已实现 Linux 64 位 Node.js WebSocket 对战服务原型。
- 服务端以 20Hz 同步移动、射击、伤害、死亡和复活状态。
- 已提供 Docker 和 Nginx 部署基础配置。

商城、完整房间系统、账号数据、更多武器逻辑和全部按钮功能仍在恢复中。
当前优先保证“自由模式 → 训练模式 → 金字塔双人对战”这一条链路稳定。

## 目录

| 路径 | 内容 |
| --- | --- |
| `client-restored/` | 当前维护的 Unity 2022.3 WebGL 客户端和恢复资源 |
| `server/` | TypeScript WebSocket 权威游戏服务 |
| `deploy/` | Docker Compose、Dockerfile 和 Nginx 示例 |
| `recovery/` | 资源审计结果、原缓存配置和恢复记录 |
| `tools/` | 解包、资源分析和恢复辅助脚本 |

Unity 的 `Library/`、`Temp/`、`Build/`、`node_modules/` 等生成目录不会提交。
二进制模型、贴图和音频通过 Git LFS 保存。

## 环境要求

- Git LFS
- Unity `2022.3.62f3c1`
- Node.js 20 或更高版本
- pnpm 10
- Docker（仅服务器部署需要）

克隆时需要同时拉取 LFS 资源：

```bash
git lfs install
git clone https://github.com/SunKeXu01/GenesisSoldierSoul.git
cd GenesisSoldierSoul
git lfs pull
```

## 本地运行

安装依赖并验证服务端：

```bash
corepack enable
pnpm install
pnpm test
```

启动开发服务：

```bash
pnpm dev
```

默认端点：

- HTTP 健康检查：`http://127.0.0.1:8080/health`
- WebSocket 对战连接：`ws://127.0.0.1:8080/game`

如果已经生成 WebGL 客户端，可以让同一服务同时托管游戏：

```bash
WEB_ROOT="$PWD/client-restored/Build/WebGL" PORT=8080 pnpm dev
```

随后访问 `http://127.0.0.1:8080/`。

## 构建 WebGL 客户端

1. 使用 Unity Hub 打开 `client-restored/`。
2. 如需重新生成恢复地图，执行菜单
   `Genesis > Create Playable Recovered Maps`。
3. 执行 `Genesis > Build Restored WebGL`。
4. 构建结果位于 `client-restored/Build/WebGL/`。

也可以使用命令行构建：

```bash
"/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -quit \
  -projectPath "$PWD/client-restored" \
  -executeMethod GenesisRestoredBuild.BuildRestoredWebGL \
  -logFile build-webgl.log
```

进入游戏后，按“自由模式 → 任一自由频道 → 训练模式”进入金字塔。
先点击 WebGL 画面以锁定鼠标，再使用以下操作：

- `WASD` 移动，鼠标控制视角，空格跳跃。
- 鼠标左键攻击，`1` 切换手枪，`3` 切换匕首。
- `R` 更换弹匣，`Esc` 返回大厅。

## Docker 部署

先完成 WebGL 构建，再修改 `deploy/compose.yaml` 中的
`ALLOWED_ORIGINS` 为正式 HTTPS 域名：

```bash
docker compose -f deploy/compose.yaml up -d --build
```

容器为 64 位 Node.js 运行环境，同时提供 WebGL 静态文件、`/health` 和 `/game`。
正式环境建议由 Nginx 或云负载均衡提供 HTTPS/WSS 和域名入口。

## 测试

```bash
pnpm test
pnpm build
```

当前服务端测试覆盖协议校验、房间状态、移动、射击、伤害和复活，共 6 项。

## 项目说明

这是面向游戏保存、技术研究和非商业恢复的社区项目，与原开发商及 4399
没有隶属或授权关系。原游戏名称、图像、音频、模型和其他内容的权利归各自权利人所有。
如权利人认为仓库中的内容需要调整或移除，请通过 GitHub Issue 联系维护者。
