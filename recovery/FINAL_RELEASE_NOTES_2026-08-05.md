# 《创世兵魂》恢复研究版交付说明

日期：2026-08-05  
状态：技术验收候选；仅限非商业恢复研究，不是获权正式发行版

## 本版结果

- 七张恢复地图全部进入可玩轮换，并通过材质、碰撞、出生点、导航、边界、音频和发布场景门禁。
- M4A1、M16、Shotgun01 完成 7 地图 × 3 主武器的 21/21 WebGL 实机矩阵。
- 七图逐一验证移动、跳跃、手枪开火/换弹、匕首挥砍、手雷投掷/爆炸后自动切回、完整 HUD 和 Tab 战绩板。
- 双浏览器完成同房生成、移动、切枪、射击、权威伤害 `100→66`、死亡 `D 1`、复活 `100 HP`、回合结束和跨图。
- 大厅房间、12 人上限、地图会话隔离、仓库装备、设置和只读资源目录已接通；没有权威依据的交易、账号和活动业务未伪造。

## 固定工具链与复现入口

| 工具 | 固定版本/来源 |
| --- | --- |
| Unity | `2022.3.62f3c1`，由 `ProjectVersion.txt` 固定 |
| Node.js | `24.16.0`，由 `.node-version` 固定 |
| pnpm | `11.9.0`，由根 `package.json#packageManager` 固定 |
| Python | `3.12.13`，由 `.python-version` 固定 |
| Python 媒体依赖 | Pillow `11.3.0`、UnityPy `1.25.2`，由 `tools/requirements.txt` 固定 |

统一入口为 `tools/verify_delivery.sh`：

```bash
tools/verify_delivery.sh --quick
tools/verify_delivery.sh --full
```

完整模式依次执行锁文件安装、服务端构建/测试、Python 工具测试、Unity EditMode、PlayMode、9 项综合门禁、诊断 WebGL 构建和 77 文件全树哈希。当前完整复验产生：

| 门禁 | 结果 |
| --- | --- |
| TypeScript | 构建通过 |
| 服务端 Vitest | 30/30 Passed |
| Python 安全工具 | 38/38 Passed |
| Unity EditMode | 65/65 Passed |
| Unity PlayMode | 6/6 Passed |
| Unity 综合门禁 | 9/9 Passed |
| 诊断 WebGL | 连续三次全量构建均为 `Success`，最终一次由统一脚本端到端完成 |

最终统一入口重建大小为 360,379,681 字节，主文件和完整清单摘要见
`delivery-webgl-build-2026-08-05.sha256`。Unity WebGL `.data` 和 `index.html` 包含
构建期数据/缓存版本，不承诺跨构建字节一致；每次交付必须保存当次清单，不能沿用旧哈希。

## 资源来源与交付分类

- 完整来源总账：`ROOT_RESOURCE_INDEX.md`、`root-resource-index.json`。
- 当前运行时选用、候选、原始样本和生成产物四类边界：`ROOT_RESOURCE_USAGE.md`。
- 正式依赖闭包、许可状态与逐组回退：`RESOURCE_CLOSURE_AUDIT.md`、`resource-closure-audit.json`。
- 专用格式转换和输出哈希：`SPECIAL_FORMAT_CONVERSION_LEDGER_2026-08-04.md`。
- APK/Windows 静态恢复：`ANDROID_STATIC_AUDIT_2026-08-04.md`、`WINDOWS_STATIC_AUDIT_2026-08-04.md`。

“当前运行时正式选用”只描述恢复工程配置，不代表取得发行权。当前 7 个运行时资源组
均因缺少批准权利记录为 D 级，A 级为 0。2026-08-06 复核后七个运行时组的前向 GUID 闭包均为 0 缺失；此前 `playable_maps` 的 19 项来自 LightingData 所属场景反向边误判。
正式构建因此按设计 fail-closed，只允许 `GENESIS_DIAGNOSTIC=1` 的研究构建。

## 回退方案

- M9 资源缺失时自动回退至 `Pistol01`；候选 AK-74M/AWP 等默认关闭。
- 恢复枪口资源缺失时使用项目生成的程序化粒子；战斗音频无批准权利时的发布安全回退为静音。
- 角色资源的发布安全回退为胶囊角色与程序化 Transform 动作；HUD 可回退为项目生成的 IMGUI 图形和文本。
- 恢复地图正式发布前必须补齐权利/GUID 闭包，或替换为项目自制且有批准记录的场景；不得通过诊断开关绕过发布门禁。
- P21 浏览器验收固定包由树哈希
  `3072e9f7ae9af8b64de8082e518c709485bd3f3037dd8b833b1ad97e30750a96`
  唯一标识；其证据不能挪用于后续不同哈希的构建。

## 已知限制

- 账号、余额、购买、充值、VIP、任务、活动和战队没有权威后端协议，当前不提供伪实现。
- 364 个 CF LTC 已全部恢复为 LTA（51,657,941 字节、失败 0、同名明文对精确匹配）；
  两份 RF199 已额外严格恢复 9,520 PNG + 1 DDS；5 个完全未分帧私有 REZ、
  两份 RF199 未知后缀、LTB 骨骼/顶点动画通道、Flash ATF 和受 Oodle 限制的
  Unreal 内容仍未完成。
- 正式发布许可尚未闭合，正式发布门禁保持关闭；技术 GUID 闭包已修正为七个运行时组 0 缺失。
- 长时间 Chromium 自动化偶发浏览器层 `UnknownError`；已与 Unity、资源、Shader 和 WebSocket 应用错误分开记录。

## 验收证据

- `P21_SEVEN_MAP_WEAPON_MATRIX_2026-08-05.md`
- `DUAL_BROWSER_NETWORK_REGRESSION_2026-08-05.md`
- `P6_AUTOMATION_GATES_2026-08-05.md`
- `webgl-p21-matrix-evidence/`（61 张 1600×900 JPEG）
