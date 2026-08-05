# Unity 安装包与武器恢复索引

此文件由 `tools/audit_unity_recovery_sources.py` 生成。
审计过程只读取数据，不运行压缩包中的 EXE/DLL。

## 安装包总览

| Unity `_Data` | 版本 | 架构 | 共享包 | 关卡 | 已恢复工程 | 武器 Prefab |
|---|---|---:|---:|---:|---:|---:|
| `_解压资源/六月单机（更新金字塔）/Test_Data` | 未识别 | Mono | 13 | 13 | 是 | 12 |
| `_解压资源/创世：遗迹杀戮/创世：遗迹杀戮/Relic Killing_Data` | 未识别 | Mono | 3 | 2 | 否 | 0 |
| `_解压资源/单机怀旧创世兵魂V1.2.1/Test_Data` | 未识别 | Mono | 13 | 13 | 是 | 26 |
| `_解压资源/新工地/新版工地_Data` | 2019.1.4f1 | Mono | 1 | 1 | 是 | 0 |
| `_解压资源/旧工地重现-天梯王者归来/梦幻兵魂号_Data` | 2019.1.4f1 | Mono | 7 | 7 | 是 | 0 |
| `_解压资源/炼钢厂的前夜/梦幻兵魂号_Data` | 2019.1.4f1 | Mono | 9 | 9 | 是 | 0 |
| `_解压资源/生化小镇电脑版/CSBH_Data` | 未识别 | Mono | 3 | 3 | 是 | 0 |
| `_解压资源/界面怀旧版（十一月更）/界面怀旧版（十一月更）/CSBH.11.1_Data` | 未识别 | Mono | 33 | 32 | 是 | 0 |
| `_解压资源/离别作/Test_Data` | 未识别 | Mono | 15 | 15 | 是 | 12 |
| `_解压资源/致敬辉煌的曾经/梦幻兵魂号_Data` | 2019.1.4f1 | Mono | 5 | 5 | 否 | 0 |
| `_解压资源/迷宫-冰与火之歌/梦幻兵魂号_Data` | 2019.1.4f1 | Mono | 7 | 7 | 是 | 0 |
| `_解压资源/退魔炮测/腿摸跑/新建文件夹/9.14_Data` | 未识别 | Mono | 1 | 0 | 否 | 0 |

## 恢复武器候选

### `_解压资源/六月单机（更新金字塔）/Test_Data`

- `GameObject/MachinegunBulletDust.prefab` — 无定位点信号
- `GameObject/MachinegunBulletImpact.prefab` — 无定位点信号
- `GameObject/MachinegunBulletSpark.prefab` — 无定位点信号
- `GameObject/Pistol.prefab` — Muzzle
- `GameObject/PistolBullet.prefab` — 无定位点信号
- `GameObject/PistolBulletDebris.prefab` — 无定位点信号
- `GameObject/PistolBulletDust.prefab` — 无定位点信号
- `GameObject/PistolBulletImpact.prefab` — 无定位点信号
- `GameObject/PistolBulletSpark.prefab` — 无定位点信号
- `GameObject/PistolMuzzleFlash.prefab` — 无定位点信号
- `GameObject/PistolShell.prefab` — 无定位点信号
- `GameObject/ShotgunPelletRock1.prefab` — 无定位点信号
- 相关动画候选：1

### `_解压资源/单机怀旧创世兵魂V1.2.1/Test_Data`

- `GameObject/AssaultRifle01.prefab` — Clip, Main, Muzzle, RightHand, Trigger, WeaponMainLocator
- `GameObject/Grenade01.prefab` — WeaponMainLocator
- `GameObject/GrenadeExplosion.prefab` — 无定位点信号
- `GameObject/GrenadeLive.prefab` — 无定位点信号
- `GameObject/Knife01.prefab` — WeaponMainLocator
- `GameObject/KnifeAttack.prefab` — 无定位点信号
- `GameObject/MachinegunBullet.prefab` — 无定位点信号
- `GameObject/MachinegunBulletDust.prefab` — 无定位点信号
- `GameObject/MachinegunBulletImpact.prefab` — 无定位点信号
- `GameObject/MachinegunBulletSpark.prefab` — 无定位点信号
- `GameObject/MachinegunMuzzleFlash.prefab` — 无定位点信号
- `GameObject/MuzzleFlashPistol01.prefab` — 无定位点信号
- `GameObject/MuzzleFlashShotgun.prefab` — 无定位点信号
- `GameObject/Pistol.prefab` — Muzzle
- `GameObject/Pistol01.prefab` — FireLocator, RightHand
- `GameObject/PistolBullet.prefab` — 无定位点信号
- `GameObject/PistolBulletDebris.prefab` — 无定位点信号
- `GameObject/PistolBulletDust.prefab` — 无定位点信号
- `GameObject/PistolBulletImpact.prefab` — 无定位点信号
- `GameObject/PistolBulletSpark.prefab` — 无定位点信号
- `GameObject/PistolMuzzleFlash.prefab` — 无定位点信号
- `GameObject/PistolShell.prefab` — 无定位点信号
- `GameObject/Shotgun01.prefab` — WeaponMainLocator
- `GameObject/ShotgunPellet.prefab` — 无定位点信号
- `GameObject/ShotgunPelletRock1.prefab` — 无定位点信号
- `GameObject/ShotgunShellUsed.prefab` — 无定位点信号
- 相关动画候选：53

### `_解压资源/离别作/Test_Data`

- `GameObject/MachinegunBulletDust.prefab` — 无定位点信号
- `GameObject/MachinegunBulletImpact.prefab` — 无定位点信号
- `GameObject/MachinegunBulletSpark.prefab` — 无定位点信号
- `GameObject/Pistol.prefab` — Muzzle
- `GameObject/PistolBullet.prefab` — 无定位点信号
- `GameObject/PistolBulletDebris.prefab` — 无定位点信号
- `GameObject/PistolBulletDust.prefab` — 无定位点信号
- `GameObject/PistolBulletImpact.prefab` — 无定位点信号
- `GameObject/PistolBulletSpark.prefab` — 无定位点信号
- `GameObject/PistolMuzzleFlash.prefab` — 无定位点信号
- `GameObject/PistolShell.prefab` — 无定位点信号
- `GameObject/ShotgunPelletRock1.prefab` — 无定位点信号
- 相关动画候选：1

## 推荐恢复顺序

1. 优先使用已恢复 Prefab 且具有 `WeaponMainLocator` / `Muzzle` 的武器。
2. 用 GUID 依赖闭包复制 Mesh、Material、Texture、AnimationClip 和 AudioClip。
3. 在独立预览场景验收后再进入 WebGL 主工程。
4. 没有完整依赖的孤立 FBX/OBJ 只作候选，不直接对玩家开放。
