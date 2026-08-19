using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenesisSoldierSoul.Multiplayer;
using GenesisSoldierSoul.WeaponActions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GenesisWeaponRecoveryAudit
{
    [Serializable]
    private sealed class WeaponReport
    {
        public string name;
        public string prefabPath;
        public bool loaded;
        public bool requiredForRuntime;
        public bool passed;
        public int rendererCount;
        public int materialCount;
        public int missingMaterialCount;
        public int textureCount;
        public int materialsWithTextureCount;
        public float texturedMaterialCoverage;
        public int clipCount;
        public string[] clips;
        public string[] requiredAnchors;
        public string[] missingAnchors;
    }

    [Serializable]
    private sealed class AuditReport
    {
        public string generatedAtUtc;
        public string unityVersion;
        public int weaponCount;
        public int passedCount;
        public int runtimeRequiredCount;
        public int runtimePassedCount;
        public WeaponReport[] weapons;
    }

    private sealed class WeaponSpec
    {
        public readonly string Name;
        public readonly string PrefabPath;
        public readonly string[] RequiredAnchors;
        public readonly bool RequiredForRuntime;
        public readonly bool AllowsColorOnlyMaterials;

        public WeaponSpec(
            string name,
            string prefabPath,
            bool requiredForRuntime,
            params string[] anchors)
            : this(name, prefabPath, requiredForRuntime, false, anchors)
        {
        }

        public WeaponSpec(
            string name,
            string prefabPath,
            bool requiredForRuntime,
            bool allowsColorOnlyMaterials,
            params string[] anchors)
        {
            Name = name;
            PrefabPath = prefabPath;
            RequiredAnchors = anchors;
            RequiredForRuntime = requiredForRuntime;
            AllowsColorOnlyMaterials = allowsColorOnlyMaterials;
        }
    }

    private static readonly WeaponSpec[] Specs =
    {
        new WeaponSpec(
            "AssaultRifle01",
            "Assets/Resources/OriginalGame/FirstPerson/AssaultRifle01.prefab",
            true,
            "WeaponMainLocator", "Main", "Muzzle", "RightHand"),
        new WeaponSpec(
            "M4A1Viewmodel",
            "Assets/Resources/OriginalGame/FirstPerson/M4A1Viewmodel.prefab",
            true,
            "WeaponMainLocator", "Recovered_M4A1_Sopmod", "Muzzle", "RightHand"),
        new WeaponSpec(
            "M16ViewmodelCandidate",
            "Assets/Resources/OriginalGame/FirstPerson/"
                + "M16ViewmodelCandidate.prefab",
            false,
            "WeaponMainLocator", "Recovered_M16_Candidate", "Muzzle", "RightHand"),
        new WeaponSpec(
            "AK74MViewmodelCandidate",
            "Assets/Resources/OriginalGame/FirstPerson/"
                + "AK74MViewmodelCandidate.prefab",
            false,
            "WeaponMainLocator", "Recovered_AK74M_Candidate", "Muzzle", "RightHand"),
        new WeaponSpec(
            "AWPViewmodelCandidate",
            "Assets/Resources/OriginalGame/FirstPerson/"
                + "AWPViewmodelCandidate.prefab",
            false,
            "WeaponMainLocator", "Recovered_AWP_Candidate", "Muzzle", "RightHand"),
        new WeaponSpec(
            "AWMTP",
            "Assets/Resources/OriginalGame/AWMTP.prefab",
            true,
            "AWM", "Muzzle"),
        new WeaponSpec(
            "AN94ViewmodelCandidate",
            "Assets/Resources/OriginalGame/FirstPerson/"
                + "AN94ViewmodelCandidate.prefab",
            false,
            "WeaponMainLocator", "Recovered_AN94_Candidate", "Muzzle", "RightHand"),
        new WeaponSpec(
            "M249",
            "Assets/Resources/OriginalGame/M249.prefab",
            true,
            "m249", "AmmoBox", "AmmoBelt"),
        new WeaponSpec(
            "FAMAS",
            "Assets/Resources/OriginalGame/FAMAS.prefab",
            true),
        new WeaponSpec(
            "FAMASTP",
            "Assets/Resources/OriginalGame/FAMASTP.prefab",
            true),
        new WeaponSpec(
            "MicroGalilBaxi",
            "Assets/Resources/OriginalGame/MicroGalilBaxi.prefab",
            true),
        new WeaponSpec(
            "MicroGalilBaxiTP",
            "Assets/Resources/OriginalGame/MicroGalilBaxiTP.prefab",
            true),
        new WeaponSpec(
            "Gatling",
            "Assets/Resources/OriginalGame/Gatling.prefab",
            true,
            true,
            "GatlingBarrelAssembly", "Muzzle"),
        new WeaponSpec(
            "GatlingTP",
            "Assets/Resources/OriginalGame/GatlingTP.prefab",
            true,
            true,
            "GatlingBarrelAssembly", "Muzzle"),
        new WeaponSpec(
            "AUGA1",
            "Assets/Resources/OriginalGame/AUGA1.prefab",
            true),
        new WeaponSpec(
            "AUGA1TP",
            "Assets/Resources/OriginalGame/AUGA1TP.prefab",
            true),
        new WeaponSpec(
            "AK47Ice",
            "Assets/Resources/OriginalGame/AK47Ice.prefab",
            true),
        new WeaponSpec(
            "AK47IceTP",
            "Assets/Resources/OriginalGame/AK47IceTP.prefab",
            true),
        new WeaponSpec(
            "HandAxe",
            "Assets/Resources/OriginalGame/HandAxe.prefab",
            true),
        new WeaponSpec(
            "Nepal",
            "Assets/Resources/OriginalGame/Nepal.prefab",
            true),
        new WeaponSpec(
            "NepalTP",
            "Assets/Resources/OriginalGame/NepalTP.prefab",
            true),
        new WeaponSpec(
            "Pistol01",
            "Assets/Resources/OriginalGame/FirstPerson/Pistol/Pistol01.prefab",
            true,
            "FireLocator", "MainMesh", "RightHand"),
        new WeaponSpec(
            "M9Viewmodel",
            "Assets/Resources/OriginalGame/FirstPerson/"
                + "M9Viewmodel.prefab",
            true,
            "FireLocator", "Recovered_M9_Candidate", "RightHand"),
        new WeaponSpec(
            "Knife01",
            "Assets/Resources/OriginalGame/FirstPerson/Knife/Knife01.prefab",
            false,
            "WeaponMainLocator"),
        new WeaponSpec(
            "Shotgun01",
            "Assets/Resources/OriginalGame/FirstPerson/RecoveredClosures/"
                + "Shotgun01/GameObject/Shotgun01.prefab",
            true,
            "WeaponMainLocator", "ReloadLocator", "WeaponMainMesh"),
        new WeaponSpec(
            "Grenade01",
            "Assets/Resources/OriginalGame/FirstPerson/RecoveredClosures/"
                + "Grenade01/GameObject/Grenade01.prefab",
            true,
            "WeaponMainLocator", "GrenadeMesh", "RingMesh", "SprintMesh"),
    };

    [MenuItem("Genesis/Weapons/Validate Recovered Prefabs")]
    public static void ValidateRecoveredWeaponPrefabs()
    {
        var reports = Specs.Select(Inspect).ToArray();
        var report = new AuditReport
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            unityVersion = Application.unityVersion,
            weaponCount = reports.Length,
            passedCount = reports.Count(item => item.passed),
            runtimeRequiredCount = reports.Count(item => item.requiredForRuntime),
            runtimePassedCount = reports.Count(
                item => item.requiredForRuntime && item.passed),
            weapons = reports,
        };
        var reportPath = Path.GetFullPath(Path.Combine(
            Application.dataPath,
            "../../recovery/recovered-weapon-validation.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true) + "\n");

        foreach (var weapon in reports)
        {
            var missing = weapon.missingAnchors.Length == 0
                ? "none"
                : string.Join(", ", weapon.missingAnchors);
            Debug.Log(string.Format(
                "[WeaponRecovery] {0}: passed={1}, renderers={2}, materials={3}, "
                + "textures={4}, texturedCoverage={5:P0}, clips={6}, "
                + "missingAnchors={7}",
                weapon.name,
                weapon.passed,
                weapon.rendererCount,
                weapon.materialCount,
                weapon.textureCount,
                weapon.texturedMaterialCoverage,
                weapon.clipCount,
                missing));
        }

        Debug.Log(string.Format(
            "[WeaponRecovery] Validation complete: {0}/{1} passed. Report: {2}",
            report.passedCount,
            report.weaponCount,
            reportPath));
        if (Application.isBatchMode
            && report.runtimePassedCount != report.runtimeRequiredCount)
            throw new InvalidOperationException(
                "Recovered weapon validation failed. See " + reportPath);
    }

    [MenuItem("Genesis/Weapons/Create Recovery Preview Scene")]
    public static void CreateRecoveryPreviewScene()
    {
        const string sceneFolder = "Assets/WeaponRecovery";
        const string scenePath = sceneFolder + "/RecoveredWeaponPreview.unity";
        if (!AssetDatabase.IsValidFolder(sceneFolder))
            AssetDatabase.CreateFolder("Assets", "WeaponRecovery");

        var scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        scene.name = "RecoveredWeaponPreview";

        var root = new GameObject("Recovered Weapon Prefabs - Source Intact");
        for (var index = 0; index < Specs.Length; index += 1)
        {
            var spec = Specs[index];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
            if (prefab == null)
                continue;
            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
                continue;
            instance.name = string.Format("{0:00}_{1}", index + 1, spec.Name);
            instance.transform.SetParent(root.transform, false);
            instance.transform.position = new Vector3((index - 2) * 2.5f, 0f, 0f);

            var label = new GameObject("Label").AddComponent<TextMesh>();
            label.transform.SetParent(root.transform, false);
            label.transform.position = new Vector3((index - 2) * 2.5f, 1.8f, 0f);
            label.text = spec.Name;
            label.fontSize = 42;
            label.characterSize = 0.08f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = Color.white;
        }

        var cameraObject = new GameObject("Preview Camera");
        var camera = cameraObject.AddComponent<Camera>();
        cameraObject.transform.position = new Vector3(0f, 1.1f, -11f);
        cameraObject.transform.LookAt(new Vector3(0f, 0.6f, 0f));
        camera.fieldOfView = 48f;

        var lightObject = new GameObject("Preview Key Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.25f;
        lightObject.transform.rotation = Quaternion.Euler(38f, -32f, 0f);
        RenderSettings.ambientLight = new Color(0.32f, 0.34f, 0.38f, 1f);

        EditorSceneManager.SaveScene(scene, scenePath);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        Debug.Log("[WeaponRecovery] Preview scene created: " + scenePath);
    }

    [MenuItem("Genesis/Weapons/Audit Grenade Viewmodel Bounds")]
    public static void AuditGrenadeViewmodelBounds()
    {
        var spec = Specs.First(item => item.Name == "Grenade01");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException("Grenade01 prefab missing.");
        var instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * 0.55f;
            var animation = instance.GetComponent<Animation>();
            if (animation != null && animation.GetClip("Idle01") != null)
            {
                animation.Play("Idle01");
                animation.Sample();
            }
            var renderers = instance.GetComponentsInChildren<Renderer>(true)
                .Where(item => item.enabled)
                .ToArray();
            if (renderers.Length == 0)
                throw new InvalidOperationException("Grenade01 has no renderers.");
            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index += 1)
                bounds.Encapsulate(renderers[index].bounds);
            Debug.Log(string.Format(
                "[WeaponRecovery] Grenade viewmodel sampled bounds: center={0}, "
                + "size={1}, localCenter={2}",
                bounds.center,
                bounds.size,
                instance.transform.InverseTransformPoint(bounds.center)));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    [MenuItem("Genesis/Weapons/Render Recovery Preview")]
    public static void RenderRecoveryPreview()
    {
        const string scenePath =
            "Assets/WeaponRecovery/RecoveredWeaponPreview.unity";
        if (!File.Exists(Path.GetFullPath(scenePath)))
            CreateRecoveryPreviewScene();
        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var camera = UnityEngine.Object.FindObjectOfType<Camera>();
        if (camera == null)
            throw new InvalidOperationException("Recovery preview camera missing.");

        const int width = 1600;
        const int height = 900;
        var renderTexture = new RenderTexture(width, height, 24);
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = renderTexture;
            camera.Render();
            RenderTexture.active = renderTexture;
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            image.Apply();
            var outputPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "../../recovery/recovered-weapon-preview.png"));
            File.WriteAllBytes(outputPath, image.EncodeToPNG());
            Debug.Log("[WeaponRecovery] Preview rendered: " + outputPath);
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(renderTexture);
            UnityEngine.Object.DestroyImmediate(image);
        }
    }

    [MenuItem("Genesis/Weapons/Render M4A1 Viewmodel Preview")]
    public static void RenderM4A1ViewmodelPreview()
    {
        RenderPrefabPreview(
            "Assets/Resources/OriginalGame/FirstPerson/M4A1Viewmodel.prefab",
            "recovered-m4a1-viewmodel-preview.png",
            new Vector3(-0.25f, 0.1f, -0.9f),
            true);
    }

    [MenuItem("Genesis/Weapons/Render M4A1 Runtime Framing")]
    public static void RenderM4A1RuntimeFraming()
    {
        RenderRuntimeFraming(
            "Assets/Resources/OriginalGame/FirstPerson/M4A1Viewmodel.prefab",
            "recovered-m4a1-runtime",
            GenesisViewmodelKind.M4A1,
            "Recovered_M4A1_Sopmod");
    }

    [MenuItem("Genesis/Weapons/Render Original Assault Rifle Runtime Framing")]
    public static void RenderOriginalAssaultRifleRuntimeFraming()
    {
        RenderRuntimeFraming(
            "Assets/Resources/OriginalGame/FirstPerson/AssaultRifle01.prefab",
            "recovered-original-assault-rifle-runtime",
            GenesisViewmodelKind.Rifle,
            "Main");
    }

    [MenuItem("Genesis/Weapons/Render Shotgun Runtime Framing")]
    public static void RenderShotgunRuntimeFraming()
    {
        RenderRuntimeFraming(
            "Assets/Resources/OriginalGame/FirstPerson/RecoveredClosures/"
                + "Shotgun01/GameObject/Shotgun01.prefab",
            "recovered-shotgun-runtime",
            GenesisViewmodelKind.Shotgun,
            "WeaponMainMesh");
    }

    [MenuItem("Genesis/Weapons/Render AK74M Runtime Framing")]
    public static void RenderAK74MRuntimeFraming()
    {
        RenderRuntimeFraming(
            "Assets/Resources/OriginalGame/FirstPerson/"
                + "AK74MViewmodelCandidate.prefab",
            "recovered-ak74m-runtime",
            GenesisViewmodelKind.AK74M,
            "Recovered_AK74M_Candidate");
    }

    [MenuItem("Genesis/Weapons/Render AWP Runtime Framing")]
    public static void RenderAWPRuntimeFraming()
    {
        RenderRuntimeFraming(
            "Assets/Resources/OriginalGame/FirstPerson/"
                + "AWPViewmodelCandidate.prefab",
            "recovered-awp-runtime",
            GenesisViewmodelKind.AWP,
            "Recovered_AWP_Candidate");
    }

    [MenuItem("Genesis/Weapons/Render AN94 Runtime Framing")]
    public static void RenderAN94RuntimeFraming()
    {
        RenderRuntimeFraming(
            "Assets/Resources/OriginalGame/FirstPerson/"
                + "AN94ViewmodelCandidate.prefab",
            "recovered-an94-runtime",
            GenesisViewmodelKind.AN94,
            "Recovered_AN94_Candidate");
    }

    [MenuItem("Genesis/Weapons/Render M249 Runtime Framing")]
    public static void RenderM249RuntimeFraming()
    {
        RenderRuntimeFraming(
            "Assets/Resources/OriginalGame/M249.prefab",
            "recovered-m249-runtime",
            GenesisViewmodelKind.M249,
            "Recovered_M249_Candidate");
    }

    private static void RenderRuntimeFraming(
        string prefabPath,
        string outputStem,
        GenesisViewmodelKind kind,
        string firearmModelName)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            throw new InvalidOperationException(
                "Runtime framing prefab is missing: " + prefabPath);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraObject = new GameObject("Runtime Viewmodel Camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.1f, 0.13f, 1f);
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 3f;

        var framingRoot = new GameObject("Runtime Viewmodel Root");
        framingRoot.transform.SetParent(cameraObject.transform, false);
        GameObject instance;
        if (kind == GenesisViewmodelKind.Shotgun
            || kind == GenesisViewmodelKind.AN94
            || kind == GenesisViewmodelKind.M249
            || kind == GenesisViewmodelKind.AWP)
        {
            var handsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/OriginalGame/FirstPerson/"
                    + "M4A1Viewmodel.prefab");
            instance = UnityEngine.Object.Instantiate(
                handsPrefab, framingRoot.transform);
            var handsReferenceWeapon = instance
                .GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item =>
                    item.name == "Recovered_M4A1_Sopmod");
            var visualPrefab = kind == GenesisViewmodelKind.AN94
                ? AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Resources/OriginalGame/AN94.prefab")
                : prefab;
            var recoveredVisual = UnityEngine.Object.Instantiate(
                visualPrefab, instance.transform);
            recoveredVisual.name = kind == GenesisViewmodelKind.AN94
                || kind == GenesisViewmodelKind.M249
                ? firearmModelName
                : "Recovered_" + kind + "_Visual";
            recoveredVisual.transform.localPosition = Vector3.zero;
            recoveredVisual.transform.localRotation =
                kind == GenesisViewmodelKind.AN94
                    ? Quaternion.Euler(0f, 0f, 90f)
                    : kind == GenesisViewmodelKind.M249
                        ? Quaternion.Euler(90f, 0f, 0f)
                        : Quaternion.identity;
            foreach (var damagedArm in instance
                .GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (kind == GenesisViewmodelKind.AN94
                    || kind == GenesisViewmodelKind.M249
                    || damagedArm.transform.IsChildOf(
                        recoveredVisual.transform))
                    damagedArm.enabled = false;
            }
            var damagedAnimation = recoveredVisual.GetComponent<Animation>();
            if (damagedAnimation != null)
                damagedAnimation.enabled = false;
            if (kind == GenesisViewmodelKind.Shotgun)
            {
                GenesisMatchController.AlignRecoveredShotgunToHands(
                    handsReferenceWeapon, recoveredVisual);
            }
            else
            {
                GenesisMatchController.AlignRecoveredWeaponToHands(
                    handsReferenceWeapon,
                    recoveredVisual,
                    kind == GenesisViewmodelKind.AN94
                        || kind == GenesisViewmodelKind.M249
                        ? recoveredVisual.name
                        : firearmModelName,
                    kind == GenesisViewmodelKind.AWP
                        ? 1.34f
                        : kind == GenesisViewmodelKind.M249 ? 0.94f : 0.86f);
            }
            foreach (var firearmPart in
                instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!firearmPart.transform.IsChildOf(recoveredVisual.transform))
                    firearmPart.enabled = false;
            }
        }
        else
            instance = UnityEngine.Object.Instantiate(
                prefab, framingRoot.transform);
        var animation = instance.GetComponent<Animation>();
        if (animation != null
            && animation.GetClip("Idle01") != null)
        {
            animation.Play("Idle01");
            animation["Idle01"].time = Mathf.Min(
                0.35f, animation["Idle01"].length * 0.25f);
            animation.Sample();
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.72f, 0.72f, 0.72f, 1f);
        var lightObject = new GameObject("Runtime Viewmodel Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.05f;
        light.transform.rotation = Quaternion.Euler(42f, -28f, 0f);

        var sizes = new[]
        {
            new Vector2Int(1280, 720),
            new Vector2Int(1280, 800),
            new Vector2Int(1200, 900),
        };
        foreach (var size in sizes)
        {
            var pose = GenesisViewmodelProfiles.Get(
                kind, size.x / (float)size.y);
            camera.aspect = size.x / (float)size.y;
            framingRoot.transform.localPosition = pose.Position;
            framingRoot.transform.localRotation = Quaternion.Euler(
                pose.EulerAngles);
            framingRoot.transform.localScale = Vector3.one * pose.Scale;
            camera.fieldOfView = pose.FieldOfView;
            camera.nearClipPlane = pose.NearClip;
            if (kind == GenesisViewmodelKind.Shotgun)
            {
                var firearm = instance.GetComponentsInChildren<Transform>(true)
                    .First(item => item.name == firearmModelName);
                var firearmRenderer = firearm.GetComponent<Renderer>();
                var targetCenter = cameraObject.transform.TransformPoint(
                    new Vector3(0.26f, -0.2f, 1.6f));
                framingRoot.transform.position +=
                    targetCenter - firearmRenderer.bounds.center;
            }
            LogRuntimeViewportAudit(
                camera, framingRoot, size, firearmModelName, kind.ToString());
            var renderTexture = new RenderTexture(size.x, size.y, 24);
            var image = new Texture2D(
                size.x, size.y, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                image.ReadPixels(new Rect(0f, 0f, size.x, size.y), 0, 0);
                image.Apply();
                var outputPath = Path.GetFullPath(Path.Combine(
                    Application.dataPath,
                    "../../recovery/" + outputStem + "-"
                        + size.x + "x" + size.y + ".png"));
                File.WriteAllBytes(outputPath, image.EncodeToPNG());
                Debug.Log("[WeaponRecovery] Runtime framing: " + outputPath);
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
        }
    }

    private static void LogRuntimeViewportAudit(
        Camera camera,
        GameObject instance,
        Vector2Int size,
        string firearmModelName,
        string label)
    {
        var model = instance.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item => item.name == firearmModelName);
        var renderers = model == null
            ? Array.Empty<Renderer>()
            : model.GetComponentsInChildren<Renderer>(true)
                .Where(item => item.enabled)
                .ToArray();
        if (renderers.Length == 0)
            throw new InvalidOperationException(
                label + " runtime framing has no enabled firearm renderers.");

        var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        var minimumDepth = float.PositiveInfinity;
        var maximumDepth = float.NegativeInfinity;
        foreach (var renderer in renderers)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            var bounds = filter != null && filter.sharedMesh != null
                ? filter.sharedMesh.bounds
                : renderer.localBounds;
            for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
            for (var z = -1; z <= 1; z += 2)
            {
                var localCorner = bounds.center + Vector3.Scale(
                    bounds.extents, new Vector3(x, y, z));
                var corner = renderer.transform.TransformPoint(localCorner);
                var viewport = camera.WorldToViewportPoint(corner);
                minimumDepth = Mathf.Min(minimumDepth, viewport.z);
                maximumDepth = Mathf.Max(maximumDepth, viewport.z);
                if (viewport.z <= camera.nearClipPlane)
                    continue;
                minimum = Vector2.Min(minimum, viewport);
                maximum = Vector2.Max(maximum, viewport);
            }
        }

        var muzzle = instance.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item => item.name == "Muzzle")
            ?? instance.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == "WeaponMainLocator");
        var muzzleViewport = muzzle == null
            ? new Vector3(-1f, -1f, -1f)
            : camera.WorldToViewportPoint(muzzle.position);
        var occupancy = maximum - minimum;
        Debug.Log(string.Format(
            "[WeaponRecovery] FramingAudit {0} {1}x{2}: firearmMin={3}, "
                + "firearmMax={4}, occupancy={5}, depth=({6:F2}, {7:F2}), "
                + "muzzle={8}",
            label, size.x, size.y, minimum, maximum, occupancy,
            minimumDepth, maximumDepth, muzzleViewport));
        if (minimum.x < -0.02f
            || maximum.x > 1.02f
            || occupancy.x > 0.62f
            || maximum.y > 0.75f
            || muzzleViewport.z <= camera.nearClipPlane
            || muzzleViewport.x < 0f
            || muzzleViewport.x > 1f
            || muzzleViewport.y < 0f
            || muzzleViewport.y > 1f)
        {
            throw new InvalidOperationException(
                label + " runtime framing audit failed at "
                    + size.x + "x" + size.y + ".");
        }
    }

    [MenuItem("Genesis/Weapons/Render M16 Source Model Preview")]
    public static void RenderM16SourceModelPreview()
    {
        RenderPrefabPreview(
            "Assets/Resources/OriginalGame/M16.prefab",
            "recovered-m16-source-model-preview.png",
            new Vector3(-0.85f, 0.18f, -0.85f),
            false);
    }

    [MenuItem("Genesis/Weapons/Render M16 Viewmodel Candidate Preview")]
    public static void RenderM16ViewmodelCandidatePreview()
    {
        RenderPrefabPreview(
            "Assets/Resources/OriginalGame/FirstPerson/"
                + "M16ViewmodelCandidate.prefab",
            "recovered-m16-viewmodel-candidate-preview.png",
            new Vector3(-0.25f, 0.1f, -0.9f),
            true);
    }

    [MenuItem("Genesis/Weapons/Render M16 Runtime Framing")]
    public static void RenderM16RuntimeFraming()
    {
        RenderRuntimeFraming(
            "Assets/Resources/OriginalGame/FirstPerson/"
                + "M16ViewmodelCandidate.prefab",
            "recovered-m16-runtime",
            GenesisViewmodelKind.M16,
            "Recovered_M16_Candidate");
    }

    [MenuItem("Genesis/Weapons/Render AK74M Viewmodel Candidate Preview")]
    public static void RenderAK74MViewmodelCandidatePreview()
    {
        RenderPrefabPreview(
            "Assets/Resources/OriginalGame/FirstPerson/"
                + "AK74MViewmodelCandidate.prefab",
            "recovered-ak74m-viewmodel-candidate-preview.png",
            new Vector3(-0.25f, 0.1f, -0.9f),
            true);
    }

    [MenuItem("Genesis/Weapons/Render AWP Viewmodel Candidate Preview")]
    public static void RenderAWPViewmodelCandidatePreview()
    {
        RenderPrefabPreview(
            "Assets/Resources/OriginalGame/FirstPerson/"
                + "AWPViewmodelCandidate.prefab",
            "recovered-awp-viewmodel-candidate-preview.png",
            new Vector3(-0.25f, 0.1f, -0.9f),
            true);
    }

    [MenuItem("Genesis/Weapons/Render M9 Viewmodel Preview")]
    public static void RenderM9ViewmodelPreview()
    {
        RenderPrefabPreview(
            "Assets/Resources/OriginalGame/FirstPerson/"
                + "M9Viewmodel.prefab",
            "recovered-m9-viewmodel-preview.png",
            new Vector3(-0.22f, 0.08f, -0.82f),
            true);
    }

    [MenuItem("Genesis/Weapons/Render M9 Runtime Framing")]
    public static void RenderM9RuntimeFraming()
    {
        const string prefabPath =
            "Assets/Resources/OriginalGame/FirstPerson/M9Viewmodel.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            throw new InvalidOperationException("Recovered M9 viewmodel is missing.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraObject = new GameObject("Runtime Viewmodel Camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.1f, 0.13f, 1f);
        camera.fieldOfView = 52f;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 3f;

        var instance = UnityEngine.Object.Instantiate(
            prefab, cameraObject.transform);
        instance.transform.localPosition = new Vector3(0.08f, -0.14f, 0.12f);
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one * 0.55f;
        var animation = instance.GetComponent<Animation>();
        if (animation != null && animation.GetClip("Idle01") != null)
        {
            animation.Play("Idle01");
            animation["Idle01"].time = Mathf.Min(
                1.1f, animation["Idle01"].length * 0.85f);
            animation.Sample();
        }

        RenderSettings.ambientMode =
            UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.7f, 0.7f, 0.7f, 1f);
        var lightObject = new GameObject("Runtime Viewmodel Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

        const int width = 960;
        const int height = 600;
        var renderTexture = new RenderTexture(width, height, 24);
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = renderTexture;
            camera.Render();
            RenderTexture.active = renderTexture;
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            image.Apply();
            var outputPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "../../recovery/recovered-m9-runtime-framing.png"));
            File.WriteAllBytes(outputPath, image.EncodeToPNG());
            Debug.Log("[WeaponRecovery] M9 runtime framing: " + outputPath);
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(renderTexture);
        }
    }

    [MenuItem("Genesis/Weapons/Render M9 Source Model Preview")]
    public static void RenderM9SourceModelPreview()
    {
        RenderPrefabPreview(
            "Assets/Resources/OriginalGame/M9.prefab",
            "recovered-m9-source-model-preview.png",
            new Vector3(-0.85f, 0.34f, -0.75f),
            false);
    }

    [MenuItem("Genesis/Weapons/Render Archived Pistol Viewmodel Reference")]
    public static void RenderArchivedPistolViewmodelReference()
    {
        RenderPrefabPreview(
            "Assets/Resources/OriginalGame/FirstPerson/Pistol/Pistol01.prefab",
            "recovered-pistol-viewmodel-reference.png",
            new Vector3(-0.22f, 0.08f, -0.82f),
            true);
    }

    [MenuItem("Genesis/Weapons/Render AN94 Source Model Preview")]
    public static void RenderAN94SourceModelPreview()
    {
        RenderPrefabPreview(
            "Assets/Resources/OriginalGame/AN94.prefab",
            "recovered-an94-source-model-preview.png",
            new Vector3(-0.85f, 0.28f, -0.85f),
            false);
    }

    private static void RenderPrefabPreview(
        string prefabPath,
        string outputName,
        Vector3 cameraOffset,
        bool sampleIdle)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            throw new InvalidOperationException(
                "Recovered preview prefab missing: " + prefabPath);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var instance = UnityEngine.Object.Instantiate(prefab);
        instance.transform.position = Vector3.zero;
        instance.transform.rotation = Quaternion.identity;
        var animation = instance.GetComponent<Animation>();
        if (sampleIdle
            && animation != null
            && animation.GetClip("Idle01") != null)
        {
            animation.Play("Idle01");
            animation.Sample();
        }
        var renderers = instance.GetComponentsInChildren<Renderer>(true)
            .Where(item => item.enabled)
            .ToArray();
        if (renderers.Length == 0)
            throw new InvalidOperationException(
                "Recovered preview prefab has no renderers: " + prefabPath);
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1))
            bounds.Encapsulate(renderer.bounds);

        var cameraObject = new GameObject("Recovered Prefab Preview Camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.1f, 0.13f, 1f);
        camera.fieldOfView = 42f;
        var size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        cameraObject.transform.position = bounds.center
            + cameraOffset * Mathf.Max(0.5f, size);
        cameraObject.transform.LookAt(bounds.center);

        var keyObject = new GameObject("Recovered Prefab Preview Key");
        var key = keyObject.AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.35f;
        keyObject.transform.rotation = Quaternion.Euler(35f, -28f, 0f);
        RenderSettings.ambientLight = new Color(0.35f, 0.38f, 0.43f, 1f);

        const int width = 1600;
        const int height = 900;
        var renderTexture = new RenderTexture(width, height, 24);
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = renderTexture;
            camera.Render();
            RenderTexture.active = renderTexture;
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            image.Apply();
            var outputPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "../../recovery/" + outputName));
            File.WriteAllBytes(outputPath, image.EncodeToPNG());
            Debug.Log("[WeaponRecovery] Prefab preview rendered: " + outputPath);
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(renderTexture);
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static WeaponReport Inspect(WeaponSpec spec)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
        if (prefab == null)
        {
            return new WeaponReport
            {
                name = spec.Name,
                prefabPath = spec.PrefabPath,
                loaded = false,
                requiredForRuntime = spec.RequiredForRuntime,
                passed = false,
                clips = Array.Empty<string>(),
                requiredAnchors = spec.RequiredAnchors,
                missingAnchors = spec.RequiredAnchors,
            };
        }

        var names = new HashSet<string>(
            prefab.GetComponentsInChildren<Transform>(true)
                .Select(item => item.name),
            StringComparer.OrdinalIgnoreCase);
        var missingAnchors = spec.RequiredAnchors
            .Where(anchor => !names.Contains(anchor))
            .ToArray();
        var renderers = prefab.GetComponentsInChildren<Renderer>(true);
        var materials = renderers.SelectMany(item => item.sharedMaterials).ToArray();
        var clips = AnimationUtility.GetAnimationClips(prefab)
            .Where(clip => clip != null)
            .Select(clip => clip.name)
            .Distinct()
            .OrderBy(name => name)
            .ToArray();
        var missingMaterials = materials.Count(material => material == null);
        var validMaterials = materials.Where(material => material != null).ToArray();
        var materialsWithTextures = validMaterials.Count(material =>
            material.GetTexturePropertyNames().Any(property =>
                material.GetTexture(property) != null));
        var textures = validMaterials
            .SelectMany(material => material.GetTexturePropertyNames()
                .Select(material.GetTexture))
            .Where(texture => texture != null)
            .Distinct()
            .ToArray();
        var texturedMaterialCoverage = materials.Length == 0
            ? 0f
            : (float)materialsWithTextures / materials.Length;

        return new WeaponReport
        {
            name = spec.Name,
            prefabPath = spec.PrefabPath,
            loaded = true,
            requiredForRuntime = spec.RequiredForRuntime,
            passed = renderers.Length > 0
                && materials.Length > 0
                && missingMaterials == 0
                && (spec.AllowsColorOnlyMaterials
                    || (textures.Length > 0
                        && texturedMaterialCoverage >= 0.75f))
                // M249 is an authentic static multi-part firearm closure.
                // Its action clips live on the runtime M4 animation root and
                // are composition-audited by RenderM249RuntimeFraming; do not
                // require duplicate clips on the source firearm prefab.
                && (clips.Length > 0 || spec.Name == "M249"
                    || spec.Name == "FAMAS" || spec.Name == "FAMASTP"
                    || spec.Name == "MicroGalilBaxi"
                    || spec.Name == "MicroGalilBaxiTP"
                    || spec.Name == "Gatling" || spec.Name == "GatlingTP"
                    || spec.Name == "AUGA1" || spec.Name == "AUGA1TP"
                    || spec.Name == "AK47Ice" || spec.Name == "AK47IceTP"
                    || spec.Name == "AWMTP"
                    || spec.Name == "HandAxe" || spec.Name == "Nepal"
                    || spec.Name == "NepalTP")
                && missingAnchors.Length == 0,
            rendererCount = renderers.Length,
            materialCount = materials.Length,
            missingMaterialCount = missingMaterials,
            textureCount = textures.Length,
            materialsWithTextureCount = materialsWithTextures,
            texturedMaterialCoverage = texturedMaterialCoverage,
            clipCount = clips.Length,
            clips = clips,
            requiredAnchors = spec.RequiredAnchors,
            missingAnchors = missingAnchors,
        };
    }
}
