# M9 第一人称正式转正记录

日期：2026-08-04

## 实现

- 正式手枪槽优先加载 `OriginalGame/FirstPerson/M9Viewmodel`。
- M9 复用原 `Pistol01` 动画骨架、双手网格、10 个动作片段和 `FireLocator`，仅替换枪体为恢复的 M9 模型与贴图材质。
- 新增 `GenesisViewmodelKind.M9` 独立配置；M9 与 `Pistol01` 回退可分别调节位置、FOV 和缩放。
- 若 M9 资源未进入构建，运行时仍会自动回退至完整的 `Pistol01`，不会生成空手枪槽。

## 自动化证据

- `recovered-weapon-validation.json`：M9 视模加载成功，17/17 材质无缺失、纹理覆盖率 100%，10 个动画片段以及 `FireLocator`、`Recovered_M9_Candidate`、`RightHand` 锚点齐全。
- `m9-promotion-editmode.xml`：14/14 EditMode 测试通过，新增 M9 独立视模配置断言。
- `m9-promotion-playmode.xml`：3/3 PlayMode 测试通过，正式控制器与真实武器 Prefab 的换弹中断、手雷回切和断线重连序列保持正常。
- `webgl-m9-promotion-build.log`：完整 WebGL 构建成功，输出 299,862,077 bytes。

## WebGL 实机序列

1. 从纪念启动页依次进入大厅、自由模式、频道和训练模式。
2. 切换到手枪槽，确认加载的是深色 M9 枪体；大小、方向、贴图亮度和双手握持正常。
3. 单发后弹药由 12/48 变为 11/48；枪管视觉方向与屏幕中心射线一致，开火动作结束后恢复待机。
4. 再射击两发并换弹，HUD 显示 `RELOADING`；完成后由 10/48 变为 12/46。
5. M4A1 → M9 往返切换后，M9 完成 Wield 并回到稳定待机，没有旧模型残留。
6. 训练 Bot 击杀本地玩家，击杀信息显示 `TARGET-04 > YOU`；复活后 M9、HUD 和弹药 12/46 正确恢复。

浏览器日志没有 Unity 运行时错误；仅有 Unity WebGL 对手动持久化同步接口的弃用提示，以及浏览器自动化层的 Chromium 噪声。
