# 辐射街区逐图恢复验收（2026-08-04）

## 结论

辐射街区已通过当前逐图统一验收。

## 证据

- 24 个 Renderer、24 个 Collider、24 个环境网格、3,444 三角形；材质槽、
  Shader、法线和 UV 缺失均为 0，最大贴图 1024。
- 出生点迁至主开放区 `(-157.06, 12.04, -34.78)`；有碰撞承托、无头顶
  遮挡，最终审计最小水平净空 6.35 米。
- 可达样本由 4 增至 58，跨度由 30.00×14.67 增至 161.50×188.17 米；
  无可达无承托 NavMesh 顶点。
- 修复全黑天空和过低曝光，配置冷色程序化天空、方向光、Trilight 与远距线性雾。
- 新增自产 `IndustrialHum` 环境循环；8 秒、22.05 kHz，RMS 0.038937，
  接缝差 0.000169，AudioSource 配置与 PCM 审计通过。
- 最终出生预览不再被飞行器模型占满；可见工业建筑、道路和集装箱通路。
- 当前源码 WebGL 构建在发布前 7/7 强门禁后成功，产物 360,069,274 字节。

## 产物

- `recovery/radiation-district-spawn-preview.png`
- `recovery/radiation-district-ambient-audit.txt`
- `recovery/map-recovery-audit-ice-fire-final.log`
- `recovery/webgl-p4-final-spawns.log`
