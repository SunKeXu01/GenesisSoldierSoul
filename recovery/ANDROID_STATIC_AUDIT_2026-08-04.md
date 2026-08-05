# Android 安装包综合静态逆向（2026-08-04）

本报告只读取 APK、DEX、ELF、Unity 序列化数据和伪装 OBB 的目录/元数据；未安装或运行 APK、DEX、SO、EXE/DLL，未绕过账号、付费或第三方认证。

## 版本矩阵

| APK | SHA-256 | 包名 | 版本 | SDK | 引擎/后端 | 引擎版本 | DEX | SO | Managed |
|---|---|---|---|---|---|---|---:|---:|---:|
| `base.apk.1` | `c90fbfd89f75a174` | `com.Game.Test` | 0.1.1T | 22→22 | Unity / Mono | 5.2.5f1 | 1 | 6 | 8 |
| `兵魂回忆录.apk.1` | `57cb40fcff6a17a9` | `com.gx.memory` | 1.0 | 16→29 | Unity / Mono | 2018.4.14c1 | 1 | 8 | 91 |
| `创世兵魂地图手机版浏览（通过缓存恢复的）.apk.1` | `698aeba0ef194559` | `com.Atom.MapPreview` | 1.0 | 21→26 | Unreal Engine / native UE4 | — | 4 | 6 | 0 |
| `遗迹杀戮(1).apk.1` | `c9745db1614ead64` | `com.CSBH.APK` | 1.22.5v | 9→29 | Unity / Mono | 5.2.5f1 | 1 | 6 | 8 |

## Manifest 与安全配置

### `base.apk.1`

权限：无声明权限。
组件：activity 1，activity-alias 0，service 0，provider 0，receiver 0。
网络安全配置：`networkSecurityConfig=None`，`usesCleartextTraffic=None`。

- activity `com.unity3d.player.UnityPlayerActivity`；actions=['android.intent.action.MAIN']；exported=None

### `兵魂回忆录.apk.1`

权限：android.permission.ACCESS_NETWORK_STATE, android.permission.INTERNET。
组件：activity 5，activity-alias 0，service 0，provider 0，receiver 0。
网络安全配置：`networkSecurityConfig=None`，`usesCleartextTraffic=None`。

- activity `com.unity3d.player.UnityPlayerActivity`；actions=['android.intent.action.MAIN']；exported=None
- activity `com.unity3d.splash.services.ads.adunit.AdUnitActivity`；actions=[]；exported=None
- activity `com.unity3d.splash.services.ads.adunit.AdUnitTransparentActivity`；actions=[]；exported=None
- activity `com.unity3d.splash.services.ads.adunit.AdUnitTransparentSoftwareActivity`；actions=[]；exported=None
- activity `com.unity3d.splash.services.ads.adunit.AdUnitSoftwareActivity`；actions=[]；exported=None

### `创世兵魂地图手机版浏览（通过缓存恢复的）.apk.1`

权限：android.permission.ACCESS_NETWORK_STATE, android.permission.ACCESS_WIFI_STATE, android.permission.INTERNET, android.permission.MODIFY_AUDIO_SETTINGS, android.permission.VIBRATE, android.permission.WAKE_LOCK, android.permission.WRITE_EXTERNAL_STORAGE, com.android.vending.BILLING, com.android.vending.CHECK_LICENSE。
组件：activity 7，activity-alias 0，service 2，provider 1，receiver 3。
网络安全配置：`networkSecurityConfig=None`，`usesCleartextTraffic=None`。

