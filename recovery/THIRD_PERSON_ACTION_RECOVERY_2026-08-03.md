# 第三人称人物动作恢复（2026-08-03）

## 本轮接入

- 保留已恢复的待机、前后走跑、左右横移和跳跃 Animator。
- 新增统一第三人称上肢动作驱动，覆盖持枪、切枪、射击、换弹、刀击、投掷和受击。
- 远端玩家的 `hit` 网络事件现在会触发受击反馈。
- 远端玩家死亡后不再被下一帧快照立即隐藏，尸体会完成倒地姿态；复活时重绑 Animator、恢复位置并清空旧动作。
- 训练 Bot 使用同一套持枪、射击、受击、死亡和复活动作。
- 第三人称武器直接使用已恢复的 M4A1、Pistol01、Knife01 和 Grenade01 资源，不使用占位几何体；M9 与第一人称一致，暂不进入正式版本。
- 武器插槽跟随右手位置，但使用角色根节点朝向，避免导入骨骼局部轴把枪口旋转到身体侧面。
- 上肢和死亡覆盖动作统一在 `LateUpdate` 应用，避免被 Animator 在同一帧重新写回。

## 自动审计

`recovery/recovered-character-animation-validation.json` 当前验证：

- 方向移动片段 9/9；
- 跳跃状态存在；
- 战斗所需骨骼 3/3；
- 第三人称正式武器资源 4/4；
- 总体 `passed: true`。

## WebGL 实机观察

