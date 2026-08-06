# 私有 REZ 内容恢复报告（2026-08-05）

## 结论

CrossFire 私有 RF/RB 包保留标准 REZ v1 外层头，但目录块经过私有处理。静态分析确认 208 个包的数据区由独立 LZMA-Alone 流串接组成，每条流对应一个资源；工具只在属性字节 `0x5d`、16 MiB 字典、声明长度、流 EOF 和解码长度全部一致时接受输出。

没有猜测目录密钥、运行客户端 EXE/DLL，原始包保持只读。遇到非 LZMA 区段立即停止并保留偏移、大小、前缀与熵证据。

## 恢复规模

- REZ 样本：242 个；其中标准包 1 个、可恢复串接 LZMA 私有包 208 个、空/仅填充数据区 26 个；原 7 个未分帧 raw/加密数据区中，两份 RF199 已恢复连续帧前缀，现为 5 个完全未分帧包 + 2 个已恢复前缀后的未知后缀。
- 验证 LZMA 资源流：46,242 条，解码 13,813,267,158 字节。
- 51 条与现存 loose 文件 SHA-256 完全一致；其余 46,191 条、13,791,715,442 字节按源哈希隔离物化。
- 明确签名包括 4,163 WAV、346 PNG、44 DDS、898 文本、2 TTF、1 OTF、1 MP3 和 1 个嵌套标准 REZ。
- 进一步确认 28,776 个 DTX v−5 贴图、8,721 个 LTB model、163 个 LithTech world/version 85 数据和 2,061 个固定头 raw texture。

## 标准贴图转换

DTX 转换器支持以下经尺寸关系验证的格式：

- 标识 3：BGRA8888；
- 标识 4：DXT1/BC1；
- 标识 5：DXT3/BC2；
- 标识 6：DXT5/BC3。

私有 raw texture 必须满足固定 18/44 字节头、明确宽高/位深，且文件总长必须精确等于头长加像素字节数。24 位按 BGR888、32 位按 BGRA8888 转换；唯一 16 位样本因通道打包未被证明而保留原文件。

转换结果：

- DTX→PNG：28,776；
- raw texture→PNG：2,060；
- 合计：30,836/30,836，5,025,788,415 字节，失败 0；
- 抽检 DXT1、DXT5、BGRA8888 和 BGR888 输出均呈现正常武器贴图/特效图像，没有明显通道错位。

## LTB 几何转换

基于 Cote-Duke LTB2X 与公开 CrossFire LithTech 运行时/打包器的结构规则独立实现了有界 Python 解析器；第三方出处记录在转换清单，未运行其 Windows 示例程序。layout-v3 按运行时定义解析 rigid/skeletal/vertex-animated/null render object、四路顶点流、matrix palette/骨骼重索引、顶点动画重复表和 OBB，并验证对象尺寸、有限浮点、索引范围和完整 GLB 2.0 块结构。

- 候选 LTB model：8,721；严格转换/复核 GLB：8,721；保留失败：0。
- GLB 共含 32,608 个网格、23,507,870 顶点、20,749,724 三角形，输出 909,248,808 字节。
- 321 个复合布局文件额外严格解析 14,896 条骨骼元数据；尚未写入 glTF skin/动画，不伪称已恢复蒙皮动画。
- 输出保留源坐标，不做未经证明的轴变换；仅声明网格几何，不伪称已恢复骨骼、蒙皮或动画。
- 每个成功项保存输入/输出哈希和 GLB 结构验证；每个失败项保存源包、stream index、输入路径和具体错误。

## 仍未完成

- 两份 RF199 已从固定数据区起点严格恢复 9,520 PNG + 1 DDS（141,831,713 字节），并在首个未知字节停止；详见 `RF199_FRAMED_PREFIX_RECOVERY_2026-08-06.md`。
- 仍完全未分帧的 5 个包为 RB001、两份 RF019、RF164、RF266；另保留两份 RF199 共 670,465,134 字节未知后缀。在没有可信边界前不做整段魔数 carving。
- LTB 静态几何 8,721/8,721 全部通过；321 个文件已有骨骼层级/绑定矩阵元数据，但完整 joints/weights/skin/动画仍保留在原 LTB 中。详见 `LTB_RUNTIME_LAYOUT_V3_RECOVERY_2026-08-06.md`。
- 364 个 loose LTC 已在 2026-08-06 全部恢复为 LTA：固定 16 字节 XOR 包装去除后，
  使用 LithTech LTC/LZSS v0 位流解码；输出 51,657,941 字节、失败 0，并由同名
  `AI3_FatalCanyon_DZ.LTA` 逐字节精确匹配验证。详见 `LTC_RECOVERY_2026-08-06.md`。
- 私有目录块仍未恢复原文件名；每项使用源包哈希和稳定 stream index 标识。

## 证据与门禁

- 内容恢复清单：`recovery/private-rez-recovery.json`。
- 媒体转换清单：`recovery/private-rez-media-conversions.json`。
- 模型转换清单：`recovery/private-rez-model-conversions.json`。
- 解码输出：`recovery/special-formats/private-rez-decoded/`。
- PNG 输出：`recovery/special-formats/private-rez-media/`。
- GLB 输出：`recovery/special-formats/private-rez-models/`。
- LTC LTA 输出：`recovery/special-formats/ltc-decoded/`。
- RF199 连续帧输出：`recovery/special-formats/private-rez-png-prefix/`。
- 工具：`tools/recover_private_rez.py`、`tools/recover_private_rez_framed_prefix.py`、`tools/convert_private_rez_media.py`、`tools/convert_ltb_models.py`、`tools/lithtech_ltc.py`。
- 专项测试：`tools/test_recover_private_rez.py`、`tools/test_convert_private_rez_media.py`、`tools/test_convert_ltb_models.py`。

统一 provenance 已验证 12/12 清单、148,829 个输出、21,433,831,251 字节，错误 0。
