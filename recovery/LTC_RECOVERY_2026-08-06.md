# CrossFire LTC 静态恢复

日期：2026-08-06

## 结论

此前标记为“高熵、未知私有格式”的 364 个 loose LTC 已全部只读解码为 LithTech LTA 文本，失败 0。解码输出共 51,657,941 字节，每项均记录源文件 SHA-256、输出 SHA-256、终止方式、文本结构证据和隔离输出路径；原始 LTC 未改写。

## 已验证格式

1. CrossFire 在标准 LithTech LTC v0 位流外重复异或 16 字节掩码 `54 83 b2 e1 10 3f 6e 9d cc fb 2a 59 88 b7 e6 15`。
2. 去除掩码后，按 LithTech 的 LSB-first bit file、12-bit 窗口偏移、4-bit 长度和 LZSS 回溯窗口解码。
3. 224 个输入含 offset=0 显式结束标记；140 个在物理 EOF 结束。两类均产生可审计 LTA 文本。
4. 本地唯一同名明文对 `AI3_FatalCanyon_DZ.LTC/.LTA` 解码后 126,744 字节逐字节完全一致，构成独立于“看起来像文本”的强校验。
5. 364/364 输出均以 LTA 根节点或注释开头；358 个通过保守括号/引号扫描，其余 6 个保留源文本结构告警，未擅自修补配置内容。

位流和窗口规则依据公开的 LithTech 源码交叉实现：[`ltacompressedfile.cpp`](https://github.com/jsj2008/lithtech/blob/master/libs/ltamgr/ltacompressedfile.cpp)、[`ltabitfile.cpp`](https://github.com/jsj2008/lithtech/blob/master/libs/ltamgr/ltabitfile.cpp)、[`lzsslimits.h`](https://github.com/jsj2008/lithtech/blob/master/libs/ltamgr/lzsslimits.h)。未下载或执行论坛转换器、客户端 EXE/DLL。

## 实现与证据

- 解码器/CLI：`tools/lithtech_ltc.py`。
- 统一审计：`tools/audit_special_formats.py`（tool version 8）。
- 回归测试：`tools/test_audit_special_formats.py`，覆盖字面量、回溯 span、显式结束标记与物理 EOF。
- 汇总：`recovery/SPECIAL_FORMAT_CONVERSION_LEDGER_2026-08-04.md`。
- 逐项机器账本：`recovery/special-format-conversion-ledger.json`。
- 隔离明文输出：`recovery/special-formats/ltc-decoded/`（可由账本和工具重复生成，不纳入 Git）。

统一 provenance 在后续 RF199 与 LTB layout-v7 恢复纳入后，当前验证 12/12 清单、148,829 个输出、21,546,511,271 字节，错误 0。

## 使用

```bash
python3 tools/lithtech_ltc.py input.LTC output.LTA
```

若目标已存在且内容不同，CLI 会拒绝覆盖。批量恢复继续由统一专用格式审计入口完成。
