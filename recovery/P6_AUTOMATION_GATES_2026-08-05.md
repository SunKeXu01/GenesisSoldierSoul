# P6 自动化门禁复验

日期：2026-08-05

## 结论

第 20 节中可由源码与自动化验证的测试、运行时资源、地图、服务端和安全提取门禁均已复验。正式资源发布门禁仍保持关闭：它不是测试失败，而是许可与恢复源 GUID 证据尚未闭合时的预期 fail-closed 行为。

## 自动测试

- Unity EditMode：65/65 Passed，`p6-editmode-2026-08-05.xml`。
- Unity PlayMode：6/6 Passed，`p6-playmode-2026-08-05.xml`。
- Python 工具链：86/86 Passed，使用工作区捆绑 Python 与 Pillow；新增 LTC、RF019/RF199 多格式连续帧，以及 LTB layout-v7 direct BoneSet、matrix palette/重索引骨骼、inverse bind、骨骼 TRS、vertex morph/weights 与法线规范化回归。
- 服务端：Vitest 30/30 Passed，TypeScript 构建通过（本轮前一阶段已复验）。

## 资源、骨骼与锚点

`GenesisFinalRegressionAudit.Run` 通过 8/8 综合门禁，报告为 `final-automated-regression.json`，日志为 `p6-final-regression-2026-08-05.log`：

- 正式武器资源与贴图材质覆盖通过。
- 第三人称九段方向移动、跳跃/落地、战斗骨骼与五类武器道具通过。
- 步枪、霰弹枪、手枪主辅手接触和躯干净距通过；刀与手雷单手挂点通过。
- 七地图严格就绪、统一验收矩阵、发布场景与音频清单通过。
- 第三人称动作结束、死亡/复活复位及远端表现语义通过。
- 创建房间地图选择条与原版确认按钮布局、点击射线互不阻挡。

## 正式资源闭包阻断

刷新 `resource-closure-audit.json` 后，7 个正式运行时资源组均为 D 级，`formalBuildAllowed=false`。Unity 的 `GenesisResourceClosureGate.EnsureFormalBuildAllowed` 随后按预期抛出阻断异常，日志为 `p6-formal-resource-gate-2026-08-05.log`。

阻断原因：

- `resource-closure-policy.json` 尚无任何批准许可记录，因此 A 级计数为 0/7。
- 2026-08-06 复核确认原先 19 个缺失 GUID 全部来自 `LightingData.asset.m_Scene` 所属场景反向边被误作前向运行时依赖；审计器现显式记录并排除该反向边，七个运行时资源组前向 GUID 闭包均为 0 缺失。正式门禁仍因 7 个运行时组缺少批准许可记录而拒绝放行，未借技术修复绕过权利门禁。详见 `GUID_CLOSURE_CORRECTION_2026-08-06.md`。

在权利记录经人工批准并补齐或正式替换这些恢复源依赖前，只允许 `GENESIS_DIAGNOSTIC=1` 的诊断构建，正式 WebGL 构建继续拒绝发布。

## 安全提取回归

`tools/test_extract_recovered_archives.py` 现明确覆盖：

- 绝对路径、盘符路径与父目录穿越拒绝。
- 物化路径必须位于 `Assets`。
- 解压树符号链接拒绝。
- Unity 分片缺口拒绝。
- 完整分片重复物化保持哈希与内容不变。
- UnityPackage 重复物化复用已验证清单并保持输出不变。

## 第 21 节固定构建

当前源码的诊断 WebGL 完整构建成功，输出目录为 `client-restored/Build/WebGL`，构建日志为 `p6-final-webgl-build-2026-08-05.log`，总大小约 344 MiB。主产物与完整文件树摘要记录在 `p6-final-webgl-build-2026-08-05.sha256`；后续七地图与双浏览器验收必须复用这份产物，不得中途重建或混用旧文件。
