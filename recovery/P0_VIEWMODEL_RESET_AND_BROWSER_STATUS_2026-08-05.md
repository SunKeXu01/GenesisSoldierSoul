# P0 视模重置与浏览器验收状态（2026-08-06 更新）

## 已实现的重置行为

- 断线立即使动作令牌失效、停止换弹/切枪协程、清除切换与投掷状态，并隐藏全部第一人称模型。
- 死亡与回合结束停止步枪、手枪、刀、手雷的旧 `Animation`，将 `Idle01.time` 归零，再隐藏所有视模。
- 复活与重连通过当前 `selectedWeapon` 重新应用正式位姿，只激活一个正确模型并从 `Idle01` 恢复。
- 所有逻辑仍复用 `GenesisWeaponActionTracker` 的 revision 失效机制，旧延迟回调不能覆盖新状态。

## 自动验证

PlayMode 使用真实资源加载路径与 Prefab，覆盖：

1. 换弹中快速连续切枪；
2. 投雷结束回到此前真实视模；
3. 手雷归一化后仍在武器相机内；
4. 断线中断换弹并隐藏全部视模，重连恢复控制及唯一正确视模；
5. 死亡停止旧 Reload、隐藏全部视模，复活只恢复手枪及 Idle，回合结束再次清空；
6. 四类视模的 Viewmodel/Animation/Arms/Weapon/Effects 角色分层。

结果：Unity EditMode `66/66`、PlayMode `9/9`、完整 WebGL 构建通过。新增的 Shotgun01 用例不只检查 `activeSelf`，还校验启用的渲染器中心位于武器相机裁剪面内，且水平、垂直屏幕占比都大于 `0.08`；该断言在初始生成和断线/重连后各执行一次。

## 浏览器实机结果

2026-08-06 已在 in-app Browser 中直接运行 WebGL 验收构建，该构建文件树 SHA-256 为 `e763099922ce515ad8a7172988e26da7538af2e16fb5f4802937324f2f27e9c4`。同一源码在全部门禁中重建后的最终文件树 SHA-256 为 `aa6957cbf1cac1a508fc8bf7a89e7e0784a8929f46445b469700373e9a0fea03`；构建中包含生成时信息，因此重建哈希不会与浏览器验收构建相同。可见验收结果：

1. Shotgun01 初始生成时武器和双手在右下角可见，不再处于武器相机远裁剪面外；
2. 死亡画面显示 `YOU DIED / RESPAWN 1s`、HP 0 且视模隐藏，复活后 HP 恢复且 Shotgun01 重新可见；
3. `ROUND OVER` 节点视模隐藏，随后正常返回房间列表；
4. 服务器中断后立即离开战斗画面，显示 `ROOM SERVER OFFLINE`，不残留视模；服务器恢复后房间列表重连，再次进入房间时只恢复当前 Shotgun01 视模。

本地留图位于 `recovery/viewmodel-reset-browser-shotgun-final-{alive,death,respawn}-2026-08-06.png`、`recovery/viewmodel-reset-browser-shotgun-round-over-2026-08-06.png`、`recovery/viewmodel-reset-browser-{disconnected,reconnected-lobby}-2026-08-06.png` 和 `recovery/viewmodel-reset-browser-shotgun-reconnected-match-2026-08-06.png`。图片按仓库策略保持在 Git 忽略列表内，不上传大型临时证据。

浏览器自动化运输层曾输出 Statsig 超时，手动中断 WebSocket 时 Unity Development Console 也按预期记录 `websocket error`；它们不是视模逻辑异常。
