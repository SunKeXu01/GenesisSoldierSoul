# 创世兵魂恢复工程

本项目尝试使用已经保存下来的《创世兵魂》Unity、Flash 缓存和游戏配置资源，
恢复一个可以通过现代浏览器运行、并逐步支持玩家联网对战的版本。

项目坚持复用现有游戏资源和恢复出的程序结构，不使用另外制作的仿制大厅或灰盒地图
替代原游戏内容。目前可从原版风格大厅进入自由模式、频道和训练模式，并在 WebGL 中
运行恢复出的联网对战地图。

## 当前进度

- 已恢复 33 个原客户端场景和大厅 UI。
- 已整理 7 组地图资源，并全部生成 WebGL 对战候选场景。
- 已接通大厅中的“训练模式”按钮与恢复地图轮换。
- 训练模式会在开放方向的可见地面扇区部署四个目标，保持至少 4 米初始间距；
  开局提供 7 秒保护、复活提供 4.5 秒保护，并把视角重新朝向目标区域，避免刚进入
  或刚复活就被地图遮挡后的机器人集火。最终运行截图见
  `recovery/recovered-training-bot-separation-final-runtime.png`。
- 已恢复第一人称控制器、HUD、雷达、计分板、生命值和近战武器模型。
- 已把恢复出的 Humanoid 待机、前进、后退、左右横移、跑步和跳跃片段接入
  二维方向混合树；训练机器人与联网角色会按实际移动方向播放动作，不再统一套用前进摆腿。
- 第一人称移动已恢复步行、冲刺、跳跃、重力、落地回弹和相机步态；武器切换会播放
  收枪/举枪过渡，而不是瞬间替换模型。
- 第一人称武器会读取真实移动方向：前后移动改变枪身纵深，左右侧移带有手臂倾斜，
  奔跑会平滑压低武器并将视野扩大最多 3 度；空中衰减步态摆动，落地叠加短促回弹。
  运行时截图见 `recovery/recovered-directional-sprint-runtime.png` 和
  `recovery/recovered-directional-strafe-runtime.png`。
- 七张地图共用恢复出的手枪模型，以及从根目录 M4A1 UnityPackage 恢复的
  四平面 WarFX 枪口火焰；M4A1、M16 模型和对应原版开火、换弹、持枪音效
  也已进入统一资源流水线，损坏的旧枪口火焰预制体不再于每次开火报告缺失脚本。
- M4A1 样板已生成独立 `M4A1Viewmodel.prefab`：保留恢复客户端的双手骨骼、
  `WeaponMainLocator` 和 7 段射击/换弹/待机动作，枪体替换为根目录
  UnityPackage 的 Sopmod FBX、5 张贴图和 4 个原材质。发布构建会自动执行武器
  Prefab 与角色动作审计；M4A1 近景预览见
  `recovery/recovered-m4a1-viewmodel-preview.png`。
- Pyramid 已恢复原工程的六面蓝天白云天空盒与三色环境光；相机启动时会自动替换
  遗留的不兼容天空 Shader。右上角击杀信息支持保留最近四条记录。
- 已实现 M4A1/M16/Shotgun01 主武器装备选择、手枪、匕首和手雷切换，独立弹匣、
  换弹、命中、死亡、3 秒复活和 3 分钟回合。手雷复用恢复工程的完整双手模型与
  `Throw` 动画，按恢复源码参数以 10 的投掷力、2.5 的上抛力和 3 秒引信运行；
  爆炸采用服务端权威半径伤害，每条命限一颗并在重生后补充。
- Shotgun01 使用恢复工程中的专用开火/泵动声，以及根目录原版声音池中的装备和装填声，
  不再错误播放 M4A1 音效。
- 战斗与移动音频统一等待 `AudioClip` 完成加载再播放，WebGL 首次开火、切枪、跳跃和
  脚步不会再直接读取尚未加载的声音长度。
