# Unreal 容器只读恢复报告（2026-08-05）

## 结论

工作区内 5 个根 Unreal 容器和 1 个 Android OBB 内嵌 Pak 均已完成只读结构处理；没有执行样本中的 EXE/DLL，也没有改写原容器。

- Windows `GenesisFantasy`：`.uproject` 经 FPakEntry SHA-1 验证后提取，`EngineAssociation` 为 `5.3`。
- Android `MapPreview`：`.uproject` 经 FPakEntry SHA-1 验证后提取，`EngineAssociation` 为 `4.27`。
- 两份 Pak 均为 Pak v11；主索引、路径哈希二级索引和完整目录二级索引 SHA-1 全部匹配。
- `GenesisFantasy-Windows.utoc` 为 UTOC v5，5659 个 TOC 条目、7857 个压缩块、376 个目录、5163 个具名文件；压缩方法为 Oodle。
- 2 组 UTOC/UCAS 以同 stem 配对并保留双方 SHA-256。

## 恢复规模

| 容器 | 目录路径 | 已验证提取条目 | 字节 | 方法 |
| --- | ---: | ---: | ---: | --- |
| GenesisFantasy-Windows.pak | 1521 | 1521 | 52,394,396 | 未压缩 1,189 + Oodle 332 |
| MapPreview-Android_Multi.pak | 2253 | 2253 | 272,015,258 | 未压缩 1,205 + Zlib 62 + Oodle 986 |
| GenesisFantasy-Windows.utoc | 5163 | 目录索引恢复，不冒充内容导出 | — | IoStore/UCAS 内容级提取未实现 |
| 合计 | 8937 | 3774 | 324,409,654 | Pak 已无不支持标志；IoStore 内容仍只读保留 |

Android 全量输出包含 512 个 `.uasset`、6 个 `.umap`、518 个 `.uexp`、122 个 `.ubulk`、282 个 PNG、30 个 INI 和 2 个 shader bytecode 等；605 个路径位于 `MapPreview/` 游戏命名空间。Windows 输出主要为 ICU/配置/PNG/SVG/插件元数据。

### 包文件闭包

新增的闭包审计以经 SHA-1 验证的 Pak 目录和真实提取清单交叉核对，不解析或猜测 UObject 内容：

- 518 个已提取主包（512 个 `.uasset`、6 个 `.umap`）全部具备目录中声明的 `.uexp/.ubulk/.uptnl` 伴随文件，文件级闭包 518/518。
- `MapPreview/` 游戏命名空间 258 个主包与 `Engine/` 命名空间 260 个主包均已闭合。
- 孤立伴随文件为 0；原先 164 个缺 Oodle 内容的包已全部补齐。
- “文件级闭包完整”只表示同 stem 的容器成员齐备；下述对象审计进一步验证其可反序列化性，但不会把 MaterialFunction 等对象误报成可直接导出的贴图或模型。

### UObject 只读解析

`tools/UnrealAssetAudit` 固定使用 MIT 许可的 UAssetAPI 1.1.0 和 `VER_UE4_27`，处理全部 518 个闭包完整候选：

- 518/518 package summary、import/export map 结构解析通过；
- 518/518 完整 UObject 反序列化通过，无 `.usmap` 依赖；
- 518/518 通过 UAssetAPI `VerifyBinaryEquality()`；
- 生成 518 份 UAssetAPI JSON 对象表示，共 124,967,020 字节，逐文件记录 SHA-256。

## 验证规则

工具按 Unreal Pak v11 `FPakEntry` 位域解析 32/64 位可变宽 offset/size、内联或显式块大小、块数、压缩方法与加密标志。方法 0 为未压缩，方法 1 通过显式提供且 SHA-256 锁定的 `pyooz 0.0.8` 解码 Oodle，方法 2 解码 Zlib。每个输出同时验证：

1. 编码表和 Pak 数据区边界；
2. 紧凑记录与序列化 `FPakEntry` 的大小/方法/块数/加密字段一致，压缩块连续且不超过声明块尺寸；
3. 存储内容的 SHA-1 与 `FPakEntry` 保存值一致，解压内容另存 SHA-1/SHA-256；
4. 输出路径拒绝绝对路径、`..` 和盘符逃逸；
5. 输出按源 SHA-256 隔离，并在统一来源账本中复核 SHA-256 与字节数。

`pyooz` wheel 的锁定 SHA-256 为 `7fd7b26bf34a293e2414b6361c16c4dff80c3ba0494fb89e85024c5fd136af61`；运行时还检查包名、版本、GPL-3.0-or-later 分类和唯一原生模块布局。Pak 两份目录的 `unsupported_flag_counts` 均为空。

## 证据

- 统一账本：`recovery/special-format-conversion-ledger.json`（工具版本 17）。
- 人工摘要：`recovery/SPECIAL_FORMAT_CONVERSION_LEDGER_2026-08-04.md`。
- Windows Pak 目录：`recovery/special-formats/unreal-index/GenesisFantasy-Windows__28a4551bc84d/pak-directory-index.json`。
- IoStore 目录：`recovery/special-formats/unreal-index/GenesisFantasy-Windows__f6b206b8b1ae/utoc-directory-index.json`。
- Android Pak 目录：`recovery/special-formats/unreal-index/MapPreview-Android_Multi__ee15b61ab0c0/pak-directory-index.json`。
- 支持内容输出：`recovery/special-formats/unreal-extracted/`。
- 包文件闭包清单：`recovery/special-formats/unreal-analysis/*/package-closure-v2.json`。
- UObject 审计：`recovery/special-formats/unreal-analysis/MapPreview-Android_Multi__ee15b61ab0c0/uassetapi-audit.json`。
- UObject JSON：`recovery/special-formats/unreal-object-json/MapPreview-Android_Multi__ee15b61ab0c0/`。
- 单元测试：`tools/test_audit_special_formats.py`，覆盖可变宽紧凑条目、Oodle、Zlib、哈希锁与重复执行稳定性。

统一转换来源门禁现为 13/13 清单、199,680 个输出、26,387,143,524 字节、错误 0。Pak Oodle 内容和 Android UObject 闭包已经完成；UTOC/UCAS 的 IoStore 内容仍保持只读目录级恢复，不冒充对象导出。