- activity `com.epicgames.ue4.SplashActivity`；actions=['android.intent.action.MAIN']；exported=None
- activity `com.epicgames.ue4.GameActivity`；actions=[]；exported=None
- activity `com.Atom.MapPreview.DownloaderActivity`；actions=[]；exported=None
- activity `com.google.android.gms.ads.AdActivity`；actions=[]；exported=false
- activity `com.google.android.gms.auth.api.signin.internal.SignInHubActivity`；actions=[]；exported=false
- activity `com.android.billingclient.api.ProxyBillingActivity`；actions=[]；exported=None
- activity `com.google.android.gms.common.api.GoogleApiActivity`；actions=[]；exported=false
- service `com.Atom.MapPreview.OBBDownloaderService`；actions=[]；exported=None
- service `com.google.android.gms.auth.api.signin.RevocationBoundService`；actions=[]；exported=true
- provider `androidx.lifecycle.ProcessLifecycleOwnerInitializer`；actions=[]；exported=false
- receiver `com.Atom.MapPreview.AlarmReceiver`；actions=[]；exported=None
- receiver `com.epicgames.ue4.LocalNotificationReceiver`；actions=[]；exported=None
- receiver `com.epicgames.ue4.MulticastBroadcastReceiver`；actions=['com.android.vending.INSTALL_REFERRER']；exported=true

### `遗迹杀戮(1).apk.1`

权限：无声明权限。
组件：activity 1，activity-alias 0，service 0，provider 0，receiver 0。
网络安全配置：`networkSecurityConfig=None`，`usesCleartextTraffic=None`。

- activity `com.unity3d.player.UnityPlayerActivity`；actions=['android.intent.action.MAIN']；exported=None

## DEX、原生库与数据容器

### `base.apk.1`

DEX 1 个；定义总量 468，引用 961，头部类定义合计 79。
原生库 6 个；ABI：armeabi-v7a, x86。
Unity 数据文件 215，序列化文件 5，资源流 7，完整分片 183，Managed 程序集 8。

- 伪装/内嵌 OBB `_exported-unity-objects/MonoScript/sharedassets0.assets__29__NetworkLobbyManager.json`：unknown，0 项，contains_unreal_pak=False，Pak footer=[]。
- 伪装/内嵌 OBB `_exported-unity-objects/MonoScript/sharedassets0.assets__53__NetworkLobbyPlayer.json`：unknown，0 项，contains_unreal_pak=False，Pak footer=[]。

### `兵魂回忆录.apk.1`

DEX 1 个；定义总量 1950，引用 3181，头部类定义合计 329。
原生库 8 个；ABI：armeabi-v7a, x86。
Unity 数据文件 383，序列化文件 8，资源流 4，完整分片 241，Managed 程序集 91。

### `创世兵魂地图手机版浏览（通过缓存恢复的）.apk.1`

DEX 4 个；定义总量 30490，引用 36924，头部类定义合计 5773。
原生库 6 个；ABI：armeabi-v7a。
Unity 数据文件 0，序列化文件 0，资源流 0，完整分片 0，Managed 程序集 0。

- 伪装/内嵌 OBB `assets/main.obb.png`：zip，1 项，contains_unreal_pak=True，Pak footer=[{'member': 'MapPreview/Content/Paks/MapPreview-Android_Multi.pak', 'version': 11, 'version_name': 'Fnv64BugFix', 'footer_offset': 94784160}]。

### `遗迹杀戮(1).apk.1`

DEX 1 个；定义总量 468，引用 961，头部类定义合计 79。
原生库 6 个；ABI：armeabi-v7a, x86。
Unity 数据文件 1254，序列化文件 1，资源流 378，完整分片 388，Managed 程序集 8。

- 伪装/内嵌 OBB `_exported-unity-objects/MonoScript/sharedassets0.assets__154__NetworkLobbyManager.json`：unknown，0 项，contains_unreal_pak=False，Pak footer=[]。
- 伪装/内嵌 OBB `_exported-unity-objects/MonoScript/sharedassets0.assets__223__NetworkLobbyPlayer.json`：unknown，0 项，contains_unreal_pak=False，Pak footer=[]。

## Mono 元数据与 IL 恢复

