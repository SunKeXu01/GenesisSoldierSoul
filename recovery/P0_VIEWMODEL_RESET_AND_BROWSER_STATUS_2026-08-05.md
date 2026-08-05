# P0 视模重置与浏览器验收状态（2026-08-05）

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

结果：Unity EditMode `66/66`、PlayMode `8/8`、最终自动门禁 `9/9`、完整 WebGL 构建通过。日志和 XML 位于 `artifacts/delivery-verification/`，最终报告为 `recovery/final-automated-regression.json`。

## 浏览器实机状态

本轮未把浏览器实机两项标记为完成：Codex in-app Browser 对 `http://127.0.0.1:8080/` 和实际局域网地址的访问均在导航层被私网策略拦截；当前也没有可连接的 Chrome 扩展实例。服务器、WebGL 构建和浏览器入口本身已由完整交付门禁验证，但这不能替代可见实机交互。

待浏览器连接可用后，需在同一构建上补做并留图：死亡、复活、回合结束、断线、重连，以及各节点的唯一模型、Idle 恢复、无瞬跳、无残留、无旧动画覆盖。完成前清单保持未勾选。

本轮 WebGL 文件树清单 SHA-256：`f16c1e64aed03d2fe8099ba31f13db18b154725904b6a8dca95294811eba0250`；该值对应 `artifacts/delivery-verification/webgl-tree.sha256` 文件内容，构建重跑后必须重新生成，不能当作长期固定产物哈希。
