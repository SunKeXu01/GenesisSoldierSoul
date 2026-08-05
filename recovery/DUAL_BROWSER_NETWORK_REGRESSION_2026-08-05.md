# 双浏览器多人网络与远端表现回归（2026-08-05）

## 结论

- 最新诊断构建（完整文件树 `3072e9f7ae9af8b64de8082e518c709485bd3f3037dd8b833b1ad97e30750a96`）再次完成双浏览器闭环：两端进入生化小镇并显示 `ONLINE 2`，均完成移动、切枪和射击；B 端经生产协议由 `100 HP` 降至 `66 HP`，随后死亡计数为 `D 1` 并在 3 秒后以 `100 HP` 复活。
- 同轮跨图/回合流程依次覆盖金字塔、新工地和生化小镇；金字塔回合结束时两端同步显示 `ROUND OVER NEXT: Ziyou1` 并返回房间列表，随后重新建房进入下一地图。
- 双浏览器在房间 `a738f865` / `MAP PYRAMID` 同房，HUD 同时显示 `ONLINE 2`；加入 QA 权威客户端后两端显示 `ONLINE 3`。
- 两个真实 WebGL 客户端均完成生成；A 端完成移动、切换手枪和实弹射击，弹匣由 `12/48` 变为 `11/48`。
- QA 客户端通过生产 WebSocket 协议命中 B 端：B 端 HP 从 `100` 降为 `66`；继续命中后死亡计数变为 `D 1`，并恢复为 `100 HP`，证明权威受击、死亡和复活链路贯通。
- 120 秒回合结束后，两个 WebGL 客户端均从战斗场景返回自由模式房间列表；仍连接的 QA 客户端使列表人数显示 `1/12`，退出后服务端清理会话。
- 战斗中停止服务端后，两个客户端安全返回房间列表；服务端重启、重新建房后，两端可再次进入同一房间。PlayMode 同时验证断线使旧动作令牌失效、重连恢复操作。
- 补齐远端表现缺口：服务端动作事件现在统一驱动第三人称动作、武器模型、枪口粒子和 3D 空间音频，且按 M4A1/M16/Shotgun01/手枪/刀/手雷选择正确资源族。

## 本轮实现

- `GenesisThirdPersonActionDriver`
  - 给远端角色增加 `spatialBlend=1`、2–45 米线性衰减的空间音源。
  - `fire/reload/equip/throw` 复用 `GenesisCombatActionCommand`，同一服务端动作同时决定姿态、武器、音频资源与枪口特效。
  - 步枪、霰弹枪和手枪使用恢复的 `Muzzle` / `FireLocator`；缺失时按武器包围盒创建稳定的第三人称枪口锚点。
  - 远端开火生成短寿命粒子和点光源；刀与手雷不会错误生成枪口闪光。
- `GenesisThirdPersonRuntimePreview`
  - 新增网络表现审计，验证 Shotgun 开火、手枪换弹、枪口粒子、资源映射与 3D 空间音频门槛。

## 自动化结果

| 门槛 | 结果 | 证据 |
| --- | --- | --- |
| 第三人称网络表现专项审计 | 通过；`remoteEffects=(fire,reload)`、`flash=True`、`spatial=1.0` | `third-person-network-effects-audit-2026-08-05.log` |
| Unity EditMode | 65/65 通过 | `network-effects-editmode-2026-08-05.xml` |
| Unity PlayMode | 6/6 通过 | `network-effects-playmode-2026-08-05.xml` |
| 服务端 Vitest | 30/30 通过 | 本轮终端回归 |
| TypeScript | `tsc -p tsconfig.json` 通过 | 本轮终端回归 |
| WebGL | 完整诊断构建成功 | `webgl-network-effects-build-2026-08-05.log` |

移除 WebGL Pointer Lock、改用 Canvas 相对鼠标增量后重新回归：EditMode **65/65**、PlayMode **6/6** 通过，证据为 `p6-latest-input-editmode-2026-08-05.xml` 与 `p6-latest-input-playmode-2026-08-05.xml`。

## WebGL 构建指纹

- 最新双端复验构建：
  - 输出大小：360,379,650 字节。
  - 完整文件树：`3072e9f7ae9af8b64de8082e518c709485bd3f3037dd8b833b1ad97e30750a96`。
  - `WebGL.data`：`0c2485b2d42ab75309a9c158a301ab01d2517faca9279035b14f728d6000eb3a`。
  - `WebGL.wasm`：`97d2116d17168021cf371493663f2b4ccacdc9e5c67134ec579532b329caa54f`。
  - 哈希清单：`p6-final-webgl-build-2026-08-05.sha256`。
  - 构建日志：`p6-webgl-no-pointer-lock-build-2026-08-05.log`。
- 以下为首次网络表现专项构建指纹，保留作历史基线：
- `WebGL.data`: `7c38557e41f048a2d3bb616c93c2f97c272df6d61e976d4673378657f9131ac1`
- `WebGL.wasm`: `1568f7ae5b920c3610e3d8690de960c9ea6e17cf31c5e87c193acfe250affa08`
- `WebGL.framework.js`: `21e60407f10bb1633e3d3aef658e34079840eec2a6a25fba3a70b999aec0b3de`
- `index.html`: `2393e384a1d2068a8b610999ae315d39d244129ba5e3f847be83953af0872bd7`
- 全量清单：`webgl-network-effects-build-2026-08-05.sha256`

## 观察项

- 最新构建不再依赖 Pointer Lock：WebGL 使用 Canvas 相对鼠标增量，两端实测仍可转向、移动、切枪和射击。
- 未发现 Unity 异常、资源 404、Shader 丢失或 WebSocket 协议错误。
- 长时间双标签自动化会话中，两端根 URL 仍记录 Chromium 自报的 `UnknownError`；其中两条时间戳在两个标签完全相同，且移除 Pointer Lock 后仍出现，因此归类为浏览器自动化层问题，不冒充应用零错误。完整原始记录见 `webgl-final-dual-evidence/latest-browser-problems.json`。
- 最新双端截图见 `webgl-final-dual-evidence/18-latest-bio-a-online2.png` 至 `23-latest-bio-b-death-respawn-d1-100hp.png`；此前的跨图、回合结束和返回列表证据为同目录 `01` 至 `17`。
