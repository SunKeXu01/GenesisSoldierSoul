# UnrealAssetAudit

只读审计 Unreal Engine 4.27 cooked `.uasset/.umap` 文件闭包，并用 UAssetAPI 生成可复核 JSON 对象表示。

工具执行两级检查：先以 raw export 模式验证 package summary/import/export map，再完整反序列化 UObject；成功项还必须通过 UAssetAPI 的读后重序列化二进制一致性检查。输入只取 `package-closure-v2.json` 中 `file_set_complete_candidate`，缺少伴随文件的包不会尝试解析。

```sh
dotnet run --project tools/UnrealAssetAudit/UnrealAssetAudit.csproj --configuration Release -- \
  --closure recovery/special-formats/unreal-analysis/MapPreview-Android_Multi__ee15b61ab0c0/package-closure-v2.json \
  --extracted-root recovery/special-formats/unreal-extracted/MapPreview-Android_Multi__ee15b61ab0c0 \
  --json-output-root recovery/special-formats/unreal-object-json/MapPreview-Android_Multi__ee15b61ab0c0 \
  --output recovery/special-formats/unreal-analysis/MapPreview-Android_Multi__ee15b61ab0c0/uassetapi-audit.json
```

依赖固定为 UAssetAPI 1.1.0；UAssetAPI 由 atenfyr 以 MIT 许可证发布。上游源码和文档：<https://github.com/atenfyr/UAssetAPI>、<https://atenfyr.github.io/UAssetAPI/>。
