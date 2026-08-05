using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenesisSoldierSoul.WeaponActions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GenesisViewmodelStructureAudit
{
    [Serializable]
    private sealed class VectorRecord
    {
        public float x;
        public float y;
        public float z;

        public static VectorRecord From(Vector3 value)
        {
            return new VectorRecord { x = value.x, y = value.y, z = value.z };
        }
    }

    [Serializable]
    private sealed class WeaponRecord
    {
        public string name;
        public string prefabPath;
        public string runtimeAssemblyNote;
        public bool loaded;
        public bool passed;
        public VectorRecord originalBoundsCenter;
        public VectorRecord originalBoundsSize;
        public VectorRecord normalizedWeaponScale;
        public VectorRecord finalPositionAt16x9;
        public VectorRecord finalEulerAt16x9;
        public float finalRootScaleAt16x9;
        public float fieldOfViewAt16x9;
        public float nearClipAt16x9;
        public string animationRootPath;
        public string armsRootPath;
        public string weaponRootPath;
        public string effectsAnchorPath;
        public string muzzleAnchorPath;
        public string rightHandAnchorPath;
        public string leftHandAnchorPath;
        public string[] missingRoles;
    }

    [Serializable]
    private sealed class Report
    {
        public string generatedAtUtc;
        public string unityVersion;
        public string coordinateSpace;
        public string normalizationMeaning;
        public int weaponCount;
        public int passedCount;
        public WeaponRecord[] weapons;
    }

    private sealed class Spec
    {
        public string Name;
        public string PrefabPath;
        public GenesisViewmodelKind Kind;
        public string AssemblyNote;
        public string[] WeaponNames;
        public bool RuntimeMuzzle;
        public string MeshPath;
        public float RuntimeWeaponScale;
        public string RuntimeWeaponPath;
    }

    private static readonly Spec[] Specs =
    {
        NewSpec("M4A1", "Assets/Resources/OriginalGame/FirstPerson/M4A1Viewmodel.prefab", GenesisViewmodelKind.M4A1, "Recovered_M4A1_Sopmod"),
        NewSpec("M16", "Assets/Resources/OriginalGame/FirstPerson/AssaultRifle01.prefab", GenesisViewmodelKind.Rifle, "Main", "WeaponMainLocator"),
        NewRuntimeMuzzleSpec("Shotgun01", "Assets/Resources/OriginalGame/FirstPerson/RecoveredClosures/Shotgun01/GameObject/Shotgun01.prefab", GenesisViewmodelKind.Shotgun, "WeaponMainMesh", "WeaponMainLocator"),
        NewSpec("M9", "Assets/Resources/OriginalGame/FirstPerson/M9Viewmodel.prefab", GenesisViewmodelKind.M9, "Recovered_M9_Candidate", "MainMesh"),
        NewSpec("Pistol01", "Assets/Resources/OriginalGame/FirstPerson/Pistol/Pistol01.prefab", GenesisViewmodelKind.Pistol, "MainMesh", "WeaponMainLocator"),
        NewKnifeSpec(),
        NewSpec("Grenade01", "Assets/Resources/OriginalGame/FirstPerson/RecoveredClosures/Grenade01/GameObject/Grenade01.prefab", GenesisViewmodelKind.Grenade, "GrenadeMesh", "WeaponMainLocator"),
    };

    [MenuItem("Genesis/Weapons/Audit Viewmodel Structure And Poses")]
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var records = Specs.Select(Inspect).ToArray();
        var report = new Report
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            unityVersion = Application.unityVersion,
            coordinateSpace = "Prefab-root local space at source root identity; final pose is ViewmodelRoot local space at 16:9.",
            normalizationMeaning = "normalizedWeaponScale is the selected weapon/model transform local scale stored in the recovered prefab; originalBounds includes every enabled renderer before the runtime profile pose.",
            weaponCount = records.Length,
            passedCount = records.Count(item => item.passed),
            weapons = records,
        };
        var outputPath = Path.GetFullPath(Path.Combine(
            Application.dataPath,
            "../../recovery/viewmodel-structure-and-pose-audit.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        File.WriteAllText(outputPath, JsonUtility.ToJson(report, true) + "\n");
        Debug.Log(string.Format(
            "[ViewmodelStructure] {0}/{1} passed. Report: {2}",
            report.passedCount,
            report.weaponCount,
            outputPath));
        if (report.passedCount != report.weaponCount)
            throw new InvalidOperationException(
                "Viewmodel structure/pose audit failed. See " + outputPath);
    }

    private static Spec NewSpec(
        string name,
        string prefabPath,
        GenesisViewmodelKind kind,
        params string[] weaponNames)
    {
        return new Spec
        {
            Name = name,
            PrefabPath = prefabPath,
            Kind = kind,
            WeaponNames = weaponNames,
        };
    }

    private static Spec NewRuntimeSpec(
        string name,
        string prefabPath,
        GenesisViewmodelKind kind,
        string assemblyNote,
        params string[] weaponNames)
    {
        var spec = NewSpec(name, prefabPath, kind, weaponNames);
        spec.AssemblyNote = assemblyNote;
        return spec;
    }

    private static Spec NewRuntimeMuzzleSpec(
        string name,
        string prefabPath,
        GenesisViewmodelKind kind,
        params string[] weaponNames)
    {
        var spec = NewSpec(name, prefabPath, kind, weaponNames);
        spec.RuntimeMuzzle = true;
        spec.AssemblyNote = "Source prefab has no muzzle node; runtime creates Muzzle under ViewmodelRoot at local (0, 0, 1.1).";
        return spec;
    }

    private static Spec NewKnifeSpec()
    {
        var spec = NewRuntimeSpec(
            "Knife01",
            "Assets/Resources/OriginalGame/FirstPerson/Pistol/Pistol01.prefab",
            GenesisViewmodelKind.Knife,
            "Runtime composition: Pistol01 right-arm rig + Assets/Resources/OriginalGame/FirstPerson/Meshes/Knife.asset at local scale 0.92.",
            "Right");
        spec.MeshPath =
            "Assets/Resources/OriginalGame/FirstPerson/Meshes/Knife.asset";
        spec.RuntimeWeaponScale = 0.92f;
        spec.RuntimeWeaponPath =
            "[runtime-composed]/Recovered_Knife_Blade";
        return spec;
    }

    private static WeaponRecord Inspect(Spec spec)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
        var record = new WeaponRecord
        {
            name = spec.Name,
            prefabPath = spec.PrefabPath,
            runtimeAssemblyNote = spec.AssemblyNote ?? string.Empty,
            loaded = prefab != null,
        };
        if (prefab == null)
        {
            record.missingRoles = new[] { "prefab" };
            return record;
        }

        var instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            var transforms = instance.GetComponentsInChildren<Transform>(true);
            var animation = instance.GetComponentInChildren<Animation>(true);
            var rightHand = Find(transforms, "RightHand", "Bip01 R Hand", "Right");
            var leftHand = Find(transforms, "LeftHand", "Bip01 L Hand", "Left");
            var arms = FindArms(instance.transform, rightHand);
            var weapon = Find(transforms, spec.WeaponNames);
            var muzzle = Find(transforms, "Muzzle", "FireLocator");
            var effects = muzzle ?? Find(transforms, "WeaponMainLocator");
            var bounds = CalculateLocalBounds(instance.transform);
            if (!string.IsNullOrEmpty(spec.MeshPath))
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(spec.MeshPath);
                if (mesh != null)
                    bounds = mesh.bounds;
            }
            var pose = GenesisViewmodelProfiles.Get(
                spec.Kind, GenesisViewmodelProfiles.ReferenceAspect);

            var missing = new List<string>();
            if (animation == null)
                missing.Add("animationRoot");
            if (arms == null)
                missing.Add("armsRoot");
            if (weapon == null)
                missing.Add("weaponRoot");
            if (effects == null)
                missing.Add("effectsAnchor");
            if (rightHand == null)
                missing.Add("rightHandAnchor");
            if (!spec.RuntimeMuzzle
                && spec.Kind != GenesisViewmodelKind.Grenade
                && spec.Kind != GenesisViewmodelKind.Knife
                && muzzle == null)
            {
                missing.Add("muzzleAnchor");
            }

            record.originalBoundsCenter = VectorRecord.From(bounds.center);
            record.originalBoundsSize = VectorRecord.From(bounds.size);
            record.normalizedWeaponScale = VectorRecord.From(
                spec.RuntimeWeaponScale > 0f
                    ? Vector3.one * spec.RuntimeWeaponScale
                    : weapon == null ? Vector3.zero : weapon.localScale);
            record.finalPositionAt16x9 = VectorRecord.From(pose.Position);
            record.finalEulerAt16x9 = VectorRecord.From(pose.EulerAngles);
            record.finalRootScaleAt16x9 = pose.Scale;
            record.fieldOfViewAt16x9 = pose.FieldOfView;
            record.nearClipAt16x9 = pose.NearClip;
            record.animationRootPath = PathOf(instance.transform,
                animation == null ? null : animation.transform);
            record.armsRootPath = PathOf(instance.transform, arms);
            record.weaponRootPath = string.IsNullOrEmpty(spec.RuntimeWeaponPath)
                ? PathOf(instance.transform, weapon)
                : spec.RuntimeWeaponPath;
            record.effectsAnchorPath = PathOf(instance.transform, effects);
            record.muzzleAnchorPath = spec.RuntimeMuzzle
                ? "[runtime-generated]/Muzzle@(0,0,1.1)"
                : PathOf(instance.transform, muzzle);
            record.rightHandAnchorPath = PathOf(instance.transform, rightHand);
            record.leftHandAnchorPath = PathOf(instance.transform, leftHand);
            record.missingRoles = missing.ToArray();
            record.passed = missing.Count == 0 && bounds.size.sqrMagnitude > 0f;
            return record;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static Transform Find(Transform[] transforms, params string[] names)
    {
        foreach (var name in names)
        {
            var match = transforms.FirstOrDefault(item => item.name == name);
            if (match != null)
                return match;
        }
        return null;
    }

    private static Transform FindArms(Transform root, Transform hand)
    {
        var named = root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item =>
                item.name.IndexOf("arm", StringComparison.OrdinalIgnoreCase) >= 0
                || item.name.IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0);
        if (named != null)
            return named;
        if (hand != null)
            return hand;
        var skinned = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
        return skinned == null ? null : skinned.transform;
    }

    private static Bounds CalculateLocalBounds(Transform root)
    {
        var hasBounds = false;
        var result = new Bounds();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)
                     .Where(item => item.enabled))
        {
            var world = renderer.bounds;
            foreach (var corner in BoundsCorners(world))
            {
                var local = root.InverseTransformPoint(corner);
                if (!hasBounds)
                {
                    result = new Bounds(local, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    result.Encapsulate(local);
                }
            }
        }
        return result;
    }

    private static IEnumerable<Vector3> BoundsCorners(Bounds bounds)
    {
        var min = bounds.min;
        var max = bounds.max;
        for (var x = 0; x < 2; x += 1)
        for (var y = 0; y < 2; y += 1)
        for (var z = 0; z < 2; z += 1)
            yield return new Vector3(
                x == 0 ? min.x : max.x,
                y == 0 ? min.y : max.y,
                z == 0 ? min.z : max.z);
    }

    private static string PathOf(Transform root, Transform target)
    {
        if (target == null)
            return string.Empty;
        if (target == root)
            return ".";
        var names = new List<string>();
        for (var current = target;
             current != null && current != root;
             current = current.parent)
        {
            names.Add(current.name);
        }
        names.Reverse();
        return string.Join("/", names);
    }
}
