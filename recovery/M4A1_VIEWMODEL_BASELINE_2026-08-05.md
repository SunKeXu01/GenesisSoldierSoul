# M4A1 视模回归基线（2026-08-05）

## 证据性质

本基线严格区分两类画面：

- `recovered-m4a1-viewmodel-preview.png` 是用 Unity Editor 从源 Prefab 重建的错误构图预览，不是浏览器实机运行截图。它保留了枪体/手臂巨大、裁切和悬浮的源空间问题，可作为修复前视觉回归证据。
- `recovered-m4a1-runtime-*.png` 是当前运行时相机、位姿配置和 `Idle01` 采样下的离屏渲染证据。早期 `m4a1-runtime-framing-baseline.log` 曾生成同名文件，但这些 PNG 后来被最终通过版本覆盖；因此不把它们误称为旧错误运行截图。

仓库没有保留下来的旧浏览器实机错误截图。此限制不得在后续报告中改写为已有实机证据。

## 修复前源空间错误预览

| 项目 | 值 |
| --- | --- |
| 文件 | `recovery/recovered-m4a1-viewmodel-preview.png` |
| SHA-256 | `97053ab7b3f8b24b5b6d67ef4d6ef22a526162ee8d61689a2e15a23cab86cd6a` |
| 尺寸 | `1600 × 900`（16:9） |
| 源 Prefab | `Assets/Resources/OriginalGame/FirstPerson/M4A1Viewmodel.prefab` |
| 动画采样 | `Idle01` |
| 相机垂直 FOV | `42°` |
| 相机偏移 | `(-0.25, 0.10, -0.90) × max(0.5, boundsLongestSide)`，随后 LookAt 包围盒中心 |
| 生成入口 | `GenesisWeaponRecoveryAudit.RenderM4A1ViewmodelPreview` |

错误特征：枪与双臂占据过大屏幕面积，左右和底部明显裁切，源 Prefab 的空间不能直接当作游戏相机最终位姿。

## 当前正式运行参数

| 项目 | 值 |
| --- | --- |
| 运行分辨率 | `1280×720`、`1280×800`、`1200×900` |
| `ViewmodelRoot` 位置（16:9） | `(0.11, -0.19, 0.14)` |
| `ViewmodelRoot` 欧拉角（16:9） | `(2, -4, 1)` |
| `ViewmodelRoot` 缩放（16:9） | `0.38` |
| 武器相机垂直 FOV | `48°` |
| Near / Far Clip | `0.01 / 3.0` |
| 原 Prefab 局部包围盒中心 | `(-0.0379851, -0.1382632, 0.6570647)` |
| 原 Prefab 局部包围盒尺寸 | `(1.3286989, 1.4485248, 1.7759240)` |
| M4A1 模型归一化局部缩放 | `(0.8518980, 0.8518980, 0.8518980)` |
| 动画根 | 源 Prefab 根；运行时位于 `ViewmodelRoot_M4A1/AnimationRoot_M4A1` |
| 武器模型路径 | `Home/.../RightHand/WeaponMainLocator/Recovered_M4A1_Sopmod` |
| 枪口锚点 | `Muzzle` |
| 右手锚点 | `Home/.../RightHand` |
| 左手锚点 | `Left`（另有左腕/手掌骨链） |

M4A1 Prefab 的生成规则是：以 `AssaultRifle01.prefab` 为动画/手臂基架，将 `M4A1.prefab` 实例化到 `WeaponMainLocator`，模型局部旋转 `(0, 180, 0)`，按源模型与原枪体包围盒最长边比值归一化，再对齐原枪体包围盒中心；禁用被替换的原枪 MeshRenderer，并保留修复肩部封口。

窄窗口校正规则：从 16:9 向 4:3 收窄时，根节点逐步向左最多 `0.04`，缩放最多降低 `5%`；垂直 FOV 不随宽高比改变。

## 当前画面与门禁数值

| 分辨率 | SHA-256 | 枪体 viewport min → max | 枪口 viewport `(x,y,z)` |
| --- | --- | --- | --- |
| 1280×720 | `a22c3b9744cfc9241c8261a89dacd7aa832fff49f4b251325117458be3a270f1` | `(0.54,-0.94) → (0.97,0.34)` | `(0.56,0.27,0.73)` |
| 1280×800 | `c43991db128a2056cf26b9e7af661a088699051022526fa5e696c8a8a143a774` | `(0.53,-0.94) → (0.96,0.34)` | `(0.55,0.27,0.72)` |
| 1200×900 | `20def004a75059c47194d18259d9b91cc19c8879ecd3b938c9ad378baf8c6f2f` | `(0.51,-0.94) → (0.94,0.33)` | `(0.53,0.26,0.70)` |

负的枪体 `min.y` 包含按设计延伸到画面下方的手臂；门禁关注枪体右侧不越界、水平占比不超过 `0.62`、顶部不遮挡主要视野，以及枪口在相机前方且落入 viewport。

完整机器记录见 `recovery/viewmodel-structure-and-pose-audit.json`；最终构图日志见 `recovery/m4a1-runtime-framing-final.log`。
