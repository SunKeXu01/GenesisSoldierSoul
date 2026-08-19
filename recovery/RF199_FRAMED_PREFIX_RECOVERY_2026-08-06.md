# 私有 REZ 连续帧资源恢复

日期：2026-08-06

## 结论

原先归为“完全未分帧”的两份 `RF019.REZ`，以及仅恢复 PNG/DDS 前缀的两份
`RF199.REZ`，现均从固定 REZ v1 数据区起点 `168` 开始按当前位置连续解析。
共严格恢复 38,567 个资源、3,654,297,962 个源字节，并为 15,973 个 DTX 和
257 个 TGA 生成 16,230 个 PNG。所有输出都按来源哈希隔离，原始 REZ 未改写。

恢复器只接受具有可证明结束边界的格式：PNG CRC/chunk、DDS/DTX 头与 mip
载荷、TGA 像素或 RLE packet、GIF sub-block/trailer、JPEG marker/EOI、结构化
配置结束标记、INI 文法、CFB DIFAT/FAT 分配范围、MP4 box chain，以及带显式
Segment 长度的 WebM，以及严格闭合的 SWF、FLV、HTML、CFSprite v5、
RPS 路径表和带已知后继的 CRC-valid 孤立 IDAT。遇到首个不支持字节立即停止；不搜索后续魔数、不做
carving，也不运行客户端程序。

## 分包结果

| 来源 | 连续资源 | 类型摘要 | 已恢复源字节 | 保留后缀 |
|---|---:|---|---:|---:|
| `CF2.0/CrossFire/rez/RF019.REZ` | 6,866 | TGA 1、DTX 6,858、GIF 2、JPEG 4、配置 1 | 1,185,934,368 | 1,176,204 |
| `CF2.0/CrossFire/rez2/RF019.REZ` | 9,111 | DTX 9,105、PNG 4、CFB 1、INI 1 | 1,659,713,437 | 1,661,180 |
| `CF2.0/CrossFire/rez/RF199.REZ` | 17,732 | PNG 17,424、DTX 10、DDS 1、TGA 202、Web bundle 4、UI layout 1、MP4 1、WebM 1、SWF 3、FLV 1、HTML 1、CFSprite 4、RPS 4、IDAT 75 | 688,355,390 | 2,869,658 |
| `CF2.0/CrossFire/rez2/RF199.REZ` | 4,858 | PNG 4,804、TGA 54 | 120,294,767 | 777,032 |
| 合计 | 38,567 | 21 类 | 3,654,297,962 | 6,484,074 |

四段未知后缀均保存精确停止偏移、大小、前缀、SHA-256 和熵证据。RF199 的
Web/媒体延伸恢复详见 `RF199_WEB_MEDIA_PREFIX_RECOVERY_2026-08-06.md`。当前前缀
不满足任何已实现的严格边界规则，因此不将后缀内部偶然出现的签名当作资源。
后续 world v85 严格解析已证明 `RF164.REZ` 和 `RF266.REZ` 的数据区
各自是一个完整 world，共 58,778,137 字节且尾字节为 0；详见
`LITHTECH_WORLD_V85_RECOVERY_2026-08-06.md`。`RB001.REZ` 后续已由加密目录
的 769/769 个 MD5 严格验证并完整闭合，见 `RB001_ENCRYPTED_DIRECTORY_RECOVERY_2026-08-09.md`；
现已没有未知 REZ 数据区尾段。

## 验证与产物

- 38,567 个源帧另加 16,230 个转换 PNG，共 54,797 个输出、
  4,155,555,345 字节，逐项大小与 SHA-256 验证通过。
- 统一 provenance：13/13 清单、195,675 个输出、25,918,542,502 字节、错误 0。
- 专项 Python 回归 32 项，覆盖各格式边界、混合顺序、错误 CRC、SWF/FLV/HTML、CP949 bundle、UI layout、CFSprite v5、RPS、孤立 IDAT、world v85、exact peer 歧义拒绝和未知尾部停止。
- 工具：`tools/recover_private_rez_framed_prefix.py`。
- 测试：`tools/test_recover_private_rez_framed_prefix.py`。
- 可再生成机器清单：`recovery/private-rez-framed-prefix-recovery.json`（Git 忽略）。
- 可再生成隔离输出：`recovery/special-formats/private-rez-png-prefix/`（Git 忽略）。
- 汇总账本：`recovery/special-format-conversion-ledger.json` 与
  `SPECIAL_FORMAT_CONVERSION_LEDGER_2026-08-04.md`。

原 7 个完全未分帧包均已获得连续边界；截至本报告初版保留 5 个有精确停止证据的
未知后缀。后续仍只在能证明边界时扩展解析器。

## 2026-08-09 后续进展

后缀确定性画像和逐字节差分先证明 RF019/RF199 的 971 个 zero-mirror 连续帧；
RB001 加密目录闭环后又确认 3 个目录表 zero-mirror 帧。合计 974 帧、6,609,886
个源字节，7 个目标包未知尾段均为 0。详见 `REZ_ZERO_MIRROR_RECOVERY_2026-08-09.md`。
