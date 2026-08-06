# SWF / ATF 静态资源提取报告

全部输入只按字节读取，不执行 ActionScript。位图转为 PNG；矢量、字体、Sprite/时间轴标签保存原始标签体；
`DefineBinaryData` 中的 ATF 独立保存为 `.atf`。原始标签体始终保留；后续转换失败会记录原因，不覆写源文件。

- 输入文件：417
- 有效 SWF：416
- 非 SWF 的 zlib 缓存载荷：1
- 资源标签：6170
- PNG：1874；矢量标签：1601；字体标签：74；Sprite 标签：1380；ATF：538
- 未保底失败：0

机器清单逐源保存输入 SHA-256、压缩方式、SWF 版本、标签计数、符号名、帧标签、Sprite 时间轴、输出大小/哈希和错误。

## ATF 后续转换

- 538/538 个 version-0 ATF 已转换，失败 0。
- 格式覆盖：346 个 `COMPRESSED` 重建为 DXT1，192 个 `COMPRESSED_ALPHA` 重建为 DXT5。
- 输出：538 DDS + 538 RGBA PNG，共 271,008,801 字节；保留 1,001 个实际存在的 mip 层。
- 转换器、机器清单和详细验证分别见 `tools/convert_atf_textures.py`、`recovery/atf-texture-conversions.json` 和 `recovery/ATF_TEXTURE_CONVERSION_2026-08-06.md`。