- 已把原大厅“仓库”面板接入可持久化装备配置，可在 M4A1、M16 与恢复的 Shotgun01 间选择，
  并从仓库直接携带当前装备进入自由模式。
- 金字塔、新工地、生化小镇、经典工地、炼钢厂、冰与火之歌和辐射街区均已通过
  材质、碰撞、出生点、导航、边界和发布场景门禁，七图全部进入当前轮换。
- 已增加随机玩家编号、在线人数、TAB 战绩榜、击杀提示、复活倒计时和
  WebSocket 断线自动重连。
- 联网位置纠偏通过 `CharacterController` 执行碰撞扫描，并修正恢复角色的
  胶囊半径与皮肤宽度，持续贴墙移动不会再穿过地图模型。
- 客户端会把经过地图碰撞处理的水平位置同步给服务器；服务端按移动速度上限
  校验后用于远端角色与命中盒，避免其他玩家看到角色穿墙，同时拦截异常瞬移。
- 已修复 WebGL 场景切换时的 `abort()`、重复按钮监听和加载画面空引用。
- 发布构建会清理可玩场景副本中的失效脚本引用，并在构建前检查正式轮换地图的
  玩家、相机、HUD、渲染器、碰撞覆盖、场景边界和出生点。
- 已实现 Linux 64 位 Node.js WebSocket 对战服务原型。
- 服务端以 20Hz 同步移动、射击、伤害、死亡和复活状态。
- 已把恢复大厅的房间列表、创建房间和加入房间按钮接入真实 HTTP/WS 服务，
  支持房间人数、12 人上限、地图会话隔离和双浏览器客户端同房对战。
- 大厅设置已接通音量、鼠标灵敏度、画质和帧率；商城入口当前提供只读资源目录，
  明确区分正式配发物与锁定候选，不伪造价格、余额、所有权、购买或充值协议。
- 固定 WebGL 构建已完成七地图 × M4A1/M16/Shotgun01 的 21/21 浏览器实机矩阵，
  并逐图验证手枪、匕首、手雷、移动/跳跃、HUD 与 Tab 战绩板。双浏览器链已验证
  同房移动、切枪、射击、权威伤害、死亡、复活、回合结束和跨图。验收报告见
  `recovery/P21_SEVEN_MAP_WEAPON_MATRIX_2026-08-05.md` 与
  `recovery/DUAL_BROWSER_NETWORK_REGRESSION_2026-08-05.md`。
- 已提供 Docker 和 Nginx 部署基础配置。

账号、交易、任务、活动、战队等缺少权威服务协议的业务功能不会伪造；更多候选武器
仍需通过依赖闭包、动作、许可与安全门禁后才能进入正式武器栏。

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
- HTTP 房间列表/创建：`http://127.0.0.1:8080/api/rooms`
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

也可以使用命令行生成恢复研究构建：

```bash
GENESIS_DIAGNOSTIC=1 \
"/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -quit \
  -projectPath "$PWD/client-restored" \
  -executeMethod GenesisRestoredBuild.BuildRestoredWebGL \
  -logFile build-webgl.log
```

当前仓库没有覆盖恢复素材的已批准权利记录，因此正式资源门禁会按设计拒绝非诊断构建。
`GENESIS_DIAGNOSTIC=1` 只用于非商业恢复研究和本地验收，不代表取得发布许可。门禁详情见
`recovery/RESOURCE_CLOSURE_AUDIT.md`。

进入游戏后，可以先在主大厅“仓库”选择 M4A1、M16 或 Shotgun01，再按
“进入自由模式 → 任一自由频道”。房间界面支持加入玩家房间、使用恢复的
“创建房间”界面建房，或按“训练模式”直接进入联网自由竞技。
当前轮换依次为金字塔、新工地、生化小镇、经典工地、炼钢厂、冰与火之歌和
辐射街区；每局结束后返回房间流程，可继续选择并创建下一张地图。
先点击 WebGL 画面使其获得键鼠焦点，再使用以下操作。WebGL 版本使用
Canvas 相对鼠标增量，不请求 Pointer Lock；桌面编辑器仍使用锁定鼠标模式。

