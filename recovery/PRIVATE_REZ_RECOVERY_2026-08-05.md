# 私有 REZ 内容恢复报告（2026-08-05）

## 结论

CrossFire 私有 RF/RB 包保留标准 REZ v1 外层头，但目录块经过私有处理。静态分析确认 208 个包的数据区由独立 LZMA-Alone 流串接组成，每条流对应一个资源；工具只在属性字节 `0x5d`、16 MiB 字典、声明长度、流 EOF 和解码长度全部一致时接受输出。

主体 LZMA 恢复没有猜测目录密钥或运行客户端 EXE/DLL，原始包保持只读。后续对 RB001 增加了显式、严格验证的 CrossFire 加密目录兼容解析；769/769 个目录 MD5 与归档数据一致。

## 恢复规模

- REZ 样本：242 个；其中标准包 1 个、可恢复串接 LZMA 私有包 208 个、空/仅填充数据区 26 个；原 7 个未分帧 raw/加密数据区均已连续恢复到数据区末尾，未知尾段为 0。
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

基于 Cote-Duke LTB2X 与公开 CrossFire LithTech 运行时/打包器的结构规则独立实现了有界 Python 解析器；第三方出处记录在转换清单，未运行其 Windows 示例程序。layout-v7 在 rigid/skeletal/vertex-animated/null render object、四路顶点流、matrix palette/骨骼重索引、顶点动画重复表、OBB 和 skin 基础上，进一步严格恢复 animation tail、压缩骨骼 TRS 与 vertex morph/weights。

- 候选 LTB model：8,721；严格转换/复核 GLB：8,721；保留失败：0。
- GLB 共含 32,608 个网格、23,507,864 顶点、20,749,724 三角形，输出 1,021,928,828 字节。
- 321 个复合布局文件严格解析 14,896 条骨骼，均已写入 glTF skin；1,844 个网格含 `JOINTS_0/WEIGHTS_0`。
- 98 个源文件实际包含 818 个动画、29,212 个关键帧；已输出 88,740 个 glTF TRS/weights 通道和 2,940 个 position morph target。其余 223 个复合 LTB 源动画数为 0。
- 输出保留源坐标，不做未经证明的轴变换；socket、weight-set 混合和外部 child-model 绑定只审计、不伪装成标准 glTF 行为。
- 每个成功项保存输入/输出哈希和 GLB 结构验证；每个失败项保存源包、stream index、输入路径和具体错误。

## 连续恢复补充

- 两份 RF019 和两份 RF199 已从固定数据区起点严格恢复 39,538 个连续资源、3,660,782,036 字节，并将 15,973 个 DTX 与 257 个 TGA 转为 16,230 个 PNG；延伸恢复包含 971 个 zero-mirror 帧、3 个 SWF、1 个 FLV、1 个 HTML、1 个 CP949 Web bundle、1 个 UI layout、4 个 CFSprite v5、4 个 RPS、75 个孤立 IDAT、10 个 DTX 和 11,380 个 PNG，详见 `REZ_ZERO_MIRROR_RECOVERY_2026-08-09.md`、`RF199_WEB_MEDIA_PREFIX_RECOVERY_2026-08-06.md`、`RF199_CFSPRITE_V5_RECOVERY_2026-08-06.md` 与 `RF199_FRAMED_PREFIX_RECOVERY_2026-08-06.md`。
- RF164 与 RF266 已按 world v85 的递归 render tail 严格恢复 2 个资源、58,778,137 字节，两者都精确到达 REZ 数据区末尾，见 `LITHTECH_WORLD_V85_RECOVERY_2026-08-06.md`。
- RB001 的加密目录已严格解析为 4 个表、769 个文件项，769/769 个目录 MD5 验证一致；768 个目录资源和 1 个 world v85 资源连续覆盖数据区，末尾 3 个目录表 zero-mirror 帧也已闭合。详见 `RB001_ENCRYPTED_DIRECTORY_RECOVERY_2026-08-09.md`。
- 7 个原未分帧包的未知尾段总计为 0；仍不做无边界魔数 carving。
- LTB 几何 8,721/8,721、321 个 skin 和源内全部 818 个骨骼/顶点动画均已转换，详见 `LTB_ANIMATION_LAYOUT_V7_RECOVERY_2026-08-06.md`。
- 364 个 loose LTC 已在 2026-08-06 全部恢复为 LTA：固定 16 字节 XOR 包装去除后，
  使用 LithTech LTC/LZSS v0 位流解码；输出 51,657,941 字节、失败 0，并由同名
  `AI3_FatalCanyon_DZ.LTA` 逐字节精确匹配验证。详见 `LTC_RECOVERY_2026-08-06.md`。
- RB001 已恢复目录原文件名；其余串接 LZMA 包仍使用源包哈希和稳定 stream index 标识。

## 证据与门禁

- 内容恢复清单：`recovery/private-rez-recovery.json`。
- 媒体转换清单：`recovery/private-rez-media-conversions.json`。
- 模型转换清单：`recovery/private-rez-model-conversions.json`。
- 解码输出：`recovery/special-formats/private-rez-decoded/`。
- PNG 输出：`recovery/special-formats/private-rez-media/`。
- GLB 输出：`recovery/special-formats/private-rez-models/`。
- LTC LTA 输出：`recovery/special-formats/ltc-decoded/`。
- 私有 REZ 连续帧输出：`recovery/special-formats/private-rez-png-prefix/`。
- 工具：`tools/recover_private_rez.py`、`tools/recover_private_rez_framed_prefix.py`、`tools/crossfire_rez_directory.py`、`tools/convert_private_rez_media.py`、`tools/convert_ltb_models.py`、`tools/lithtech_ltc.py`。
- 专项测试：`tools/test_recover_private_rez.py`、`tools/test_convert_private_rez_media.py`、`tools/test_convert_ltb_models.py`。

后续 Unreal Oodle/UObject 闭包完成后，统一 provenance 已验证 13/13 清单、199,680 个输出、26,387,143,524 字节，错误 0。
