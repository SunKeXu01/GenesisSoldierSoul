# APK 武器依赖闭包审计（2026-08-03）

## 来源与方法

- 来源包：`遗迹杀戮(1).apk.1`
- SHA-256：`c9745db1614ead6401bdfb74328f1147f898dd080942749ce7d77b635329a5a2`
- APK 只通过 `bsdtar` 隔离解包；DEX、SO 和应用程序均未执行。
- 265 段 `sharedassets0.assets`、97 段 `sharedassets1.assets`、24 段 `sharedassets2.assets` 已按连续编号校验后重组；每个重组文件的哈希见 `recovery/unity-split-materialization.json`。
- UnityPy 1.25.2 只读解析 17 个重组序列化资源文件，共 10,491 个对象；总表见 `recovery/APK_UNITY_ASSET_INVENTORY_2026-08-03.md`。

## Glock/USP 第一人称候选

`sharedassets2.assets` 中的 `Glock` 对象确实不是孤立模型：

- 根对象 `Glock`（path id 343）包含 Animator、AudioSource 和旧 MonoBehaviour。
- AnimatorController `Glock`（path id 227）明确引用 `Normal`、`Reload`、`Run`、`Shoot`、`Start` 五段 AnimationClip（path id 188–192）。
- 枪体层级名为 `USP_o`，包含 7 个可见 MeshFilter/MeshRenderer 部件和独立弹匣 `DanJia`。
- 第一人称手部只有 `Hand (1)` 下的两个立方体网格，不是可验证的手指骨架与蒙皮手部。
- 枪体使用 `New Material 1`、`02 - Default` 和 `Default-Material`；三者的 `_MainTex` 均为空。现有 `xinusp_heitie.94053` Texture2D 没有被这些材质引用。

结论：该对象具备“模型＋材质＋动作控制器”，但不具备可证明的“贴图绑定＋第一人称手部骨架”完整闭包。它保留为 `Glock/USP` 研究候选，不进入正式武器栏。

## 其他武器线索

- 同一资源文件存在完整 `M4`、`SL`（手雷）和 `Dao` 层级及控制器，但它们对应当前已经恢复的武器类别，不构成新的正式武器类型。
- `ak47_sd.28568`、`ak47_paozhang.28568`、`AK圣诞` 和 `AK炮仗` 仅形成贴图/材质线索，没有在该闭包内找到对应 AK47 枪体和第一人称动作层级。
- `兵魂回忆录.apk.1` 的武器命中项主要是 UI 图片/Sprite；没有 AnimationClip 与相应武器模型闭包。

## 正式接入决定

本轮没有发现满足“模型＋贴图＋第一人称手部骨架＋动画”的新武器。因此：

- 不用该 Glock/USP 替代当前正式手枪。
- M16、AK-74M、AWP 等原有不完整候选继续关闭。
- 只有后续找到并核对缺失贴图绑定与手部骨架来源，才进入预制体适配和 WebGL 实机阶段。
