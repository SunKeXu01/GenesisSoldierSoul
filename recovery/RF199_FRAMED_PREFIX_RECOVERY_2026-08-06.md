# RF199 连续帧资源恢复

日期：2026-08-06

## 结论

此前归类为“未分帧”的两份 `RF199.REZ` 已从固定 REZ v1 数据区起点 `168` 开始，按当前位置连续恢复 9,520 个 PNG 和 1 个 DDS，共 141,831,713 字节。所有 PNG 均要求完整 `IHDR/IDAT/IEND` 结构和逐 chunk CRC；DDS 要求标准 124 字节头、可计算的像素格式/维度/mipmap 载荷长度。遇到首个不支持字节立即停止，未搜索后续魔数、未做 carving、未运行客户端程序。

## 分包结果

| 来源 | PNG | DDS | 已恢复字节 | 停止偏移 | 保留后缀 |
|---|---:|---:|---:|---:|---:|
| `CF2.0/CrossFire/rez/RF199.REZ` | 5,200 | 1（256×256 DXT1） | 70,400,840 | 70,401,008 | 620,824,208 |
| `CF2.0/CrossFire/rez2/RF199.REZ` | 4,320 | 0 | 71,430,873 | 71,431,041 | 49,640,926 |
| 合计 | 9,520 | 1 | 141,831,713 | — | 670,465,134 |

第一份包的实际连续序列是 `2,888 PNG → 1 DDS → 2,312 PNG`，证明不能把“首个非 PNG”误当成整个可证明前缀的终点；恢复器因此只在当前位置分派 PNG/DDS 两种严格解析器。第二份为连续 4,320 个 PNG。两份的未知后缀均原样保留。

## 验证

- 9,521/9,521 输出的大小与 SHA-256 和清单一致。
- Pillow 对 9,520 PNG 与 1 个 DDS 全部 `verify()` 成功，错误 0。
- 共 6,036 个唯一内容哈希、3,485 个重复实例、112 组图像尺寸；重复项仍逐来源/偏移留证，不擅自去重源记录。
- 统一 provenance 在后续 LTB layout-v3 恢复纳入后：12/12 清单、148,829 个输出、21,433,831,251 字节、错误 0。
- Python 回归覆盖精确偏移、PNG CRC 拒绝、DDS 头计算、PNG→DDS→PNG 顺序及未知后缀停止。

## 实现与产物

- 工具：`tools/recover_private_rez_framed_prefix.py`。
- 测试：`tools/test_recover_private_rez_framed_prefix.py`。
- 机器清单：`recovery/private-rez-framed-prefix-recovery.json`（可再生成，Git 忽略）。
- 隔离输出：`recovery/special-formats/private-rez-png-prefix/`（可再生成，Git 忽略）。
- 汇总账本：`recovery/special-format-conversion-ledger.json` 与 `SPECIAL_FORMAT_CONVERSION_LEDGER_2026-08-04.md`。

原 7 个完全未分帧包现缩小为：5 个仍从数据区起点未知，以及 2 个 RF199 仅剩已明确偏移和哈希证据的未知后缀。下一步仍不得绕过边界验证去搜索后缀内部资源。
