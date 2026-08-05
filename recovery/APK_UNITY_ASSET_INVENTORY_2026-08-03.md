# APK Unity 资源静态审计（2026-08-03）

输入来自隔离 APK 解包及完整 `*.splitN` 重组结果；只读取 Unity 序列化对象目录，不执行 APK、DEX 或原生库。

解析器：UnityPy `1.25.2`。

| 安装包资源文件 | 大小 | 对象 | Mesh | Material | Texture2D | AnimationClip | AudioClip | 武器名候选 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `/Users/Admin/Documents/创世兵魂/_安全解压资源/base.apk.1__c90fbfd89f75/_materialized-unity-data/sharedassets0.assets` | 7.37 MiB | 79 | 0 | 1 | 3 | 1 | 3 | 0 |
| `/Users/Admin/Documents/创世兵魂/_安全解压资源/base.apk.1__c90fbfd89f75/_materialized-unity-data/sharedassets1.assets` | 13.18 MiB | 86 | 0 | 11 | 29 | 2 | 30 | 0 |
| `/Users/Admin/Documents/创世兵魂/_安全解压资源/base.apk.1__c90fbfd89f75/_materialized-unity-data/sharedassets2.assets` | 9.03 MiB | 31 | 3 | 14 | 10 | 0 | 0 | 0 |
| `/Users/Admin/Documents/创世兵魂/_安全解压资源/base.apk.1__c90fbfd89f75/_materialized-unity-data/sharedassets3.assets` | 24.41 MiB | 31 | 14 | 5 | 10 | 0 | 0 | 0 |
| `/Users/Admin/Documents/创世兵魂/_安全解压资源/base.apk.1__c90fbfd89f75/_materialized-unity-data/sharedassets4.assets` | 83.48 MiB | 114 | 0 | 66 | 41 | 2 | 2 | 0 |
| `/Users/Admin/Documents/创世兵魂/_安全解压资源/base.apk.1__c90fbfd89f75/_materialized-unity-data/sharedassets5.assets` | 34.58 MiB | 82 | 24 | 18 | 36 | 0 | 0 | 0 |
| `/Users/Admin/Documents/创世兵魂/_安全解压资源/兵魂回忆录.apk.1__57cb40fcff6a/_materialized-unity-data/sharedassets0.assets` | 37.95 MiB | 38 | 0 | 1 | 16 | 0 | 3 | 4 |
| `/Users/Admin/Documents/创世兵魂/_安全解压资源/兵魂回忆录.apk.1__57cb40fcff6a/_materialized-unity-data/sharedassets1.assets` | 2.13 MiB | 7 | 0 | 0 | 3 | 0 | 0 | 0 |
| `/Users/Admin/Documents/创世兵魂/_安全解压资源/兵魂回忆录.apk.1__57cb40fcff6a/_materialized-unity-data/sharedassets2.assets` | 79.61 MiB | 238 | 0 | 0 | 117 | 0 | 1 | 0 |
| `/Users/Admin/Documents/创世兵魂/_安全解压资源/兵魂回忆录.apk.1__57cb40fcff6a/_materialized-unity-data/sharedassets3.assets` | 1.79 MiB | 7 | 0 | 0 | 3 | 0 | 0 | 0 |
| `/Users/Admin/Documents/创世兵魂/_安全解压资源/兵魂回忆录.apk.1__57cb40fcff6a/_materialized-unity-data/sharedassets4.assets` | 100.20 MiB | 618 | 462 | 89 | 43 | 0 | 3 | 2 |
| `/Users/Admin/Documents/创世兵魂/_安全解压资源/兵魂回忆录.apk.1__57cb40fcff6a/_materialized-unity-data/sharedassets5.assets` | 2.69 MiB | 5 | 0 | 0 | 2 | 0 | 0 | 0 |
| `/Users/Admin/Documents/创世兵魂/_安全解压资源/兵魂回忆录.apk.1__57cb40fcff6a/_materialized-unity-data/sharedassets6.assets` | 13.21 MiB | 23 | 5 | 9 | 5 | 0 | 1 | 0 |
| `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/level0` | 1.51 MiB | 7597 | 0 | 0 | 0 | 0 | 0 | 12 |
| `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/sharedassets0.assets` | 264.28 MiB | 378 | 0 | 8 | 123 | 1 | 0 | 0 |
| `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/sharedassets1.assets` | 96.87 MiB | 568 | 0 | 2 | 264 | 8 | 3 | 0 |
| `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/sharedassets2.assets` | 23.21 MiB | 589 | 41 | 31 | 82 | 29 | 19 | 12 |

