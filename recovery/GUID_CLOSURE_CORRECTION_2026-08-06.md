# Unity GUID 闭包反向边修正（2026-08-06）

## 结论

`playable_maps` 此前报告的 19 个缺失 GUID 并非当前可玩场景的前向运行时依赖。它们全部由以下链路误扩展产生：

`Assets/PlayableMaps/Pyramid.unity` → `LightingData.asset` → `m_Scene` → 原始 `Scenep.unity` → 旧 UFPS/游戏脚本。

Unity 的 `LightingData.asset.m_Scene` 是指向所属源场景的反向标识。旧审计器对所有 YAML GUID 一视同仁，沿该反向边进入原始恢复场景，继而把 19 个已不属于当前运行时前向闭包的旧脚本统计为缺失。

## 修正

- `tools/audit_resource_closures.py` 只对文件名严格为 `LightingData.asset` 的 `m_Scene` 字段识别所属场景反向边。
- 其他 LightingData GUID（光照贴图、LightProbe 等）仍按普通前向依赖遍历。
- 每条被忽略反向边写入 `ignored_back_references`，包含路径、GUID 和原因；不会静默隐藏。
- 新增回归测试，证明源场景中的缺失脚本不会经反向边污染运行时闭包，同时 LightingData 本身仍在闭包中。

## 结果

- `playable_maps`：2,155 个前向闭包资产，缺失 GUID 0，显式忽略反向边 1。
- 其余六个运行时资源组：缺失 GUID 均为 0。
- 8 个审计组仍全部为 D 级，`formalBuildAllowed=false`，原因保持为缺少批准权利记录。

本修正只消除审计方向错误，不添加旧脚本、不改写原始恢复资源，也不改变许可门禁。

## 验证

- `tools/test_audit_resource_closures.py`：3/3 通过。
- `recovery/resource-closure-audit.json`：七个运行时组 `missing_guid_count=0`。
- `recovery/RESOURCE_CLOSURE_AUDIT.md`：表格单独列出 Missing GUIDs 与 Ignored backlinks。
