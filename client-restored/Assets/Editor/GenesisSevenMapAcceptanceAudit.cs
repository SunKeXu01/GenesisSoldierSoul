#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using GenesisSoldierSoul.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

/// <summary>
/// Converts the map recovery checks into the eight explicit, per-map acceptance
/// rows used by the recovery checklist. Metrics are deliberately conservative
/// editor-side proxies so the same gate can run before every WebGL publication.
/// </summary>
public static class GenesisSevenMapAcceptanceAudit
{
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

    [Serializable]
    private sealed class RecoveryReport
    {
        public RecoveryMap[] maps;
    }

    [Serializable]
    private sealed class RecoveryMap
    {
        public string map;
        public int rendererCount;
        public int colliderCount;
        public int lightCount;
        public int missingMaterialSlots;
        public int missingShaders;
        public int missingRequiredNormals;
        public int missingRequiredUv;
        public int maximumTextureDimension;
        public int environmentAudioSourceCount;
        public int proceduralAmbientSourceCount;
        public int missingEnvironmentAudioClips;
        public bool hasSkybox;
        public bool fogEnabled;
        public bool groundBelowSpawn;
        public bool overheadBlocked;
        public bool spawnOnNavMesh;
        public int unsupportedReachableNavMeshVertices;
        public int reachableNavMeshSamples;
        public Vector2 reachableNavMeshSpan;
        public Vector3 visibleBoundsSize;
        public Vector3 spawn;
        public long triangleCount;
        public bool strictReady;
    }

    [Serializable]
    private sealed class AcceptanceMap
    {
        public string map;
        public string displayName;
        public bool visualReady;
        public bool modelAndVisibilityReady;
        public bool collisionReady;
        public bool spawnReady;
        public bool navigationAndTrainingReady;
        public bool anomalyReady;
        public bool radarAndAudioReady;
        public bool webGlReady;
        public bool serverSpawnFairnessReady;
        public bool passed;
        public int transparentRendererCount;
        public int transparentSpawnBlockers;
        public int boundaryRayHits;
        public int reachableSamplesAudited;
        public int supportedReachableSamples;
        public int flatSupportSamples;
        public int slopeSupportSamples;
        public int trainingTargetCount;
        public int reachableTrainingTargets;
        public float minimumTrainingTargetSpacing;
        public float reachableHeightSpan;
        public int estimatedBatchUpperBound;
        public long estimatedResidentAssetBytes;
        public int spatialAudioSourceCount;
        public string[] failures;
    }

    [Serializable]
    private sealed class AcceptanceReport
    {
        public string generatedAtUtc;
        public string unityVersion;
        public int mapCount;
        public int passedMapCount;
        public int passedCategoryCount;
        public int totalCategoryCount;
        public AcceptanceMap[] maps;
    }