## 武器名称候选

- `/Users/Admin/Documents/创世兵魂/_安全解压资源/兵魂回忆录.apk.1__57cb40fcff6a/_materialized-unity-data/sharedassets0.assets` — Texture2D `rifle 步枪_爱给网_aigei_com` (path id 7)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/兵魂回忆录.apk.1__57cb40fcff6a/_materialized-unity-data/sharedassets0.assets` — Texture2D `武器` (path id 10)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/兵魂回忆录.apk.1__57cb40fcff6a/_materialized-unity-data/sharedassets0.assets` — Sprite `rifle 步枪_爱给网_aigei_com` (path id 27)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/兵魂回忆录.apk.1__57cb40fcff6a/_materialized-unity-data/sharedassets0.assets` — Sprite `武器` (path id 30)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/兵魂回忆录.apk.1__57cb40fcff6a/_materialized-unity-data/sharedassets4.assets` — Texture2D `CS-枪支-913yeskycs23_爱给网_aigei_com` (path id 133)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/兵魂回忆录.apk.1__57cb40fcff6a/_materialized-unity-data/sharedassets4.assets` — Sprite `CS-枪支-913yeskycs23_爱给网_aigei_com` (path id 618)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/level0` — GameObject `切换主武器` (path id 40)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/level0` — GameObject `切换投掷武器` (path id 72)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/level0` — GameObject `切换副武器` (path id 521)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/level0` — GameObject `武器选择` (path id 747)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/level0` — GameObject `武器选择` (path id 782)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/level0` — GameObject `狙击镜移动速度` (path id 1159)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/level0` — GameObject `丢弃武器` (path id 1197)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/level0` — GameObject `切换近身武器` (path id 1326)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/level0` — GameObject `武器选择` (path id 1359)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/level0` — GameObject `武器选择` (path id 1419)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/level0` — GameObject `切换武器` (path id 1482)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/level0` — GameObject `武器背包` (path id 1518)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/sharedassets2.assets` — Material `圣诞手雷` (path id 23)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/sharedassets2.assets` — Texture2D `ak47_sd.28568` (path id 44)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/sharedassets2.assets` — Texture2D `圣诞手雷` (path id 61)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/sharedassets2.assets` — Texture2D `ak47_paozhang.28568` (path id 82)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/sharedassets2.assets` — AudioClip `Fireinthehole_Grenade` (path id 203)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/sharedassets2.assets` — AudioClip `rifle` (path id 204)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/sharedassets2.assets` — AudioClip `Fireinthehole_Grenade` (path id 212)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/sharedassets2.assets` — Sprite `ak47_sd.28568` (path id 235)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/sharedassets2.assets` — Sprite `ak47_paozhang.28568` (path id 248)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/sharedassets2.assets` — GameObject `Weapon` (path id 267)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/sharedassets2.assets` — GameObject `All_Weapon` (path id 276)
- `/Users/Admin/Documents/创世兵魂/_安全解压资源/遗迹杀戮_1_.apk.1__c9745db1614e/_materialized-unity-data/sharedassets2.assets` — GameObject `Weapon` (path id 336)

## 结论门禁

- 名称命中只用于候选定位，不代表资源闭包完整。
- 正式接入仍必须同时证明模型、贴图/材质、第一人称手部骨架和动作依赖。
- DEX、SO 与来源不明程序未执行；PAK/UCAS/UTOC 未使用猜测性解包。
