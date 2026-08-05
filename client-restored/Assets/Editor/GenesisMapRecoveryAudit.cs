#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;
using GenesisSoldierSoul.Multiplayer;

public static class GenesisMapRecoveryAudit
{
    [Serializable]
    private sealed class MapReport
    {
        public string map;
        public int rendererCount;
        public int colliderCount;
        public int lightCount;
        public int missingMaterialSlots;
        public int missingShaders;
        public int backgroundOutlierRenderers;
        public int nearbyEnvironmentRenderers;
        public int distinctVisualMaterialSignatures;
        public bool hasSkybox;
        public Vector3 visibleBoundsCenter;
        public Vector3 visibleBoundsSize;
        public Vector3 spawn;
        public bool groundBelowSpawn;
        public float groundDistance;
        public bool overheadBlocked;
        public float minimumHorizontalClearance;
        public float averageHorizontalClearance;
        public bool spawnOnNavMesh;
        public int navMeshVertices;
        public int environmentMeshCount;
        public int missingRequiredNormals;
        public int missingRequiredUv;
        public long triangleCount;
        public int maximumTextureDimension;
        public int environmentAudioSourceCount;
        public int proceduralAmbientSourceCount;
        public int missingEnvironmentAudioClips;
        public bool fogEnabled;
        public int navMeshIslandCount;
        public float spawnNavMeshComponentRatio;
        public int unsupportedNavMeshVertices;
        public int unsupportedReachableNavMeshVertices;
        public float navMeshReachableSampleRatio;
        public int reachableNavMeshSamples;
        public Vector2 reachableNavMeshSpan;
        public bool strictReady;
        public string[] failures;
    }

    [Serializable]
    private sealed class AuditReport
    {
        public string generatedAtUtc;
        public int mapCount;
        public int strictReadyCount;
        public MapReport[] maps;
    }

    private static readonly string[] MapNames =
    {
        "Pyramid",
        "NewConstructionSite",
        "BiochemicalTown",
        "ClassicConstructionSite",
        "SteelFactory",
        "IceFireMaze",
        "RadiationDistrict",
    };

