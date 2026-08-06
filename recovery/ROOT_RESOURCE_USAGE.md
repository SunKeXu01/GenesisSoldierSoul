# 项目根目录资源复用记录

更新日期：2026-08-05

## 交付分类与边界

| 分类 | 定义 | 当前位置/账本 | 发布含义 |
| --- | --- | --- | --- |
| 当前运行时正式选用资源 | 七张可玩地图、M4A1/M16/Shotgun01、M9（缺失时回退 Pistol01）、Knife01、Grenade01、角色、HUD、音效和战斗特效 | `client-restored/Assets/`；逐组来源见 `resource-closure-audit.json` | 技术上进入诊断构建，但因缺少批准权利记录当前均为 D 级，不等于可正式发布 |
| 候选资源 | AK-74M、AWP、AN94、M249、FAMAS、加特林、手斧、尼泊尔等未完成全部动作/依赖/许可门禁的资源 | 原始隔离池与候选审计报告 | 默认关闭，不进入正式武器栏 |
| 不可变原始样本 | APK、Windows 客户端、Unity `_Data`、UnityPackage、REZ、Flash、Unreal 容器及来源压缩包 | 工作区原路径、APFS 写时复制快照、`root-resource-index.json` | 只读保存，不执行来源不明程序，不直接打入 WebGL |
| 生成产物 | 安全提取目录、对象导出、格式转换、恢复场景/Prefab、WebGL 构建、截图、测试 XML/日志和审计 JSON | `recovery/`、隔离提取目录、`client-restored/Build/` | 必须能追溯输入哈希与工具流程；构建和临时产物不冒充原始资源 |

正式发布判定只以 `RESOURCE_CLOSURE_AUDIT.md` 和机器可读闭包报告为准。当前 A 级为 0，
非诊断构建按设计拒绝；“正式选用”仅表示恢复工程当前的运行时配置和实机验收状态。

## 已盘点的主要资源池

可重复生成的全量统计见 `recovery/ROOT_RESOURCE_INDEX.md` 和
`recovery/root-resource-index.json`。索引覆盖 8 个资源池、3,828 个文件或 Unity
`_Data` 样本以及 283 个压缩包或发布包；其中 36 个普通压缩包已成功读取目录，
242 个 REZ 转交专用只读解析器处理，另有 5 个 Unreal PAK/UCAS/UTOC 容器需要
后续专用工具。总账包含逐样本 SHA-256、类型、版本、架构、来源和 860 个精确重复组。
336 个原始样本另以 APFS 写时复制方式保存进只读快照库；详见
`recovery/FULL_WORKSPACE_SAMPLE_INVENTORY_2026-08-04.md`。生成命令为：

```bash
./tools/audit_root_resources.py --workspace .. \
  --json recovery/root-resource-index.json \
  --markdown recovery/ROOT_RESOURCE_INDEX.md
```

| 位置 | 规模与内容 | 当前处理 |
| --- | --- | --- |
| `../CF2.0` | 约 24GB；3172 个 WAV、236 个 REZ、UI 脚本与 LTB/LTC 数据 | 已接入爆头、连杀、刀杀、开局和回合结束播报；标准 REZ 已能安全列出/提取 |
| `../_解压资源` | 约 5.3GB；原版 Flash 缓存、独立音效、武器模型和多个发布包 | 已接入受击、死亡、命中、回合音效；新增隔离解压的 35MB 武器模型池 |
| `../原程序恢复` | 约 2.6GB；9 个恢复 Unity 工程、1134 个 C# 文件、101 个场景 | 已复用 Humanoid 走、跑、跳和横移动画，并比对移动/武器实现 |
| `client` | 旧恢复客户端，约 14996 个资源文件 | 已补入 HUD 图标、准星和带双手骨骼的第一人称步枪动画资源 |
| `../R8-cs1.6` | 约 960KB；10 个 C++ 源文件、87 个 Valve SDK 头文件和一个二进制库 | 已审计；没有模型、贴图、音效或动画，且主体是 CS 1.6 注入/作弊代码，不编译、不接入运行时 |
| 根目录压缩包 | 20 余个 ZIP/RAR/UnityPackage/APK | 原包保留；优先使用 `_解压资源` 和 `原程序恢复` 的隔离解压结果 |

## 本轮已经进入 WebGL 工程的资源

