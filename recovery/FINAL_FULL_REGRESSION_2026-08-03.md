# 最终全量回归进度（2026-08-03）

## 已通过的自动门禁

- Unity EditMode 动作状态测试 4/4：`recovery/final-full-regression-editmode.xml`。
- 最终自动化聚合门禁 6/6：`recovery/final-automated-regression.json`。
  - 正式武器资源、锚点、动作和贴图覆盖。
  - 第三人称方向移动、跳跃、战斗骨骼和正式道具。
  - 七地图严格恢复审计 7/7。
  - 七地图 WebGL 发布门禁。
  - 七地图可玩音频清单。
  - 第三人称动作结束、死亡和复活复位。
- 正式运行时武器 6/6 通过；M16 贴图材质覆盖仅 2.5%，自动排除：`recovery/recovered-weapon-validation.json`。
- 服务器测试 21/21、TypeScript 构建通过。
- 安全资源测试 5/5，18 个 Unity 分片重组幂等通过。
- 最终 WebGL 完整构建成功，产物 299,757,197 字节：`recovery/final-full-regression-webgl-build.log`。

## 最终 WebGL 双客户端实机冒烟

- 两个客户端同时进入 `RadiationDistrict`，双方 HUD 均显示 `ONLINE 2`。
- M4A1、雷达、准星、阵营比分、生命/护甲、弹药和四槽武器栏正常显示。
- 当前正式副武器 `Pistol01` 可见，连续 5 次开火后弹药从 12 降到 7，动作结束后姿态稳定。
- 手枪换弹开始后立即切回 M4A1，等待超过原换弹时长后仍保持 M4A1，没有旧动作覆盖。
- M4A1、手枪、刀和手雷快速交叉切换后，最终手雷持有姿态正常。
- 手雷投掷后自动回切 M4A1；爆炸伤害将生命从 100 降到 52，HUD 即时同步。
- 双端运行日志未出现 Unity 异常；只保留旧恢复按钮目标缺失警告、Unity WebGL 持久化 API 弃用警告和浏览器自动化层 `UnknownError`。

## 七地图最终浏览器扫图准备

新增 `server/tools/create-map-qa-rooms.mjs`，通过服务器正式房间 API 幂等创建 7 张地图的 QA 房间。当前房间 ID 与地图映射见 `recovery/final-map-qa-rooms.json`。

## 尚待最后确认

自动审计已经覆盖 7 图，且各图此前均有独立恢复/浏览器证据；为了让“最终全量验收”达到同一构建、同一服务器会话下的强证据标准，还需在最终构建中依次进入新建的 7 个 QA 房间，各取一次出生画面并确认无 Unity 异常。完成该扫图后再结束总目标。