- 完整 WebGL 构建成功，7 张地图验证通过。
- 训练 Bot 能显示恢复的 M4A1，并保持走跑/横移动作。
- M4A1 尺寸和持枪高度可接受。
- 第一轮枪体横穿胸前；最终将第三人称 M4A1 轴向修正为 180°，正面观察时枪口呈朝向玩家的透视缩短。
- 浏览器截图：`recovery/recovered-third-person-weapon-runtime.png`。
- 双 WebGL 客户端已实际进入同一服务器房间，服务端健康检查确认 `rooms=1`、`players=2`，两端 HUD 均显示 `ONLINE 2`。
- 双端观察暴露出网络远端人物把模型中心放到脚底坐标、导致人物沉入地面的错误；现已改为把模型脚底对齐网络锚点。
- 服务器房间结束后，客户端不再各自轮换到下一张地图，而是统一返回房间列表，避免同房玩家落入不同场景。
- 返回房间列表前会主动解除 FPS 鼠标锁定并清理旧房间会话，避免大厅可见但无法点击。
- 上述修复后的完整 WebGL 构建再次成功；构建日志为 `recovery/webgl-third-person-lobby-unlock-build.log`。
- 双端实机进一步定位出 Pyramid 大厅房间使用绝对坐标、客户端又叠加场景原点的协议错误；服务器 `pyramid` 配置现已统一为场景相对坐标。修复后远端人物从双倍世界坐标返回观察者前方，脚底和约 1.9 m 身高可见。
- 已新增 Pyramid 场景相对坐标回归测试，服务器现为 20/20、解包相关测试仍为 3/3。
- 删除固定肩骨持枪覆盖，改为保留已恢复的 `OneHand` Animator 作为稳定持枪基础；同时隐藏预制体中的旧 `AssaultrifleSig`、`clipsig` 和 `Bullet` 网格，避免与正式武器道具重叠。
- 姿态修复后的完整 WebGL 构建成功，7 张地图与第三人称资源审计通过；日志为 `recovery/webgl-third-person-pose-fix-build.log`。
- 新增真实 `RemotePlayer` 运行时受控预览，直接实例化正式预制体、正式 Animator 和同一个 `GenesisThirdPersonActionDriver`。四种道具均启用且贴合右手：M4A1 长度、方向和握把位置可接受；M9、Knife、Grenade 的包围盒与右手位置一致。证据目录为 `recovery/third-person-prop-preview/`，日志为 `recovery/third-person-runtime-preview-anchor-audit-final.log`。
- 浏览器近景确认 M9 第三人称 OBJ 在正式地图光照下仍会泛白；因此第三人称继续使用已验证的 `Pistol01` 枪体。第一人称 M9 已于 2026-08-04 使用独立视模材质和原手枪动画骨架完成转正，两条资源路径互不覆盖。
- WebGL 初始镜头已从恢复场景遗留的陡峭俯角归零为水平视线；新构建进入 New Construction Site 后可直接观察前方远端人物。构建日志为 `recovery/webgl-third-person-camera-m9-fix-build.log`。
- 非零网络锚点脚底审计通过，`footError=0`；浏览器中后续出现的局部遮挡来自该地图窄走廊、墙体和高差，不是模型中心再次偏移。
- 浏览器连续开火首次暴露出程序化动作使用累乘旋转且结束时不恢复基础姿态，导致 M4 和双臂永久卡在头顶。动作驱动现会在每次动作开始时捕获干净基础旋转，并在动作结束、切换动作、死亡和复活时显式恢复；WebGL 复测连续开火结束 1.7 秒后持枪姿态正常。
- 运行时审计新增死亡/复活复位：位置、角色旋转和左右臂误差均为 `0.0000`；同一审计中开火动作确实改变手臂，结束后左右臂误差均为 `0.0000`。日志为 `recovery/third-person-pistol01-fallback-audit.log`。
- 最终 WebGL 构建再次成功，方向片段 9/9、跳跃、战斗骨骼 3/3、武器道具 4/4 均通过；日志为 `recovery/webgl-third-person-pistol01-death-reset-final-build.log`。
- 纪念启动页现保留鼠标原始流程，同时允许 `Enter`、小键盘回车或空格直接进入恢复后的 `Ziyou1` 房间大厅；冷启动和重载后均已在浏览器验证。最终入口构建日志为 `recovery/webgl-direct-room-lobby-third-person-build.log`。
- 浏览器近景已确认第三人称回退的 `Pistol01` 为深色枪体，位置、大小和右手挂点正常；第一人称则使用已单独验收的 M9 视模。
- QA 网络角色的权威跳跃轨迹为 `0.300, 0.550, 0.750, 0.900, 1.000, 1.050, 1.050, 1.000, 0.900, 0.750, 0.550, 0.300, 0.000`。远端角色原先因短跳弧被垂直插值吞掉而看似贴地；现改为水平坐标继续平滑、垂直坐标直接服从权威快照。同步浏览器抓帧已清楚显示起跳、峰值和落地，持枪姿态连续。修复构建日志为 `recovery/webgl-third-person-authoritative-jump-build.log`。
- 同步浏览器抓帧已取得 M4A1 换弹开始、中段和复位画面；手臂及枪体动作可辨识，动作结束后恢复正常持枪，没有卡死或旧动作覆盖。
- 第二个网络 QA 角色已对观察目标完成真实权威击杀：浏览器显示受击/倒地姿态和击杀信息，约 3 秒后角色自动复活并恢复正常 M4A1 持枪姿态；服务端快照同时确认 `alive=false, health=0` 后重新变为 `alive=true`。
- 2026-08-04 复审发现 `ApplyHoldingPose` 虽已定义但未进入每帧动作链，现已在 Animator 更新后的 `Apply()` 中实际执行。步枪、霰弹枪、手枪、刀和手雷因此使用五套不同上肢角度，不再只是代码中的死分支。
- Shotgun01 已作为独立第三人称道具接入，保留枪体、泵动、扳机、插弹和装填网格；前后预览输出为 `third-person-prop-preview/shotgun-front.png` 和 `shotgun-side.png`。五类武器资源审计为 5/5。
- 客户端动作协议已保留 `shotgun01`，服务端协议同步接受并广播；服务端新增独立 `lastActionSequence`，旧序列动作会在广播前丢弃。客户端原有 `lastActionSequence` 仍作第二层防重放。服务端 22/22 测试和 TypeScript 构建通过。
- 五武器运行时审计通过，开火结束和死亡/复活后的左右臂、位置、旋转复位误差均为 `0.0000`；日志为 `recovery/third-person-five-weapon-runtime-audit.log`。

## 仍需继续验收

- 第三人称走跑、横移、跳跃、持枪、切枪、射击、换弹、受击、死亡和复活主链路已完成 WebGL 实机验收。
- 后续仅保留美术微调：根据更近距离画面继续优化左手与弹匣的贴合；这不阻塞当前第三人称功能恢复节点。