- `base.apk.1`：类型 14、字段 73、方法 62、常量 1、编译器状态机 0；完整元数据表和 IL 位于 `/Users/Admin/Documents/创世兵魂/GenesisSoldierSoul/recovery/android-mono-metadata/base.apk.1__c90fbfd89f75`。
- `兵魂回忆录.apk.1`：类型 103、字段 553、方法 786、常量 127、编译器状态机 4；完整元数据表和 IL 位于 `/Users/Admin/Documents/创世兵魂/GenesisSoldierSoul/recovery/android-mono-metadata/兵魂回忆录.apk.1__57cb40fcff6a`。
- `创世兵魂地图手机版浏览（通过缓存恢复的）.apk.1`：非 Mono 包，不适用。
- `遗迹杀戮(1).apk.1`：类型 60、字段 723、方法 399、常量 1、编译器状态机 1；完整元数据表和 IL 位于 `/Users/Admin/Documents/创世兵魂/GenesisSoldierSoul/recovery/android-mono-metadata/遗迹杀戮_1_.apk.1__c9745db1614e`。

三个 Unity 包均为 Mono；当前样本没有 `libil2cpp.so` 或 `global-metadata.dat`，因此 IL2CPP 字段布局/方法地址恢复明确记为不适用。
Unreal 包的 PAK 尾部版本为 11（`Fnv64BugFix`）；[Epic 官方 `FPakInfo` API 文档](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/PakFile/FPakInfo_2)可确认该格式枚举，但样本未嵌入可验证的 UE4 点版本，因此不从容器版本反推具体引擎补丁号。

## Unity 场景与资源对象交叉盘点

| APK | 重组文件 | 对象 | GameObject | Mesh | AnimationClip | Material | Texture2D | Shader | AudioClip | Font | Sprite |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `base.apk.1` | 6 | 423 | 0 | 41 | 5 | 115 | 129 | 13 | 35 | 0 | 11 |
| `兵魂回忆录.apk.1` | 7 | 936 | 0 | 467 | 0 | 99 | 189 | 8 | 8 | 0 | 155 |
| `创世兵魂地图手机版浏览（通过缓存恢复的）.apk.1` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `遗迹杀戮(1).apk.1` | 4 | 9132 | 1625 | 41 | 38 | 41 | 469 | 14 | 22 | 12 | 416 |

`遗迹杀戮(1)` 提供最完整的场景层级、动作和游戏逻辑；`兵魂回忆录` 提供更丰富的新版本网格/UI；`base.apk.1` 与 `遗迹杀戮(1)` 共享 Unity 5.2.5f1 运行时及若干资源流，可用于交叉补缺。对象计数是只读目录恢复结果，不代表所有依赖闭包自动完整。
三个 Unity APK 已进一步导出 10426 个清单相关对象；失败 0。贴图/Sprite、Mesh、音频、Shader、字体使用标准格式，场景/Prefab、骨架、动作、材质与 UI 引用使用类型树 JSON，不支持解码的旧对象保留原始二进制。

## 跨版本补全结论

四包之间发现 17 组跨包完全相同的已索引样本。Unity 三包提供可恢复的程序集、场景和序列化资源；Unreal 地图浏览包提供独立的 PAK/IoStore 方向，不能按 Unity GUID 合并。
已有 UnityPy 对象审计覆盖 17 个重组序列化文件、10,491 个对象；Glock/USP 候选仍缺贴图绑定和第一人称手骨骼，不能进入正式武器栏。

## 输出与边界

- 机器报告：`recovery/android-static-audit.json`。
- Mono 表与完整 IL：`recovery/android-mono-metadata/`。
- Unity 对象总账：`recovery/apk-unity-asset-inventory.json`。
- Unity 对象导出：`recovery/android-unity-object-exports.json` 与 `recovery/ANDROID_UNITY_OBJECT_EXPORTS_2026-08-04.md`。
- 本轮恢复的是静态结构和可识别行为证据，不声称破解加密容器、在线认证或付费逻辑。
