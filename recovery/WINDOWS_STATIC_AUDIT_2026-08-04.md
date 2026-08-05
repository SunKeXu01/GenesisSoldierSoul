# Windows 客户端静态逆向报告

此报告由 `tools/audit_windows_clients.py` 生成。未知 EXE/DLL 从未被执行、注册或注入；
PE 结构由审计器直接按字节解析，托管 DLL 仅交给 `monodis` 做静态元数据/IL 读取。

## 总览

- Unity Windows 发布包：12
- Unreal Windows 发布包：1
- 发布根目录：14
- PE 出现次数 / 去重后模块：948 / 309
- Mono `Assembly-CSharp.dll`：12
- Unity 关键资源文件：428
- Unity 恢复覆盖：12/12

## Unity 发布包

| `_Data` | Unity | 后端 | 架构 | 场景 | Shared | Bundle | Streaming | 已恢复工程 |
|---|---|---|---|---:|---:|---:|---:|---|
| `_解压资源/六月单机（更新金字塔）/Test_Data` | 2018.4.14c1 | Mono | x86 | 13 | 13 | 0 | 0 | AssetRipper |
| `_解压资源/创世：遗迹杀戮/创世：遗迹杀戮/Relic Killing_Data` | 5.2.5f1 | Mono | x86 | 2 | 3 | 0 | 0 | 逐对象清单 |
| `_解压资源/单机怀旧创世兵魂V1.2.1/Test_Data` | 2018.4.14c1 | Mono | x86 | 13 | 13 | 0 | 0 | AssetRipper |
| `_解压资源/新工地/新版工地_Data` | 2019.1.4f1 | Mono | x86 | 1 | 1 | 0 | 0 | AssetRipper |
| `_解压资源/旧工地重现-天梯王者归来/梦幻兵魂号_Data` | 2019.1.4f1 | Mono | x86 | 7 | 7 | 0 | 0 | AssetRipper |
| `_解压资源/炼钢厂的前夜/梦幻兵魂号_Data` | 2019.1.4f1 | Mono | x86 | 9 | 9 | 0 | 0 | AssetRipper |
| `_解压资源/生化小镇电脑版/CSBH_Data` | 2018.4.14c1 | Mono | x86 | 3 | 3 | 0 | 0 | AssetRipper |
| `_解压资源/界面怀旧版（十一月更）/界面怀旧版（十一月更）/CSBH.11.1_Data` | 5.0.2f1 | Mono | x86 | 32 | 33 | 0 | 0 | AssetRipper |
| `_解压资源/离别作/Test_Data` | 2018.4.14c1 | Mono | x86 | 15 | 15 | 0 | 0 | AssetRipper |
| `_解压资源/致敬辉煌的曾经/梦幻兵魂号_Data` | 2019.1.4f1 | Mono | x86 | 5 | 5 | 0 | 0 | 逐对象清单 |
| `_解压资源/迷宫-冰与火之歌/梦幻兵魂号_Data` | 2019.1.4f1 | Mono | x86 | 7 | 7 | 0 | 0 | AssetRipper |
| `_解压资源/退魔炮测/腿摸跑/新建文件夹/9.14_Data` | 5.2.5f1 | Mono | x86 | 0 | 1 | 0 | 0 | 逐对象清单 |

## Mono 游戏程序集

### `_解压资源/六月单机（更新金字塔）/Test_Data/Managed/Assembly-CSharp.dll`

- 类型 / 字段 / 方法 / 常量：336 / 2549 / 2789 / 286
- 游戏流程 / 武器 / 网络 / 资源语义类型：34 / 35 / 8 / 26
- 完整 IL：`recovery/windows-mono-metadata/01_Test/Assembly-CSharp.il`

### `_解压资源/创世：遗迹杀戮/创世：遗迹杀戮/Relic Killing_Data/Managed/Assembly-CSharp.dll`

- 类型 / 字段 / 方法 / 常量：54 / 649 / 307 / 0
- 游戏流程 / 武器 / 网络 / 资源语义类型：12 / 8 / 2 / 9
- 完整 IL：`recovery/windows-mono-metadata/02_Relic_Killing/Assembly-CSharp.il`

### `_解压资源/单机怀旧创世兵魂V1.2.1/Test_Data/Managed/Assembly-CSharp.dll`

- 类型 / 字段 / 方法 / 常量：335 / 2547 / 2782 / 286
- 游戏流程 / 武器 / 网络 / 资源语义类型：34 / 35 / 8 / 26
- 完整 IL：`recovery/windows-mono-metadata/03_Test/Assembly-CSharp.il`

### `_解压资源/新工地/新版工地_Data/Managed/Assembly-CSharp.dll`

- 类型 / 字段 / 方法 / 常量：8 / 21 / 17 / 3
- 游戏流程 / 武器 / 网络 / 资源语义类型：2 / 1 / 0 / 1
- 完整 IL：`recovery/windows-mono-metadata/04_新版工地/Assembly-CSharp.il`

### `_解压资源/旧工地重现-天梯王者归来/梦幻兵魂号_Data/Managed/Assembly-CSharp.dll`

- 类型 / 字段 / 方法 / 常量：30 / 127 / 171 / 6
- 游戏流程 / 武器 / 网络 / 资源语义类型：8 / 0 / 0 / 3
- 完整 IL：`recovery/windows-mono-metadata/05_梦幻兵魂号/Assembly-CSharp.il`

### `_解压资源/炼钢厂的前夜/梦幻兵魂号_Data/Managed/Assembly-CSharp.dll`