- 旧客户端 HUD：步枪、手枪、刀具 64×64 图标及准星。
- 创世兵魂原版音频：子弹命中、角色受击、死亡、开局和胜利。
- CF2.0 PCM 播报：爆头、双杀、三杀、多杀、刀杀、团队竞技开局和胜利。
- 旧客户端第一人称步枪、手枪和近战武器：步枪/手枪使用完整双手层级、武器网格、材质、贴图，以及开火、换弹、空仓换弹、待机和切枪动画。
- `单机怀旧创世兵魂V1.2.1` 的完整 `Shotgun01` 依赖闭包：18 个 Prefab、动画、材质、贴图依赖已按原 GUID 接入；训练场已验证枪体与双手可见，`Fire01`、`Reload01/02`、弹药变化及主/副武器切换正常。
- `单机怀旧创世兵魂V1.2.1` 的完整 `Grenade01` 依赖闭包：双手骨骼、手雷、拉环、4 个渲染器、4 个材质和 5 个动画片段均已按原 GUID 接入。游戏内按 `4` 切换，播放恢复的 `Throw` 动画后生成物理投掷物，并自动切回主武器。投掷力 10、上抛力 2.5、引信 3 秒、爆炸半径 6 取自恢复源码 `vp_Grenade.cs`；联网伤害由服务器判定，每条命限一颗并随重生补充。
- CF2.0 的 `Submarine_Grenade_Boom.wav` 与投掷语音已转为 Unity 可直接加载的 PCM 资源，分别用于爆炸和 “Fire in the hole” 提示。运行时验收截图见 `recovery/recovered-grenade-hold-runtime.png`、`recovery/recovered-grenade-throw-runtime.png`、`recovery/recovered-grenade-auto-switch-runtime.png` 和 `recovery/recovered-grenade-particle-explosion-runtime.png`；最终 WebGL 粒子使用内置 Sprite Shader，已确认没有缺失材质、紫色 Shader 或资源加载错误。
- 旧刀具预制体的 `Home` 骨骼坐标与手部蒙皮已损坏；WebGL 版本复用其中完好的 `Knife` 网格和 `Knife01` 材质，并与手枪资源中完好的右臂骨架重组为单手近战视角。切换、移动起伏和下劈由统一第一人称动作系统驱动，不再显示拉丝手掌或悬空刀。
- 原程序恢复工程的 Humanoid 动画：待机、前后走、左右横移、前后跑、左右跑和跳跃。训练目标与联网角色共用生成的 `RemotePlayer.controller`；控制器使用 `MoveX/MoveZ` 二维方向混合，世界移动方向会先转换到角色本地空间，侧移和后退不再错误播放前进动作。自动审计结果见 `recovery/recovered-character-animation-validation.json`，WebGL 跳跃与切枪验证截图见 `recovery/recovered-player-jump-runtime.png` 和 `recovery/recovered-weapon-switch-runtime.png`。
- `单机怀旧创世兵魂V1.2.1` 中完整的 UFPS 第一人称源码与预设：已提取 `vp_FPWeapon` 的武器弹簧、`vp_Bob` 的移动起伏、`vp_WeaponHandler` 的切枪流程，以及步枪默认位姿 `(0.14,-0.31,-0.04)`、旋转 `(-3.07,-2.65,0)`、武器相机 FOV 35 和开镜 FOV 12。旧 UFPS 相机与当前恢复相机的 Z 轴约定不同，因此数值作为校准依据而非直接照搬；新武器按 `WeaponMainLocator` 驱动的原枪渲染边界自动归一化，公共 viewmodel 根节点负责切枪、换弹、后坐与移动起伏。
- 原工程 `FPSController.prefab` 的运动参数已重新接入：步行 5、奔跑 10、跳跃初速度 10、重力倍率 2，并恢复相机步态与落地回弹。角色不再只让武器在屏幕上平移。
- 第一人称动作在恢复参数上继续接入本地移动方向：前进/后退改变枪身纵深，侧移驱动武器滚转，奔跑平滑压低枪身并最多扩展 3 度视野；运行时验收见 `recovery/recovered-directional-sprint-runtime.png` 与 `recovery/recovered-directional-strafe-runtime.png`。
- `六月单机更新金字塔` 中完整的六面蓝天白云天空盒已经重新接回 Pyramid。运行时会用场景 `RenderSettings` 中的有效六面材质替换相机遗留的旧 Shader 材质，并恢复三色环境光。环境审计确认旧出生点上方 3.71 米处存在屋顶遮挡，因此训练与联网出生点已迁到原地图中央露天庭院 `(174.83,438.40,107.32)`；地图审计会阻止天空盒引用再次丢失。
- Shotgun01 不再复用 M4A1 音效：开火与泵动使用其原恢复工程中的 `ShotgunFirePump.ogg`，装备和装填使用 `_解压资源` 原版声音池中的 M1887 音效；三项资源由 `tools/import_recovered_assets.sh` 可重复同步。
- 根目录 `m4a1（带枪花）/枪花.unitypackage` 的四平面 WarFX 枪口火焰、贴图、材质、网格与加法 Shader 已原路径导入，并在构建时复制为 `Resources/OriginalGame/RecoveredMuzzleFlash.prefab`。运行时优先使用这一完整资源，只有资源缺失时才回退到程序化光球；带失效脚本的旧 Pyramid 枪口火焰不再实例化。
- 同目录 `m4a1.unitypackage` 的 Sopmod FBX、5 张漫反射/法线贴图和 4 个材质已组合进 `Resources/OriginalGame/FirstPerson/M4A1Viewmodel.prefab`。复合 Prefab 保留恢复客户端原有双手蒙皮、`WeaponMainLocator`、枪口锚点和 7 段动作，隐藏旧 AssaultRifle 枪体后使用原包枪体材质，不再只是把孤立模型存进 `Resources`。武器审计确认 9 个渲染器、10 个材质引用、7 段动画和全部必需锚点，近景预览输出为 `recovery/recovered-m4a1-viewmodel-preview.png`。
- `tools/extract_recovered_archives.py` 提供来源隔离的安全批量解压：解压前拒绝绝对路径、`..` 路径穿越和盘符路径，解压后拒绝符号链接，并使用相对路径与 SHA-256 为目标目录命名，避免不同包中的同名 `Assets/*` 相互覆盖。UnityPackage 会额外读取每个 GUID 项的 `pathname`，在 `_materialized/Assets/` 重建原工程目录、资源文件与 `.meta`，同时生成类型和路径清单，不需要启动 Unity 或运行包内程序。首次实跑已把两个 M4A1 UnityPackage 的 50 与 43 个原始条目隔离，并还原为 19 项可检索资源（M4A1 模型 1、枪口火焰网格 1、贴图 8、材质/Prefab 8、Shader 1）；结果记录在 `recovery/safe-extraction-index.json`，路径穿越和越出 `Assets/` 的回归测试见 `tools/test_extract_recovered_archives.py`。
- 4 个根目录 APK/APK.1 已按各自 SHA-256 隔离静态提取，来源清单见 `recovery/safe-apk-extraction-index.json`。新增 `tools/materialize_unity_split_assets.py` 会拒绝缺号分片，并把连续的 Unity `*.splitN` 只读重组到包内 `_materialized-unity-data/`；本轮重组 18 个文件、测试 5/5 通过，清单见 `recovery/unity-split-materialization.json`。UnityPy 只读审计 10,491 个对象后发现一个带 Glock 控制器和五段动作的 USP 候选，但其材质没有贴图绑定、手部只是立方体网格，未达到正式闭包门禁；详见 `recovery/APK_WEAPON_CLOSURE_AUDIT_2026-08-03.md`。
- 四包已进一步完成 Manifest、DEX、ELF、引擎/后端、OBB 与 Mono 元数据综合审计。三个 Unity 包分别识别为 `5.2.5f1`、`2018.4.14c1`、`5.2.5f1` Mono；地图浏览包识别为 UE4 原生包，其伪装成 `main.obb.png` 的 ZIP 内含 Pak 格式 11（`Fnv64BugFix`），但没有可验证的 UE4 点版本字符串。三份 `Assembly-CSharp.dll` 已静态恢复 177 个类型、1,349 个字段、1,247 个方法和完整 IL。机器及人工报告分别为 `recovery/android-static-audit.json` 与 `recovery/ANDROID_STATIC_AUDIT_2026-08-04.md`，完整 Mono 表位于 `recovery/android-mono-metadata/`。
- Windows 发布包已完成统一只读静态审计：12 套 Unity Mono 包覆盖 `5.0.2f1`、`5.2.5f1`、`2018.4.14c1`、`2019.1.4f1`，另识别 1 套 Unreal x86-64 发布包及 CF2.0 客户端；948 个 PE 出现位置归并为 309 个唯一模块，均保留架构、节区、导入/导出、资源段和分类字符串证据。12 份 `Assembly-CSharp.dll` 已生成完整 IL，共 1,326 类型、9,260 字段和 9,787 方法。综合报告见 `recovery/WINDOWS_STATIC_AUDIT_2026-08-04.md`。
- 9 套 Windows Unity 包已有来源对应的 AssetRipper 恢复工程；对余下 3 套包新增哈希隔离的逐对象导出，共保存 33,797 个对象、372,259,582 字节，失败 0。第二次同参数执行全部命中既有哈希，报告及清单见 `recovery/WINDOWS_UNITY_OBJECT_EXPORTS_2026-08-04.md` 与 `recovery/windows-unity-object-exports.json`。
- 专用格式转换已建立统一证据账本：2 份 UnityPackage 的 19 个资产保持 GUID、原路径与 `.meta`；417 个 SWF/误标缓存共提取 1,874 个 PNG、1,601 个矢量标签、74 个字体标签、1,380 个 Sprite/时间轴和 538 个 ATF；538 个 ATF 已全部转为 538 DDS + 538 RGBA PNG，保留 1,001 个有效 mip 层；私有 REZ 已恢复 8,721 个 LTB GLB，其中 321 个含 glTF skin，98 个含 818 个骨骼/顶点动画、88,740 个通道和 2,940 个 morph target，另有 40,356 PNG、1 DDS 和 51,657,941 字节 LTA；标准媒体含 15 GLB、8 PNG、188 PCM WAV。综合账本逐项复核 149,905 个输出、21,817,520,072 字节，错误 0，见 `recovery/SPECIAL_FORMAT_CONVERSION_LEDGER_2026-08-04.md`。
- Unreal 只读解析已验证 Windows 与 Android 两份 Pak v11 索引 SHA-1，分别记录 1,521 与 2,253 个条目；两组 UTOC/UCAS 完成版本、条目/压缩块和哈希配对。当前容器使用 Oodle/IoStore，内容级对象恢复仍作为明确待办，不会运行包内程序或猜测解密。
- 击杀信息从单条覆盖改为最多四条的滚动队列，连续击杀和联网死亡不再互相吞掉，布局与参考录像右上角多行击杀提示一致。
- 联网战斗协议现会广播装备、开火、换弹、挥刀和投掷动作；远端角色在基础移动 Animator 之后叠加上肢动作，动作序列号独立去重，避免旧消息重放覆盖当前姿态。双 WebGL 客户端已在同一动态房间完成在线人数和远端角色生成验证。
- 参考画面风格 HUD 已恢复：复用原圆形雷达和顶部阵营比分资源，并补齐动态准星、生命/护甲、弹药、四槽武器栏、计分板及击杀信息。WebGL 已验证武器切换、生命伤害、多人击杀信息和 Tab 计分板联动；记录见 `recovery/REFERENCE_HUD_RECOVERY_2026-08-03.md`。
- 第三人称动作已进一步统一到共享驱动器：远端玩家与训练 Bot 现在使用正式 M4A1、M9、Knife01 和 Grenade01 模型，并支持持枪、切枪、射击、换弹、受击、倒地与复活复位；恢复和验收记录见 `recovery/THIRD_PERSON_ACTION_RECOVERY_2026-08-03.md`。
- 7 张恢复地图现已全部进入客户端轮换和服务器大厅白名单，并补齐碰撞、NavMesh 与严格出生点审计；钢铁工厂已用 5 类程序化工业材质替换全白材质，冰火迷宫黑色海面已恢复为可见水面，辐射区出生点已移至开放导航区并面向可通行庭院。7 图均达到基础可玩恢复标准，后续仍需最终美术与网络回归，详见 `recovery/SEVEN_MAP_RECOVERY_2026-08-03.md`。
- 根目录武器包中的 AK-74M 与 AWP 已完成实验性参数和模型适配，但它们缺少各自完整的第一人称手部动画闭包，目前在正式仓库中关闭。M9 已采用独立手枪构图完成持有、开火、换弹、切枪、死亡、复活与 WebGL 全序列验收，并已转为正式副武器；`Pistol01` 仅保留为资源缺失时的回退。记录见 `recovery/M9_FIRST_PERSON_PROMOTION_2026-08-04.md`。
- 正式资源闭包现采用 fail-closed 的 A/B/C/D 机器分级。7 个拟进入正式构建的资源组均有来源和回退记录，但仓库内没有覆盖恢复素材的权利/许可证明，因此当前全部判为 D、A 级为 0；这不会被“技术验收通过”自动覆盖。`GENESIS_DIAGNOSTIC=1` 仍可生成恢复研究构建，非诊断 WebGL 构建会先核验策略哈希、审计时效和 A 级计数并拒绝发布。策略、逐组依赖结果和回退账本见 `recovery/resource-closure-policy.json`、`recovery/resource-closure-audit.json` 与 `recovery/RESOURCE_CLOSURE_AUDIT.md`。

