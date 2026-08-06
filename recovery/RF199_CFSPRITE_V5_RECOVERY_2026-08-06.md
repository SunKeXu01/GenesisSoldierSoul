# RF199 CFSprite v5 连续资源恢复

日期：2026-08-06

## 结论

`CF2.0/CrossFire/rez/RF199.REZ` 在旧停止偏移 `287,530,227` 处不是
不可分割密文，而是一个自定界的 CrossFire `CFSprite` v5 序列化资源。
严格解析后恢复 2,291 字节，精确停在 `287,532,518`，没有搜索或
跳过随后的未知资源。

## 边界证据

- 固定类名：16 位小端长度 `8` + ASCII `CFSprite`。
- 文件头：版本 `5`、schema `9`、保留字段 `0`、tick rate `30`、画布
  `232×232`、条目数 `8`。
- 每个条目验证顺序索引、三个有界 ASCII 字符串、PNG 路径、固定
  flags、矩形参数和关键帧数。
- 23 个关键帧全部验证严格递增 tick、重复 tick 一致、资源索引
  一致、7 个有限浮点变换参数和结束 tick。
- 原帧 SHA-256：`cbcbb7b28bb4931005e2132dc54a5a4276fef60b2070263a77ed7665f3e65a0f`。

另外三个同版本样本用于字段对照；其中 369 字节样本按同一文法
精确到达下一个 `CFSprite` 起点。边界由计数和字段长度推导，不依赖
后继签名。

## 产物与验证

- 隔离输出：`recovery/special-formats/private-rez-png-prefix/RF199__20517f96983a/cfsprite-07438.xfi`
  （可再生成，Git 忽略）。
- 恢复器：`tools/recover_private_rez_framed_prefix.py` v14。
- 专项回归：28/28，包含完整 CFSprite 与不一致 tick 拒绝。
- 统一 provenance：13/13 清单、185,382 个输出、25,517,719,462 字节，
  错误 0。
