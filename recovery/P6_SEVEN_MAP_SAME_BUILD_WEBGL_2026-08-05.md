# P6 七地图同构建 WebGL 验收

日期：2026-08-05

## 结果

同一份修复后诊断 WebGL 构建连续创建房间并联网进入七张地图，7/7 成功。每张地图都显示正确地图标识、`ONLINE 1`、比分/计时、雷达、生命/护甲、准星、武器栏和第一人称武器。

固定构建：

- 输出：`client-restored/Build/WebGL`
- 大小：360,465,275 字节
- 完整文件树摘要：`7dae596432c1c5fdf5a53e767999c45dbab93b0289779d84f80508e7e58adb64`
- 主产物哈希：`p6-final-webgl-build-2026-08-05.sha256`
- 构建日志：`p6-final-webgl-build-2026-08-05.log`

## 本轮修复

首次创建房间实测发现 `GenesisMapSelector` 位于确认按钮上方并拦截点击。现已：

- 将地图选择条从 `y=-112` 下移至 `y=-175`。
- 禁止地图名称文本拦截射线。
- 新增 `GenesisLobbyUiAudit.AuditCreateRoomMapSelector`，验证选择条与确认按钮不重叠、仍在 Canvas 内、文本不拦截点击。
- 将三处 Unity 2022 已弃用的 `Arial.ttf` 内置字体名改为 `LegacyRuntime.ttf`。

专项布局门禁通过，最新 EditMode 65/65、PlayMode 6/6、综合门禁 8/8 通过后重新生成固定构建。

## 七地图证据

截图目录：`recovery/webgl-final-evidence`

1. `01-pyramid-online-spawn.png`
2. `02-new-construction-online-spawn.png`
3. `03-biochemical-town-online-spawn.png`
4. `04-classic-construction-online-spawn.png`
5. `05-steel-factory-online-spawn.png`
6. `06-ice-fire-maze-online-spawn.png`
7. `07-radiation-district-online-spawn.png`

每次从房间列表打开创建房间界面，仅向右切换一张地图，使用原版确认按钮通过同源 `/api/rooms` 创建房间，然后等待对应场景与 WebSocket 会话就绪；返回房间列表后继续下一张，期间未重建或替换 WebGL 文件。

## 控制台闭环复测

`browser-warnings-errors.json` 保存本轮浏览器警告/错误，共 14 条 warning、6 条 error。房间 API 404 已通过同源托管消除，七图均成功联网；但仍有：

- 恢复场景旧 `Shenghua / Tianti / Fangjian` 脚本的目标对象未找到警告。
- 金字塔两个负缩放 `BoxCollider` 警告。
- 浏览器报告的 Chromium `UnknownError`。

后续修复并复测：

- `Shenghua / Tianti / Fangjian` 将缺失的可选旧按钮视为正常场景配置，不再输出误导性警告。
- 地图清洗器把负世界缩放下的 `BoxCollider` 等价迁移到正缩放根级代理，并新增七图自动审计；首次修复 2 个，第二次清洗 0 个，证明幂等。
- WebGL 模板启用 `autoSyncPersistentDataPath`，消除 Unity 持久化目录弃用警告。
- 单标签短流程从启动、主大厅、自由一频道、创建房间到金字塔 `ONLINE 1` 复测为 **0 warning / 0 error**；旧按钮、负缩放、资源 404、Shader 和 WebSocket 问题均未出现。
- 后续更长的双标签跨图与战斗会话仍捕获 Chromium 自报的 `UnknownError`，两端包含完全相同时间戳，且在移除 Pointer Lock 后仍可复现。该项归为浏览器自动化层错误；应用侧仍无 Unity、资源 404、Shader 或 WebSocket 协议错误。原始记录见 `webgl-final-dual-evidence/latest-browser-problems.json`。

单标签短流程零告警证据：`browser-zero-warning-errors-final-2026-08-05.json`；较长双端会话分类证据：`webgl-final-dual-evidence/latest-browser-problems.json`。

最新清理构建：

- 输出大小：360,381,472 字节（Unity 报告 360,381,430 字节）。
- 完整文件树摘要：`5e73130f909fe3dbc57070d114a2b8920cc24b0bd1d16c683d32b3e68f56abd9`。
- 构建哈希：`p6-final-webgl-build-2026-08-05.sha256`。
- 构建日志：`p6-console-zero-warning-webgl-build-2026-08-05.log`。
- 最终综合门禁 9/9、EditMode 65/65、PlayMode 6/6 通过。

最新双浏览器跨图、移动、切枪、射击、受击、死亡与复活已经完成；逐图全部正式武器动作、音效、命中与关键动作截图仍待继续。
