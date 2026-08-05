# Unreal 容器只读恢复报告（2026-08-05）

## 结论

工作区内 5 个根 Unreal 容器和 1 个 Android OBB 内嵌 Pak 均已完成只读结构处理；没有执行样本中的 EXE/DLL，也没有改写原容器。

- Windows `GenesisFantasy`：`.uproject` 经 FPakEntry SHA-1 验证后提取，`EngineAssociation` 为 `5.3`。
- Android `MapPreview`：`.uproject` 经 FPakEntry SHA-1 验证后提取，`EngineAssociation` 为 `4.27`。
- 两份 Pak 均为 Pak v11；主索引、路径哈希二级索引和完整目录二级索引 SHA-1 全部匹配。
- `GenesisFantasy-Windows.utoc` 为 UTOC v5，5659 个 TOC 条目、7857 个压缩块、376 个目录、5163 个具名文件；压缩方法为 Oodle。
- 2 组 UTOC/UCAS 以同 stem 配对并保留双方 SHA-256。

## 恢复规模

| 容器 | 目录路径 | 已验证提取的支持条目 | 字节 | 仍为 Oodle/不支持编码 |
| --- | ---: | ---: | ---: | ---: |
| GenesisFantasy-Windows.pak | 1521 | 1189 | 702641 | 332 |
| MapPreview-Android_Multi.pak | 2253 | 1267（未压缩 1205 + Zlib 62） | 11969853 | 986 |
| GenesisFantasy-Windows.utoc | 5163 | 目录索引恢复，不冒充内容导出 | — | Oodle/UCAS 内容待可信解码器 |
| 合计 | 8937 | 2456 | 12672494 | Pak 1318 + IoStore 内容 |

Android 未压缩输出包含 220 个 `.uasset`、65 个 `.uexp`、206 个 PNG（含大小写扩展名）、14 个 INI、2 个 shader bytecode 等；其中 84 个路径位于 `MapPreview/` 游戏命名空间，已覆盖多张地图的纹理候选。Windows 未压缩输出主要为 ICU/配置/PNG/SVG/插件元数据。

### 包文件闭包

新增的闭包审计以经 SHA-1 验证的 Pak 目录和真实提取清单交叉核对，不解析或猜测 UObject 内容：

- 220 个已提取主包（`.uasset/.umap`）中，56 个具备目录中声明的全部 `.uexp/.ubulk/.uptnl` 伴随文件，164 个仍缺伴随文件；不存在“目录未声明外置伴随文件”的自包含候选。
- `MapPreview/` 游戏命名空间有 71 个主包；其中 8 个文件级闭包完整，均为第一人称武器/角色使用的 Material Layer 包；其余 63 个仍缺 Oodle 编码伴随文件。
- 另有 9 个已提取 `.uexp` 的主包仍未提取，列为孤立伴随文件，不冒充完整资源。
- “文件级闭包完整”只表示同 stem 的容器成员齐备；下述对象审计进一步验证其可反序列化性，但不会把 MaterialFunction 等对象误报成可直接导出的贴图或模型。

### UObject 只读解析

新增 `tools/UnrealAssetAudit`，固定使用 MIT 许可的 UAssetAPI 1.1.0 和 `VER_UE4_27`，只处理上述 56 个闭包完整候选：

- 56/56 package summary、import/export map 结构解析通过；
- 56/56 完整 UObject 反序列化通过，无 `.usmap` 依赖；
- 56/56 通过 UAssetAPI `VerifyBinaryEquality()`，证明读后重序列化与原始 `.uasset/.uexp` 字节一致；
- 生成 56 份 UAssetAPI JSON 对象表示，共 585,357 字节，逐文件记录 SHA-256；
- 游戏命名空间的 8 个对象均识别为 `MaterialFunction`/`NormalExport`，不是可直接转为 PNG/GLTF 的媒体载荷，因此标准媒体转换项仍保持未完成。

## 验证规则

工具仅接受两种已完全确认的 Pak v11 编码：紧凑标志 `0xe0000000` 的未压缩条目，以及压缩方法索引 2、序列化方法同为 2 的标准 Zlib 分块条目。每个输出同时验证：

1. 编码表和 Pak 数据区边界；
2. 紧凑记录与序列化 `FPakEntry` 的大小/方法/加密字段一致，Zlib 块连续且不超过声明块尺寸；
3. 存储内容的 SHA-1 与 `FPakEntry` 保存值一致，解压内容另存 SHA-1/SHA-256；
4. 输出路径拒绝绝对路径、`..` 和盘符逃逸；
5. 输出按源 SHA-256 隔离，并在统一来源账本中复核 SHA-256 与字节数。

Oodle 或其他未实现标志只计数并保留，不猜测、不输出伪内容。

## 证据

- 统一账本：`recovery/special-format-conversion-ledger.json`（工具版本 5）。
- 人工摘要：`recovery/SPECIAL_FORMAT_CONVERSION_LEDGER_2026-08-04.md`。
- Windows Pak 目录：`recovery/special-formats/unreal-index/GenesisFantasy-Windows__28a4551bc84d/pak-directory-index.json`。
- IoStore 目录：`recovery/special-formats/unreal-index/GenesisFantasy-Windows__f6b206b8b1ae/utoc-directory-index.json`。
- Android Pak 目录：`recovery/special-formats/unreal-index/MapPreview-Android_Multi__ee15b61ab0c0/pak-directory-index.json`。
- 支持内容输出：`recovery/special-formats/unreal-extracted/`。
- 包文件闭包清单：`recovery/special-formats/unreal-analysis/*/package-closure.json`。
- UObject 审计：`recovery/special-formats/unreal-analysis/MapPreview-Android_Multi__ee15b61ab0c0/uassetapi-audit.json`。
- UObject JSON：`recovery/special-formats/unreal-object-json/MapPreview-Android_Multi__ee15b61ab0c0/`。
- 单元测试：`tools/test_audit_special_formats.py`，11/11 通过。

统一转换来源门禁现为 8/8 清单、53,196 个输出、1,513,588,932 字节、错误 0。剩余 Oodle 内容恢复继续归入“对象提取”和“标准媒体转换”未完成项，不影响“容器已只读处理”的结论。
