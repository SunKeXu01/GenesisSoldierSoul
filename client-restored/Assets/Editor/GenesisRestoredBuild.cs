#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class GenesisRestoredBuild
{
    private static readonly Dictionary<string, string> RecoveredMapScenes =
        new Dictionary<string, string>
        {
            { "Pyramid", "Assets/RecoveredMaps/JunePyramid/金字塔/Scenep.unity" },
            { "NewConstructionSite", "Assets/RecoveredMaps/NewConstructionSite/gd.unity" },
            { "BiochemicalTown", "Assets/RecoveredMaps/BiochemicalTown/Scenes/20160409.unity" },
            { "ClassicConstructionSite", "Assets/RecoveredMaps/ClassicConstructionSite/Scenes/Scenegd.unity" },
            { "SteelFactory", "Assets/RecoveredMaps/SteelFactory/Scenes/Scenelgc.unity" },
            { "IceFireMaze", "Assets/RecoveredMaps/IceFireMaze/Scenes/SceneMG.unity" },
            { "RadiationDistrict", "Assets/RecoveredMaps/RadiationDistrict/辐射街区/Scenefsjq.unity" }
        };

    [MenuItem("Genesis/Create Playable Recovered Maps")]
    public static void CreatePlayableRecoveredMaps()
    {
        const string templatePath = "Assets/Scenes/Gongdi1.unity";
        const string outputDirectory = "Assets/PlayableMaps";
        Directory.CreateDirectory(outputDirectory);
        CreateRemotePlayerPrefab();
        CreateCombatResourcePrefabs();

        foreach (var recovered in RecoveredMapScenes)
        {
            if (!File.Exists(recovered.Value))
            {
                Debug.LogWarning("Recovered map is not present yet: " + recovered.Value);
                continue;
            }

            var templateScene = EditorSceneManager.OpenScene(templatePath, OpenSceneMode.Single);
            var templateRoots = templateScene.GetRootGameObjects()
                .Where(IsTemplateGameplayRoot)
                .ToArray();

            var mapScene = EditorSceneManager.OpenScene(recovered.Value, OpenSceneMode.Additive);
            var existingPlayer = mapScene.GetRootGameObjects()
                .FirstOrDefault(IsExistingPlayerRoot);
            var spawnPosition = existingPlayer == null
                ? new Vector3(0f, 2f, 0f)
                : existingPlayer.transform.position + Vector3.up;
            var spawnRotation = existingPlayer == null
                ? Quaternion.identity
                : Quaternion.Euler(0f, existingPlayer.transform.eulerAngles.y, 0f);

            foreach (var root in mapScene.GetRootGameObjects().Where(IsOldGameplayRoot).ToArray())
                UnityEngine.Object.DestroyImmediate(root);

            var clonedObjects = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            foreach (var templateRoot in templateRoots)
            {
                var clone = UnityEngine.Object.Instantiate(templateRoot);
                clone.name = templateRoot.name;
                SceneManager.MoveGameObjectToScene(clone, mapScene);
                MapClonedHierarchy(templateRoot.transform, clone.transform, clonedObjects);
                if (clone.name == "First Person Player")
                {
                    clone.transform.position = spawnPosition;
                    clone.transform.rotation = spawnRotation;
                }
            }
            RestoreCrossRootReferences(clonedObjects);

            var outputPath = outputDirectory + "/" + recovered.Key + ".unity";
            EditorSceneManager.SaveScene(mapScene, outputPath, false);
            EditorSceneManager.CloseScene(templateScene, true);
            Debug.Log("Created playable recovered map: " + outputPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("Genesis/Validate Playable Recovered Maps")]
    public static void ValidatePlayableRecoveredMaps()
    {
        var failures = new List<string>();
        foreach (var map in RecoveredMapScenes.Keys)
        {
            var scenePath = "Assets/PlayableMaps/" + map + ".unity";
            if (!File.Exists(scenePath))
            {
                failures.Add(map + ": scene file is missing");
                continue;
            }

            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var player = roots.FirstOrDefault(root =>
                root.name == "First Person Player");
            var rendererCount = roots.Sum(root =>
                root.GetComponentsInChildren<Renderer>(true).Length);
            var colliderCount = roots.Sum(root =>
                root.GetComponentsInChildren<Collider>(true).Length);
            var canvasCount = roots.Sum(root =>
                root.GetComponentsInChildren<Canvas>(true).Length);

            if (player == null)
                failures.Add(map + ": First Person Player is missing");
            else
            {
                if (player.GetComponentInChildren<CharacterController>(true) == null)
                    failures.Add(map + ": CharacterController is missing");
                if (player.GetComponentInChildren<Camera>(true) == null)
                    failures.Add(map + ": first-person camera is missing");
            }
            if (rendererCount == 0)
                failures.Add(map + ": no recovered renderers");
            if (colliderCount == 0)
                failures.Add(map + ": no recovered colliders");
            if (canvasCount == 0)
                failures.Add(map + ": combat HUD canvas is missing");

            Debug.Log(string.Format(
                "[GenesisMapValidation] {0}: renderers={1}, colliders={2}, canvases={3}",
                map, rendererCount, colliderCount, canvasCount));
        }

        if (failures.Count > 0)
            throw new Exception(
                "Playable map validation failed:\n" + string.Join("\n", failures));
        Debug.Log(
            "[GenesisMapValidation] All recovered playable maps passed validation.");
    }

    private static void CreateRemotePlayerPrefab()
    {
        const string sourcePath =
            "Assets/RecoveredMaps/NewConstructionSite/gd.unity";
        const string prefabDirectory = "Assets/Resources/OriginalGame";
        const string prefabPath = prefabDirectory + "/RemotePlayer.prefab";
        if (!File.Exists(sourcePath))
            return;

        var sourceScene = EditorSceneManager.OpenScene(sourcePath, OpenSceneMode.Single);
        var source = sourceScene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == "Player");
        if (source == null)
        {
            Debug.LogWarning("Original remote-player model was not found in " + sourcePath);
            return;
        }

        Directory.CreateDirectory(prefabDirectory);
        var clone = UnityEngine.Object.Instantiate(source);
        clone.name = "创世兵魂角色";
        clone.transform.position = Vector3.zero;
        clone.transform.rotation = Quaternion.identity;
        foreach (var audioListener in clone.GetComponentsInChildren<AudioListener>(true))
            UnityEngine.Object.DestroyImmediate(audioListener);
        foreach (var camera in clone.GetComponentsInChildren<Camera>(true))
            UnityEngine.Object.DestroyImmediate(camera);
        PrefabUtility.SaveAsPrefabAsset(clone, prefabPath);
        UnityEngine.Object.DestroyImmediate(clone);
        Debug.Log("Created original-resource remote player prefab: " + prefabPath);
    }

    [MenuItem("Genesis/Create Combat Resource Prefabs")]
    public static void CreateCombatResourcePrefabs()
    {
        const string prefabDirectory = "Assets/Resources/OriginalGame";
        Directory.CreateDirectory(prefabDirectory);
        CopyRuntimePrefab(
            "Assets/RecoveredMaps/JunePyramid/GameObject/Pistol.prefab",
            prefabDirectory + "/Pistol.prefab");
        CopyRuntimePrefab(
            "Assets/RecoveredMaps/JunePyramid/GameObject/PistolMuzzleFlash.prefab",
            prefabDirectory + "/PistolMuzzleFlash.prefab");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void CopyRuntimePrefab(string sourcePath, string destinationPath)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(destinationPath) != null)
            return;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath) == null)
        {
            Debug.LogWarning("Recovered combat prefab was not found: " + sourcePath);
            return;
        }
        if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
        {
            Debug.LogError(
                "Failed to copy recovered combat prefab to Resources: "
                + sourcePath);
            return;
        }
        Debug.Log("Created recovered combat resource prefab: " + destinationPath);
    }

    [MenuItem("Genesis/Audit Original Scene Buttons")]
    public static void AuditAllScenes()
    {
        var output = new StringBuilder();
        output.AppendLine("scene,button,path,active,canvasX,canvasY,width,height,graphic,persistentCalls");

        foreach (var sceneSetting in EditorBuildSettings.scenes.Where(scene => scene.enabled))
        {
            if (!File.Exists(sceneSetting.path))
                continue;

            var scene = EditorSceneManager.OpenScene(sceneSetting.path, OpenSceneMode.Single);
            foreach (var button in Resources.FindObjectsOfTypeAll<Button>()
                         .Where(item => item.gameObject.scene == scene))
            {
                var canvas = button.GetComponentInParent<Canvas>();
                var bounds = canvas == null
                    ? new Bounds(Vector3.zero, Vector3.zero)
                    : RectTransformUtility.CalculateRelativeRectTransformBounds(
                        canvas.transform, button.transform);
                var rawImage = button.targetGraphic as RawImage;
                var image = button.targetGraphic as Image;
                var graphicName = rawImage != null && rawImage.texture != null
                    ? rawImage.texture.name
                    : image != null && image.sprite != null
                        ? image.sprite.name
                        : button.targetGraphic == null
                            ? string.Empty
                            : button.targetGraphic.name;
                var calls = new List<string>();
                for (var index = 0; index < button.onClick.GetPersistentEventCount(); index++)
                {
                    var target = button.onClick.GetPersistentTarget(index);
                    var method = button.onClick.GetPersistentMethodName(index);
                    calls.Add((target == null ? "<missing>" : target.GetType().Name) + "." + method);
                }

                output.Append(Csv(scene.path)).Append(',')
                    .Append(Csv(button.name)).Append(',')
                    .Append(Csv(HierarchyPath(button.transform))).Append(',')
                    .Append(button.gameObject.activeInHierarchy ? "true" : "false").Append(',')
                    .Append(bounds.center.x.ToString("0.##")).Append(',')
                    .Append(bounds.center.y.ToString("0.##")).Append(',')
                    .Append(bounds.size.x.ToString("0.##")).Append(',')
                    .Append(bounds.size.y.ToString("0.##")).Append(',')
                    .Append(Csv(graphicName)).Append(',')
                    .AppendLine(Csv(string.Join(";", calls)));
            }
        }

        var auditDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../recovery"));
        Directory.CreateDirectory(auditDirectory);
        var auditPath = Path.Combine(auditDirectory, "restored-button-audit.csv");
        File.WriteAllText(auditPath, output.ToString(), new UTF8Encoding(true));
        Debug.Log("Genesis button audit written to " + auditPath);
    }

    [MenuItem("Genesis/Audit Scene Roots")]
    public static void AuditSceneRoots()
    {
        var output = new StringBuilder();
        output.AppendLine("scene,root,components,renderers,colliders,children");
        var scenePaths = EditorBuildSettings.scenes.Where(item => item.enabled)
            .Select(item => item.path)
            .Concat(AssetDatabase.FindAssets("t:Scene", new[] { "Assets/RecoveredMaps" })
                .Select(AssetDatabase.GUIDToAssetPath))
            .Distinct();

        foreach (var scenePath in scenePaths)
        {
            if (!File.Exists(scenePath))
                continue;
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
            {
                output.Append(Csv(scenePath)).Append(',')
                    .Append(Csv(root.name)).Append(',')
                    .Append(Csv(string.Join(";", root.GetComponents<Component>()
                        .Where(component => component != null)
                        .Select(component => component.GetType().Name)))).Append(',')
                    .Append(root.GetComponentsInChildren<Renderer>(true).Length).Append(',')
                    .Append(root.GetComponentsInChildren<Collider>(true).Length).Append(',')
                    .AppendLine(root.GetComponentsInChildren<Transform>(true).Length.ToString());
            }
        }

        var auditDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../recovery"));
        Directory.CreateDirectory(auditDirectory);
        var auditPath = Path.Combine(auditDirectory, "restored-scene-roots.csv");
        File.WriteAllText(auditPath, output.ToString(), new UTF8Encoding(true));
        Debug.Log("Genesis scene-root audit written to " + auditPath);
    }

    [MenuItem("Genesis/Audit Playable Map Visibility")]
    public static void AuditPlayableMapVisibility()
    {
        const string scenePath = "Assets/PlayableMaps/NewConstructionSite.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var output = new StringBuilder();
        var renderers = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
            .ToArray();
        var enabledRenderers = renderers
            .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy)
            .ToArray();
        if (enabledRenderers.Length > 0)
        {
            var bounds = enabledRenderers[0].bounds;
            foreach (var renderer in enabledRenderers.Skip(1))
                bounds.Encapsulate(renderer.bounds);
            output.AppendLine(
                $"Renderers={renderers.Length} enabled={enabledRenderers.Length} bounds={bounds}");
        }

        foreach (var camera in scene.GetRootGameObjects()
                     .SelectMany(root => root.GetComponentsInChildren<Camera>(true)))
        {
            output.AppendLine(
                $"Camera {HierarchyPath(camera.transform)} active={camera.gameObject.activeInHierarchy} " +
                $"enabled={camera.enabled} position={camera.transform.position} rotation={camera.transform.eulerAngles} " +
                $"clear={camera.clearFlags} mask={camera.cullingMask} near={camera.nearClipPlane} far={camera.farClipPlane}");
        }

        foreach (var light in scene.GetRootGameObjects()
                     .SelectMany(root => root.GetComponentsInChildren<Light>(true)))
        {
            output.AppendLine(
                $"Light {HierarchyPath(light.transform)} active={light.gameObject.activeInHierarchy} " +
                $"enabled={light.enabled} type={light.type} intensity={light.intensity}");
        }

        foreach (var root in scene.GetRootGameObjects()
                     .Where(root => root.name.Contains("Player")))
            output.AppendLine($"Player {root.name} position={root.transform.position}");

        var auditDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../recovery"));
        var auditPath = Path.Combine(auditDirectory, "playable-map-visibility.txt");
        File.WriteAllText(auditPath, output.ToString());
        Debug.Log(output.ToString());
    }

    [MenuItem("Genesis/Audit Pyramid Gameplay")]
    public static void AuditPyramidGameplay()
    {
        const string scenePath = "Assets/PlayableMaps/Pyramid.unity";
        const string pistolPath =
            "Assets/RecoveredMaps/JunePyramid/GameObject/Pistol.prefab";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var output = new StringBuilder();
        foreach (var root in scene.GetRootGameObjects()
                     .Where(root => root.name == "First Person Player"
                                    || root.GetComponent<Canvas>() != null))
        {
            output.AppendLine(
                $"ROOT {root.name} active={root.activeSelf} path={HierarchyPath(root.transform)}");
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var components = string.Join(
                    ";",
                    transform.GetComponents<Component>()
                        .Where(component => component != null)
                        .Select(component => component.GetType().Name));
                var text = transform.GetComponent<Text>();
                var image = transform.GetComponent<Image>();
                var rawImage = transform.GetComponent<RawImage>();
                output.Append("  ")
                    .Append(HierarchyPath(transform))
                    .Append(" activeSelf=").Append(transform.gameObject.activeSelf)
                    .Append(" active=").Append(transform.gameObject.activeInHierarchy)
                    .Append(" components=").Append(components);
                if (text != null)
                    output.Append(" text=").Append(Csv(text.text));
                if (image != null && image.sprite != null)
                    output.Append(" sprite=").Append(image.sprite.name);
                if (rawImage != null && rawImage.texture != null)
                    output.Append(" texture=").Append(rawImage.texture.name);
                output.AppendLine();
            }
        }

        var pistol = AssetDatabase.LoadAssetAtPath<GameObject>(pistolPath);
        if (pistol != null)
        {
            var instance = UnityEngine.Object.Instantiate(pistol);
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1))
                    bounds.Encapsulate(renderer.bounds);
                output.AppendLine(
                    $"PISTOL renderers={renderers.Length} bounds={bounds}");
            }
            UnityEngine.Object.DestroyImmediate(instance);
        }

        const string sourceScenePath =
            "Assets/RecoveredMaps/JunePyramid/金字塔/Scenep.unity";
        var sourceScene = EditorSceneManager.OpenScene(
            sourceScenePath, OpenSceneMode.Single);
        var sourcePlayer = sourceScene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == "test_one");
        if (sourcePlayer != null)
        {
            output.AppendLine("SOURCE_PLAYER test_one");
            foreach (var transform in sourcePlayer.GetComponentsInChildren<Transform>(true))
            {
                output.Append("  ")
                    .Append(HierarchyPath(transform))
                    .Append(" active=").Append(transform.gameObject.activeInHierarchy)
                    .Append(" localPosition=").Append(transform.localPosition)
                    .Append(" components=")
                    .AppendLine(string.Join(
                        ";",
                        transform.GetComponents<Component>()
                            .Where(component => component != null)
                            .Select(component => component.GetType().Name)));
            }
        }

        var auditDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../recovery"));
        var auditPath = Path.Combine(auditDirectory, "pyramid-gameplay-audit.txt");
        File.WriteAllText(auditPath, output.ToString());
        Debug.Log(output.ToString());
    }

    [MenuItem("Genesis/Build Restored WebGL")]
    public static void BuildRestoredWebGL()
    {
        var diagnosticBuild =
            Environment.GetEnvironmentVariable("GENESIS_DIAGNOSTIC") == "1";
        var sceneReplacements = new Dictionary<string, string>
        {
            { "Assets/Scenes/Gongdi1.unity", "Assets/PlayableMaps/NewConstructionSite.unity" },
            { "Assets/Scenes/Jidixiaozhen.unity", "Assets/PlayableMaps/BiochemicalTown.unity" },
            { "Assets/Scenes/GDold1.unity", "Assets/PlayableMaps/ClassicConstructionSite.unity" }
        };
        var scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled && File.Exists(scene.path))
            .Select(scene => sceneReplacements.TryGetValue(scene.path, out var replacement)
                ? replacement
                : scene.path)
            .ToList();

        foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/PlayableMaps" }))
        {
            var recoveredScene = AssetDatabase.GUIDToAssetPath(guid);
            if (!scenes.Contains(recoveredScene))
                scenes.Add(recoveredScene);
        }

        PlayerSettings.productName = "创世兵魂";
        PlayerSettings.companyName = "创世兵魂恢复项目";
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.exceptionSupport = diagnosticBuild
            ? WebGLExceptionSupport.FullWithStacktrace
            : WebGLExceptionSupport.FullWithoutStacktrace;
        PlayerSettings.WebGL.dataCaching = true;

        var outputPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Build/WebGL"));
        Directory.CreateDirectory(outputPath);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = outputPath,
            target = BuildTarget.WebGL,
            options = diagnosticBuild ? BuildOptions.Development : BuildOptions.None
        });

        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("Restored WebGL build failed: " + report.summary.result);

        AddWebCacheVersion(outputPath);
        Debug.Log($"Restored WebGL build complete: {outputPath} ({report.summary.totalSize} bytes)");
    }

    private static string HierarchyPath(Transform transform)
    {
        var names = new Stack<string>();
        while (transform != null)
        {
            names.Push(transform.name);
            transform = transform.parent;
        }
        return string.Join("/", names);
    }

    private static bool IsTemplateGameplayRoot(GameObject root)
    {
        return root.name == "First Person Player"
               || root.name == "EventSystem"
               || root.name == "Audio Source"
               || root.name == "Canvas";
    }

    private static bool IsExistingPlayerRoot(GameObject root)
    {
        return root.GetComponentInChildren<Camera>(true) != null
               || root.GetComponentInChildren<CharacterController>(true) != null
               || root.name.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0
               || root.name.IndexOf("hero", StringComparison.OrdinalIgnoreCase) >= 0
               || root.name == "test_one"
               || root.name == "ppsh";
    }

    private static bool IsOldGameplayRoot(GameObject root)
    {
        return IsExistingPlayerRoot(root)
               || root.GetComponentInChildren<Canvas>(true) != null
               || root.GetComponentInChildren<UnityEngine.EventSystems.EventSystem>(true) != null;
    }

    private static void MapClonedHierarchy(
        Transform source,
        Transform clone,
        IDictionary<UnityEngine.Object, UnityEngine.Object> clonedObjects)
    {
        clonedObjects[source.gameObject] = clone.gameObject;
        clonedObjects[source] = clone;

        var sourceComponents = source.GetComponents<Component>();
        var cloneComponents = clone.GetComponents<Component>();
        for (var index = 0; index < Math.Min(sourceComponents.Length, cloneComponents.Length); index++)
        {
            if (sourceComponents[index] != null && cloneComponents[index] != null)
                clonedObjects[sourceComponents[index]] = cloneComponents[index];
        }

        for (var index = 0; index < Math.Min(source.childCount, clone.childCount); index++)
            MapClonedHierarchy(source.GetChild(index), clone.GetChild(index), clonedObjects);
    }

    private static void RestoreCrossRootReferences(
        IDictionary<UnityEngine.Object, UnityEngine.Object> clonedObjects)
    {
        foreach (var pair in clonedObjects.Where(pair => pair.Key is Component))
        {
            var source = new SerializedObject(pair.Key);
            var clone = new SerializedObject(pair.Value);
            var property = source.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference
                    || property.objectReferenceValue == null
                    || !clonedObjects.TryGetValue(property.objectReferenceValue, out var mapped))
                    continue;

                var cloneProperty = clone.FindProperty(property.propertyPath);
                if (cloneProperty != null)
                    cloneProperty.objectReferenceValue = mapped;
            }
            clone.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static string Csv(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }

    private static void AddWebCacheVersion(string outputPath)
    {
        var indexPath = Path.Combine(outputPath, "index.html");
        if (!File.Exists(indexPath))
            return;
        var version = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var html = File.ReadAllText(indexPath);
        html = Regex.Replace(
            html,
            @"/WebGL\.(loader\.js|data|framework\.js|wasm)(?:\?v=\d+)?""",
            match =>
                "/WebGL." +
                match.Groups[1].Value +
                "?v=" +
                version +
                "\"");
        File.WriteAllText(indexPath, html);
    }
}
#endif
