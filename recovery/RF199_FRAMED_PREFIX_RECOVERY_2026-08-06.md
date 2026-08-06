# 私有 REZ 连续帧资源恢复

日期：2026-08-06

## 结论

原先归为“完全未分帧”的两份 `RF019.REZ`，以及仅恢复 PNG/DDS 前缀的两份
`RF199.REZ`，现均从固定 REZ v1 数据区起点 `168` 开始按当前位置连续解析。
共严格恢复 28,274 个资源、3,253,474,922 个源字节，并为 15,973 个 DTX 和
257 个 TGA 生成 16,230 个 PNG。所有输出都按来源哈希隔离，原始 REZ 未改写。

恢复器只接受具有可证明结束边界的格式：PNG CRC/chunk、DDS/DTX 头与 mip
载荷、TGA 像素或 RLE packet、GIF sub-block/trailer、JPEG marker/EOI、结构化
配置结束标记、INI 文法、CFB DIFAT/FAT 分配范围、MP4 box chain，以及带显式
Segment 长度的 WebM，以及严格闭合的 SWF、FLV、HTML 和 CFSprite v5。遇到首个不支持字节立即停止；不搜索后续魔数、不做
carving，也不运行客户端程序。

## 分包结果

| 来源 | 连续资源 | 类型摘要 | 已恢复源字节 | 保留后缀 |
|---|---:|---|---:|---:|
| `CF2.0/CrossFire/rez/RF019.REZ` | 6,866 | TGA 1、DTX 6,858、GIF 2、JPEG 4、配置 1 | 1,185,934,368 | 1,176,204 |
| `CF2.0/CrossFire/rez2/RF019.REZ` | 9,111 | DTX 9,105、PNG 4、CFB 1、INI 1 | 1,659,713,437 | 1,661,180 |
| `CF2.0/CrossFire/rez/RF199.REZ` | 7,439 | PNG 7,213、DTX 10、DDS 1、TGA 202、Web bundle 4、UI layout 1、MP4 1、WebM 1、SWF 3、FLV 1、HTML 1、CFSprite 1 | 287,532,350 | 403,692,698 |
| `CF2.0/CrossFire/rez2/RF199.REZ` | 4,858 | PNG 4,804、TGA 54 | 120,294,767 | 777,032 |
| 合计 | 28,274 | 19 类 | 3,253,474,922 | 407,307,114 |

四段未知后缀均保存精确停止偏移、大小、前缀、SHA-256 和熵证据。RF199 的
Web/媒体延伸恢复详见 `RF199_WEB_MEDIA_PREFIX_RECOVERY_2026-08-06.md`。当前前缀
不满足任何已实现的严格边界规则，因此不将后缀内部偶然出现的签名当作资源。
后续 world v85 严格解析已证明 `RF164.REZ` 和 `RF266.REZ` 的数据区
各自是一个完整 world，共 58,778,137 字节且尾字节为 0；详见
`LITHTECH_WORLD_V85_RECOVERY_2026-08-06.md`。`RB001.REZ` 后续又由 492 个
同源 loose 精确区段恢复连续前缀，见 `RB001_EXACT_PEER_PREFIX_RECOVERY_2026-08-06.md`；
现已没有从数据区起点完全未知的包。

## 验证与产物

- 28,274 个源帧另加 16,230 个转换 PNG，共 44,504 个输出、
  3,754,732,305 字节，逐项大小与 SHA-256 验证通过。
- 统一 provenance：13/13 清单、185,382 个输出、25,517,719,462 字节、错误 0。
- 专项 Python 回归 28 项，覆盖各格式边界、混合顺序、错误 CRC、SWF/FLV/HTML、CP949 bundle、UI layout、CFSprite v5、world v85、exact peer 歧义拒绝和未知尾部停止。
- 工具：`tools/recover_private_rez_framed_prefix.py`。
- 测试：`tools/test_recover_private_rez_framed_prefix.py`。
- 可再生成机器清单：`recovery/private-rez-framed-prefix-recovery.json`（Git 忽略）。
- 可再生成隔离输出：`recovery/special-formats/private-rez-png-prefix/`（Git 忽略）。
- 汇总账本：`recovery/special-format-conversion-ledger.json` 与
  `SPECIAL_FORMAT_CONVERSION_LEDGER_2026-08-04.md`。

原 7 个完全未分帧包均已获得连续边界，当前保留 5 个有精确停止证据的未知
后缀。下一步只有在能证明边界时才继续扩展解析器。