这些资源由 `tools/import_recovered_assets.sh` 可重复同步，不依赖运行来源不明的 EXE。

## 已解压并可继续接入的根目录武器包

`../创世兵魂武器.rar` 已隔离解压至 `../_解压资源/创世兵魂武器/`，原压缩包未修改。当前确认包括：

- AK-74M（FBX）、M16（FBX）、AWP（OBJ）。
- AN94、M249、手斧（3DS），以及 M249 的 Maya 源文件。
- M9 手枪（OBJ/3DS/C4D 与贴图）。
- 两个 M4A1 UnityPackage。
- 尼泊尔军刀、FAMAS、加特林的 3ds Max 源文件。

AK-74M 与 AWP 已完成实验性适配，但不会在缺少自身完整第一人称动作时进入正式武器栏；M9 已完成正式副武器验收，`Pistol01` 只保留为资源缺失回退。其余模型并不都自带第一人称手部、骨骼和动作，后续仍需按同一流程适配，不能直接当成已经可玩的第一人称武器。

## REZ 安全提取状态

新增 `tools/rez_extract.py`，无需运行压缩包中的 Windows EXE，可对标准 LithTech/Jupiter REZ v1 执行：

```bash
./tools/rez_extract.py "../CF2.0/CrossFire/engine.rez"
./tools/rez_extract.py "../CF2.0/CrossFire/engine.rez" \
  --output /tmp/engine-rez --ext DTX
```

