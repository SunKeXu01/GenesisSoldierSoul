# RF199 CFSprite v5、RPS 与 PNG 链恢复

日期：2026-08-06

## 结论

`CF2.0/CrossFire/rez/RF199.REZ` 在旧停止偏移 `287,530,227` 处不是
不可分割密文，而是 `CFSprite` v5、CrossFire RPS 路径表、CRC-valid
PNG 和孤立 IDAT chunk 构成的连续链。本轮严格恢复 10,294 个资源、
400,825,331 字节，将精确停止点前移到 `688,355,558`。

## 边界证据

- 固定类名：16 位小端长度 `8` + ASCII `CFSprite`。
- 文件头：版本 `5`、schema `9`、保留字段 `0`、tick rate `30`、画布
  `232×232`、条目数 `8`。
- 每个条目验证顺序索引、三个有界 ASCII 字符串、PNG 路径、固定
  flags、矩形参数和关键帧数。
- 23 个关键帧全部验证严格递增 tick、重复 tick 一致、资源索引
  一致、7 个有限浮点变换参数和结束 tick。
- 首个原帧 SHA-256：`cbcbb7b28bb4931005e2132dc54a5a4276fef60b2070263a77ed7665f3e65a0f`。

另外三个同版本样本用于字段对照。关键帧表之后独立出现 4 个 RPS：
其文法是“路径数 + CP949 长度字符串 + 顺序索引数组 + 零保留字段”。
零路径样本仍能由索引尾部自定界，证明 RPS 不是 CFSprite 的猜测延伸。

RPS 后的 10,211 个 PNG 均逐 chunk 验证 CRC、IHDR/IDAT/IEND 顺序。另有 75 个
孤立 IDAT chunk 在前一 PNG 已闭合后出现；它们只在自身 CRC 正确且后继
为已知帧时保留为 `.idat`，不伪装成可独立显示的 PNG。

## 产物与验证

- 隔离输出：`recovery/special-formats/private-rez-png-prefix/RF199__20517f96983a/cfsprite-07438.xfi`
  （可再生成，Git 忽略）。
- 恢复器：`tools/recover_private_rez_framed_prefix.py` v15。
- 专项回归：32/32，包含 CFSprite tick、RPS 索引和孤立 IDAT 错误拒绝。
- 统一 provenance：13/13 清单、195,675 个输出、25,918,542,502 字节，
  错误 0。