    [MenuItem("Genesis/Audit Seven Map Unified Acceptance")]
    public static void Run()
    {
        GenesisMapRecoveryAudit.AuditMapRecoveryReadiness();
        var recoveryPath = Path.GetFullPath(Path.Combine(
            Application.dataPath, "../../recovery/map-recovery-readiness.json"));
        var recovery = JsonUtility.FromJson<RecoveryReport>(
            File.ReadAllText(recoveryPath));
        if (recovery == null || recovery.maps == null
            || recovery.maps.Length != MapNames.Length)
        {
            throw new InvalidOperationException(
                "Seven-map recovery report is missing or incomplete.");
        }

        var displayNames = MapNames
            .Select(GenesisMultiplayerBootstrap.GetDisplayName)
            .ToArray();
        if (displayNames.Any(string.IsNullOrWhiteSpace)
            || displayNames.Distinct(StringComparer.Ordinal).Count()
                != MapNames.Length)
        {
            throw new InvalidOperationException(
                "Playable map display names are missing or duplicated.");
        }

        var reports = new List<AcceptanceMap>();
        foreach (var mapName in MapNames)
        {
            var baseline = recovery.maps.First(item => item.map == mapName);
            reports.Add(AuditMap(mapName, baseline));
        }

        var report = new AcceptanceReport
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            unityVersion = Application.unityVersion,
            mapCount = reports.Count,
            passedMapCount = reports.Count(item => item.passed),
            passedCategoryCount = reports.Sum(CountPassedCategories),
            totalCategoryCount = reports.Count * 8,
            maps = reports.ToArray(),
        };
        var outputPath = Path.GetFullPath(Path.Combine(
            Application.dataPath,
            "../../recovery/seven-map-unified-acceptance.json"));
        File.WriteAllText(
            outputPath,
            JsonUtility.ToJson(report, true) + "\n",
            new UTF8Encoding(false));
        Debug.Log(string.Format(
            "[GenesisSevenMapAcceptance] maps={0}/{1}; categories={2}/{3}; report={4}",
            report.passedMapCount,
            report.mapCount,
            report.passedCategoryCount,
            report.totalCategoryCount,
            outputPath));
        if (report.passedMapCount != report.mapCount)
        {
            throw new InvalidOperationException(
                "Seven-map unified acceptance failed:\n"
                + string.Join("\n", reports
                    .Where(item => !item.passed)
                    .Select(item => item.map + ": "
                        + string.Join(" | ", item.failures))));
        }
    }

    public static void EnsureReady()
    {
        Run();
    }

    private static AcceptanceMap AuditMap(
        string mapName, RecoveryMap baseline)
    {
        var scene = EditorSceneManager.OpenScene(
            "Assets/PlayableMaps/" + mapName + ".unity",
            OpenSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        Physics.SyncTransforms();
        var roots = scene.GetRootGameObjects();
        var player = roots.FirstOrDefault(root =>
            root.name == "First Person Player");
        if (player == null)
            throw new InvalidOperationException(mapName + " has no player root.");

        var renderers = roots
            .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
            .Where(item => item.enabled && item.gameObject.activeInHierarchy)
            .ToArray();
        var colliders = roots
            .SelectMany(root => root.GetComponentsInChildren<Collider>(true))
            .Where(item => item.enabled && item.gameObject.activeInHierarchy
                && !item.isTrigger)
            .ToArray();
        var transparentRenderers = renderers
            .Where(IsTransparent)
            .ToArray();
        var transparentBlockers = transparentRenderers.Count(renderer =>
        {
            var collider = renderer.GetComponent<Collider>();
            return collider != null && collider.enabled && !collider.isTrigger
                && Vector3.Distance(
                    collider.bounds.ClosestPoint(baseline.spawn),
                    baseline.spawn) < 1.2f;
        });

        var physics = scene.GetPhysicsScene();
        var boundaryHits = 0;
        for (var index = 0; index < 32; index += 1)
        {
            var direction = Quaternion.Euler(0f, index * 11.25f, 0f)
                * Vector3.forward;
            RaycastHit hit;
            if (physics.Raycast(
                    baseline.spawn + Vector3.up * 1.25f,
                    direction,
                    out hit,
                    220f,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                boundaryHits += 1;
            }
        }

        var reachable = ReachableSamples(baseline.spawn);
        var supported = 0;
        var flat = 0;
        var slopes = 0;
        var minimumHeight = float.PositiveInfinity;
        var maximumHeight = float.NegativeInfinity;
        foreach (var point in reachable)
        {
            minimumHeight = Mathf.Min(minimumHeight, point.y);
            maximumHeight = Mathf.Max(maximumHeight, point.y);
            RaycastHit hit;
            if (!physics.Raycast(
                    point + Vector3.up * 1.25f,
                    Vector3.down,
                    out hit,
                    2.75f,
                    ~0,
                    QueryTriggerInteraction.Ignore)
                || hit.normal.y < 0.55f)
            {
                continue;
            }
            supported += 1;
            if (hit.normal.y >= 0.98f)
                flat += 1;
            else
                slopes += 1;
        }
        var heightSpan = reachable.Count == 0
            ? float.PositiveInfinity
            : maximumHeight - minimumHeight;

        var training = AuditTrainingTargets(player.transform, physics);
        var estimatedBatches = renderers.Sum(renderer =>
            Mathf.Max(1, renderer.sharedMaterials == null
                ? 0
                : renderer.sharedMaterials.Length));
        var residentBytes = EstimateResidentAssetBytes(renderers);
        var spatialAudio = roots
            .SelectMany(root => root.GetComponentsInChildren<AudioSource>(true))
            .Count(source => source.enabled
                && source.gameObject.activeInHierarchy
                && source.spatialBlend > 0.01f);

        var failures = new List<string>();
        var fogPolicyReady = mapName == "Pyramid"
            ? !baseline.fogEnabled
            : baseline.fogEnabled;
        var visualReady = baseline.strictReady
            && baseline.missingMaterialSlots == 0
            && baseline.missingShaders == 0
            && baseline.hasSkybox
            && fogPolicyReady
            && baseline.lightCount > 0;
        Check(visualReady, "materials/textures/shaders/sky/fog/light", failures);

        var modelReady = baseline.missingRequiredNormals == 0
            && baseline.missingRequiredUv == 0
            && transparentBlockers == 0
            && baseline.visibleBoundsSize.x >= 12f
            && baseline.visibleBoundsSize.z >= 12f
            && baseline.visibleBoundsSize.x < 5000f
            && baseline.visibleBoundsSize.y < 5000f
            && baseline.visibleBoundsSize.z < 5000f;
        Check(modelReady, "scale/normals/UV/transparency/occlusion", failures);

        var collisionReady = baseline.colliderCount >= 10
            && reachable.Count >= 4
            && supported >= Mathf.CeilToInt(reachable.Count * 0.98f)
            && flat > 0
            && boundaryHits >= 2;
        Check(collisionReady, "floor/wall/stair/slope/boundary collision", failures);

        var horizontalForward = player.transform.forward;
        horizontalForward.y = 0f;
        var spawnReady = baseline.groundBelowSpawn
            && !baseline.overheadBlocked
            && baseline.spawnOnNavMesh
            && horizontalForward.sqrMagnitude > 0.5f
            && HasSymmetricServerSpawnPairs(mapName);
        Check(spawnReady, "safe spawn/orientation/clearance/fairness", failures);

        var navigationReady = baseline.reachableNavMeshSamples >= 4
            && Mathf.Max(
                baseline.reachableNavMeshSpan.x,
                baseline.reachableNavMeshSpan.y) >= 12f
            && training.targetCount == 4
            && training.reachableTargets == 4
            && training.minimumSpacing >= 1.5f;
        Check(navigationReady, "NavMesh/training targets/reachable area", failures);

        var anomalyReady = baseline.unsupportedReachableNavMeshVertices == 0
            && supported == reachable.Count
            && heightSpan < 1000f
            && transparentBlockers == 0;
        Check(anomalyReady, "fall/stuck/wall-pass/height/air-wall anomalies", failures);

        var radarAudioReady = !string.IsNullOrWhiteSpace(
                GenesisMultiplayerBootstrap.GetDisplayName(mapName))
            && baseline.environmentAudioSourceCount > 0
            && baseline.missingEnvironmentAudioClips == 0;
        Check(radarAudioReady, "radar/map name/audio zones/ambience", failures);

        var webGlReady = baseline.maximumTextureDimension <= 2048
            && baseline.missingShaders == 0
            && estimatedBatches <= 6000
            && baseline.triangleCount <= 15000000L
            && residentBytes <= 1536L * 1024L * 1024L;
        Check(webGlReady, "WebGL memory/batches/textures/shaders", failures);

        return new AcceptanceMap
        {
            map = mapName,
            displayName = GenesisMultiplayerBootstrap.GetDisplayName(mapName),
            visualReady = visualReady,
            modelAndVisibilityReady = modelReady,
            collisionReady = collisionReady,
            spawnReady = spawnReady,
            navigationAndTrainingReady = navigationReady,
            anomalyReady = anomalyReady,
            radarAndAudioReady = radarAudioReady,
            webGlReady = webGlReady,
            serverSpawnFairnessReady = HasSymmetricServerSpawnPairs(mapName),
            passed = failures.Count == 0,
            transparentRendererCount = transparentRenderers.Length,
            transparentSpawnBlockers = transparentBlockers,
            boundaryRayHits = boundaryHits,
            reachableSamplesAudited = reachable.Count,
            supportedReachableSamples = supported,
            flatSupportSamples = flat,
            slopeSupportSamples = slopes,
            trainingTargetCount = training.targetCount,
            reachableTrainingTargets = training.reachableTargets,
            minimumTrainingTargetSpacing = training.minimumSpacing,
            reachableHeightSpan = heightSpan,
            estimatedBatchUpperBound = estimatedBatches,
            estimatedResidentAssetBytes = residentBytes,
            spatialAudioSourceCount = spatialAudio,
            failures = failures.ToArray(),
        };
    }

    private static List<Vector3> ReachableSamples(Vector3 spawn)
    {
        var vertices = NavMesh.CalculateTriangulation().vertices;
        var samples = new List<Vector3>();
        if (vertices == null || vertices.Length == 0)
            return samples;
        var stride = Mathf.Max(1, vertices.Length / 96);
        for (var index = 0; index < vertices.Length && samples.Count < 96;
             index += stride)
        {
            var path = new NavMeshPath();
            if (NavMesh.CalculatePath(
                    spawn,
                    vertices[index],
                    NavMesh.AllAreas,
                    path)
                && path.status == NavMeshPathStatus.PathComplete)
            {
                samples.Add(vertices[index]);
            }
        }
        return samples;
    }

    private struct TrainingAudit
    {
        public int targetCount;
        public int reachableTargets;
        public float minimumSpacing;
    }

    private static TrainingAudit AuditTrainingTargets(
        Transform player, PhysicsScene physics)
    {
        var arena = player.gameObject.AddComponent<GenesisTrainingArena>();
        var targetAnchors = new List<GameObject>();
        try
        {
            var type = typeof(GenesisTrainingArena);
            SetField(type, arena, "localPlayer", player);
            SetField(type, arena, "arenaOrigin", player.position);
            var findOpen = type.GetMethod(
                "FindOpenDirection",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var spawnPoint = type.GetMethod(
                "SpawnPoint",
                BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic);
            if (findOpen == null || spawnPoint == null)
                throw new InvalidOperationException(
                    "Training target placement API is incomplete.");
            var openForward = (Vector3)findOpen.Invoke(arena, null);
            SetField(type, arena, "openForward", openForward);
            var botsField = type.GetField(
                "bots", BindingFlags.Instance | BindingFlags.NonPublic);
            var botType = type.Assembly.GetType(
                "GenesisSoldierSoul.Multiplayer.GenesisTrainingBot", true);
            var bots = botsField == null
                ? null
                : botsField.GetValue(arena) as IList;
            if (bots == null)
                throw new InvalidOperationException("Training bot list is missing.");
            var points = new List<Vector3>();
            for (var index = 0; index < 4; index += 1)
            {
                var point = (Vector3)spawnPoint.Invoke(
                    arena, new object[] { index });
                points.Add(point);
                var anchor = new GameObject("AcceptanceTrainingTarget_" + index);
                anchor.transform.position = point;
                bots.Add(anchor.AddComponent(botType));
                targetAnchors.Add(anchor);
            }
            var reachable = 0;
            foreach (var point in points)
            {
                NavMeshHit navHit;
                RaycastHit support;
                var onNavigation = NavMesh.SamplePosition(
                    point, out navHit, 3f, NavMesh.AllAreas);
                var hasSupport = physics.Raycast(
                        point + Vector3.up * 1.25f,
                        Vector3.down,
                        out support,
                        3f,
                        ~0,
                        QueryTriggerInteraction.Ignore)
                    && support.normal.y >= 0.55f;
                if (onNavigation && hasSupport)
                {
                    reachable += 1;
                }
                else
                {
                    NavMeshHit wideNavHit;
                    var wideNavigation = NavMesh.SamplePosition(
                        point, out wideNavHit, 10f, NavMesh.AllAreas);
                    Debug.LogWarning(
                        "[GenesisSevenMapAcceptance] training target invalid: "
                        + point + "; nav=" + onNavigation
                        + "; support=" + hasSupport
                        + "; wideNav=" + wideNavigation
                        + "; widePosition="
                        + (wideNavigation ? wideNavHit.position.ToString() : "<none>"));
                }
            }
            var minimumSpacing = float.PositiveInfinity;
            for (var left = 0; left < points.Count; left += 1)
            for (var right = left + 1; right < points.Count; right += 1)
            {
                var delta = points[left] - points[right];
                delta.y = 0f;
                minimumSpacing = Mathf.Min(minimumSpacing, delta.magnitude);
            }
            return new TrainingAudit
            {
                targetCount = points.Count,
                reachableTargets = reachable,
                minimumSpacing = minimumSpacing,
            };
        }
        finally
        {
            foreach (var anchor in targetAnchors)
                UnityEngine.Object.DestroyImmediate(anchor);
            UnityEngine.Object.DestroyImmediate(arena);
        }
    }

    private static bool HasSymmetricServerSpawnPairs(string mapName)
    {
        if (Array.IndexOf(MapNames, mapName) < 0)
            return false;
        const float offset = 2f;
        var points = new[]
        {
            new Vector2(-offset, -offset),
            new Vector2(offset, offset),
            new Vector2(-offset, offset),
            new Vector2(offset, -offset),
            new Vector2(0f, -offset * 1.4f),
            new Vector2(0f, offset * 1.4f),
        };
        for (var index = 0; index < points.Length; index += 2)
        {
            if ((points[index] + points[index + 1]).sqrMagnitude > 0.0001f)
                return false;
        }
        return true;
    }

    private static void SetField(
        Type type, object instance, string fieldName, object value)
    {
        var field = type.GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException(
                "Training field is missing: " + fieldName);
        field.SetValue(instance, value);
    }

    private static long EstimateResidentAssetBytes(Renderer[] renderers)
    {
        var assets = new HashSet<UnityEngine.Object>();
        foreach (var renderer in renderers)
        {
            var meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
                assets.Add(meshFilter.sharedMesh);
            var skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null && skinned.sharedMesh != null)
                assets.Add(skinned.sharedMesh);
            foreach (var material in renderer.sharedMaterials ?? new Material[0])
            {
                if (material == null)
                    continue;
                assets.Add(material);
                foreach (var propertyName in material.GetTexturePropertyNames())
                {
                    var texture = material.GetTexture(propertyName);
                    if (texture != null)
                        assets.Add(texture);
                }
            }
        }
        return assets.Sum(asset => Profiler.GetRuntimeMemorySizeLong(asset));
    }

    private static bool IsTransparent(Renderer renderer)
    {
        return (renderer.sharedMaterials ?? new Material[0]).Any(material =>
            material != null
            && (material.renderQueue >= 3000
                || material.HasProperty("_Mode")
                    && material.GetFloat("_Mode") >= 2f));
    }

    private static void Check(
        bool passed, string category, ICollection<string> failures)
    {
        if (!passed)
            failures.Add(category + " failed");
    }

    private static int CountPassedCategories(AcceptanceMap item)
    {
        return new[]
        {
            item.visualReady,
            item.modelAndVisibilityReady,
            item.collisionReady,
            item.spawnReady,
            item.navigationAndTrainingReady,
            item.anomalyReady,
            item.radarAndAudioReady,
            item.webGlReady,
        }.Count(value => value);
    }
}
#endif
