# 经典工地逐图恢复验收（2026-08-04）

## 结论

经典工地已通过当前逐图统一验收。发布前清理流程的金字塔出生点回退问题也在本轮
被发现并修复，强地图门禁现直接接入 WebGL 构建入口。

## 地图证据

- 318 个 Renderer、205 个 Collider、302 个环境网格、124,010 三角形。
- 材质槽、Shader、法线、UV 缺失均为 0；最大贴图 2048。
- 出生点有碰撞承托、无头顶遮挡，最小/平均水平净空 8.58/11.15 米。
- 可达样本 73、跨度 141.17×165.67 米；无可达无承托顶点。
- 保留原有 20 盏灯与开场语音，替换渲染为黑色的旧天空，新增 WebGL 支持的
  程序化天空、Trilight 环境光与线性雾。
- 新增自产 `ConstructionWind` 环境循环；8 秒、22.05 kHz，RMS 0.058390，
  接缝差 0.000091，循环/非自动播放/非空间化配置通过。

## 发布门禁修复

- 删除 `SanitizePlayableRecoveredMaps` 中将金字塔强制写回历史坐标的旧逻辑。
- `BuildRestoredWebGL` 现在在清理后调用 `EnsureMapRecoveryReadiness`；任一地图
  碰撞、出生、导航、边界或视觉强门禁失败都会直接阻止构建。
- 独立执行清理与门禁保持 7/7；完整诊断 WebGL 构建日志同时记录
  `Build gate passed 7/7` 和构建成功，产物 365,994,291 字节。

## 产物

- `recovery/classic-construction-spawn-preview.png`
- `recovery/classic-construction-ambient-audit.txt`
- `recovery/map-recovery-audit-classic.log`
- `recovery/build-sanitizer-idempotence.log`
- `recovery/build-map-gate-idempotence.log`
- `recovery/webgl-classic-build-gate.log`
