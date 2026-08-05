using System;
using System.IO;
using System.Linq;
using System.Reflection;
using GenesisSoldierSoul.WeaponActions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GenesisThirdPersonRuntimePreview
{
    private const string DriverTypeName =
        "GenesisSoldierSoul.Multiplayer.GenesisThirdPersonActionDriver";

    [MenuItem("Genesis/Audit Third Person Runtime Props")]
    public static void Audit()
    {
        var prefab = Resources.Load<GameObject>("OriginalGame/RemotePlayer");
        if (prefab == null)
            throw new InvalidOperationException("RemotePlayer prefab is missing.");

        var outputDirectory = Path.GetFullPath(Path.Combine(
            Application.dataPath, "../../recovery/third-person-prop-preview"));
        Directory.CreateDirectory(outputDirectory);

        AuditNetworkNormalization(prefab);
        AuditSharedActionSemantics(prefab);
        AuditNetworkPresentationEffects(prefab);
        AuditActionReset(prefab);
        foreach (var weapon in new[]
            { "rifle", "shotgun", "pistol", "knife", "grenade" })
            RenderWeapon(prefab, weapon, outputDirectory);

        Debug.Log("[GenesisThirdPersonPreview] Wrote previews to " + outputDirectory);
    }

    private static void AuditNetworkPresentationEffects(GameObject prefab)
    {
        var instance = UnityEngine.Object.Instantiate(prefab);
        StripPreviewPhysics(instance);
        Normalize(instance.transform);
        var driverType = typeof(
            GenesisSoldierSoul.Multiplayer.GenesisNetworkClient)
            .Assembly.GetType(DriverTypeName, true);
        var driver = Activator.CreateInstance(
            driverType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { instance.transform },
            null);
        var playAction = driverType.GetMethod(
            "PlayAction",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(string), typeof(string) },
            null);
        var effectField = driverType.GetField(
            "lastPresentationEffect", BindingFlags.Instance | BindingFlags.NonPublic);
        var audioField = driverType.GetField(
            "lastAudioResource", BindingFlags.Instance | BindingFlags.NonPublic);
        playAction.Invoke(driver, new object[] { "shotgun01", "fire" });
        var fireEffect = effectField.GetValue(driver) as string;
        var fireAudio = audioField.GetValue(driver) as string;
        var flash = UnityEngine.Object.FindObjectsOfType<ParticleSystem>()
            .FirstOrDefault(candidate => candidate.name == "RemoteMuzzleFlash");
        playAction.Invoke(driver, new object[] { "pistol", "reload" });
        var reloadEffect = effectField.GetValue(driver) as string;
        var reloadAudio = audioField.GetValue(driver) as string;
        var source = instance.GetComponent<AudioSource>();
        var hasFlash = flash != null;
        var spatialBlend = source == null ? -1f : source.spatialBlend;
        Debug.Log(string.Format(
            "[GenesisThirdPersonPreview] remoteEffects=({0},{1}), "
                + "audio=({2},{3}), flash={4}, spatial={5:F1}",
            fireEffect, reloadEffect, fireAudio, reloadAudio,
            hasFlash, spatialBlend));
        if (flash != null)
            UnityEngine.Object.DestroyImmediate(flash.gameObject);
        UnityEngine.Object.DestroyImmediate(instance);
        if (fireEffect != "fire" || reloadEffect != "reload"
            || fireAudio != "OriginalGame/Audio/Shotgun01/fire"
            || reloadAudio != "music/reload" || !hasFlash
            || spatialBlend < 0.99f)
        {
            throw new InvalidOperationException(
                "Remote server action did not drive matching weapon effects/audio.");
        }
    }

    private static void AuditSharedActionSemantics(GameObject prefab)
    {
        GenesisCombatActionCommand localMirror;
        GenesisCombatActionCommand remoteReplica;
        GenesisCombatActionCommand trainingBot;
        if (!GenesisCombatActionSemantics.TryFromState(
                "shotgun01", GenesisWeaponActionState.Firing, out localMirror)
            || !GenesisCombatActionSemantics.TryCreate(
                "shotgun01", "fire", out remoteReplica)
            || !GenesisCombatActionSemantics.TryCreate(
                "shotgun01", "fire", out trainingBot)
            || !localMirror.Equals(remoteReplica)
            || !localMirror.Equals(trainingBot))
        {
            throw new InvalidOperationException(
                "Local, remote and training action semantics diverged.");
        }

        var instance = UnityEngine.Object.Instantiate(prefab);
        StripPreviewPhysics(instance);
        Normalize(instance.transform);
        var driverType = typeof(
            GenesisSoldierSoul.Multiplayer.GenesisNetworkClient)
            .Assembly.GetType(DriverTypeName, true);
        var driver = Activator.CreateInstance(
            driverType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { instance.transform },
            null);
        var playAction = driverType.GetMethod(
            "PlayAction",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(string), typeof(string) },
            null);
        var weaponField = driverType.GetField(
            "weapon", BindingFlags.Instance | BindingFlags.NonPublic);
        var actionField = driverType.GetField(
            "action", BindingFlags.Instance | BindingFlags.NonPublic);
        playAction.Invoke(driver, new object[] { "shotgun01", "fire" });
        var shotgunWeapon = weaponField.GetValue(driver) as string;
        var shotgunAction = actionField.GetValue(driver) as string;
        playAction.Invoke(driver, new object[] { "grenade", "equip" });
        var grenadeEquip = actionField.GetValue(driver) as string;
        playAction.Invoke(driver, new object[] { "grenade", "throw" });
        var grenadeThrow = actionField.GetValue(driver) as string;
        UnityEngine.Object.DestroyImmediate(instance);

        Debug.Log(string.Format(
            "[GenesisThirdPersonPreview] semanticParity={0}, "
                + "shotgun=({1},{2}), grenadeEquip={3}, grenadeThrow={4}",
            true, shotgunWeapon, shotgunAction, grenadeEquip, grenadeThrow));
        if (shotgunWeapon != "shotgun" || shotgunAction != "fire"
            || grenadeEquip != "equip" || grenadeThrow != "throw")
        {
            throw new InvalidOperationException(
                "Third-person driver did not preserve canonical semantics.");
        }
    }

    private static void AuditNetworkNormalization(GameObject prefab)
    {
        var anchor = new GameObject("NonZeroRemoteAnchor");
        anchor.transform.position = new Vector3(11f, 5f, -7f);
        var instance = UnityEngine.Object.Instantiate(prefab, anchor.transform);
        var normalize = typeof(
            GenesisSoldierSoul.Multiplayer.GenesisNetworkClient).GetMethod(
                "NormalizeRemotePresentation",
                BindingFlags.Static | BindingFlags.NonPublic);
        if (normalize == null)
            throw new MissingMethodException("NormalizeRemotePresentation");
        normalize.Invoke(null, new object[] { instance.transform, anchor.transform });
        var bounds = BoundsOf(instance);
        var footError = Mathf.Abs(bounds.min.y - anchor.transform.position.y);
        Debug.Log(string.Format(
            "[GenesisThirdPersonPreview] nonZeroAnchor={0}, feetY={1:F4}, "
                + "footError={2:F4}",
            anchor.transform.position, bounds.min.y, footError));
        UnityEngine.Object.DestroyImmediate(anchor);
        if (footError > 0.02f)
            throw new InvalidOperationException(
                "Remote player feet do not align with a non-zero anchor.");
    }

    private static void AuditActionReset(GameObject prefab)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var instance = UnityEngine.Object.Instantiate(prefab);
        StripPreviewPhysics(instance);
        Normalize(instance.transform);
        var animator = instance.GetComponentInChildren<Animator>(true);
        var controller = Resources.Load<RuntimeAnimatorController>(
            "OriginalGame/Character/RemotePlayer");
        if (animator != null && controller != null)
        {
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.Rebind();
            animator.SetBool("Grounded", true);
            animator.Update(0.5f);
        }

        var leftArm = FindDeepChild(instance.transform, "Marine_L_UpperArm");
        var rightArm = FindDeepChild(instance.transform, "Marine_R_UpperArm");
        if (leftArm == null || rightArm == null)
            throw new InvalidOperationException("Remote action arm bones are missing.");
        var leftBase = leftArm.localRotation;
        var rightBase = rightArm.localRotation;
        var driverType = typeof(
            GenesisSoldierSoul.Multiplayer.GenesisNetworkClient)
            .Assembly.GetType(DriverTypeName, true);
        var driver = Activator.CreateInstance(
            driverType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { instance.transform },
            null);
        var playAction = driverType.GetMethod(
            "PlayAction",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(string), typeof(string) },
            null);
        var apply = driverType.GetMethod(
            "Apply",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        apply.Invoke(driver, null);
        leftBase = leftArm.localRotation;
        rightBase = rightArm.localRotation;
        var actionStartedAt = driverType.GetField(
            "actionStartedAt", BindingFlags.Instance | BindingFlags.NonPublic);
        playAction.Invoke(driver, new object[] { "rifle", "fire" });
        actionStartedAt.SetValue(driver, Time.time - 0.09f);
        apply.Invoke(driver, null);
        var changed = Quaternion.Angle(leftBase, leftArm.localRotation) > 0.1f
            || Quaternion.Angle(rightBase, rightArm.localRotation) > 0.1f;
        actionStartedAt.SetValue(driver, Time.time - 1f);
        apply.Invoke(driver, null);
        var leftError = Quaternion.Angle(leftBase, leftArm.localRotation);
        var rightError = Quaternion.Angle(rightBase, rightArm.localRotation);
        var playDeath = driverType.GetMethod(
            "PlayDeath",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var respawn = driverType.GetMethod(
            "Respawn",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var visual = instance.transform;
        var visualBasePosition = visual.localPosition;
        var visualBaseRotation = visual.localRotation;
        playAction.Invoke(driver, new object[] { "rifle", "reload" });
        playDeath.Invoke(driver, null);
        apply.Invoke(driver, null);
        respawn.Invoke(driver, null);
        apply.Invoke(driver, null);
        var respawnPositionError = Vector3.Distance(
            visualBasePosition, visual.localPosition);
        var respawnRotationError = Quaternion.Angle(
            visualBaseRotation, visual.localRotation);
        var respawnLeftError = Quaternion.Angle(
            leftBase, leftArm.localRotation);
        var respawnRightError = Quaternion.Angle(
            rightBase, rightArm.localRotation);
        Debug.Log(string.Format(
            "[GenesisThirdPersonPreview] actionChanged={0}, "
                + "actionResetError=({1:F4},{2:F4}), "
                + "respawnResetError=({3:F4},{4:F4},{5:F4},{6:F4})",
            changed, leftError, rightError, respawnPositionError,
            respawnRotationError, respawnLeftError, respawnRightError));
        UnityEngine.Object.DestroyImmediate(instance);
        if (!changed || leftError > 0.02f || rightError > 0.02f
            || respawnPositionError > 0.001f || respawnRotationError > 0.02f
            || respawnLeftError > 0.02f || respawnRightError > 0.02f)
            throw new InvalidOperationException(
                "Third-person action/death state did not restore its base pose.");
    }

    private static void RenderWeapon(
        GameObject prefab, string weapon, string outputDirectory)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var instance = UnityEngine.Object.Instantiate(prefab);
        instance.name = "PreviewRemotePlayer";
        StripPreviewPhysics(instance);
        Normalize(instance.transform);

        var animator = instance.GetComponentInChildren<Animator>(true);
        var controller = Resources.Load<RuntimeAnimatorController>(
            "OriginalGame/Character/RemotePlayer");
        if (animator != null && controller != null)
        {
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.Rebind();
            animator.Update(0.5f);
        }

        var driverType = typeof(
            GenesisSoldierSoul.Multiplayer.GenesisNetworkClient)
            .Assembly.GetType(DriverTypeName, true);
        var driver = Activator.CreateInstance(
            driverType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { instance.transform },
            null);
        driverType.GetMethod(
            "PlayAction",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(string), typeof(string) },
            null)
            .Invoke(driver, new object[] { weapon, "equip" });
        if (animator != null)
            animator.Update(0.5f);
        driverType.GetMethod(
            "Apply",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Invoke(driver, null);

        var prop = FindDeepChild(instance.transform, "ThirdPerson_" + weapon);
        if (prop == null)
            throw new InvalidOperationException("Missing runtime prop: " + weapon);
        var propBounds = BoundsOf(prop.gameObject);
        var characterBounds = BoundsOf(instance);
        var rightHand = FindDeepChild(instance.transform, "Marine_R_Hand");
        var leftHand = FindDeepChild(instance.transform, "Marine_L_Hand");
        var spine = FindDeepChild(instance.transform, "Marine_Spine2");
        var rightContactDistance = rightHand == null
            ? float.PositiveInfinity
            : Mathf.Sqrt(propBounds.SqrDistance(rightHand.position));
        var leftContactDistance = leftHand == null
            ? float.PositiveInfinity
            : Mathf.Sqrt(propBounds.SqrDistance(leftHand.position));
        var torsoClearance = spine == null
            ? float.PositiveInfinity
            : Mathf.Sqrt(propBounds.SqrDistance(spine.position));
        Debug.Log(string.Format(
            "[GenesisThirdPersonPreview] weapon={0}, active={1}, localPosition={2}, "
                + "localRotation={3}, lossyScale={4}, propBounds={5}, "
                + "characterBounds={6}, rightContact={7:F4}, "
                + "leftContact={8:F4}, torsoClearance={9:F4}",
            weapon, prop.gameObject.activeInHierarchy, prop.localPosition,
            prop.localEulerAngles, prop.lossyScale, propBounds, characterBounds,
            rightContactDistance, leftContactDistance, torsoClearance));
        var requiresSupportHand = weapon == "rifle"
            || weapon == "shotgun"
            || weapon == "pistol";
        if (!prop.gameObject.activeInHierarchy
            || rightContactDistance > 0.04f
            || (requiresSupportHand && leftContactDistance > 0.045f)
            || ((weapon == "rifle" || weapon == "shotgun")
                && torsoClearance < 0.07f))
        {
            throw new InvalidOperationException(
                "Third-person contact/penetration gate failed for " + weapon);
        }
        foreach (var renderer in prop.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null)
                    continue;
                Debug.Log(string.Format(
                    "[GenesisThirdPersonPreview] weapon={0}, renderer={1}, material={2}, "
                        + "shader={3}, color={4}, metallic={5}, glossiness={6}",
                    weapon, renderer.name, material.name,
                    material.shader == null ? "missing" : material.shader.name,
                    material.HasProperty("_Color") ? material.color : Color.clear,
                    material.HasProperty("_Metallic")
                        ? material.GetFloat("_Metallic") : -1f,
                    material.HasProperty("_Glossiness")
                        ? material.GetFloat("_Glossiness") : -1f));
            }
        }

        var cameraObject = new GameObject("PreviewCamera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.16f, 0.18f, 0.2f, 1f);
        camera.fieldOfView = 36f;
        camera.transform.position = new Vector3(0f, 1.15f, 4.6f);
        camera.transform.LookAt(new Vector3(0f, 1f, 0f));

        var keyObject = new GameObject("KeyLight");
        var key = keyObject.AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.15f;
        key.transform.rotation = Quaternion.Euler(36f, 150f, 0f);
        var fillObject = new GameObject("FillLight");
        var fill = fillObject.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.55f;
        fill.transform.rotation = Quaternion.Euler(20f, -35f, 0f);

        WritePreview(camera, Path.Combine(outputDirectory, weapon + "-front.png"));
        camera.transform.position = new Vector3(4.6f, 1.15f, 0f);
        camera.transform.LookAt(new Vector3(0f, 1f, 0f));
        WritePreview(camera, Path.Combine(outputDirectory, weapon + "-side.png"));
    }

    private static void WritePreview(Camera camera, string outputPath)
    {
        var texture = new RenderTexture(768, 768, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = texture;
        camera.Render();
        RenderTexture.active = texture;
        var image = new Texture2D(768, 768, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, 768, 768), 0, 0);
        image.Apply();
        File.WriteAllBytes(outputPath, image.EncodeToPNG());
        RenderTexture.active = null;
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(texture);
    }

    private static void Normalize(Transform presentation)
    {
        const float targetHeight = 1.9f;
        var renderers = presentation.GetComponentsInChildren<Renderer>(true);
        var bounds = BoundsOf(renderers);
        if (bounds.size.y > 0.01f)
            presentation.localScale *= targetHeight / bounds.size.y;
        bounds = BoundsOf(renderers);
        presentation.position += Vector3.up * (targetHeight * 0.5f) - bounds.center;
    }

    private static void StripPreviewPhysics(GameObject root)
    {
        foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
            UnityEngine.Object.DestroyImmediate(body);
        foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            UnityEngine.Object.DestroyImmediate(collider);
        foreach (var light in root.GetComponentsInChildren<Light>(true))
            UnityEngine.Object.DestroyImmediate(light);
    }

    private static Bounds BoundsOf(GameObject root)
    {
        return BoundsOf(root.GetComponentsInChildren<Renderer>(true));
    }

    private static Bounds BoundsOf(Renderer[] renderers)
    {
        var found = false;
        var bounds = default(Bounds);
        foreach (var renderer in renderers)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        return bounds;
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
                return child;
            var nested = FindDeepChild(child, name);
            if (nested != null)
                return nested;
        }
        return null;
    }
}
