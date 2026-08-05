# 全空间原始样本盘点验收（2026-08-04）

## 结论

工作区根目录及 `CF2.0`、`_解压资源`、`_安全解压资源`、`原程序恢复`、旧客户端、恢复工程和散落发布包已经完成只读静态盘点。机器总账为 `recovery/root-resource-index.json`，逐样本人工总账为 `recovery/ROOT_RESOURCE_INDEX.md`。

- 资源池：8 个。
- 文件或 Unity `_Data` 样本：3,828 个。
- 压缩包/发布容器：283 个，其中普通包可列目录 36 个、REZ 242 个、需专用只读解析器的 Unreal 容器 5 个。
- 精确 SHA-256 重复组：860 个。
- 记录到版本证据的样本：215 个；记录到架构证据的样本：2,035 个。无法从静态文件头可靠确定的值显式保存为 `null`/空数组，不进行猜测。
- 样本总账摘要 SHA-256：`e88424a9d72e1a76939ec39ce77dc7b766c670d6c1b835f132976fc9c9ac0e4e`。

## 覆盖格式

总账覆盖 APK/APK.1、EXE、DLL、SO、Unity `_Data`、`.assets`、`.resS/.resource`、UnityPackage、REZ、PAK/UCAS/UTOC、SWF、ZIP/RAR/7Z。除后缀外，扫描器还使用文件头识别扩展名损坏或伪装的 UnityFS、ZIP、RAR、7Z、PE、ELF、SWF 和 ATF；本次额外识别出 `.xem/.fxd` 中的 4 个 PE 库及 2 个非标准后缀 PE 可执行文件。

每条记录包含原路径、样本类型、字节数、SHA-256、可识别版本、架构、引擎、权限、来源分级、重复组及规范路径。`workspace_original` 与 `extracted_or_generated` 分开记录，避免把提取结果误当成新原始来源。

## 原始样本只读库

`tools/seal_original_samples.py` 已将 336 个 `workspace_original` 样本保存到工作区隐藏目录 `.original-samples-vault`：

- 逻辑大小 `24,018,152,005` 字节。
- 仅使用 APFS `cp -c` 写时复制克隆；克隆失败时明确停止，不回退为完整字节复制。
- 每个快照均重新校验 SHA-256 并设为 `0444`，所有快照目录设为 `0555`。
- 快照清单位于 `.original-samples-vault/manifest.json`，记录源路径、快照路径、大小、哈希和生成方式。
- 源样本内容、权限和时间戳不被修改；快照目录被总索引排除。建库后复跑索引仍为 3,828 条，摘要保持不变。

提取工具继续使用“来源相对路径 + 来源 SHA-256”命名隔离输出。同名 `Assets/*` 不会在不同包之间互相覆盖；现有 APK 与 UnityPackage 提取清单分别为 `recovery/safe-apk-extraction-index.json` 和 `recovery/safe-extraction-index.json`。

## 验证

- `python3 -m unittest -v tools/test_audit_root_resources.py tools/test_seal_original_samples.py tools/test_extract_recovered_archives.py`：9/9 通过。
- 覆盖 PE/APK/Unity 识别、版本与架构、扩展名损坏的 UnityFS、精确重复、确定性摘要、不修改源文件、路径穿越、UnityPackage 路径物化与 Unity 分片缺号拒绝。
- 全量扫描完成后检查：3,828 条记录均具备必需字段和 64 位十六进制 SHA-256，无 `unknown_sample`。

## 后续边界

本轮完成的是样本库和总索引，不等同于所有容器内容均已逆向。CF 私有 REZ、Unreal IoStore、Flash 内部资源、APK DEX/Manifest 以及各 Windows 发布版本的托管/原生行为仍按后续清单分批静态分析。未知 EXE/DLL 未被执行、注册或注入。