- `WASD` 移动，鼠标控制视角，空格跳跃。
- 鼠标左键攻击，`1` 切换当前装备的主武器，`2` 切换手枪，`3` 切换匕首，
  `4` 切换手雷，按住 `Tab` 查看战绩。手雷投出后会自动切回主武器。
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

当前服务端 30 项测试覆盖协议校验、房间创建与人数、七地图规范化与会话隔离、
移动、碰撞位置同步、防瞬移、射速/视角/动作序列、权威弹药与伤害、出生保护、
死亡复活、回合截止，以及装备、开火、换弹、近战和投掷动作广播。当前 Unity
门禁为 EditMode 65/65、PlayMode 6/6。

Unity 菜单 `Genesis > Validate Playable Recovered Maps` 会逐张检查七个对战场景的
第一人称角色、相机、HUD、原资源渲染器和碰撞体，缺失任意关键项都会使验证失败。

### 可重复交付门禁

仓库固定 Node `24.16.0`、pnpm `11.9.0`、Python `3.12.13` 和 Unity
`2022.3.62f3c1`。先准备 `.node-version`/`.python-version` 对应的运行时，再执行：

```bash
tools/verify_delivery.sh --quick
tools/verify_delivery.sh --full
```

快速模式执行锁文件安装、TypeScript 构建、Vitest 和 Python 安全工具测试；完整模式
另执行 Unity EditMode、PlayMode、9 项综合门禁、诊断 WebGL 构建和完整文件树 SHA-256
清单。非标准安装位置可通过 `PYTHON_BIN`、`UNITY_EDITOR` 和
`DELIVERY_OUTPUT_DIR` 显式覆盖。输出默认写入忽略的
`artifacts/delivery-verification/`，不会混入正式恢复证据。

## 已知限制

- 恢复资源目前全部因缺少已批准权利记录而归为 D 级；A 级资源数为 0，正式发布构建
  会 fail-closed。技术验收通过不等同于获得内容发布许可。
- 七个运行时资源组的静态前向 GUID 闭包当前均为 0 缺失；审计器会显式记录并忽略
  `LightingData.asset` 的 `m_Scene` 所属场景反向边，避免从可玩场景错误回溯到旧源场景。
  正式资源门禁仍因缺少已批准权利记录而拒绝放行。
- 364 个 CrossFire LTC 已用固定 XOR 包装 + LithTech LZSS 规则全部恢复为 LTA，
  并由同名明文样本逐字节验证；仍未完成的是 7 个未分帧私有 REZ、654 个 LTB
  结构变体/骨骼动画、Flash ATF 标准媒体转换和受 Oodle 限制的 Unreal 内容。
  工具只做静态读取，不执行来源不明程序。
- 商城仅为只读资源目录；账号、余额、购买、充值、VIP、任务、活动和战队业务没有
  权威后端依据，因此不提供伪实现。
- AK-74M、AWP 等候选资源默认关闭；未同时满足第一人称动作闭包、许可和安全门禁前，
  不进入正式武器栏。
- 长时间 Chromium 自动化会偶发浏览器层 `UnknownError`；当前验收已单独分类，未发现
  Unity 异常、资源 404、Shader 丢失或 WebSocket 协议错误。
- Unity WebGL 的 `.data` 与带时间戳缓存参数的 `index.html` 不保证跨构建字节一致；
  每次完整门禁都会重新生成全树哈希，不能用旧构建哈希替代当前产物验证。

## 项目说明

这是面向游戏保存、技术研究和非商业恢复的社区项目，与原开发商及 4399
没有隶属或授权关系。原游戏名称、图像、音频、模型和其他内容的权利归各自权利人所有。
如权利人认为仓库中的内容需要调整或移除，请通过 GitHub Issue 联系维护者。