    [MenuItem("Genesis/Audit Map Recovery Readiness")]
    public static void AuditMapRecoveryReadiness()
    {
        var reports = new List<MapReport>();
        foreach (var mapName in MapNames)
            reports.Add(AuditMap(mapName));

        var report = new AuditReport
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            mapCount = reports.Count,
            strictReadyCount = reports.Count(item => item.strictReady),
            maps = reports.ToArray(),
        };
        var recoveryDirectory = Path.GetFullPath(
            Path.Combine(Application.dataPath, "../../recovery"));
        Directory.CreateDirectory(recoveryDirectory);
        var jsonPath = Path.Combine(
            recoveryDirectory, "map-recovery-readiness.json");
        File.WriteAllText(
            jsonPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));

        foreach (var item in reports)
        {
            Debug.Log(string.Format(
                "[GenesisMapRecoveryAudit] {0}: ready={1}; renderers={2}; "
                + "colliders={3}; materialsMissing={4}; shadersMissing={5}; "
                + "ground={6} ({7:F2}m); overhead={8}; navVertices={9}; "
                + "failures={10}",
                item.map,
                item.strictReady,
                item.rendererCount,
                item.colliderCount,
                item.missingMaterialSlots,
                item.missingShaders,
                item.groundBelowSpawn,
                item.groundDistance,
                item.overheadBlocked,
                item.navMeshVertices,
                item.failures.Length == 0
                    ? "none"
                    : string.Join(" | ", item.failures)));
        }
        Debug.Log(
            "[GenesisMapRecoveryAudit] Strict ready "
            + report.strictReadyCount + "/" + report.mapCount
            + "; report=" + jsonPath);
    }

    public static void EnsureMapRecoveryReadiness()
    {
        var failures = MapNames
            .Select(AuditMap)
            .Where(report => !report.strictReady)
            .Select(report => report.map + ": "
                + string.Join(" | ", report.failures))
            .ToArray();
        if (failures.Length > 0)
        {
            throw new InvalidOperationException(
                "Playable map recovery gate failed:\n"
                + string.Join("\n", failures));
        }
        Debug.Log(
            "[GenesisMapRecoveryAudit] Build gate passed "
            + MapNames.Length + "/" + MapNames.Length);
    }

    [MenuItem("Genesis/Repair Map Collision And Navigation")]
    public static void RepairMapCollisionAndNavigation()
    {
        var navMeshDirectory = "Assets/PlayableMaps/NavMesh";
        Directory.CreateDirectory(navMeshDirectory);
        foreach (var mapName in MapNames)
        {
            var scenePath = "Assets/PlayableMaps/" + mapName + ".unity";
            var scene = EditorSceneManager.OpenScene(
                scenePath, OpenSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            if (mapName == "SteelFactory")
                NormalizeSteelFactoryScale(scene);
            var addedColliders = 0;
            if (mapName == "SteelFactory"
                || mapName == "RadiationDistrict")
            {
                foreach (var filter in scene.GetRootGameObjects()
                             .SelectMany(root =>
                                 root.GetComponentsInChildren<MeshFilter>(true)))
                {
                    if (filter.sharedMesh == null
                        || !filter.gameObject.activeInHierarchy
                        || filter.GetComponent<Renderer>() == null
                        || filter.GetComponent<Collider>() != null
                        || IsGameplayRig(filter.transform))
                    {
                        continue;
                    }
                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    collider.convex = false;
                    addedColliders += 1;
                }
            }

            var navigationObject = scene.GetRootGameObjects()
                .FirstOrDefault(root => root.name == "GenesisNavigation");
            if (navigationObject == null)
                navigationObject = new GameObject("GenesisNavigation");
            var surface = navigationObject.GetComponent<NavMeshSurface>();
            if (surface == null)
                surface = navigationObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = mapName == "Pyramid"
                ? NavMeshCollectGeometry.RenderMeshes
                : NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.defaultArea = 0;
            surface.overrideTileSize = true;
            surface.tileSize = 256;
            surface.overrideVoxelSize = false;
            surface.minRegionArea = 1.5f;
            surface.BuildNavMesh();
            if (surface.navMeshData == null)
                throw new InvalidOperationException(
                    "NavMesh bake produced no data for " + mapName);

            var navMeshPath = navMeshDirectory + "/" + mapName + ".asset";
            var existingPath = AssetDatabase.GetAssetPath(surface.navMeshData);
            if (string.IsNullOrEmpty(existingPath))
            {
                if (AssetDatabase.LoadAssetAtPath<NavMeshData>(navMeshPath) != null)
                    AssetDatabase.DeleteAsset(navMeshPath);
                AssetDatabase.CreateAsset(surface.navMeshData, navMeshPath);
            }
            if (mapName == "Pyramid"
                || mapName == "NewConstructionSite"
                || mapName == "BiochemicalTown"
                || mapName == "RadiationDistrict"
                || mapName == "IceFireMaze")
            {
                var player = scene.GetRootGameObjects()
                    .FirstOrDefault(root => root.name == "First Person Player");
                if (player == null)
                    throw new InvalidOperationException(
                        mapName + " player root is missing");
                PlaceNearbySafeSpawn(
                    scene,
                    player.transform,
                    mapName == "Pyramid"
                        ? 38f
                        : mapName == "IceFireMaze"
                            ? 1000f
                            : mapName == "BiochemicalTown"
                                ? 45f
                                : mapName == "RadiationDistrict" ? 90f : 18f,
                    mapName);
            }
            if (mapName == "SteelFactory")
            {
                var player = scene.GetRootGameObjects()
                    .FirstOrDefault(root => root.name == "First Person Player");
                if (player == null)
                    throw new InvalidOperationException(
                        "SteelFactory player root is missing");
                PlaceSteelFactorySafeSpawn(scene, player.transform);
                Debug.Log(
                    "[GenesisMapRepair] SteelFactory spawn moved to "
                    + player.transform.position);
            }
            EditorUtility.SetDirty(surface);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log(
                "[GenesisMapRepair] " + mapName
                + ": collidersAdded=" + addedColliders
                + "; navMesh=" + navMeshPath);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("Genesis/Diagnose Steel Factory Spawn")]
    public static void DiagnoseSteelFactorySpawn()
    {
        const string scenePath = "Assets/PlayableMaps/SteelFactory.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        var renderers = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
            .Where(renderer => renderer.enabled
                && renderer.gameObject.activeInHierarchy
                && renderer.bounds.size.x < 500f
                && renderer.bounds.size.y < 500f
                && renderer.bounds.size.z < 500f)
            .ToArray();
        var geometryCenter = renderers.Length == 0
            ? Vector3.zero
            : renderers.Aggregate(
                new Vector3(), (sum, renderer) => sum + renderer.bounds.center)
                / renderers.Length;
        var vertices = NavMesh.CalculateTriangulation().vertices;
        var candidates = vertices
            .Select(vertex => new
            {
                position = vertex,
                nearby = renderers.Count(renderer =>
                    Vector3.Distance(
                        renderer.bounds.ClosestPoint(vertex + Vector3.up * 1.2f),
                        vertex + Vector3.up * 1.2f) < 65f),
                centerDistance = Vector2.Distance(
                    new Vector2(vertex.x, vertex.z),
                    new Vector2(geometryCenter.x, geometryCenter.z)),
            })
            .OrderByDescending(item => item.nearby)
            .ThenBy(item => item.centerDistance)
            .Take(20)
            .ToArray();
        var highestCandidates = vertices
            .Select(vertex => new
            {
                position = vertex,
                nearby = renderers.Count(renderer =>
                    Vector3.Distance(
                        renderer.bounds.ClosestPoint(vertex + Vector3.up * 1.2f),
                        vertex + Vector3.up * 1.2f) < 65f),
                centerDistance = Vector2.Distance(
                    new Vector2(vertex.x, vertex.z),
                    new Vector2(geometryCenter.x, geometryCenter.z)),
            })
            .Where(item => item.nearby > 0)
            .OrderByDescending(item => item.position.y)
            .ThenByDescending(item => item.nearby)
            .ThenBy(item => item.centerDistance)
            .Take(20)
            .ToArray();
        Debug.Log(
            "[GenesisSteelSpawn] geometryCenter=" + geometryCenter
            + "; navVertices=" + vertices.Length
            + "; navY=" + vertices.Min(item => item.y).ToString("F2")
            + ".." + vertices.Max(item => item.y).ToString("F2")
            + "\n" + string.Join(
                "\n",
                candidates.Select(item =>
                    item.position + "; nearby=" + item.nearby
                    + "; centerDistance=" + item.centerDistance.ToString("F2")))
            + "\nHighest:\n" + string.Join(
                "\n",
                highestCandidates.Select(item =>
                    item.position + "; nearby=" + item.nearby
                    + "; centerDistance=" + item.centerDistance.ToString("F2"))));
    }

    [MenuItem("Genesis/Capture Steel Factory Normalized Preview")]
    public static void CaptureSteelFactoryNormalizedPreview()
    {
        CaptureMapPreview(
            "SteelFactory", "steel-factory-normalized-preview.png");
    }

    [MenuItem("Genesis/Capture Pyramid Spawn Preview")]
    public static void CapturePyramidSpawnPreview()
    {
        CaptureMapPreview("Pyramid", "pyramid-safe-spawn-preview.png");
    }

    [MenuItem("Genesis/Capture New Construction Spawn Preview")]
    public static void CaptureNewConstructionSpawnPreview()
    {
        CaptureMapPreview(
            "NewConstructionSite", "new-construction-spawn-preview.png");
    }

    [MenuItem("Genesis/Capture Biochemical Town Spawn Preview")]
    public static void CaptureBiochemicalTownSpawnPreview()
    {
        CaptureMapPreview(
            "BiochemicalTown", "biochemical-town-spawn-preview.png");
    }

    [MenuItem("Genesis/Capture Classic Construction Spawn Preview")]
    public static void CaptureClassicConstructionSpawnPreview()
    {
        CaptureMapPreview(
            "ClassicConstructionSite",
            "classic-construction-spawn-preview.png");
    }

    [MenuItem("Genesis/Capture Radiation District Spawn Preview")]
    public static void CaptureRadiationDistrictSpawnPreview()
    {
        CaptureMapPreview(
            "RadiationDistrict", "radiation-district-spawn-preview.png");
    }

    [MenuItem("Genesis/Capture Ice Fire Maze Spawn Preview")]
    public static void CaptureIceFireMazeSpawnPreview()
    {
        CaptureMapPreview("IceFireMaze", "ice-fire-maze-spawn-preview.png");
    }

    [MenuItem("Genesis/Audit New Construction Ambience")]
    public static void AuditNewConstructionAmbience()
    {
        AuditProceduralAmbience(
            "NewConstructionSite",
            GenesisProceduralAmbient.AmbientProfile.ConstructionWind,
            "new-construction-ambient-audit.txt");
    }

    [MenuItem("Genesis/Audit Biochemical Town Ambience")]
    public static void AuditBiochemicalTownAmbience()
    {
        AuditProceduralAmbience(
            "BiochemicalTown",
            GenesisProceduralAmbient.AmbientProfile.TownNight,
            "biochemical-town-ambient-audit.txt");
    }

    [MenuItem("Genesis/Audit Classic Construction Ambience")]
    public static void AuditClassicConstructionAmbience()
    {
        AuditProceduralAmbience(
            "ClassicConstructionSite",
            GenesisProceduralAmbient.AmbientProfile.ConstructionWind,
            "classic-construction-ambient-audit.txt");
    }

    [MenuItem("Genesis/Audit Radiation District Ambience")]
    public static void AuditRadiationDistrictAmbience()
    {
        AuditProceduralAmbience(
            "RadiationDistrict",
            GenesisProceduralAmbient.AmbientProfile.IndustrialHum,
            "radiation-district-ambient-audit.txt");
    }

    [MenuItem("Genesis/Audit Ice Fire Maze Ambience")]
    public static void AuditIceFireMazeAmbience()
    {
        AuditProceduralAmbience(
            "IceFireMaze",
            GenesisProceduralAmbient.AmbientProfile.ColdWind,
            "ice-fire-maze-ambient-audit.txt");
    }

    private static void AuditProceduralAmbience(
        string mapName,
        GenesisProceduralAmbient.AmbientProfile expectedProfile,
        string reportName)
    {
        var scene = EditorSceneManager.OpenScene(
            "Assets/PlayableMaps/" + mapName + ".unity",
            OpenSceneMode.Single);
        var ambience = scene.GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<GenesisProceduralAmbient>(true))
            .SingleOrDefault();
        if (ambience == null)
            throw new InvalidOperationException(
                mapName + " procedural ambience is missing");
        var source = ambience.GetComponent<AudioSource>();
        var clip = ambience.GenerateForEditorAudit();
        if (clip == null)
            throw new InvalidOperationException("Procedural ambience created no clip");
        var samples = new float[clip.samples];
        if (!clip.GetData(samples, 0))
            throw new InvalidOperationException("Procedural ambience PCM read failed");
        var rms = Mathf.Sqrt(samples.Select(value => value * value).Average());
        var seamDelta = Mathf.Abs(samples[0] - samples[samples.Length - 1]);
        var failures = new List<string>();
        if (ambience.CurrentProfile != expectedProfile)
            failures.Add("procedural ambience profile is incorrect");
        if (clip.frequency != 22050)
            failures.Add("frequency is not 22050 Hz");
        if (clip.samples != 176400)
            failures.Add("sample count is not eight seconds");
        if (rms < 0.005f || rms > 0.2f)
            failures.Add("RMS level is outside the safe ambience range");
        if (seamDelta > 0.01f)
            failures.Add("loop seam is discontinuous");
        if (!source.loop || source.playOnAwake || source.spatialBlend != 0f)
            failures.Add("AudioSource loop/autoplay/spatial configuration is invalid");
        var recoveryDirectory = Path.GetFullPath(
            Path.Combine(Application.dataPath, "../../recovery"));
        Directory.CreateDirectory(recoveryDirectory);
        var reportPath = Path.Combine(
            recoveryDirectory, reportName);
        File.WriteAllText(
            reportPath,
            string.Format(
                "map={0}\nprofile={1}\nclip={2}\nsamples={3}\nfrequency={4}\n"
                + "length={5:F3}\nrms={6:F6}\nseamDelta={7:F6}\nloop={8}\n"
                + "playOnAwake={9}\nspatialBlend={10:F1}\nfailures={11}\n",
                mapName,
                ambience.CurrentProfile,
                clip.name,
                clip.samples,
                clip.frequency,
                clip.length,
                rms,
                seamDelta,
                source.loop,
                source.playOnAwake,
                source.spatialBlend,
                failures.Count == 0 ? "none" : string.Join(" | ", failures)),
            new UTF8Encoding(false));
        ambience.ReleaseEditorAuditClip();
        if (failures.Count > 0)
            throw new InvalidOperationException(string.Join(" | ", failures));
        Debug.Log(
            "[GenesisAmbientAudit] " + mapName + " passed; report="
            + reportPath);
    }

    [MenuItem("Genesis/Repair Pyramid Decorative UVs")]
    public static void RepairPyramidDecorativeUvs()
    {
        const string scenePath = "Assets/PlayableMaps/Pyramid.unity";
        const string outputDirectory = "Assets/PlayableMaps/Generated/Pyramid";
        Directory.CreateDirectory(outputDirectory);
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var requiredNames = new HashSet<string>(
            new[] { "Line08", "Line09", "Line14", "Line15" },
            StringComparer.Ordinal);
        var filters = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<MeshFilter>(true))
            .Where(filter => requiredNames.Contains(filter.name))
            .ToArray();
        if (filters.Length != requiredNames.Count)
            throw new InvalidOperationException(
                "Pyramid decorative UV targets expected 4 but found " + filters.Length);

        foreach (var filter in filters)
        {
            var source = filter.sharedMesh;
            if (source == null)
                throw new InvalidOperationException(filter.name + " mesh is missing");
            var outputPath = outputDirectory + "/" + filter.name + "Uv.asset";
            var repaired = AssetDatabase.LoadAssetAtPath<Mesh>(outputPath);
            var sourceIsOutput = AssetDatabase.GetAssetPath(source) == outputPath;
            if (repaired == null)
            {
                repaired = UnityEngine.Object.Instantiate(source);
                repaired.name = filter.name + "Uv";
                AssetDatabase.CreateAsset(repaired, outputPath);
            }
            else if (!sourceIsOutput)
            {
                EditorUtility.CopySerialized(source, repaired);
                repaired.name = filter.name + "Uv";
            }
            repaired.uv = GeneratePlanarUv(repaired.vertices, repaired.bounds);
            EditorUtility.SetDirty(repaired);
            filter.sharedMesh = repaired;
            EditorUtility.SetDirty(filter);
            Debug.Log(
                "[GenesisMapRepair] Pyramid " + filter.name
                + " UV repaired using " + outputPath);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("Genesis/Diagnose Pyramid Navigation")]
    public static void DiagnosePyramidNavigation()
    {
        DiagnoseMapNavigation("Pyramid");
    }

    [MenuItem("Genesis/Diagnose New Construction Navigation")]
    public static void DiagnoseNewConstructionNavigation()
    {
        DiagnoseMapNavigation("NewConstructionSite");
    }

    [MenuItem("Genesis/Diagnose Ice Fire Navigation")]
    public static void DiagnoseIceFireNavigation()
    {
        DiagnoseMapNavigation("IceFireMaze");
        var scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.enabled
                    && renderer.gameObject.activeInHierarchy)
                .ToArray();
            if (renderers.Length == 0)
                continue;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1))
                bounds.Encapsulate(renderer.bounds);
            Debug.Log(string.Format(
                "[GenesisIceStructure] root={0}; renderers={1}; colliders={2}; "
                + "center={3}; size={4}",
                root.name,
                renderers.Length,
                root.GetComponentsInChildren<Collider>(true).Count(collider =>
                    collider.enabled && collider.gameObject.activeInHierarchy),
                bounds.center,
                bounds.size));
        }
    }

    private static void DiagnoseMapNavigation(string mapName)
    {
        var scene = EditorSceneManager.OpenScene(
            "Assets/PlayableMaps/" + mapName + ".unity", OpenSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        var player = scene.GetRootGameObjects()
            .First(root => root.name == "First Person Player");
        var physics = scene.GetPhysicsScene();
        var targets = EvenlySampleNavMeshVertices(
            NavMesh.CalculateTriangulation().vertices, 256);
        NavMeshHit startHit;
        if (!NavMesh.SamplePosition(
            player.transform.position, out startHit, 3f, NavMesh.AllAreas))
        {
            throw new InvalidOperationException(
                mapName + " spawn is not on its NavMesh");
        }
        var groups = new Dictionary<string, int>();
        var path = new NavMeshPath();
        var reachable = 0;
        foreach (var target in targets)
        {
            NavMeshHit targetHit;
            if (!NavMesh.SamplePosition(
                target + Vector3.up * 0.1f,
                out targetHit,
                0.75f,
                NavMesh.AllAreas))
            {
                continue;
            }
            var isReachable = NavMesh.CalculatePath(
                startHit.position,
                targetHit.position,
                NavMesh.AllAreas,
                path)
                && path.status == NavMeshPathStatus.PathComplete;
            if (isReachable)
                reachable += 1;
            RaycastHit hit;
            var support = physics.Raycast(
                targetHit.position + Vector3.up * 0.4f,
                Vector3.down,
                out hit,
                3f,
                ~0,
                QueryTriggerInteraction.Ignore)
                ? HierarchyPath(hit.collider.transform)
                : "<unsupported>";
            var heightBand = Mathf.FloorToInt(targetHit.position.y / 2f) * 2;
            var key = (isReachable ? "reachable" : "isolated")
                + "|y=" + heightBand + "|" + support;
            groups[key] = groups.TryGetValue(key, out var count)
                ? count + 1
                : 1;
        }
        foreach (var group in groups
                     .OrderByDescending(item => item.Value)
                     .Take(40))
        {
            Debug.Log(
                "[GenesisMapNav:" + mapName + "] "
                + group.Value + " " + group.Key);
        }
        Debug.Log(string.Format(
            "[GenesisMapNav:{0}] reachable={1}/{2} ({3:P1}); spawn={4}",
            mapName,
            reachable,
            targets.Length,
            targets.Length == 0 ? 0f : reachable / (float)targets.Length,
            startHit.position));
    }

    private static string HierarchyPath(Transform transform)
    {
        var names = new Stack<string>();
        for (var current = transform; current != null; current = current.parent)
            names.Push(current.name);
        return string.Join("/", names.ToArray());
    }

    private static Vector2[] GeneratePlanarUv(Vector3[] vertices, Bounds bounds)
    {
        var sizes = new[]
        {
            new { axis = 0, size = bounds.size.x },
            new { axis = 1, size = bounds.size.y },
            new { axis = 2, size = bounds.size.z },
        }.OrderByDescending(item => item.size).ToArray();
        var first = sizes[0].axis;
        var second = sizes[1].axis;
        var min = bounds.min;
        var size = bounds.size;
        var result = new Vector2[vertices.Length];
        for (var index = 0; index < vertices.Length; index += 1)
        {
            result[index] = new Vector2(
                Mathf.InverseLerp(Component(min, first), Component(min + size, first), Component(vertices[index], first)),
                Mathf.InverseLerp(Component(min, second), Component(min + size, second), Component(vertices[index], second)));
        }
        return result;
    }

    private static float Component(Vector3 value, int axis)
    {
        return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
    }

    private static void CaptureMapPreview(string mapName, string outputName)
    {
        var scenePath = "Assets/PlayableMaps/" + mapName + ".unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        var player = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == "First Person Player");
        var camera = player == null
            ? null
            : player.GetComponentInChildren<Camera>(true);
        if (camera == null)
            throw new InvalidOperationException(mapName + " preview camera is missing");

        var texture = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        var previousTarget = camera.targetTexture;
        var previousClearFlags = camera.clearFlags;
        var previousActive = RenderTexture.active;
        try
        {
            texture.Create();
            camera.targetTexture = texture;
            camera.aspect = 16f / 9f;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.Render();
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
            image.Apply(false, false);
            var recoveryDirectory = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../../recovery"));
            Directory.CreateDirectory(recoveryDirectory);
            var path = Path.Combine(
                recoveryDirectory, outputName);
            File.WriteAllBytes(path, image.EncodeToPNG());
            Debug.Log("[GenesisMapPreview] " + mapName + " wrote " + path);
        }
        finally
        {
            camera.targetTexture = previousTarget;
            camera.clearFlags = previousClearFlags;
            RenderTexture.active = previousActive;
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(image);
        }
    }

    [MenuItem("Genesis/Diagnose Promoted Map Visuals")]
    public static void DiagnosePromotedMapVisuals()
    {
        var output = new StringBuilder();
        foreach (var mapName in new[]
                 {
                     "SteelFactory", "IceFireMaze", "RadiationDistrict",
                 })
        {
            var scene = EditorSceneManager.OpenScene(
                "Assets/PlayableMaps/" + mapName + ".unity",
                OpenSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            var roots = scene.GetRootGameObjects();
            var player = roots.FirstOrDefault(root =>
                root.name == "First Person Player");
            var spawn = player == null ? Vector3.zero : player.transform.position;
            var camera = player == null
                ? null
                : player.GetComponentInChildren<Camera>(true);
            var renderers = roots
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => renderer.enabled
                    && renderer.gameObject.activeInHierarchy
                    && !IsGameplayRig(renderer.transform))
                .ToArray();

            output.AppendLine("## " + mapName)
                .AppendLine("spawn=" + spawn)
                .AppendLine("rotation="
                    + (player == null ? Quaternion.identity : player.transform.rotation))
                .AppendLine("cameraForward="
                    + (camera == null ? Vector3.forward : camera.transform.forward))
                .AppendLine("renderers=" + renderers.Length)
                .AppendLine("Nearby renderers:");
            foreach (var renderer in renderers
                         .OrderBy(renderer => Vector3.Distance(
                             renderer.bounds.ClosestPoint(spawn), spawn))
                         .Take(30))
            {
                var distance = Vector3.Distance(
                    renderer.bounds.ClosestPoint(spawn), spawn);
                output.Append("- ").Append(GetHierarchyPath(renderer.transform))
                    .Append(" distance=").Append(distance.ToString("F2"))
                    .Append(" center=").Append(renderer.bounds.center)
                    .Append(" size=").Append(renderer.bounds.size)
                    .Append(" materials=")
                    .AppendLine(string.Join(", ", renderer.sharedMaterials
                        .Select(DescribeMaterial)));
            }

            output.AppendLine("Material groups:");
            foreach (var group in renderers
                         .SelectMany(renderer => renderer.sharedMaterials
                             .Select(material => new { renderer, material }))
                         .GroupBy(item => DescribeMaterial(item.material))
                         .OrderByDescending(group => group.Count()))
            {
                output.Append("- count=").Append(group.Count())
                    .Append(" ").AppendLine(group.Key);
            }
            output.AppendLine();
        }

        var recoveryDirectory = Path.GetFullPath(
            Path.Combine(Application.dataPath, "../../recovery"));
        Directory.CreateDirectory(recoveryDirectory);
        var outputPath = Path.Combine(
            recoveryDirectory, "promoted-map-visual-diagnosis.txt");
        File.WriteAllText(outputPath, output.ToString(), new UTF8Encoding(false));
        Debug.Log("[GenesisMapVisualDiagnosis] report=" + outputPath);
    }

    [MenuItem("Genesis/Repair Promoted Map Visuals")]
    public static void RepairPromotedMapVisuals()
    {
        RepairSteelFactoryMaterials();
        RepairNewConstructionLighting();
        RepairBiochemicalTownAtmosphere();
        RepairClassicConstructionAtmosphere();
        RepairRadiationDistrictAtmosphere();
        RepairIceFireAtmosphere();
        RepairIceFireOcean();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void RepairNewConstructionLighting()
    {
        const string scenePath =
            "Assets/PlayableMaps/NewConstructionSite.unity";
        const string materialDirectory =
            "Assets/PlayableMaps/Generated/NewConstructionSite";
        Directory.CreateDirectory(materialDirectory);
        var skybox = GetOrCreateProceduralSkybox(
            materialDirectory + "/NewConstructionSkybox.mat",
            new Color(0.62f, 0.49f, 0.38f, 1f),
            new Color(0.2f, 0.17f, 0.16f, 1f),
            0.82f,
            0.9f);
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        var sunObject = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == "GenesisNewConstructionSun");
        if (sunObject == null)
            sunObject = new GameObject("GenesisNewConstructionSun");
        var sun = sunObject.GetComponent<Light>();
        if (sun == null)
            sun = sunObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.82f, 0.66f, 1f);
        sun.intensity = 0.75f;
        sun.shadows = LightShadows.Soft;
        sunObject.transform.rotation = Quaternion.Euler(38f, -28f, 0f);
        var ambienceObject = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == "GenesisConstructionAmbience");
        if (ambienceObject == null)
            ambienceObject = new GameObject("GenesisConstructionAmbience");
        var ambienceSource = ambienceObject.GetComponent<AudioSource>();
        if (ambienceSource == null)
            ambienceSource = ambienceObject.AddComponent<AudioSource>();
        if (ambienceObject.GetComponent<GenesisProceduralAmbient>() == null)
            ambienceObject.AddComponent<GenesisProceduralAmbient>();
        ambienceSource.playOnAwake = false;
        ambienceSource.loop = true;
        ambienceSource.spatialBlend = 0f;
        ambienceSource.volume = 0.11f;
        EditorUtility.SetDirty(ambienceSource);
        RenderSettings.sun = sun;
        RenderSettings.skybox = skybox;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.55f, 0.48f, 0.42f, 1f);
        RenderSettings.ambientEquatorColor = new Color(0.36f, 0.32f, 0.3f, 1f);
        RenderSettings.ambientGroundColor = new Color(0.18f, 0.16f, 0.14f, 1f);
        RenderSettings.ambientIntensity = 0.85f;
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(0.46f, 0.39f, 0.34f, 1f);
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 45f;
        RenderSettings.fogEndDistance = 140f;
        EditorUtility.SetDirty(sun);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(
            "[GenesisMapVisualRepair] NewConstructionSite sky and light repaired");
    }

    private static void RepairBiochemicalTownAtmosphere()
    {
        const string scenePath = "Assets/PlayableMaps/BiochemicalTown.unity";
        const string materialDirectory =
            "Assets/PlayableMaps/Generated/BiochemicalTown";
        Directory.CreateDirectory(materialDirectory);
        var skybox = GetOrCreateProceduralSkybox(
            materialDirectory + "/BiochemicalTownSkybox.mat",
            new Color(0.36f, 0.43f, 0.5f, 1f),
            new Color(0.16f, 0.13f, 0.1f, 1f),
            0.68f,
            0.78f);
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        var sun = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Light>(true))
            .FirstOrDefault(light => light.type == LightType.Directional);
        if (sun == null)
        {
            var sunObject = new GameObject("GenesisBiochemicalSun");
            sun = sunObject.AddComponent<Light>();
        }
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.79f, 0.59f, 1f);
        sun.intensity = 0.68f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(42f, 26f, 0f);
        var ambienceObject = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == "GenesisBiochemicalAmbience");
        if (ambienceObject == null)
            ambienceObject = new GameObject("GenesisBiochemicalAmbience");
        var source = ambienceObject.GetComponent<AudioSource>();
        if (source == null)
            source = ambienceObject.AddComponent<AudioSource>();
        var ambience = ambienceObject.GetComponent<GenesisProceduralAmbient>();
        if (ambience == null)
            ambience = ambienceObject.AddComponent<GenesisProceduralAmbient>();
        ambience.Configure(
            GenesisProceduralAmbient.AmbientProfile.TownNight, 0.09f);
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        RenderSettings.sun = sun;
        RenderSettings.skybox = skybox;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.38f, 0.42f, 0.46f, 1f);
        RenderSettings.ambientEquatorColor = new Color(0.31f, 0.27f, 0.23f, 1f);
        RenderSettings.ambientGroundColor = new Color(0.14f, 0.11f, 0.09f, 1f);
        RenderSettings.ambientIntensity = 0.82f;
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(0.36f, 0.34f, 0.31f, 1f);
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 70f;
        RenderSettings.fogEndDistance = 190f;
        EditorUtility.SetDirty(sun);
        EditorUtility.SetDirty(source);
        EditorUtility.SetDirty(ambience);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(
            "[GenesisMapVisualRepair] BiochemicalTown atmosphere repaired");
    }

    private static void RepairClassicConstructionAtmosphere()
    {
        const string scenePath =
            "Assets/PlayableMaps/ClassicConstructionSite.unity";
        const string materialDirectory =
            "Assets/PlayableMaps/Generated/ClassicConstructionSite";
        Directory.CreateDirectory(materialDirectory);
        var skybox = GetOrCreateProceduralSkybox(
            materialDirectory + "/ClassicConstructionSkybox.mat",
            new Color(0.42f, 0.52f, 0.61f, 1f),
            new Color(0.18f, 0.17f, 0.16f, 1f),
            0.76f,
            0.88f);
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        var sun = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Light>(true))
            .FirstOrDefault(light => light.type == LightType.Directional);
        if (sun == null)
        {
            var sunObject = new GameObject("GenesisClassicConstructionSun");
            sun = sunObject.AddComponent<Light>();
        }
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.88f, 0.72f, 1f);
        sun.intensity = 0.72f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(46f, -34f, 0f);
        var ambienceObject = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == "GenesisClassicAmbience");
        if (ambienceObject == null)
            ambienceObject = new GameObject("GenesisClassicAmbience");
        var source = ambienceObject.GetComponent<AudioSource>();
        if (source == null)
            source = ambienceObject.AddComponent<AudioSource>();
        var ambience = ambienceObject.GetComponent<GenesisProceduralAmbient>();
        if (ambience == null)
            ambience = ambienceObject.AddComponent<GenesisProceduralAmbient>();
        ambience.Configure(
            GenesisProceduralAmbient.AmbientProfile.ConstructionWind, 0.1f);
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        RenderSettings.sun = sun;
        RenderSettings.skybox = skybox;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.45f, 0.5f, 0.54f, 1f);
        RenderSettings.ambientEquatorColor = new Color(0.34f, 0.32f, 0.29f, 1f);
        RenderSettings.ambientGroundColor = new Color(0.16f, 0.14f, 0.12f, 1f);
        RenderSettings.ambientIntensity = 0.88f;
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(0.42f, 0.42f, 0.4f, 1f);
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 95f;
        RenderSettings.fogEndDistance = 260f;
        EditorUtility.SetDirty(sun);
        EditorUtility.SetDirty(source);
        EditorUtility.SetDirty(ambience);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(
            "[GenesisMapVisualRepair] ClassicConstructionSite atmosphere repaired");
    }

    private static void RepairRadiationDistrictAtmosphere()
    {
        const string scenePath = "Assets/PlayableMaps/RadiationDistrict.unity";
        const string materialDirectory =
            "Assets/PlayableMaps/Generated/RadiationDistrict";
        Directory.CreateDirectory(materialDirectory);
        var skybox = GetOrCreateProceduralSkybox(
            materialDirectory + "/RadiationDistrictSkybox.mat",
            new Color(0.27f, 0.34f, 0.36f, 1f),
            new Color(0.1f, 0.12f, 0.11f, 1f),
            0.92f,
            0.72f);
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        var sun = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Light>(true))
            .FirstOrDefault(light => light.type == LightType.Directional);
        if (sun == null)
        {
            var sunObject = new GameObject("GenesisRadiationSun");
            sun = sunObject.AddComponent<Light>();
        }
        sun.type = LightType.Directional;
        sun.color = new Color(0.72f, 0.82f, 0.78f, 1f);
        sun.intensity = 0.78f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(54f, 18f, 0f);
        var ambienceObject = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == "GenesisRadiationAmbience");
        if (ambienceObject == null)
            ambienceObject = new GameObject("GenesisRadiationAmbience");
        var source = ambienceObject.GetComponent<AudioSource>();
        if (source == null)
            source = ambienceObject.AddComponent<AudioSource>();
        var ambience = ambienceObject.GetComponent<GenesisProceduralAmbient>();
        if (ambience == null)
            ambience = ambienceObject.AddComponent<GenesisProceduralAmbient>();
        ambience.Configure(
            GenesisProceduralAmbient.AmbientProfile.IndustrialHum, 0.085f);
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        RenderSettings.sun = sun;
        RenderSettings.skybox = skybox;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.34f, 0.4f, 0.4f, 1f);
        RenderSettings.ambientEquatorColor = new Color(0.24f, 0.27f, 0.25f, 1f);
        RenderSettings.ambientGroundColor = new Color(0.1f, 0.11f, 0.1f, 1f);
        RenderSettings.ambientIntensity = 0.92f;
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(0.27f, 0.31f, 0.29f, 1f);
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 110f;
        RenderSettings.fogEndDistance = 320f;
        EditorUtility.SetDirty(sun);
        EditorUtility.SetDirty(source);
        EditorUtility.SetDirty(ambience);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(
            "[GenesisMapVisualRepair] RadiationDistrict atmosphere repaired");
    }

    private static void RepairIceFireAtmosphere()
    {
        const string scenePath = "Assets/PlayableMaps/IceFireMaze.unity";
        const string materialDirectory =
            "Assets/PlayableMaps/Generated/IceFireMaze";
        Directory.CreateDirectory(materialDirectory);
        var skybox = GetOrCreateProceduralSkybox(
            materialDirectory + "/IceFireMazeSkybox.mat",
            new Color(0.38f, 0.53f, 0.67f, 1f),
            new Color(0.13f, 0.16f, 0.18f, 1f),
            0.58f,
            0.9f);
        var iceWall = GetOrCreateMaterial(
            materialDirectory + "/IceMazeWall.mat",
            new Color(0.28f, 0.55f, 0.7f, 1f), 0.08f, 0.32f);
        var fireWall = GetOrCreateMaterial(
            materialDirectory + "/FireMazeWall.mat",
            new Color(0.58f, 0.2f, 0.08f, 1f), 0.05f, 0.2f);
        var ground = GetOrCreateMaterial(
            materialDirectory + "/IceFireGround.mat",
            new Color(0.16f, 0.2f, 0.22f, 1f), 0.02f, 0.18f);
        ConfigurePatternMaterial(
            iceWall,
            GetOrCreatePatternTexture(
                materialDirectory + "/IceMazeWallPattern.asset",
                new Color32(48, 102, 132, 255),
                new Color32(112, 181, 211, 255),
                2),
            new Vector2(2.5f, 2.5f), 0.06f, 0.32f);
        ConfigurePatternMaterial(
            fireWall,
            GetOrCreatePatternTexture(
                materialDirectory + "/FireMazeWallPattern.asset",
                new Color32(112, 43, 22, 255),
                new Color32(210, 93, 28, 255),
                3),
            new Vector2(2.5f, 2.5f), 0.04f, 0.2f);
        ConfigurePatternMaterial(
            ground,
            GetOrCreatePatternTexture(
                materialDirectory + "/IceFireGroundPattern.asset",
                new Color32(39, 48, 52, 255),
                new Color32(66, 77, 82, 255),
                0),
            new Vector2(12f, 12f), 0.02f, 0.16f);
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        var themedWalls = 0;
        foreach (var renderer in scene.GetRootGameObjects()
                     .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                     .Where(renderer => renderer.enabled
                         && renderer.gameObject.activeInHierarchy
                         && renderer.GetComponent<Collider>() != null
                         && renderer.bounds.size.y >= 2f
                         && renderer.sharedMaterials.Any(material =>
                             material != null
                             && (material.name.StartsWith("Archmodels_115_038")
                                 || material.name == "qiang4"))))
        {
            var zone = Mathf.FloorToInt((renderer.bounds.center.x + 50f) / 100f);
            var selected = (zone & 1) == 0 ? iceWall : fireWall;
            renderer.sharedMaterials = Enumerable.Repeat(
                selected, Mathf.Max(1, renderer.sharedMaterials.Length)).ToArray();
            EditorUtility.SetDirty(renderer);
            themedWalls += 1;
        }
        var groundRenderer = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
            .FirstOrDefault(renderer => renderer.name == "Plane2");
        if (groundRenderer != null)
        {
            groundRenderer.sharedMaterial = ground;
            EditorUtility.SetDirty(groundRenderer);
        }
        var sun = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Light>(true))
            .FirstOrDefault(light => light.type == LightType.Directional);
        if (sun == null)
        {
            var sunObject = new GameObject("GenesisIceFireSun");
            sun = sunObject.AddComponent<Light>();
        }
        sun.type = LightType.Directional;
        sun.color = new Color(0.76f, 0.86f, 1f, 1f);
        sun.intensity = 0.82f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(48f, -22f, 0f);
        var ambienceObject = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == "GenesisIceFireAmbience");
        if (ambienceObject == null)
            ambienceObject = new GameObject("GenesisIceFireAmbience");
        var source = ambienceObject.GetComponent<AudioSource>();
        if (source == null)
            source = ambienceObject.AddComponent<AudioSource>();
        var ambience = ambienceObject.GetComponent<GenesisProceduralAmbient>();
        if (ambience == null)
            ambience = ambienceObject.AddComponent<GenesisProceduralAmbient>();
        ambience.Configure(
            GenesisProceduralAmbient.AmbientProfile.ColdWind, 0.1f);
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        RenderSettings.sun = sun;
        RenderSettings.skybox = skybox;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.46f, 0.57f, 0.68f, 1f);
        RenderSettings.ambientEquatorColor = new Color(0.32f, 0.37f, 0.4f, 1f);
        RenderSettings.ambientGroundColor = new Color(0.13f, 0.15f, 0.17f, 1f);
        RenderSettings.ambientIntensity = 0.9f;
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(0.42f, 0.5f, 0.56f, 1f);
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 260f;
        RenderSettings.fogEndDistance = 780f;
        EditorUtility.SetDirty(sun);
        EditorUtility.SetDirty(source);
        EditorUtility.SetDirty(ambience);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(
            "[GenesisMapVisualRepair] IceFireMaze atmosphere repaired; themedWalls="
            + themedWalls);
    }

    [MenuItem("Genesis/Diagnose Radiation Spawn Candidates")]
    public static void DiagnoseRadiationSpawnCandidates()
    {
        const string scenePath = "Assets/PlayableMaps/RadiationDistrict.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        var physics = scene.GetPhysicsScene();
        var player = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == "First Person Player");
        if (player == null)
            throw new InvalidOperationException("RadiationDistrict player is missing");
        var origin = player.transform.position;
        var directions = new[]
        {
            Vector3.forward,
            Vector3.back,
            Vector3.left,
            Vector3.right,
            new Vector3(1f, 0f, 1f).normalized,
            new Vector3(-1f, 0f, 1f).normalized,
            new Vector3(1f, 0f, -1f).normalized,
            new Vector3(-1f, 0f, -1f).normalized,
        };
        var candidates = NavMesh.CalculateTriangulation().vertices
            .Distinct()
            .Where(vertex => Vector2.Distance(
                new Vector2(vertex.x, vertex.z),
                new Vector2(origin.x, origin.z)) < 90f)
            .Select(vertex =>
            {
                var eye = vertex + Vector3.up * 1.6f;
                var clearances = directions.Select(direction =>
                {
                    RaycastHit hit;
                    return physics.Raycast(
                        eye,
                        direction,
                        out hit,
                        30f,
                        ~0,
                        QueryTriggerInteraction.Ignore)
                        ? hit.distance
                        : 30f;
                }).ToArray();
                RaycastHit overhead;
                var overheadDistance = physics.Raycast(
                    eye,
                    Vector3.up,
                    out overhead,
                    12f,
                    ~0,
                    QueryTriggerInteraction.Ignore)
                    ? overhead.distance
                    : 12f;
                return new
                {
                    position = vertex,
                    minimum = clearances.Min(),
                    average = clearances.Average(),
                    forward = clearances[0],
                    overhead = overheadDistance,
                    originDistance = Vector2.Distance(
                        new Vector2(vertex.x, vertex.z),
                        new Vector2(origin.x, origin.z)),
                };
            })
            .Where(item => item.minimum > 2.5f && item.overhead > 3f)
            .OrderByDescending(item => item.minimum)
            .ThenByDescending(item => item.average)
            .ThenBy(item => item.originDistance)
            .Take(30)
            .ToArray();
        Debug.Log(
            "[GenesisRadiationSpawn] origin=" + origin + "\n"
            + string.Join("\n", candidates.Select(item => string.Format(
                "{0}; min={1:F2}; avg={2:F2}; forward={3:F2}; overhead={4:F2}; originDistance={5:F2}",
                item.position,
                item.minimum,
                item.average,
                item.forward,
                item.overhead,
                item.originDistance))));
    }

    private static void RepairSteelFactoryMaterials()
    {
        const string scenePath = "Assets/PlayableMaps/SteelFactory.unity";
        const string materialDirectory =
            "Assets/RecoveredMaps/SteelFactory/Material/Recovery";
        Directory.CreateDirectory(materialDirectory);
        var floor = GetOrCreateMaterial(
            materialDirectory + "/SteelFloor.mat",
            new Color(0.18f, 0.22f, 0.24f, 1f),
            0.15f,
            0.18f);
        var deck = GetOrCreateMaterial(
            materialDirectory + "/SteelDeck.mat",
            new Color(0.28f, 0.32f, 0.33f, 1f),
            0.28f,
            0.22f);
        var wall = GetOrCreateMaterial(
            materialDirectory + "/SteelWall.mat",
            new Color(0.22f, 0.28f, 0.31f, 1f),
            0.32f,
            0.26f);
        var structure = GetOrCreateMaterial(
            materialDirectory + "/SteelStructure.mat",
            new Color(0.12f, 0.16f, 0.18f, 1f),
            0.52f,
            0.32f);
        var accent = GetOrCreateMaterial(
            materialDirectory + "/SteelRustAccent.mat",
            new Color(0.42f, 0.19f, 0.08f, 1f),
            0.12f,
            0.16f);
        var skybox = GetOrCreateSteelFactorySkybox(
            materialDirectory + "/SteelFactorySkybox.mat");
        ConfigurePatternMaterial(
            floor,
            GetOrCreatePatternTexture(
                materialDirectory + "/SteelFloorPattern.asset",
                new Color32(45, 54, 58, 255),
                new Color32(68, 78, 82, 255),
                0),
            new Vector2(3f, 3f), 0.12f, 0.24f);
        ConfigurePatternMaterial(
            deck,
            GetOrCreatePatternTexture(
                materialDirectory + "/SteelDeckPattern.asset",
                new Color32(61, 72, 76, 255),
                new Color32(91, 102, 105, 255),
                1),
            new Vector2(4f, 4f), 0.22f, 0.3f);
        ConfigurePatternMaterial(
            wall,
            GetOrCreatePatternTexture(
                materialDirectory + "/SteelWallPattern.asset",
                new Color32(49, 65, 72, 255),
                new Color32(78, 94, 101, 255),
                1),
            new Vector2(3f, 3f), 0.28f, 0.26f);
        ConfigurePatternMaterial(
            structure,
            GetOrCreatePatternTexture(
                materialDirectory + "/SteelStructurePattern.asset",
                new Color32(28, 35, 38, 255),
                new Color32(55, 64, 67, 255),
                2),
            new Vector2(4f, 4f), 0.48f, 0.34f);
        ConfigurePatternMaterial(
            accent,
            GetOrCreatePatternTexture(
                materialDirectory + "/SteelRustPattern.asset",
                new Color32(103, 48, 23, 255),
                new Color32(165, 83, 31, 255),
                3),
            new Vector2(3f, 3f), 0.08f, 0.18f);

        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        var assigned = 0;
        foreach (var renderer in scene.GetRootGameObjects()
                     .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                     .Where(renderer => renderer.enabled
                         && renderer.gameObject.activeInHierarchy
                         && !IsGameplayRig(renderer.transform)))
        {
            var size = renderer.bounds.size;
            var largest = Mathf.Max(size.x, size.y, size.z);
            Material selected;
            if (size.y < 1.5f)
                selected = renderer.bounds.center.y < -120f ? floor : deck;
            else if (largest < 80f)
                selected = accent;
            else if (size.x < 1.5f || size.z < 1.5f)
                selected = wall;
            else
                selected = structure;
            renderer.sharedMaterials = Enumerable.Repeat(
                selected, Mathf.Max(1, renderer.sharedMaterials.Length)).ToArray();
            EditorUtility.SetDirty(renderer);
            assigned += 1;
        }
        RenderSettings.skybox = skybox;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.48f, 0.56f, 0.62f, 1f);
        RenderSettings.ambientEquatorColor = new Color(0.3f, 0.34f, 0.35f, 1f);
        RenderSettings.ambientGroundColor = new Color(0.14f, 0.13f, 0.12f, 1f);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(0.32f, 0.39f, 0.43f, 1f);
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 180f;
        RenderSettings.fogEndDistance = 750f;
        var ambienceObject = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == "GenesisSteelFactoryAmbience");
        if (ambienceObject == null)
            ambienceObject = new GameObject("GenesisSteelFactoryAmbience");
        var source = ambienceObject.GetComponent<AudioSource>();
        if (source == null)
            source = ambienceObject.AddComponent<AudioSource>();
        var ambience = ambienceObject.GetComponent<GenesisProceduralAmbient>();
        if (ambience == null)
            ambience = ambienceObject.AddComponent<GenesisProceduralAmbient>();
        ambience.Configure(
            GenesisProceduralAmbient.AmbientProfile.IndustrialHum, 0.085f);
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        EditorUtility.SetDirty(source);
        EditorUtility.SetDirty(ambience);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[GenesisMapVisualRepair] SteelFactory materials=" + assigned);
    }

    private static void RepairIceFireOcean()
    {
        const string scenePath = "Assets/PlayableMaps/IceFireMaze.unity";
        const string materialPath =
            "Assets/RecoveredMaps/IceFireMaze/Material/RecoveredOcean.mat";
        var ocean = GetOrCreateMaterial(
            materialPath,
            new Color(0.16f, 0.34f, 0.43f, 1f),
            0.05f,
            0.55f);
        var waterTexture = AssetDatabase.LoadAssetAtPath<Texture>(
            "Assets/RecoveredMaps/IceFireMaze/Texture2D/water_tex.png");
        ocean.mainTexture = waterTexture;
        EditorUtility.SetDirty(ocean);

        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        var oceanRenderer = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
            .FirstOrDefault(renderer => renderer.name == "OceanPlane");
        if (oceanRenderer == null)
            throw new InvalidOperationException("IceFireMaze OceanPlane is missing");
        oceanRenderer.sharedMaterial = ocean;
        EditorUtility.SetDirty(oceanRenderer);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[GenesisMapVisualRepair] IceFireMaze ocean repaired");
    }

    private static Texture2D GetOrCreatePatternTexture(
        string path,
        Color32 baseColor,
        Color32 lineColor,
        int pattern)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (texture == null)
        {
            texture = new Texture2D(128, 128, TextureFormat.RGBA32, true);
            texture.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(texture, path);
        }
        if (texture.width != 128 || texture.height != 128)
            texture.Reinitialize(128, 128, TextureFormat.RGBA32, true);
        for (var y = 0; y < 128; y += 1)
        {
            for (var x = 0; x < 128; x += 1)
            {
                bool line;
                switch (pattern)
                {
                    case 0:
                        line = x % 32 < 2 || y % 32 < 2;
                        break;
                    case 1:
                        line = x % 32 < 2 || y % 64 < 2;
                        break;
                    case 2:
                        line = (x + y) % 48 < 2 || (x - y + 128) % 48 < 2;
                        break;
                    default:
                        line = (x * 13 + y * 7 + x * y) % 47 < 5;
                        break;
                }
                var noise = ((x * 73 + y * 151 + x * y * 3) & 31) / 255f;
                var color = line ? lineColor : baseColor;
                color.r = (byte)Mathf.Clamp(color.r + noise * 255f, 0f, 255f);
                color.g = (byte)Mathf.Clamp(color.g + noise * 210f, 0f, 255f);
                color.b = (byte)Mathf.Clamp(color.b + noise * 170f, 0f, 255f);
                texture.SetPixel(x, y, color);
            }
        }
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Bilinear;
        texture.Apply(true, false);
        EditorUtility.SetDirty(texture);
        return texture;
    }

    private static void ConfigurePatternMaterial(
        Material material,
        Texture texture,
        Vector2 scale,
        float metallic,
        float smoothness)
    {
        var shader = Shader.Find("Standard");
        if (shader == null)
            throw new InvalidOperationException("Standard shader is missing");
        material.shader = shader;
        material.mainTexture = texture;
        material.mainTextureScale = scale;
        material.color = Color.white;
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Glossiness", smoothness);
        material.renderQueue = -1;
        EditorUtility.SetDirty(material);
    }

    private static void NormalizeSteelFactoryScale(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        var environment = roots.FirstOrDefault(root => root.name == "1");
        var player = roots.FirstOrDefault(root => root.name == "First Person Player");
        if (environment == null || player == null)
            throw new InvalidOperationException("SteelFactory scale roots are incomplete");
        if (environment.transform.localScale.x < 0.2f)
            return;

        var renderers = environment.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy)
            .ToArray();
        if (renderers.Length == 0)
            throw new InvalidOperationException("SteelFactory environment is empty");
        var oldBounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1))
            oldBounds.Encapsulate(renderer.bounds);
        var targetSpawn = oldBounds.center
            + (player.transform.position - oldBounds.center) * 0.125f;

        environment.transform.localScale *= 0.125f;
        Physics.SyncTransforms();
        var newBounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1))
            newBounds.Encapsulate(renderer.bounds);
        environment.transform.position += oldBounds.center - newBounds.center;
        player.transform.position = targetSpawn;
        var towardCenter = oldBounds.center - targetSpawn;
        towardCenter.y = 0f;
        if (towardCenter.sqrMagnitude > 0.01f)
            player.transform.rotation = Quaternion.LookRotation(towardCenter.normalized);
        EditorUtility.SetDirty(environment.transform);
        EditorUtility.SetDirty(player.transform);
        Debug.Log(
            "[GenesisMapRepair] SteelFactory normalized to 1/8 scale; targetSpawn="
            + targetSpawn);
    }

    private static void PlaceSteelFactorySafeSpawn(Scene scene, Transform player)
    {
        var physics = scene.GetPhysicsScene();
        var renderers = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
            .Where(renderer => renderer.enabled
                && renderer.gameObject.activeInHierarchy
                && !IsGameplayRig(renderer.transform))
            .ToArray();
        var directions = HorizontalDirections();
        var candidates = NavMesh.CalculateTriangulation().vertices
            .Distinct()
            .Select(vertex =>
            {
                var eye = vertex + Vector3.up * 1.6f;
                var distances = directions
                    .Select(direction => HorizontalClearance(
                        physics, eye, direction, 18f))
                    .ToArray();
                var nearby = renderers.Count(renderer =>
                    Vector3.Distance(renderer.bounds.ClosestPoint(eye), eye) < 32f);
                return new
                {
                    vertex,
                    distances,
                    minimum = distances.Min(),
                    average = distances.Average(),
                    nearby,
                    originalDistance = Vector3.Distance(vertex, player.position),
                };
            })
            .Where(item => item.minimum >= 4f && item.nearby >= 8)
            .OrderByDescending(item => item.minimum * 2f
                + item.average + Mathf.Min(item.nearby, 40) * 0.15f
                - item.originalDistance * 0.015f)
            .ToArray();
        if (candidates.Length == 0)
            throw new InvalidOperationException(
                "SteelFactory has no horizontally safe NavMesh spawn candidate");
        var selected = candidates[0];
        var eyePosition = selected.vertex + Vector3.up * 1.6f;
        var feature = renderers
            .Select(renderer => new
            {
                renderer,
                distance = Vector3.Distance(
                    renderer.bounds.ClosestPoint(eyePosition), eyePosition),
            })
            .Where(item => item.distance >= 8f
                && item.distance <= 45f
                && item.renderer.bounds.size.y >= 2f
                && Mathf.Max(
                    item.renderer.bounds.size.x,
                    item.renderer.bounds.size.z) <= 60f)
            .OrderBy(item => item.distance)
            .FirstOrDefault();
        var forward = feature == null
            ? Vector3.forward
            : feature.renderer.bounds.center - eyePosition;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f
            || HorizontalClearance(
                physics,
                selected.vertex + Vector3.up * 1.6f,
                forward.normalized,
                4f) < 4f)
        {
            var forwardIndex = Array.IndexOf(
                selected.distances, selected.distances.Max());
            forward = directions[forwardIndex];
        }
        player.position = selected.vertex + Vector3.up * 1.2f;
        player.rotation = Quaternion.LookRotation(forward.normalized);
        EditorUtility.SetDirty(player);
        Debug.Log(string.Format(
            "[GenesisMapRepair] SteelFactory safe spawn min={0:F2}; avg={1:F2}; nearby={2}; forward={3}",
            selected.minimum,
            selected.average,
            selected.nearby,
            forward.normalized));
    }

    private static void PlaceNearbySafeSpawn(
        Scene scene, Transform player, float searchRadius, string mapName)
    {
        var physics = scene.GetPhysicsScene();
        var origin = player.position;
        var directions = HorizontalDirections();
        var triangulation = NavMesh.CalculateTriangulation();
        var environmentRenderers = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
            .Where(renderer => renderer.enabled
                && renderer.gameObject.activeInHierarchy
                && !IsGameplayRig(renderer.transform))
            .ToArray();
        var structuralRenderers = environmentRenderers
            .Where(renderer => renderer.GetComponent<Collider>() != null
                && renderer.bounds.size.y >= 1f
                && Mathf.Max(
                    renderer.bounds.size.x,
                    renderer.bounds.size.z) >= 2f)
            .ToArray();
        var reachabilityTargets = EvenlySampleNavMeshVertices(
            triangulation.vertices, 128);
        var geometricCandidates = NavMeshCandidatePoints(triangulation)
            .Distinct()
            .Where(vertex => Vector2.Distance(
                new Vector2(vertex.x, vertex.z),
                new Vector2(origin.x, origin.z)) <= searchRadius)
            .Select(vertex =>
            {
                var eye = vertex + Vector3.up * 1.6f;
                var distances = directions.Select(direction =>
                    HorizontalClearance(physics, eye, direction, 12f)).ToArray();
                return new
                {
                    vertex,
                    distances,
                    minimum = distances.Min(),
                    average = distances.Average(),
                    originDistance = Vector2.Distance(
                        new Vector2(vertex.x, vertex.z),
                        new Vector2(origin.x, origin.z)),
                    nearby = structuralRenderers.Count(renderer =>
                        Vector3.Distance(
                            renderer.bounds.ClosestPoint(vertex), vertex)
                        <= (mapName == "IceFireMaze" ? 65f : 35f)),
                };
            })
            .Where(item => item.minimum >= 2f)
            .OrderByDescending(item => item.minimum * 2f
                + item.average
                - (mapName == "IceFireMaze"
                    ? 0f
                    : item.originDistance * 0.12f))
            .Take(mapName == "IceFireMaze" ? 2048 : 512)
            .ToArray();
        if (geometricCandidates.Length == 0)
            throw new InvalidOperationException(
                mapName + " has no nearby safe NavMesh spawn candidate");
        var candidates = geometricCandidates
            .Select(item => new
            {
                item.vertex,
                item.distances,
                item.minimum,
                item.average,
                item.originDistance,
                item.nearby,
                reachableRatio = NavMeshReachabilityRatio(
                    item.vertex, reachabilityTargets),
            })
            .OrderByDescending(item => item.reachableRatio)
            .ThenByDescending(item => mapName == "IceFireMaze"
                ? item.nearby
                : 0)
            .ThenByDescending(item => item.minimum * 2f
                + item.average
                - (mapName == "IceFireMaze"
                    ? 0f
                    : item.originDistance * 0.12f))
            .ToArray();
        var selected = candidates[0];
        var feature = structuralRenderers
            .Select(renderer => new
            {
                renderer,
                distance = Vector3.Distance(
                    renderer.bounds.ClosestPoint(selected.vertex),
                    selected.vertex),
            })
            .Where(item => item.distance >= 5f
                && item.distance <= 60f
                && item.renderer.bounds.size.y >= 2f
                && Mathf.Max(
                    item.renderer.bounds.size.x,
                    item.renderer.bounds.size.z) <= 150f)
            .OrderBy(item => item.distance)
            .FirstOrDefault();
        var forward = feature == null
            ? Vector3.zero
            : feature.renderer.bounds.center - selected.vertex;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f
            || HorizontalClearance(
                physics,
                selected.vertex + Vector3.up * 1.6f,
                forward.normalized,
                4f) < 4f)
        {
            var forwardIndex = Array.IndexOf(
                selected.distances, selected.distances.Max());
            forward = directions[forwardIndex];
        }
        player.position = selected.vertex + Vector3.up * 1.2f;
        player.rotation = Quaternion.LookRotation(forward.normalized);
        EditorUtility.SetDirty(player);
        Debug.Log(string.Format(
            "[GenesisMapRepair] {0} safe spawn min={1:F2}; avg={2:F2}; "
            + "moved={3:F2}m; reachable={4:P1}; nearby={5}",
            mapName,
            selected.minimum,
            selected.average,
            selected.originDistance,
            selected.reachableRatio,
            selected.nearby));
    }

    private static IEnumerable<Vector3> NavMeshCandidatePoints(
        NavMeshTriangulation triangulation)
    {
        foreach (var vertex in triangulation.vertices)
            yield return vertex;
        for (var index = 0;
             index + 2 < triangulation.indices.Length;
             index += 3)
        {
            yield return (
                triangulation.vertices[triangulation.indices[index]]
                + triangulation.vertices[triangulation.indices[index + 1]]
                + triangulation.vertices[triangulation.indices[index + 2]]) / 3f;
        }
    }

    private static Vector3[] EvenlySampleNavMeshVertices(
        Vector3[] vertices, int maximumSamples)
    {
        if (vertices == null || vertices.Length == 0)
            return Array.Empty<Vector3>();
        var stride = Mathf.Max(1, vertices.Length / maximumSamples);
        return vertices
            .Where((vertex, index) => index % stride == 0)
            .Take(maximumSamples)
            .ToArray();
    }

    private static float NavMeshReachabilityRatio(
        Vector3 start, IReadOnlyList<Vector3> targets)
    {
        if (targets == null || targets.Count == 0)
            return 0f;
        NavMeshHit startHit;
        if (!NavMesh.SamplePosition(start, out startHit, 3f, NavMesh.AllAreas))
            return 0f;
        var reachable = 0;
        var sampled = 0;
        var path = new NavMeshPath();
        foreach (var target in targets)
        {
            sampled += 1;
            NavMeshHit targetHit;
            if (!NavMesh.SamplePosition(
                target + Vector3.up * 0.1f,
                out targetHit,
                0.75f,
                NavMesh.AllAreas))
            {
                continue;
            }
            if (NavMesh.CalculatePath(
                startHit.position,
                targetHit.position,
                NavMesh.AllAreas,
                path)
                && path.status == NavMeshPathStatus.PathComplete)
            {
                reachable += 1;
            }
        }
        return sampled == 0 ? 0f : reachable / (float)sampled;
    }

    private static Vector3[] HorizontalDirections()
    {
        return new[]
        {
            Vector3.forward,
            Vector3.back,
            Vector3.left,
            Vector3.right,
            new Vector3(1f, 0f, 1f).normalized,
            new Vector3(-1f, 0f, 1f).normalized,
            new Vector3(1f, 0f, -1f).normalized,
            new Vector3(-1f, 0f, -1f).normalized,
        };
    }

    private static float HorizontalClearance(
        PhysicsScene physics, Vector3 origin, Vector3 direction, float range)
    {
        RaycastHit hit;
        return physics.Raycast(
            origin,
            direction,
            out hit,
            range,
            ~0,
            QueryTriggerInteraction.Ignore)
            ? hit.distance
            : range;
    }

    private static Material GetOrCreateMaterial(
        string path,
        Color color,
        float metallic,
        float smoothness)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = Shader.Find("Standard");
        material.color = color;
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Glossiness", smoothness);
        material.SetFloat("_Mode", 0f);
        material.renderQueue = -1;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material GetOrCreateSteelFactorySkybox(string path)
    {
        var shader = Shader.Find("Skybox/Procedural");
        if (shader == null)
            throw new InvalidOperationException("Procedural skybox shader is missing");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetColor("_SkyTint", new Color(0.34f, 0.43f, 0.5f, 1f));
        material.SetColor("_GroundColor", new Color(0.12f, 0.14f, 0.15f, 1f));
        material.SetFloat("_AtmosphereThickness", 0.72f);
        material.SetFloat("_Exposure", 0.78f);
        material.SetFloat("_SunSize", 0.025f);
        material.SetFloat("_SunSizeConvergence", 4f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material GetOrCreateProceduralSkybox(
        string path,
        Color skyTint,
        Color groundColor,
        float atmosphereThickness,
        float exposure)
    {
        var shader = Shader.Find("Skybox/Procedural");
        if (shader == null)
            throw new InvalidOperationException("Procedural skybox shader is missing");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetColor("_SkyTint", skyTint);
        material.SetColor("_GroundColor", groundColor);
        material.SetFloat("_AtmosphereThickness", atmosphereThickness);
        material.SetFloat("_Exposure", exposure);
        material.SetFloat("_SunSize", 0.035f);
        material.SetFloat("_SunSizeConvergence", 4f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static string DescribeMaterial(Material material)
    {
        if (material == null)
            return "<missing>";
        var texture = material.mainTexture;
        var color = material.HasProperty("_Color")
            ? material.color.ToString()
            : "n/a";
        return string.Format(
            "{0} [asset={1}; shader={2}; texture={3}; color={4}]",
            material.name,
            AssetDatabase.GetAssetPath(material),
            material.shader == null ? "<missing>" : material.shader.name,
            texture == null
                ? "<none>"
                : texture.name + "@" + AssetDatabase.GetAssetPath(texture),
            color);
    }

    private static string GetHierarchyPath(Transform transform)
    {
        var path = transform.name;
        for (var current = transform.parent; current != null; current = current.parent)
            path = current.name + "/" + path;
        return path;
    }

    private static MapReport AuditMap(string mapName)
    {
        var scenePath = "Assets/PlayableMaps/" + mapName + ".unity";
        var scene = EditorSceneManager.OpenScene(
            scenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        var roots = scene.GetRootGameObjects();
        var renderers = roots
            .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
            .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy)
            .ToArray();
        var colliders = roots
            .SelectMany(root => root.GetComponentsInChildren<Collider>(true))
            .Where(collider => collider.enabled && collider.gameObject.activeInHierarchy)
            .ToArray();
        var lights = roots
            .SelectMany(root => root.GetComponentsInChildren<Light>(true))
            .Where(light => light.enabled && light.gameObject.activeInHierarchy)
            .ToArray();
        var player = roots.FirstOrDefault(root => root.name == "First Person Player");
        var spawn = player == null ? Vector3.zero : player.transform.position;

        var environmentMeshFilters = roots
            .SelectMany(root => root.GetComponentsInChildren<MeshFilter>(true))
            .Where(filter => filter.gameObject.activeInHierarchy
                && !IsGameplayRig(filter.transform)
                && filter.sharedMesh != null)
            .ToArray();
        var missingRequiredNormals = 0;
        var missingRequiredUv = 0;
        long triangleCount = 0;
        foreach (var filter in environmentMeshFilters)
        {
            var mesh = filter.sharedMesh;
            var renderer = filter.GetComponent<Renderer>();
            var materials = renderer == null
                ? new Material[0]
                : renderer.sharedMaterials;
            var requiresNormals = materials.Any(material => material != null
                && material.shader != null
                && !material.shader.name.Contains("Unlit"));
            var requiresUv = materials.Any(material => material != null
                && material.mainTexture != null);
            if (requiresNormals && mesh.normals.Length != mesh.vertexCount)
                missingRequiredNormals += 1;
            if (requiresUv && mesh.uv.Length != mesh.vertexCount)
            {
                missingRequiredUv += 1;
                Debug.LogWarning(
                    "[GenesisMapRecoveryAudit] " + mapName
                    + " textured mesh missing UV: "
                    + GetHierarchyPath(filter.transform)
                    + "; mesh=" + mesh.name
                    + "; bounds="
                    + (renderer == null ? "<none>" : renderer.bounds.ToString())
                    + "; materials="
                    + string.Join(",", materials.Select(material =>
                        material == null ? "<missing>" : material.name)));
            }
            for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh += 1)
                triangleCount += (long)mesh.GetIndexCount(subMesh) / 3L;
        }
        var environmentMaterials = renderers
            .Where(renderer => !IsGameplayRig(renderer.transform))
            .SelectMany(renderer => renderer.sharedMaterials)
            .Where(material => material != null)
            .Distinct()
            .ToArray();
        var maximumTextureDimension = environmentMaterials
            .SelectMany(material => material.GetTexturePropertyNames()
                .Select(property => material.GetTexture(property) as Texture2D))
            .Where(texture => texture != null)
            .Select(texture => Mathf.Max(texture.width, texture.height))
            .DefaultIfEmpty(0)
            .Max();
        var environmentAudioSources = roots
            .SelectMany(root => root.GetComponentsInChildren<AudioSource>(true))
            .Where(source => source.enabled
                && source.gameObject.activeInHierarchy
                && !IsGameplayRig(source.transform))
            .ToArray();
        var environmentAudioSourceCount = environmentAudioSources.Length;
        var proceduralAmbientSourceCount = environmentAudioSources.Count(source =>
            source.GetComponent<GenesisProceduralAmbient>() != null);
        var missingEnvironmentAudioClips = environmentAudioSources.Count(source =>
            source.clip == null
            && source.GetComponent<GenesisProceduralAmbient>() == null
            && (source.playOnAwake || source.loop));

        var missingMaterialSlots = 0;
        var missingShaders = 0;
        foreach (var renderer in renderers)
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null)
                    missingMaterialSlots += 1;
                else if (material.shader == null
                    || material.shader.name == "Hidden/InternalErrorShader"
                    || !material.shader.isSupported)
                    missingShaders += 1;
            }
        }

        var geometryRenderers = renderers
            .Where(renderer =>
                renderer.bounds.size.x < 5000f
                && renderer.bounds.size.y < 5000f
                && renderer.bounds.size.z < 5000f)
            .ToArray();
        var bounds = geometryRenderers.Length == 0
            ? new Bounds(Vector3.zero, Vector3.zero)
            : geometryRenderers[0].bounds;
        foreach (var renderer in geometryRenderers.Skip(1))
            bounds.Encapsulate(renderer.bounds);
        var nearbyEnvironmentRenderers = geometryRenderers.Count(renderer =>
            !IsGameplayRig(renderer.transform)
            && renderer.bounds.size.x < 500f
            && renderer.bounds.size.y < 500f
            && renderer.bounds.size.z < 500f
            && Vector3.Distance(
                renderer.bounds.ClosestPoint(spawn + Vector3.up * 1.2f),
                spawn + Vector3.up * 1.2f) < 65f);
        var distinctVisualMaterialSignatures = geometryRenderers
            .Where(renderer => !IsGameplayRig(renderer.transform))
            .SelectMany(renderer => renderer.sharedMaterials)
            .Where(material => material != null)
            .Select(material =>
            {
                var texturePath = material.mainTexture == null
                    ? "<none>"
                    : AssetDatabase.GetAssetPath(material.mainTexture);
                var color = material.HasProperty("_Color")
                    ? material.color
                    : Color.white;
                return string.Format(
                    "{0}|{1:F2},{2:F2},{3:F2}",
                    texturePath,
                    color.r,
                    color.g,
                    color.b);
            })
            .Distinct()
            .Count();

        var physics = scene.GetPhysicsScene();
        RaycastHit groundHit;
        var groundOrigin = spawn + Vector3.up * 2f;
        var groundBelow = physics.Raycast(
            groundOrigin,
            Vector3.down,
            out groundHit,
            12f,
            ~0,
            QueryTriggerInteraction.Ignore);
        var groundDistance = groundBelow
            ? groundOrigin.y - groundHit.point.y
            : -1f;
        RaycastHit overheadHit;
        var overheadBlocked = physics.Raycast(
            spawn + Vector3.up * 0.2f,
            Vector3.up,
            out overheadHit,
            2.1f,
            ~0,
            QueryTriggerInteraction.Ignore);
        var horizontalClearances = HorizontalDirections()
            .Select(direction => HorizontalClearance(
                physics, spawn + Vector3.up * 1.6f, direction, 12f))
            .ToArray();

        NavMeshHit navHit;
        var spawnOnNavMesh = NavMesh.SamplePosition(
            spawn, out navHit, 3f, NavMesh.AllAreas);
        var triangulation = NavMesh.CalculateTriangulation();
        var navAnalysis = AnalyzeNavMesh(triangulation, spawn, physics);
        var failures = new List<string>();
        if (renderers.Length == 0)
            failures.Add("no visible renderers");
        if (colliders.Length < 10)
            failures.Add("fewer than 10 enabled colliders");
        if (missingMaterialSlots > 0)
            failures.Add("missing material slots");
        if (missingShaders > 0)
            failures.Add("missing or unsupported shaders");
        if (RenderSettings.skybox == null
            || RenderSettings.skybox.shader == null
            || !RenderSettings.skybox.shader.isSupported)
        {
            failures.Add("missing or unsupported skybox");
        }
        if (missingRequiredNormals > 0)
            failures.Add("lit environment meshes are missing normals");
        if (missingRequiredUv > 0)
            failures.Add("textured environment meshes are missing UVs");
        if (maximumTextureDimension > 4096)
            failures.Add("environment texture exceeds 4096 pixels");
        if (!groundBelow || groundDistance > 4.5f)
            failures.Add("spawn has no nearby supporting collision");
        if (overheadBlocked)
            failures.Add("spawn has overhead obstruction");
        if (horizontalClearances.Min() < 2f)
            failures.Add("spawn is too close to a horizontal obstruction");
        if (bounds.size.x > 5000f || bounds.size.z > 5000f)
            failures.Add("implausibly large visible bounds");
        if (!spawnOnNavMesh || triangulation.vertices.Length == 0)
            failures.Add("spawn is not on a baked NavMesh");
        if (navAnalysis.reachableSamples < 4)
            failures.Add("spawn NavMesh region has fewer than 4 reachable samples");
        if (Mathf.Max(
                navAnalysis.reachableSpan.x,
                navAnalysis.reachableSpan.y) < 12f)
        {
            failures.Add("spawn NavMesh region spans fewer than 12 horizontal metres");
        }
        if (navAnalysis.unsupportedReachableVertices > 0)
        {
            failures.Add(
                "spawn-reachable NavMesh contains vertices without collision support");
        }
        if (nearbyEnvironmentRenderers == 0)
            failures.Add("spawn has no nearby visible environment geometry");
        if (geometryRenderers.Length > 20
            && distinctVisualMaterialSignatures < 2)
        {
            failures.Add("environment has no meaningful material variation");
        }
        if (mapName == "SteelFactory"
            && (bounds.size.x > 350f
                || bounds.size.z > 350f
                || bounds.size.y > 80f))
        {
            failures.Add("SteelFactory retains implausible export scale");
        }
        if (mapName == "SteelFactory" && nearbyEnvironmentRenderers < 20)
            failures.Add("SteelFactory spawn is visually sparse");
        var requiresRecoveredAtmosphere = mapName == "NewConstructionSite"
            || mapName == "BiochemicalTown"
            || mapName == "ClassicConstructionSite"
            || mapName == "SteelFactory"
            || mapName == "RadiationDistrict"
            || mapName == "IceFireMaze";
        if (requiresRecoveredAtmosphere && proceduralAmbientSourceCount == 0)
        {
            failures.Add(mapName + " procedural ambience is missing");
        }
        if (requiresRecoveredAtmosphere && lights.Length == 0)
            failures.Add(mapName + " has no recovered light");
        if (requiresRecoveredAtmosphere && !RenderSettings.fog)
            failures.Add(mapName + " recovered fog is disabled");

        return new MapReport
        {
            map = mapName,
            rendererCount = renderers.Length,
            colliderCount = colliders.Length,
            lightCount = lights.Length,
            missingMaterialSlots = missingMaterialSlots,
            missingShaders = missingShaders,
            backgroundOutlierRenderers =
                renderers.Length - geometryRenderers.Length,
            nearbyEnvironmentRenderers = nearbyEnvironmentRenderers,
            distinctVisualMaterialSignatures = distinctVisualMaterialSignatures,
            hasSkybox = RenderSettings.skybox != null
                && RenderSettings.skybox.shader != null,
            visibleBoundsCenter = bounds.center,
            visibleBoundsSize = bounds.size,
            spawn = spawn,
            groundBelowSpawn = groundBelow,
            groundDistance = groundDistance,
            overheadBlocked = overheadBlocked,
            minimumHorizontalClearance = horizontalClearances.Min(),
            averageHorizontalClearance = horizontalClearances.Average(),
            spawnOnNavMesh = spawnOnNavMesh,
            navMeshVertices = triangulation.vertices.Length,
            environmentMeshCount = environmentMeshFilters.Length,
            missingRequiredNormals = missingRequiredNormals,
            missingRequiredUv = missingRequiredUv,
            triangleCount = triangleCount,
            maximumTextureDimension = maximumTextureDimension,
            environmentAudioSourceCount = environmentAudioSourceCount,
            proceduralAmbientSourceCount = proceduralAmbientSourceCount,
            missingEnvironmentAudioClips = missingEnvironmentAudioClips,
            fogEnabled = RenderSettings.fog,
            navMeshIslandCount = navAnalysis.islandCount,
            spawnNavMeshComponentRatio = navAnalysis.spawnComponentRatio,
            unsupportedNavMeshVertices = navAnalysis.unsupportedVertices,
            unsupportedReachableNavMeshVertices =
                navAnalysis.unsupportedReachableVertices,
            navMeshReachableSampleRatio = navAnalysis.reachableSampleRatio,
            reachableNavMeshSamples = navAnalysis.reachableSamples,
            reachableNavMeshSpan = navAnalysis.reachableSpan,
            strictReady = failures.Count == 0,
            failures = failures.ToArray(),
        };
    }

    private struct NavMeshAnalysis
    {
        public int islandCount;
        public float spawnComponentRatio;
        public int unsupportedVertices;
        public int unsupportedReachableVertices;
        public float reachableSampleRatio;
        public int reachableSamples;
        public Vector2 reachableSpan;
    }

    private static NavMeshAnalysis AnalyzeNavMesh(
        NavMeshTriangulation triangulation, Vector3 spawn, PhysicsScene physics)
    {
        var vertices = triangulation.vertices;
        if (vertices == null || vertices.Length == 0)
            return new NavMeshAnalysis();
        var parents = Enumerable.Range(0, vertices.Length).ToArray();
        Func<int, int> find = null;
        find = index =>
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }
            return index;
        };
        Action<int, int> union = (left, right) =>
        {
            var leftRoot = find(left);
            var rightRoot = find(right);
            if (leftRoot != rightRoot)
                parents[rightRoot] = leftRoot;
        };
        var coincidentVertices = new Dictionary<string, int>();
        for (var index = 0; index < vertices.Length; index += 1)
        {
            var vertex = vertices[index];
            var key = Mathf.RoundToInt(vertex.x * 10f) + ":"
                + Mathf.RoundToInt(vertex.y * 10f) + ":"
                + Mathf.RoundToInt(vertex.z * 10f);
            int existing;
            if (coincidentVertices.TryGetValue(key, out existing))
                union(existing, index);
            else
                coincidentVertices.Add(key, index);
        }
        for (var index = 0; index + 2 < triangulation.indices.Length; index += 3)
        {
            var first = triangulation.indices[index];
            var second = triangulation.indices[index + 1];
            var third = triangulation.indices[index + 2];
            union(first, second);
            union(second, third);
            union(third, first);
        }
        var groups = Enumerable.Range(0, vertices.Length)
            .GroupBy(find)
            .ToDictionary(group => group.Key, group => group.Count());
        var spawnIndex = Enumerable.Range(0, vertices.Length)
            .OrderBy(index => (vertices[index] - spawn).sqrMagnitude)
            .First();
        var spawnRoot = find(spawnIndex);
        var unsupportedVertices = vertices.Where(vertex =>
            !physics.Raycast(
                vertex + Vector3.up * 0.4f,
                Vector3.down,
                out _,
                3f,
                ~0,
                QueryTriggerInteraction.Ignore))
            .ToArray();
        NavMeshHit startHit;
        var reachable = 0;
        var sampled = 0;
        var unsupportedReachable = 0;
        var hasReachableBounds = false;
        var reachableBounds = new Bounds();
        if (NavMesh.SamplePosition(spawn, out startHit, 3f, NavMesh.AllAreas))
        {
            var unsupportedPath = new NavMeshPath();
            foreach (var vertex in unsupportedVertices)
            {
                NavMeshHit unsupportedHit;
                if (NavMesh.SamplePosition(
                    vertex + Vector3.up * 0.1f,
                    out unsupportedHit,
                    0.75f,
                    NavMesh.AllAreas)
                    && NavMesh.CalculatePath(
                        startHit.position,
                        unsupportedHit.position,
                        NavMesh.AllAreas,
                        unsupportedPath)
                    && unsupportedPath.status == NavMeshPathStatus.PathComplete)
                {
                    unsupportedReachable += 1;
                }
            }
            var stride = Mathf.Max(1, vertices.Length / 128);
            var path = new NavMeshPath();
            for (var index = 0; index < vertices.Length && sampled < 128; index += stride)
            {
                sampled += 1;
                NavMeshHit targetHit;
                if (!NavMesh.SamplePosition(
                    vertices[index] + Vector3.up * 0.1f,
                    out targetHit,
                    0.75f,
                    NavMesh.AllAreas))
                {
                    continue;
                }
                if (NavMesh.CalculatePath(
                    startHit.position,
                    targetHit.position,
                    NavMesh.AllAreas,
                    path)
                    && path.status == NavMeshPathStatus.PathComplete)
                {
                    reachable += 1;
                    if (!hasReachableBounds)
                    {
                        reachableBounds = new Bounds(
                            targetHit.position, Vector3.zero);
                        hasReachableBounds = true;
                    }
                    else
                    {
                        reachableBounds.Encapsulate(targetHit.position);
                    }
                }
            }
        }
        return new NavMeshAnalysis
        {
            islandCount = groups.Count,
            spawnComponentRatio = groups[spawnRoot] / (float)vertices.Length,
            unsupportedVertices = unsupportedVertices.Length,
            unsupportedReachableVertices = unsupportedReachable,
            reachableSampleRatio = sampled == 0 ? 0f : reachable / (float)sampled,
            reachableSamples = reachable,
            reachableSpan = hasReachableBounds
                ? new Vector2(reachableBounds.size.x, reachableBounds.size.z)
                : Vector2.zero,
        };
    }

    private static bool IsGameplayRig(Transform candidate)
    {
        for (var current = candidate; current != null; current = current.parent)
        {
            if (current.name == "First Person Player"
                || current.GetComponentInParent<Canvas>() != null)
            {
                return true;
            }
        }
        return false;
    }
}
#endif
