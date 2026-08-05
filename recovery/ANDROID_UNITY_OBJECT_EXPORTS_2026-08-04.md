# Android Unity 对象导出清单（2026-08-04）

输出按 APK 来源目录隔离；源 APK、DEX、SO 和 Unity 数据均只读，未运行任何包内代码。可直接转换的贴图、Sprite、Mesh、音频、Shader、文本和字体使用标准格式；场景/Prefab 层级、骨骼、动画、材质和 UI 组件保存为类型树 JSON，失败对象保留原始二进制。

| APK | 输入序列化文件 | 资源流 | 导出对象 | 直接/JSON成功 | 原始回退 | 失败 |
|---|---:|---:|---:|---:|---:|---:|
| `base.apk.1` | 6 | 8 | 417 | 393 | 24 | 0 |
| `兵魂回忆录.apk.1` | 7 | 4 | 927 | 927 | 0 | 0 |
| `遗迹杀戮(1).apk.1` | 4 | 378 | 9082 | 5492 | 3590 | 0 |

## 类型汇总

Animation 3，AnimationClip 43，Animator 8，AnimatorController 13，AudioClip 65，Avatar 4，Canvas 1，CanvasGroup 1，CanvasRenderer 1406，Font 12，GameObject 1625，LightmapSettings 1，Material 255，Mesh 549，MeshFilter 25，MeshRenderer 25，MonoBehaviour 3158，MonoScript 177，NavMeshData 1，ParticleSystem 11，ParticleSystemRenderer 11，RectTransform 1545，RenderSettings 1，Shader 35，Sprite 582，SpriteRenderer 2，Texture2D 787，Transform 80。

## 表示方式

- `Texture2D/Sprite` → PNG；`Mesh` → OBJ；`AudioClip` → WAV/原编码样本；`Shader` → Shader 文本；字体 → TTF/OTF/原字节。
- `GameObject/Transform/RectTransform/Avatar/Animator/AnimationClip/Material/MonoBehaviour` 等 → 类型树 JSON，用于恢复场景、Prefab、骨架、动作、材质和 UI 引用关系。
- 解码不支持时输出 `.bin`，同时在机器清单记录错误；没有对象因解码失败而静默丢失。
- Unreal OBB/PAK 不由此工具猜测性解包，继续按专用格式任务处理。

依赖固定在 `tools/requirements.txt`（UnityPy 1.25.2、Pillow 11.3.0）。导出命令：

```bash
PYTHONPATH=/path/to/pinned/site-packages python3 tools/export_android_unity_objects.py \
  --workspace .. --materialization-manifest recovery/unity-split-materialization.json \
  --extraction-index recovery/safe-apk-extraction-index.json \
  --json recovery/android-unity-object-exports.json \
  --markdown recovery/ANDROID_UNITY_OBJECT_EXPORTS_2026-08-04.md
```
