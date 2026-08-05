# HUD 与大厅闭环审计（2026-08-04）

## 已验证结果

- 雷达保留恢复底图并叠加可读轮廓、本地玩家方向和实时目标点。
- 当前对战规则为自由混战，没有服务器队伍字段；因此本地玩家为青色，所有存活远端玩家及训练机器人按红色敌方显示，不伪造队友关系。
- 雷达按 45 米世界范围映射到 94 像素半径，超出范围的目标钳制在边缘。
- 建房界面可循环选择并持久化七张地图；POST 建房请求使用所选地图，不再写死 `pyramid`。
- 仓库为 M4A1、M16、Shotgun01 建立隔离图层和 384×384 RenderTexture 预览；模型自动按包围盒归一化并缓慢旋转，装备切换会立即刷新真实 Prefab，预览灯光不会影响大厅主相机。
- 地图 ID、场景名、非法值回退及雷达坐标由 `GenesisLobbyRadarAudit` 自动验证。
- HUD 使用 1280×720 参考画布、宽高各 50% 匹配；800×600、1024×768、1280×720、1920×1080、2560×1080、3440×1440、3840×2160 的八个主要面板边界均通过自动验收。
- `Loading` 和 `Ziyou1` 共 98 个恢复按钮已检查；两个空目标持久化回调已移除，复验 `invalidEvents=0`。
- 删除五个旧场景脚本按通用名称重复查找按钮的 `Start()` 绑定，保留场景内有效的序列化回调与显式运行时接线。

## 验收命令

```text
Unity -batchmode -quit -projectPath client-restored \
  -executeMethod GenesisLobbyRadarAudit.ValidateLobbyAndRadar

Unity -batchmode -quit -projectPath client-restored \
  -executeMethod GenesisLobbyUiAudit.AuditLobbyButtons
```

结果：脚本编译成功；七图映射、雷达数值、七种视口和三件仓库预览资源通过；按钮审计 98 个、坏回调 0 个。

## 保留缺口

- 设置、商城及其他大厅功能仍严格按资源和逻辑证据逐项恢复。
