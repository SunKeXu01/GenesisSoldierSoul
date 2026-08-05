#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
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
        CreateRemotePlayerAnimatorController();
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
            var mapBounds = CalculateVisibleBounds(mapScene);
            var spawnPosition = existingPlayer == null
                ? new Vector3(0f, 2f, 0f)
                : existingPlayer.transform.position + Vector3.up;
            var spawnRotation = existingPlayer == null
                ? Quaternion.identity
                : Quaternion.Euler(0f, existingPlayer.transform.eulerAngles.y, 0f);
            if (mapBounds.HasValue
                && !ContainsWithMargin(
                    mapBounds.Value, spawnPosition, 2f))
            {
                RaycastHit hit;
                var bounds = mapBounds.Value;
                var rayOrigin = new Vector3(
                    bounds.center.x,
                    bounds.max.y + 20f,
                    bounds.center.z);
                if (mapScene.GetPhysicsScene().Raycast(
                        rayOrigin,
                        Vector3.down,
                        out hit,
                        bounds.size.y + 40f,
                        ~0,
                        QueryTriggerInteraction.Ignore))
                {
                    spawnPosition = hit.point + Vector3.up * 1.2f;
                    spawnRotation = Quaternion.identity;
                    Debug.Log(
                        $"Repaired out-of-bounds spawn for {recovered.Key}: " +
                        spawnPosition);
                }
            }

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
            var visibleRenderers = roots
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(renderer =>
                    renderer.enabled && renderer.gameObject.activeInHierarchy)
                .ToArray();
            var gameplayRenderers = visibleRenderers
                .Where(renderer =>
                    renderer.bounds.size.x < 5000f
                    && renderer.bounds.size.y < 5000f
                    && renderer.bounds.size.z < 5000f)
                .ToArray();

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
            if (map == "Pyramid"
                && (RenderSettings.skybox == null
                    || RenderSettings.skybox.shader == null))
            {
                failures.Add(
                    map + ": recovered six-sided skybox is missing");
            }

            if (GenesisSoldierSoul.Multiplayer.GenesisMultiplayerBootstrap
                    .IsPlayableMap(map))
            {
                if (colliderCount < 10)
                    failures.Add(
                        map + ": promoted map has fewer than 10 colliders");
                if (gameplayRenderers.Length == 0)
                {
                    failures.Add(
                        map + ": promoted map has no visible renderers");
                }
                else if (player != null)
                {
                    // Recovered scenes can contain an enormous sky/background
                    // mesh. It is visual dressing, not playable geometry, and
                    // must not expand the spawn/bounds validation volume.
                    var bounds = gameplayRenderers[0].bounds;
                    foreach (var renderer in gameplayRenderers.Skip(1))
                        bounds.Encapsulate(renderer.bounds);
                    var spawn = player.transform.position;
                    const float horizontalMargin = 5f;
                    if (spawn.x < bounds.min.x - horizontalMargin
                        || spawn.x > bounds.max.x + horizontalMargin
                        || spawn.z < bounds.min.z - horizontalMargin
                        || spawn.z > bounds.max.z + horizontalMargin)
                    {
                        failures.Add(
                            map + ": promoted spawn is outside visible map bounds");
                    }
                    if (bounds.size.x > 5000f || bounds.size.z > 5000f)
                        failures.Add(
                            map + ": promoted map has implausibly large bounds");
                }
            }

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

    [MenuItem("Genesis/Sanitize Playable Recovered Maps")]
    public static void SanitizePlayableRecoveredMaps()
    {
        var totalRemoved = 0;
        foreach (var map in RecoveredMapScenes.Keys)
        {
            var scenePath = "Assets/PlayableMaps/" + map + ".unity";
            if (!File.Exists(scenePath))
                continue;

            var scene = EditorSceneManager.OpenScene(
                scenePath, OpenSceneMode.Single);
            var removed = 0;
            var sceneChanged = false;
            foreach (var gameObject in scene.GetRootGameObjects()
                         .SelectMany(root =>
                             root.GetComponentsInChildren<Transform>(true))
                         .Select(transform => transform.gameObject))
            {
                removed += GameObjectUtility
                    .RemoveMonoBehavioursWithMissingScript(gameObject);
            }

            var deferredAudioCount = 0;
            foreach (var source in scene.GetRootGameObjects()
                         .SelectMany(root =>
                             root.GetComponentsInChildren<AudioSource>(true)))
            {
                if (!source.playOnAwake)
                    continue;
                source.playOnAwake = false;
                if (source.GetComponent<
                        GenesisSoldierSoul.Multiplayer
                            .GenesisDeferredAudioSource>() == null)
                {
                    source.gameObject.AddComponent<
                        GenesisSoldierSoul.Multiplayer
                            .GenesisDeferredAudioSource>();
                }
                deferredAudioCount += 1;
                sceneChanged = true;
            }

            if (removed > 0)
                sceneChanged = true;

            var repairedColliderCount = RepairNegativeScaleBoxColliders(scene);
            if (repairedColliderCount > 0)
                sceneChanged = true;

            if (!sceneChanged)
                continue;
            EditorSceneManager.SaveScene(scene);
            totalRemoved += removed;
            if (removed > 0)
                Debug.Log(
                    $"[GenesisMapSanitize] {map}: removed {removed} missing scripts");
            if (deferredAudioCount > 0)
                Debug.Log(
                    $"[GenesisMapSanitize] {map}: deferred "
                    + $"{deferredAudioCount} WebGL scene audio sources");
            if (repairedColliderCount > 0)
                Debug.Log(
                    $"[GenesisMapSanitize] {map}: moved "
                    + $"{repairedColliderCount} negative-scale BoxColliders "
                    + "to positive-scale proxies");
        }

        AssetDatabase.SaveAssets();
        Debug.Log(
            $"[GenesisMapSanitize] Removed {totalRemoved} missing scripts " +
            "from playable copies; recovered source scenes were unchanged.");
    }

    private static int RepairNegativeScaleBoxColliders(Scene scene)
    {
        var colliders = scene.GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<BoxCollider>(true))
            .Where(collider => collider != null
                && HasNegativeScale(collider.transform.lossyScale))
            .ToArray();
        foreach (var source in colliders)
        {
            var sourceTransform = source.transform;
            var proxy = new GameObject(
                source.gameObject.name + " [Positive BoxCollider Proxy]");
            SceneManager.MoveGameObjectToScene(proxy, scene);
            proxy.layer = source.gameObject.layer;
            proxy.tag = source.gameObject.tag;
            proxy.SetActive(source.gameObject.activeInHierarchy);
            GameObjectUtility.SetStaticEditorFlags(
                proxy,
                GameObjectUtility.GetStaticEditorFlags(source.gameObject));

            proxy.transform.position = sourceTransform.TransformPoint(source.center);
            proxy.transform.rotation = sourceTransform.rotation;
            var worldScale = sourceTransform.lossyScale;
            proxy.transform.localScale = new Vector3(
                Mathf.Abs(worldScale.x),
                Mathf.Abs(worldScale.y),
                Mathf.Abs(worldScale.z));

            var replacement = proxy.AddComponent<BoxCollider>();
            replacement.center = Vector3.zero;
            replacement.size = new Vector3(
                Mathf.Abs(source.size.x),
                Mathf.Abs(source.size.y),
                Mathf.Abs(source.size.z));
            replacement.isTrigger = source.isTrigger;
            replacement.sharedMaterial = source.sharedMaterial;
            replacement.enabled = source.enabled;
            UnityEngine.Object.DestroyImmediate(source);
        }
        return colliders.Length;
    }

    [MenuItem("Genesis/Audit Playable Collider Scales")]
    public static void AuditPlayableColliderScales()
    {
        var failures = new List<string>();
        foreach (var map in RecoveredMapScenes.Keys)
        {
            var scenePath = "Assets/PlayableMaps/" + map + ".unity";
            if (!File.Exists(scenePath))
                continue;
            var scene = EditorSceneManager.OpenScene(
                scenePath, OpenSceneMode.Single);
            failures.AddRange(scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<BoxCollider>(true))
                .Where(collider => collider.enabled
                    && HasNegativeScale(collider.transform.lossyScale))
                .Select(collider => map + ": "
                    + HierarchyPath(collider.transform)));
        }
        if (failures.Count > 0)
            throw new InvalidOperationException(
                "Negative-scale playable BoxColliders remain:\n"
                + string.Join("\n", failures));
        Debug.Log(
            "[GenesisColliderScaleAudit] All playable BoxColliders use "
            + "non-negative world scale.");
    }

    private static bool HasNegativeScale(Vector3 scale)
    {
        return scale.x < 0f || scale.y < 0f || scale.z < 0f;
    }

    [MenuItem("Genesis/Audit Playable Audio")]
    public static void AuditPlayableAudio()
    {
        var output = new StringBuilder();
        output.AppendLine(
            "scene,path,clip,playOnAwake,enabled,loadInBackground,preloadAudioData,behaviours");
        foreach (var map in RecoveredMapScenes.Keys)
        {
            var scenePath = "Assets/PlayableMaps/" + map + ".unity";
            if (!File.Exists(scenePath))
                continue;
            var scene = EditorSceneManager.OpenScene(
                scenePath, OpenSceneMode.Single);
            foreach (var source in scene.GetRootGameObjects()
                         .SelectMany(root =>
                             root.GetComponentsInChildren<AudioSource>(true)))
            {
                var assetPath = source.clip == null
                    ? string.Empty
                    : AssetDatabase.GetAssetPath(source.clip);
                var importer = string.IsNullOrEmpty(assetPath)
                    ? null
                    : AssetImporter.GetAtPath(assetPath) as AudioImporter;
                var behaviours = source.GetComponents<MonoBehaviour>()
                    .Where(component => component != null)
                    .Select(component =>
                        component.GetType().Name + ":" + component.enabled);
                output.Append(Csv(map)).Append(',')
                    .Append(Csv(HierarchyPath(source.transform))).Append(',')
                    .Append(Csv(source.clip == null
                        ? string.Empty
                        : source.clip.name)).Append(',')
                    .Append(source.playOnAwake).Append(',')
                    .Append(source.enabled).Append(',')
                    .Append(importer != null && importer.loadInBackground).Append(',')
                    .Append(importer != null
                        && importer.defaultSampleSettings.preloadAudioData)
                    .Append(',')
                    .AppendLine(Csv(string.Join(";", behaviours)));
            }
        }
        var auditDirectory = Path.GetFullPath(
            Path.Combine(Application.dataPath, "../../recovery"));
        Directory.CreateDirectory(auditDirectory);
        var auditPath = Path.Combine(
            auditDirectory, "playable-audio-audit.csv");
        File.WriteAllText(auditPath, output.ToString(), new UTF8Encoding(true));
        Debug.Log("Genesis playable audio audit written to " + auditPath);
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

    private static void ConfigureRecoveredAudioImporters()
    {
        var changed = 0;
        var roots = new[]
        {
            "Assets/Resources/OriginalGame/Audio",
            "Assets/Resources/music",
        };
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", roots))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
                continue;
            var settings = importer.defaultSampleSettings;
            var needsChange = !settings.preloadAudioData
                || settings.loadType != AudioClipLoadType.DecompressOnLoad
                || importer.loadInBackground;
            if (!needsChange)
                continue;
            settings.preloadAudioData = true;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = false;
            importer.SaveAndReimport();
            changed += 1;
        }
        if (changed > 0)
            Debug.Log(
                "[GenesisAudioImport] Configured " + changed
                + " combat clips for deterministic WebGL preload.");
    }

    [MenuItem("Genesis/Audit Remote Player Prefab")]
    public static void AuditRemotePlayerPrefab()
    {
        const string prefabPath = "Assets/Resources/OriginalGame/RemotePlayer.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            throw new Exception("Remote player prefab is missing: " + prefabPath);

        var instance = UnityEngine.Object.Instantiate(prefab);
        instance.transform.position = Vector3.zero;
        instance.transform.rotation = Quaternion.identity;
        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            throw new Exception("Remote player prefab contains no renderers.");
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1))
            bounds.Encapsulate(renderer.bounds);
        Debug.Log(string.Format(
            "[GenesisRemotePlayerAudit] renderers={0}, center={1}, size={2}, rootScale={3}",
            renderers.Length, bounds.center, bounds.size, instance.transform.localScale));
        foreach (var renderer in renderers)
        {
            Debug.Log(string.Format(
                "[GenesisRemotePlayerAudit] renderer={0}, type={1}, enabled={2}, active={3}, layer={4}, bounds={5}",
                renderer.name, renderer.GetType().Name, renderer.enabled,
                renderer.gameObject.activeInHierarchy,
                LayerMask.LayerToName(renderer.gameObject.layer), renderer.bounds));
        }
        UnityEngine.Object.DestroyImmediate(instance);
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
        CopyRuntimePrefab(
            "Assets/JMO Assets/WarFX/_Effects/MuzzleFlashes/4Planes/"
                + "WFX_MF 4P RIFLE1.prefab",
            prefabDirectory + "/RecoveredMuzzleFlash.prefab");
        CreateRuntimePrefabFromModel(
            "Assets/Modern Weapons Pack/M4A1/FBX/M4A1 Sopmod.fbx",
            prefabDirectory + "/M4A1.prefab",
            "M4A1");
        CreateRuntimePrefabFromModel(
            "Assets/RecoveredWeapons/M16/M16.fbx",
            prefabDirectory + "/M16.prefab",
            "M16");
        CreateRecoveredM16MaterialClosure();
        ApplyRecoveredM16MaterialsToPrefab(prefabDirectory + "/M16.prefab");
        CreateRuntimePrefabFromModel(
            "Assets/Resources/OriginalGame/Weapons/M9/M9.obj",
            prefabDirectory + "/M9.prefab",
            "M9");
        CreateRecoveredM9Material();
        CreateFirstPersonRifleViewmodels();
        CreateFirstPersonM9Viewmodel();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void CreateFirstPersonRifleViewmodels()
    {
        CreateFirstPersonRifleViewmodel(
            "Assets/Resources/OriginalGame/M4A1.prefab",
            "Assets/Resources/OriginalGame/FirstPerson/M4A1Viewmodel.prefab",
            "M4A1Viewmodel",
            "Recovered_M4A1_Sopmod",
            Quaternion.Euler(0f, 180f, 0f));
        // The root M16 FBX has complete geometry and embedded materials but no
        // external texture closure. Keep this candidate out of the runtime
        // loadout until the visual preview passes instead of replacing the
        // currently usable archived rifle with an unverified white model.
        CreateFirstPersonRifleViewmodel(
            "Assets/Resources/OriginalGame/M16.prefab",
            "Assets/Resources/OriginalGame/FirstPerson/"
                + "M16ViewmodelCandidate.prefab",
            "M16ViewmodelCandidate",
            "Recovered_M16_Candidate",
            Quaternion.Euler(-90f, 0f, 0f));
        CreateFirstPersonRifleViewmodel(
            "Assets/Resources/OriginalGame/Weapons/AK74M/AK-74M.FBX",
            "Assets/Resources/OriginalGame/FirstPerson/"
                + "AK74MViewmodelCandidate.prefab",
            "AK74MViewmodelCandidate",
            "Recovered_AK74M_Candidate",
            Quaternion.Euler(-90f, 0f, 0f));
        CreateFirstPersonRifleViewmodel(
            "Assets/Resources/OriginalGame/Weapons/AWP/awp.obj",
            "Assets/Resources/OriginalGame/FirstPerson/"
                + "AWPViewmodelCandidate.prefab",
            "AWPViewmodelCandidate",
            "Recovered_AWP_Candidate",
            Quaternion.identity);
    }

    private static void CreateRecoveredM16MaterialClosure()
    {
        const string directory =
            "Assets/Resources/OriginalGame/Weapons/M16/GeneratedClosure";
        Directory.CreateDirectory(directory);
        CreateM16Texture(
            directory + "/M16_Gunmetal_Texture.asset",
            new Color(0.42f, 0.44f, 0.45f, 1f), 17);
        CreateM16Texture(
            directory + "/M16_Polymer_Texture.asset",
            new Color(0.2f, 0.21f, 0.215f, 1f), 41);
        CreateM16Texture(
            directory + "/M16_Steel_Texture.asset",
            new Color(0.6f, 0.62f, 0.63f, 1f), 73);
        CreateM16Material(
            directory + "/M16_Gunmetal.mat",
            directory + "/M16_Gunmetal_Texture.asset",
            0.25f, 0.26f);
        CreateM16Material(
            directory + "/M16_Polymer.mat",
            directory + "/M16_Polymer_Texture.asset",
            0.05f, 0.16f);
        CreateM16Material(
            directory + "/M16_Steel.mat",
            directory + "/M16_Steel_Texture.asset",
            0.42f, 0.38f);
    }

    private static void CreateM16Texture(
        string path, Color baseColor, int seed)
    {
        const int size = 64;
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        var created = texture == null;
        if (created)
            texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false);
        texture.name = Path.GetFileNameWithoutExtension(path);
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Bilinear;
        var pixels = new Color[size * size];
        for (var y = 0; y < size; y += 1)
        {
            for (var x = 0; x < size; x += 1)
            {
                var hash = unchecked(x * 73856093 ^ y * 19349663 ^ seed * 83492791);
                var noise = ((hash & 255) / 255f - 0.5f) * 0.09f;
                var machining = (x + seed) % 16 == 0 ? 0.035f : 0f;
                pixels[y * size + x] = new Color(
                    Mathf.Clamp01(baseColor.r + noise + machining),
                    Mathf.Clamp01(baseColor.g + noise + machining),
                    Mathf.Clamp01(baseColor.b + noise + machining),
                    1f);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(true, false);
        if (created)
            AssetDatabase.CreateAsset(texture, path);
        else
            EditorUtility.SetDirty(texture);
    }

    private static void CreateM16Material(
        string materialPath,
        string texturePath,
        float metallic,
        float glossiness)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard"))
            {
                name = Path.GetFileNameWithoutExtension(materialPath),
            };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        material.color = Color.white;
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Glossiness", glossiness);
        EditorUtility.SetDirty(material);
    }

    private static void ApplyRecoveredM16MaterialsToPrefab(string prefabPath)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
            throw new InvalidOperationException(
                "Recovered M16 runtime prefab is missing: " + prefabPath);
        try
        {
            ApplyRecoveredM16Materials(
                root.GetComponentsInChildren<Renderer>(true));
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ApplyRecoveredM16Materials(Renderer[] renderers)
    {
        const string directory =
            "Assets/Resources/OriginalGame/Weapons/M16/GeneratedClosure";
        var gunmetal = AssetDatabase.LoadAssetAtPath<Material>(
            directory + "/M16_Gunmetal.mat");
        var polymer = AssetDatabase.LoadAssetAtPath<Material>(
            directory + "/M16_Polymer.mat");
        var steel = AssetDatabase.LoadAssetAtPath<Material>(
            directory + "/M16_Steel.mat");
        if (gunmetal == null || polymer == null || steel == null)
            throw new InvalidOperationException(
                "Recovered M16 generated material closure is incomplete.");

        foreach (var renderer in renderers)
        {
            var name = renderer.name.ToLowerInvariant();
            Material selected;
            if (name.Contains("stock") || name.Contains("grip")
                || name.Contains("handle") || name.Contains("guard"))
                selected = polymer;
            else if (name.Contains("barrel") || name.Contains("bolt")
                || name.Contains("trigger") || name.Contains("sight"))
                selected = steel;
            else
            {
                // The source FBX mostly uses generic mesh names. A stable,
                // sparse accent keeps mechanical parts readable without
                // claiming that an invented texture is an original atlas.
                var stableNameSum = renderer.name.Sum(character => (int)character);
                selected = stableNameSum % 17 == 0
                    ? steel
                    : stableNameSum % 11 == 0 ? polymer : gunmetal;
            }
            var count = Mathf.Max(1, renderer.sharedMaterials.Length);
            renderer.sharedMaterials = Enumerable.Repeat(selected, count).ToArray();
        }
    }

    private static void CreateFirstPersonRifleViewmodel(
        string modelPath,
        string destinationPath,
        string viewmodelName,
        string modelName,
        Quaternion modelRotation)
    {
        const string rigPath =
            "Assets/Resources/OriginalGame/FirstPerson/AssaultRifle01.prefab";
        var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(rigPath);
        var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (rigPrefab == null || modelPrefab == null)
        {
            Debug.LogWarning(
                viewmodelName + " dependencies are missing: "
                + rigPath + ", " + modelPath);
            return;
        }

        var rig = UnityEngine.Object.Instantiate(rigPrefab);
        rig.name = viewmodelName;
        try
        {
            var gunRenderers = rig.GetComponentsInChildren<MeshRenderer>(true);
            if (gunRenderers.Length == 0)
                throw new InvalidOperationException(
                    "Recovered rifle rig has no firearm mesh renderers.");
            var targetBounds = gunRenderers[0].bounds;
            foreach (var renderer in gunRenderers.Skip(1))
                targetBounds.Encapsulate(renderer.bounds);

            var socket = rig.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == "WeaponMainLocator");
            if (socket == null)
                throw new InvalidOperationException(
                    "Recovered rifle rig has no WeaponMainLocator.");
            var model = UnityEngine.Object.Instantiate(modelPrefab, socket);
            model.name = modelName;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = modelRotation;
            model.transform.localScale = Vector3.one;
            foreach (var collider in model.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);

            var modelRenderers = model.GetComponentsInChildren<Renderer>(true);
            if (modelRenderers.Length == 0)
                throw new InvalidOperationException(
                    viewmodelName + " model has no renderers.");
            if (viewmodelName == "AK74MViewmodelCandidate"
                || viewmodelName == "AWPViewmodelCandidate")
                ApplyArchivedRifleCandidateMaterial(modelRenderers);
            var modelBounds = modelRenderers[0].bounds;
            foreach (var renderer in modelRenderers.Skip(1))
                modelBounds.Encapsulate(renderer.bounds);
            var sourceLength = Mathf.Max(
                modelBounds.size.x,
                Mathf.Max(modelBounds.size.y, modelBounds.size.z));
            var targetLength = Mathf.Max(
                targetBounds.size.x,
                Mathf.Max(targetBounds.size.y, targetBounds.size.z));
            if (sourceLength > 0.0001f)
                model.transform.localScale *= targetLength / sourceLength;
            modelBounds = modelRenderers[0].bounds;
            foreach (var renderer in modelRenderers.Skip(1))
                modelBounds.Encapsulate(renderer.bounds);
            model.transform.position += targetBounds.center - modelBounds.center;

            foreach (var renderer in gunRenderers)
                renderer.enabled = false;
            CreateRifleShoulderCap(rig, viewmodelName);
            PrefabUtility.SaveAsPrefabAsset(rig, destinationPath);
            Debug.Log(
                "Created recovered animated rifle viewmodel: "
                + destinationPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rig);
        }
    }

    private static void ApplyArchivedRifleCandidateMaterial(Renderer[] renderers)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Resources/OriginalGame/FirstPerson/Materials/"
            + "AssaultRifle01.mat");
        if (material == null || material.mainTexture == null)
            throw new InvalidOperationException(
                "Archived rifle candidate fallback material is incomplete.");
        foreach (var renderer in renderers)
        {
            var count = Mathf.Max(1, renderer.sharedMaterials.Length);
            renderer.sharedMaterials = Enumerable.Repeat(material, count).ToArray();
        }
    }

    private static void CreateRifleShoulderCap(
        GameObject rig,
        string viewmodelName)
    {
        var shoulder = rig.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item => item.name == "Left_Elbow");
        var leftArm = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .FirstOrDefault(item => item.name == "Left");
        if (shoulder == null || leftArm == null || leftArm.sharedMaterial == null)
            throw new InvalidOperationException(
                viewmodelName + " cannot cap the open left forearm.");

        var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        cap.name = "LeftForearmCap";
        cap.transform.SetParent(shoulder, false);
        cap.transform.localPosition = Vector3.zero;
        cap.transform.localRotation = Quaternion.identity;
        cap.transform.localScale = Vector3.one * 0.1f;
        cap.GetComponent<MeshRenderer>().sharedMaterial = leftArm.sharedMaterial;
        var collider = cap.GetComponent<Collider>();
        if (collider != null)
            UnityEngine.Object.DestroyImmediate(collider);
    }

    private static void CreateRecoveredM9Material()
    {
        const string texturePath =
            "Assets/Resources/OriginalGame/Weapons/M9/m9.jpg";
        const string materialPath =
            "Assets/Resources/OriginalGame/Weapons/M9/M9_Recovered.mat";
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null)
        {
            Debug.LogWarning("Recovered M9 diffuse atlas is missing: " + texturePath);
            return;
        }
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            material.name = "M9_Recovered";
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.mainTexture = texture;
        material.color = Color.white;
        material.SetFloat("_Metallic", 0.42f);
        material.SetFloat("_Glossiness", 0.38f);
        EditorUtility.SetDirty(material);
    }

    private static void CreateFirstPersonM9Viewmodel()
    {
        const string rigPath =
            "Assets/Resources/OriginalGame/FirstPerson/Pistol/Pistol01.prefab";
        const string modelPath = "Assets/Resources/OriginalGame/M9.prefab";
        const string materialPath =
            "Assets/Resources/OriginalGame/Weapons/M9/M9_Recovered.mat";
        const string destinationPath =
            "Assets/Resources/OriginalGame/FirstPerson/"
            + "M9Viewmodel.prefab";
        var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(rigPath);
        var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (rigPrefab == null || modelPrefab == null || material == null)
        {
            Debug.LogWarning("M9 viewmodel candidate dependencies are incomplete.");
            return;
        }

        var rig = UnityEngine.Object.Instantiate(rigPrefab);
        rig.name = "M9Viewmodel";
        try
        {
            var gunRenderers = rig.GetComponentsInChildren<MeshRenderer>(true);
            if (gunRenderers.Length == 0)
                throw new InvalidOperationException(
                    "Recovered pistol rig has no firearm mesh renderers.");
            var targetBounds = gunRenderers[0].bounds;
            foreach (var renderer in gunRenderers.Skip(1))
                targetBounds.Encapsulate(renderer.bounds);
            var socket = rig.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == "MainMesh");
            if (socket == null)
                throw new InvalidOperationException(
                    "Recovered pistol rig has no MainMesh socket.");

            // MainMesh is the archived pistol mesh itself, not a neutral socket:
            // it carries a 0.0299 import scale and a 180 degree model-axis
            // correction. Parenting another imported model below it compounds
            // both transforms and puts the replacement almost vertically in
            // front of the camera. Attach beside it under the animated
            // RightHand bone and copy the archived mesh pose instead.
            var model = UnityEngine.Object.Instantiate(
                modelPrefab, socket.parent);
            model.name = "Recovered_M9_Candidate";
            model.transform.localPosition = socket.localPosition;
            model.transform.localRotation = socket.localRotation;
            model.transform.localScale = Vector3.one;
            foreach (var collider in model.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            var modelRenderers = model.GetComponentsInChildren<Renderer>(true);
            if (modelRenderers.Length == 0)
                throw new InvalidOperationException("Recovered M9 has no renderers.");
            foreach (var renderer in modelRenderers)
            {
                var materialCount = Mathf.Max(1, renderer.sharedMaterials.Length);
                renderer.sharedMaterials = Enumerable
                    .Repeat(material, materialCount)
                    .ToArray();
            }
            var modelBounds = modelRenderers[0].bounds;
            foreach (var renderer in modelRenderers.Skip(1))
                modelBounds.Encapsulate(renderer.bounds);
            var sourceLength = Mathf.Max(
                modelBounds.size.x,
                Mathf.Max(modelBounds.size.y, modelBounds.size.z));
            var targetLength = Mathf.Max(
                targetBounds.size.x,
                Mathf.Max(targetBounds.size.y, targetBounds.size.z));
            if (sourceLength > 0.0001f)
                model.transform.localScale *= targetLength / sourceLength;
            modelBounds = modelRenderers[0].bounds;
            foreach (var renderer in modelRenderers.Skip(1))
                modelBounds.Encapsulate(renderer.bounds);
            model.transform.position += targetBounds.center - modelBounds.center;
            foreach (var renderer in gunRenderers)
                renderer.enabled = false;
            PrefabUtility.SaveAsPrefabAsset(rig, destinationPath);
            Debug.Log("Created recovered M9 viewmodel: " + destinationPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rig);
        }
    }

    private static void CreateRuntimePrefabFromModel(
        string sourcePath,
        string destinationPath,
        string objectName)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(destinationPath) != null)
            return;
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (source == null)
        {
            Debug.LogWarning("Recovered weapon model was not found: " + sourcePath);
            return;
        }

        var instance = UnityEngine.Object.Instantiate(source);
        instance.name = objectName;
        foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
            UnityEngine.Object.DestroyImmediate(collider);
        PrefabUtility.SaveAsPrefabAsset(instance, destinationPath);
        UnityEngine.Object.DestroyImmediate(instance);
        Debug.Log("Created recovered weapon resource prefab: " + destinationPath);
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
        var output = new StringBuilder();
        foreach (var mapName in RecoveredMapScenes.Keys)
        {
            var scenePath = "Assets/PlayableMaps/" + mapName + ".unity";
            var scene = EditorSceneManager.OpenScene(
                scenePath, OpenSceneMode.Single);
            output.AppendLine("MAP " + mapName);
            var renderers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .ToArray();
            var enabledRenderers = renderers
                .Where(renderer =>
                    renderer.enabled && renderer.gameObject.activeInHierarchy)
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
                         .SelectMany(root =>
                             root.GetComponentsInChildren<Camera>(true)))
            {
                output.AppendLine(
                    $"Camera {HierarchyPath(camera.transform)} active={camera.gameObject.activeInHierarchy} " +
                    $"enabled={camera.enabled} position={camera.transform.position} rotation={camera.transform.eulerAngles} " +
                    $"clear={camera.clearFlags} mask={camera.cullingMask} near={camera.nearClipPlane} far={camera.farClipPlane}");
            }

            foreach (var light in scene.GetRootGameObjects()
                         .SelectMany(root =>
                             root.GetComponentsInChildren<Light>(true)))
            {
                output.AppendLine(
                    $"Light {HierarchyPath(light.transform)} active={light.gameObject.activeInHierarchy} " +
                    $"enabled={light.enabled} type={light.type} intensity={light.intensity}");
            }

            foreach (var root in scene.GetRootGameObjects()
                         .Where(root => root.name.Contains("Player")))
                output.AppendLine(
                    $"Player {root.name} active={root.activeSelf} " +
                    $"position={root.transform.position} rotation={root.transform.eulerAngles}");
        }

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

        var playerCamera = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Camera>(true))
            .FirstOrDefault(camera => camera.CompareTag("MainCamera"));
        if (playerCamera != null)
        {
            var upwardRay = new Ray(playerCamera.transform.position, Vector3.up);
            output.AppendLine(
                "ENVIRONMENT camera=" + playerCamera.transform.position
                + " skybox="
                + (RenderSettings.skybox == null
                    ? "none"
                    : RenderSettings.skybox.name));
            foreach (var renderer in scene.GetRootGameObjects()
                         .SelectMany(root =>
                             root.GetComponentsInChildren<Renderer>(true))
                         .Where(renderer =>
                             renderer.enabled
                             && renderer.gameObject.activeInHierarchy))
            {
                float distance;
                if (renderer.bounds.IntersectRay(upwardRay, out distance)
                    && distance >= 0f
                    && distance <= 100f)
                {
                    output.AppendLine(
                        "  OVERHEAD " + HierarchyPath(renderer.transform)
                        + " distance=" + distance.ToString("0.00")
                        + " bounds=" + renderer.bounds
                        + " material="
                        + (renderer.sharedMaterial == null
                            ? "none"
                            : renderer.sharedMaterial.name));
                }
            }

            var mapRenderers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(renderer =>
                    renderer.enabled
                    && renderer.gameObject.activeInHierarchy
                    && !renderer.transform.IsChildOf(playerCamera.transform.root))
                .ToArray();
            if (mapRenderers.Length > 0)
            {
                var mapBounds = mapRenderers[0].bounds;
                foreach (var renderer in mapRenderers.Skip(1))
                    mapBounds.Encapsulate(renderer.bounds);
                var physicsScene = scene.GetPhysicsScene();
                for (var gridX = 0; gridX <= 8; gridX++)
                for (var gridZ = 0; gridZ <= 12; gridZ++)
                {
                    var x = Mathf.Lerp(mapBounds.min.x + 1f,
                        mapBounds.max.x - 1f, gridX / 8f);
                    var z = Mathf.Lerp(mapBounds.min.z + 1f,
                        mapBounds.max.z - 1f, gridZ / 12f);
                    RaycastHit groundHit;
                    if (!physicsScene.Raycast(
                            new Vector3(x, mapBounds.max.y + 12f, z),
                            Vector3.down,
                            out groundHit,
                            mapBounds.size.y + 24f,
                            ~0,
                            QueryTriggerInteraction.Ignore)
                        || Vector3.Dot(groundHit.normal, Vector3.up) < 0.72f)
                    {
                        continue;
                    }

                    RaycastHit ceilingHit;
                    var open = !physicsScene.Raycast(
                        groundHit.point + Vector3.up * 1.2f,
                        Vector3.up,
                        out ceilingHit,
                        18f,
                        ~0,
                        QueryTriggerInteraction.Ignore);
                    if (open)
                        output.AppendLine(
                            "  OPEN_SPAWN "
                            + (groundHit.point + Vector3.up * 1.2f)
                            + " ground="
                            + HierarchyPath(groundHit.transform));
                }
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
        if (!diagnosticBuild)
            GenesisResourceClosureGate.EnsureFormalBuildAllowed();
        ConfigureRecoveredAudioImporters();
        CreateRemotePlayerAnimatorController();
        CreateCombatResourcePrefabs();
        GenesisWeaponRecoveryAudit.ValidateRecoveredWeaponPrefabs();
        GenesisCharacterAnimationAudit.ValidateRecoveredLocomotion();
        SanitizePlayableRecoveredMaps();
        GenesisMapRecoveryAudit.EnsureMapRecoveryReadiness();
        GenesisSevenMapAcceptanceAudit.EnsureReady();

        // Refuse to publish if a promoted rotation map loses its gameplay rig,
        // collision coverage or a sane in-bounds spawn during later recovery.
        ValidatePlayableRecoveredMaps();

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

        EnableAutomaticPersistentDataSync(outputPath);
        AddWebCacheVersion(outputPath);
        Debug.Log($"Restored WebGL build complete: {outputPath} ({report.summary.totalSize} bytes)");
    }

    private static void EnableAutomaticPersistentDataSync(string outputPath)
    {
        var indexPath = Path.Combine(outputPath, "index.html");
        var html = File.ReadAllText(indexPath);
        const string marker = "        showBanner: unityShowBanner,\n";
        const string setting =
            "        autoSyncPersistentDataPath: true,\n";
        if (html.Contains(setting))
            return;
        if (!html.Contains(marker))
            throw new InvalidOperationException(
                "Unity WebGL template no longer exposes the expected config "
                + "marker: " + indexPath);
        File.WriteAllText(
            indexPath,
            html.Replace(marker, marker + setting),
            new UTF8Encoding(false));
    }

    [MenuItem("Genesis/Characters/Rebuild Remote Locomotion Controller")]
    public static void CreateRemotePlayerAnimatorController()
    {
        const string controllerPath =
            "Assets/Resources/OriginalGame/Character/RemotePlayer.controller";
        var controller =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);

        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(
            "Assets/Resources/OriginalGame/Character/Animations/StandIdleOneHand.anim");
        var walkForward = AssetDatabase.LoadAssetAtPath<AnimationClip>(
            "Assets/Resources/OriginalGame/Character/Animations/WalkForward.anim");
        var runForward = AssetDatabase.LoadAssetAtPath<AnimationClip>(
            "Assets/Resources/OriginalGame/Character/Animations/Run.anim");
        var walkBackward = AssetDatabase.LoadAssetAtPath<AnimationClip>(
            "Assets/Resources/OriginalGame/Character/Animations/WalkBackwardOneHand.anim");
        var runBackward = AssetDatabase.LoadAssetAtPath<AnimationClip>(
            "Assets/Resources/OriginalGame/Character/Animations/RunBackwardOneHand.anim");
        var walkLeft = AssetDatabase.LoadAssetAtPath<AnimationClip>(
            "Assets/Resources/OriginalGame/Character/Animations/WalkStrafeLeftOneHand.anim");
        var runLeft = AssetDatabase.LoadAssetAtPath<AnimationClip>(
            "Assets/Resources/OriginalGame/Character/Animations/RunStrafeLeftOneHand.anim");
        var walkRight = AssetDatabase.LoadAssetAtPath<AnimationClip>(
            "Assets/Resources/OriginalGame/Character/Animations/WalkStrafeRightOneHand.anim");
        var runRight = AssetDatabase.LoadAssetAtPath<AnimationClip>(
            "Assets/Resources/OriginalGame/Character/Animations/RunStrafeRightOneHand.anim");
        var jump = AssetDatabase.LoadAssetAtPath<AnimationClip>(
            "Assets/Resources/OriginalGame/Character/Animations/Jump.anim");
        var locomotionClips = new[]
        {
            idle, walkForward, runForward, walkBackward, runBackward,
            walkLeft, runLeft, walkRight, runRight
        };
        if (locomotionClips.Any(clip => clip == null) || jump == null)
        {
            Debug.LogWarning(
                "[GenesisAnimation] 第三人称恢复动画尚未导入，跳过状态机生成。");
            return;
        }

        if (controller != null)
        {
            UpgradeRemotePlayerLandingState(controller, jump);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(controllerPath));
        controller =
            AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
        controller.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
        controller.AddParameter(
            "Grounded", AnimatorControllerParameterType.Bool);

        var stateMachine = controller.layers[0].stateMachine;
        var locomotion = stateMachine.AddState("Locomotion");
        var blendTree = new BlendTree
        {
            name = "Recovered Locomotion",
            blendType = BlendTreeType.FreeformCartesian2D,
            blendParameter = "MoveX",
            blendParameterY = "MoveZ",
            useAutomaticThresholds = false
        };
        AssetDatabase.AddObjectToAsset(blendTree, controller);
        blendTree.AddChild(idle, Vector2.zero);
        blendTree.AddChild(walkForward, new Vector2(0f, 0.5f));
        blendTree.AddChild(runForward, new Vector2(0f, 1f));
        blendTree.AddChild(walkBackward, new Vector2(0f, -0.5f));
        blendTree.AddChild(runBackward, new Vector2(0f, -1f));
        blendTree.AddChild(walkLeft, new Vector2(-0.5f, 0f));
        blendTree.AddChild(runLeft, new Vector2(-1f, 0f));
        blendTree.AddChild(walkRight, new Vector2(0.5f, 0f));
        blendTree.AddChild(runRight, new Vector2(1f, 0f));
        locomotion.motion = blendTree;
        stateMachine.defaultState = locomotion;

        var jumpState = stateMachine.AddState("Jump");
        jumpState.motion = jump;
        var landState = stateMachine.AddState("Land");
        landState.motion = jump;
        landState.cycleOffset = 0.68f;
        landState.speed = 1.35f;
        var toJump = locomotion.AddTransition(jumpState);
        toJump.hasExitTime = false;
        toJump.duration = 0.08f;
        toJump.AddCondition(
            AnimatorConditionMode.IfNot, 0f, "Grounded");
        var toLand = jumpState.AddTransition(landState);
        toLand.hasExitTime = false;
        toLand.duration = 0.06f;
        toLand.AddCondition(
            AnimatorConditionMode.If, 0f, "Grounded");
        var toLocomotion = landState.AddTransition(locomotion);
        toLocomotion.hasExitTime = true;
        toLocomotion.exitTime = 0.28f;
        toLocomotion.duration = 0.08f;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            "[GenesisAnimation] 已生成第三人称八方向走跑跳状态机: " + controllerPath);
    }

    private static void UpgradeRemotePlayerLandingState(
        AnimatorController controller,
        AnimationClip jump)
    {
        if (controller.layers.Length == 0)
            throw new InvalidOperationException(
                "Remote player animator has no layers.");
        var stateMachine = controller.layers[0].stateMachine;
        var locomotion = stateMachine.states
            .Select(item => item.state)
            .FirstOrDefault(item => item.name == "Locomotion");
        var jumpState = stateMachine.states
            .Select(item => item.state)
            .FirstOrDefault(item => item.name == "Jump");
        if (locomotion == null || jumpState == null)
            throw new InvalidOperationException(
                "Remote player animator is missing locomotion or jump.");
        var landState = stateMachine.states
            .Select(item => item.state)
            .FirstOrDefault(item => item.name == "Land");
        if (landState == null)
            landState = stateMachine.AddState("Land");
        landState.motion = jump;
        landState.cycleOffset = 0.68f;
        landState.speed = 1.35f;

        foreach (var transition in jumpState.transitions.ToArray())
            jumpState.RemoveTransition(transition);
        foreach (var transition in landState.transitions.ToArray())
            landState.RemoveTransition(transition);
        var toLand = jumpState.AddTransition(landState);
        toLand.hasExitTime = false;
        toLand.duration = 0.06f;
        toLand.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
        var toLocomotion = landState.AddTransition(locomotion);
        toLocomotion.hasExitTime = true;
        toLocomotion.exitTime = 0.28f;
        toLocomotion.duration = 0.08f;

        EditorUtility.SetDirty(landState);
        EditorUtility.SetDirty(jumpState);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log(
            "[GenesisAnimation] 已升级第三人称独立落地状态: "
            + controller.name);
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

    private static Bounds? CalculateVisibleBounds(Scene scene)
    {
        var renderers = scene.GetRootGameObjects()
            .Where(root => !IsExistingPlayerRoot(root))
            .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
            .Where(renderer =>
                renderer.enabled && renderer.gameObject.activeInHierarchy)
            .ToArray();
        if (renderers.Length == 0)
            return null;

        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1))
            bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    private static bool ContainsWithMargin(
        Bounds bounds,
        Vector3 position,
        float margin)
    {
        bounds.Expand(margin * 2f);
        return bounds.Contains(position);
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