- 类型 / 字段 / 方法 / 常量：31 / 136 / 180 / 6
- 游戏流程 / 武器 / 网络 / 资源语义类型：8 / 0 / 0 / 3
- 完整 IL：`recovery/windows-mono-metadata/06_梦幻兵魂号/Assembly-CSharp.il`

### `_解压资源/生化小镇电脑版/CSBH_Data/Managed/Assembly-CSharp.dll`

- 类型 / 字段 / 方法 / 常量：15 / 44 / 58 / 10
- 游戏流程 / 武器 / 网络 / 资源语义类型：2 / 2 / 0 / 1
- 完整 IL：`recovery/windows-mono-metadata/07_CSBH/Assembly-CSharp.il`

### `_解压资源/界面怀旧版（十一月更）/界面怀旧版（十一月更）/CSBH.11.1_Data/Managed/Assembly-CSharp.dll`

- 类型 / 字段 / 方法 / 常量：116 / 280 / 355 / 0
- 游戏流程 / 武器 / 网络 / 资源语义类型：12 / 1 / 0 / 4
- 完整 IL：`recovery/windows-mono-metadata/08_CSBH.11.1/Assembly-CSharp.il`

### `_解压资源/离别作/Test_Data/Managed/Assembly-CSharp.dll`

- 类型 / 字段 / 方法 / 常量：338 / 2557 / 2799 / 286
- 游戏流程 / 武器 / 网络 / 资源语义类型：35 / 35 / 8 / 26
- 完整 IL：`recovery/windows-mono-metadata/09_Test/Assembly-CSharp.il`

### `_解压资源/致敬辉煌的曾经/梦幻兵魂号_Data/Managed/Assembly-CSharp.dll`

- 类型 / 字段 / 方法 / 常量：18 / 71 / 98 / 3
- 游戏流程 / 武器 / 网络 / 资源语义类型：4 / 0 / 0 / 2
- 完整 IL：`recovery/windows-mono-metadata/10_梦幻兵魂号/Assembly-CSharp.il`

### `_解压资源/迷宫-冰与火之歌/梦幻兵魂号_Data/Managed/Assembly-CSharp.dll`

- 类型 / 字段 / 方法 / 常量：21 / 88 / 133 / 3
- 游戏流程 / 武器 / 网络 / 资源语义类型：4 / 0 / 0 / 3
- 完整 IL：`recovery/windows-mono-metadata/11_梦幻兵魂号/Assembly-CSharp.il`

### `_解压资源/退魔炮测/腿摸跑/新建文件夹/9.14_Data/Managed/Assembly-CSharp.dll`

- 类型 / 字段 / 方法 / 常量：24 / 191 / 98 / 5
- 游戏流程 / 武器 / 网络 / 资源语义类型：3 / 6 / 0 / 1
- 完整 IL：`recovery/windows-mono-metadata/12_9.14/Assembly-CSharp.il`

## 原生 PE 与关键调用关系

- 原生去重模块：74
- 具有 Win32 资源段：65
- 常见导入模块：kernel32.dll (72), user32.dll (33), advapi32.dll (27), ole32.dll (25), gdi32.dll (21), winmm.dll (19), ws2_32.dll (18), oleaut32.dll (18), shell32.dll (17), version.dll (15), shlwapi.dll (14), msvcrt.dll (11), psapi.dll (10), wininet.dll (7), imm32.dll (7)
- 每个模块的节区哈希/熵、导入函数、导出符号、资源类型、配置/网络/资源字符串均在 JSON 中保留。

## 跨版本补全线索

- `_解压资源/单机怀旧创世兵魂V1.2.1/Test_Data`：恢复工程资产 5752，关键资源 63 个 / 554799106 字节
- `_解压资源/离别作/Test_Data`：恢复工程资产 5722，关键资源 70 个 / 534990103 字节
- `_解压资源/六月单机（更新金字塔）/Test_Data`：恢复工程资产 5645，关键资源 63 个 / 508782026 字节
- `_解压资源/迷宫-冰与火之歌/梦幻兵魂号_Data`：恢复工程资产 1289，关键资源 28 个 / 258406248 字节
- `_解压资源/炼钢厂的前夜/梦幻兵魂号_Data`：恢复工程资产 901，关键资源 34 个 / 156517289 字节
- `_解压资源/界面怀旧版（十一月更）/界面怀旧版（十一月更）/CSBH.11.1_Data`：恢复工程资产 574，关键资源 74 个 / 307886408 字节
- `_解压资源/旧工地重现-天梯王者归来/梦幻兵魂号_Data`：恢复工程资产 454，关键资源 29 个 / 151944480 字节
- `_解压资源/生化小镇电脑版/CSBH_Data`：恢复工程资产 325，关键资源 18 个 / 162550364 字节
- `_解压资源/新工地/新版工地_Data`：恢复工程资产 46，关键资源 7 个 / 11216012 字节
- `_解压资源/创世：遗迹杀戮/创世：遗迹杀戮/Relic Killing_Data`：恢复工程资产 0，关键资源 12 个 / 446777192 字节
- `_解压资源/致敬辉煌的曾经/梦幻兵魂号_Data`：恢复工程资产 0，关键资源 23 个 / 114809036 字节
- `_解压资源/退魔炮测/腿摸跑/新建文件夹/9.14_Data`：恢复工程资产 0，关键资源 7 个 / 39771032 字节

跨包完全相同的关键资源哈希组：40。
具体共享路径与各包独有的游戏流程/武器/网络/资源类型见机器可读 JSON。

## 安全边界

- `target_executed: false`
- `target_registered: false`
- `target_injected: false`
- 原始样本未改写；所有输出位于 `GenesisSoldierSoul/recovery/`。
