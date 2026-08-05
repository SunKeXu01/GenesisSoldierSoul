# 生化小镇逐图恢复验收（2026-08-04）

## 结论

生化小镇已通过当前逐图统一验收，可进入最终七图浏览器实机回归。

## 修复与证据

- 出生点从贴近大墙的旧位置迁至开放广场 `(39.73, 2.89, 34.13)`；地面承托
  距离 3.20 米、上方无遮挡，八方向最小/平均净空 9.96/11.59 米。
- 出生区可达样本 39、跨度 93.40×92.80 米；无可达无承托 NavMesh 顶点。
- 176 个环境网格、178 个 Collider、3,851,527 三角形；材质槽、Shader、法线、
  UV 缺失均为 0，最大贴图 512。
- 替换渲染为黑色的内置默认天空，配置程序化暮色天空、暖色方向光、Trilight
  环境光与线性雾；修复后出生画面可见开放广场和完整建筑轮廓。
- 新增自产 `TownNight` 程序化环境声。PCM 为 8 秒、22.05 kHz，RMS
  0.016648，循环接缝差 0.000009；无空引用，循环与非空间化配置通过。
- 七图严格门禁保持 7/7；当前源码诊断 WebGL 全量构建成功，产物
  366,118,501 字节。

## 产物

- `recovery/biochemical-town-spawn-preview.png`
- `recovery/biochemical-town-ambient-audit.txt`
- `recovery/map-recovery-readiness.json`
- `recovery/map-recovery-audit-biochemical.log`
- `recovery/webgl-biochemical-atmosphere-build.log`

浏览器控制运行时本轮不可用，因此最终双浏览器和控制台回归仍归 P6 全量验收，
未在本报告中提前宣称完成。
