# 新工地逐图恢复验收（2026-08-04）

## 已通过

- 35 个 Renderer、31 个 Collider、34 个环境网格、1,236 三角形。
- 材质槽、Shader、法线、UV 缺失均为 0；最大贴图 1024。
- 出生点从 `fangzi (3)/Cube` 屋顶小孤岛迁至主地面，位置
  `(318.33, 392.08, -548.67)`。
- 出生点有碰撞承托、无头顶遮挡，最小/平均水平净空为 2.23/5.46 米。
- 可达样本 112，跨度 18.67×15.33 米，抽样可达率 87.5%，无可达无承托
  NavMesh 顶点。
- 原全黑天空已替换为 WebGL 支持的程序化天空；新增暖色方向光、Trilight
  环境光和线性雾。七图强门禁复验仍为 7/7。
- 新增项目自产的确定性程序化工地风声，不引入外部音频版权；运行时生成 8 秒、
  22.05 kHz 单声道循环。PCM 审计 RMS 为 0.058390，循环接缝差为 0.000091，
  AudioSource 无空引用、禁用自动播放并由运行时组件在场景激活后启动。
- 诊断 WebGL 全量构建成功，产物 349 MB；新运行时代码通过 WebGL 后端编译。

## 证据

- `recovery/new-construction-spawn-preview.png`
- `recovery/new-construction-nav-diagnosis.log`
- `recovery/map-recovery-readiness.json`
- `recovery/map-recovery-audit-newsite-visual.log`
- `recovery/new-construction-ambient-audit.txt`
- `recovery/new-construction-ambient-audit-final.log`
- `recovery/webgl-newsite-ambient-build.log`

## 结论

新工地已满足当前逐图统一验收条件，主清单整项可以关闭。浏览器控制运行时在本轮
环境中不可用，因此没有把“自动化浏览器听音”作为已完成证据；最终七图浏览器实机
回归仍保留在 P6 全量验收项中。