已经验证 `engine.rez` 可列出并选择性提取 `CONSOLE/CONSOLE_FONT.DTX` 和 RenderStyle LTB。236 个 `RF*.REZ` 虽然保留标准 v1 头，但目录块经过 CF 私有加密/混淆，工具会把它们标记为不支持并安全跳过，不会把随机数据写进工程。LTB/DTX 仍需独立转换才能供 Unity 使用。

## 仍需转换后才能使用

- CF 私有目录中仍有 3 个完全未知 REZ 和 4 个有精确停止证据的未知后缀；两份 RF019 与两份 RF199 的可证明连续前缀已恢复 27,087 个资源、3,215,875,246 个源字节，其中 15,963 个 DTX 和 257 个 TGA 已转为 16,220 个 PNG。LTB 几何已 8,721/8,721 转换，321 个复合 LTB 的 14,896 条骨骼和 1,844 个网格已生成 skin，源内 818 个骨骼/顶点动画已全部生成标准 glTF animation/morph。标准 `engine.rez` 的 DTX 已转 PNG，其中 15 个 LTB 实为 RenderStyle 而非几何模型。364 个 loose LTC 已全部恢复为 LTA，不再列为未知格式。
- Flash SWF 中的 538 个 ATF 已转为 DDS/PNG；矢量和字体标签仍保留原始标签体，尚未转成 SVG/TTF。位图 PNG 与时间轴元数据已经可审计使用。
- Unity 发布包中未完成依赖闭包的 Prefab、动画控制器和特效。
- Unreal PAK/UCAS/UTOC 以及 Android OBB 中的地图资源。

WebGL 不会直接塞入全部数十 GB 文件；只把运行时实际引用的资源打进构建，其余保留为可追溯素材库。

## 明确不进入游戏运行时的内容

- `../R8-cs1.6` 仅含 CS 1.6 客户端注入、武器状态读取、透视/自瞄/无散布相关源码和 Valve SDK 声明，没有可复用的美术资源。其二进制 `detours.lib` 来源和许可也不足，因此只保留为已审计的外部参考，不复制、不执行、不链接。
- 压缩包里的来源不明 EXE/DLL 不作为解包或转换依赖。
