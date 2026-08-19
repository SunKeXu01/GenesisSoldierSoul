using System;
using System.Collections.Generic;
using GenesisSoldierSoul.WeaponActions;
using UnityEngine;

namespace GenesisSoldierSoul.Multiplayer
{
    /// <summary>
    /// Applies the recovered locomotion-compatible upper-body combat poses and
    /// attaches real recovered weapon meshes to a third-person character.
    /// </summary>
    internal sealed class GenesisThirdPersonActionDriver
    {
        private readonly Transform visual;
        private readonly Transform leftArm;
        private readonly Transform rightArm;
        private readonly Transform leftForearm;
        private readonly Transform rightForearm;
        private readonly Transform leftHand;
        private readonly Transform rightHand;
        private readonly Transform weaponSocket;
        private readonly Quaternion leftArmRest;
        private readonly Quaternion rightArmRest;
        private readonly Vector3 visualRestPosition;
        private readonly Quaternion visualRestRotation;
        private readonly Dictionary<string, GameObject> weaponProps =
            new Dictionary<string, GameObject>();
        private readonly Dictionary<string, Transform> muzzleAnchors =
            new Dictionary<string, Transform>();
        private readonly AudioSource spatialAudio;
        private readonly GenesisDeferredOneShotAudio deferredAudio;

        private string weapon = "rifle";
        private string action;
        private string lastPresentationEffect;
        private string lastAudioResource;
        private float actionStartedAt = -10f;
        private float actionDuration;
        private float hitStartedAt = -10f;
        private float deathStartedAt = -10f;
        private bool alive = true;
        private Quaternion actionBaseLeft;
        private Quaternion actionBaseRight;
        private bool hasActionBase;

        public bool Alive { get { return alive; } }
        public string Weapon { get { return weapon; } }

        public GenesisThirdPersonActionDriver(Transform presentation)
        {
            visual = presentation;
            leftArm = FindDeepChild(visual, "Marine_L_UpperArm");
            rightArm = FindDeepChild(visual, "Marine_R_UpperArm");
            leftForearm = FindDeepChild(visual, "Marine_L_Forearm");
            rightForearm = FindDeepChild(visual, "Marine_R_Forearm");
            leftHand = FindDeepChild(visual, "Marine_L_Hand");
            rightHand = FindDeepChild(visual, "Marine_R_Hand");
            if (visual != null && rightHand != null)
            {
                var socketObject = new GameObject("ThirdPersonWeaponSocket");
                weaponSocket = socketObject.transform;
                weaponSocket.SetParent(visual, false);
                UpdateWeaponSocket();
            }
            leftArmRest = Rotation(leftArm);
            rightArmRest = Rotation(rightArm);
            visualRestPosition = visual == null
                ? Vector3.zero
                : visual.localPosition;
            visualRestRotation = visual == null
                ? Quaternion.identity
                : visual.localRotation;
            if (visual != null)
            {
                spatialAudio = visual.gameObject.AddComponent<AudioSource>();
                spatialAudio.playOnAwake = false;
                spatialAudio.spatialBlend = 1f;
                spatialAudio.rolloffMode = AudioRolloffMode.Linear;
                spatialAudio.minDistance = 2f;
                spatialAudio.maxDistance = 45f;
                spatialAudio.volume = 0.78f;
                deferredAudio = visual.gameObject
                    .AddComponent<GenesisDeferredOneShotAudio>();
            }
            DisableArchivedWeaponRenderers();
            BuildWeaponProps();
            SetWeapon("rifle");
        }

        public void PlayAction(string requestedWeapon, string requestedAction)
        {
            GenesisCombatActionCommand command;
            if (!GenesisCombatActionSemantics.TryCreate(
                    requestedWeapon, requestedAction, out command))
                return;
            PlayAction(command);
        }

        public void PlayAction(GenesisCombatActionCommand command)
        {
            if (!alive)
                return;
            RestoreActionBase();
            SetWeapon(command.Weapon);
            action = command.State == GenesisWeaponActionState.Melee
                ? "knife"
                : command.Kind == GenesisCombatActionKind.Throw
                    ? "throw"
                    : command.WireAction;
            actionDuration = command.PresentationDuration;
            hasActionBase = false;
            actionStartedAt = Time.time;
            PlayPresentationEffect(command);
        }

        public void PlayHit()
        {
            if (alive)
                hitStartedAt = Time.time;
        }

        public void PlayDeath()
        {
            if (!alive)
                return;
            RestoreActionBase();
            alive = false;
            action = null;
            deathStartedAt = Time.time;
        }

        public void Respawn()
        {
            RestoreActionBase();
            alive = true;
            action = null;
            actionStartedAt = -10f;
            hitStartedAt = -10f;
            deathStartedAt = -10f;
            if (visual != null)
            {
                visual.localPosition = visualRestPosition;
                visual.localRotation = visualRestRotation;
                visual.gameObject.SetActive(true);
            }
            SetWeapon("rifle");
        }

        public void Apply()
        {
            if (visual == null)
                return;
            if (!alive)
            {
                ApplyDeath();
                return;
            }

            visual.localPosition = visualRestPosition;
            visual.localRotation = visualRestRotation;
            ApplyHoldingPose();
            UpdateWeaponSocket();
            ApplyWeaponHandContacts();
            ApplyCombatAction();
            ApplyHitReaction();
        }

        private void DisableArchivedWeaponRenderers()
        {
            if (visual == null)
                return;
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name.StartsWith("AssaultrifleSig",
                        StringComparison.Ordinal)
                    || renderer.name.StartsWith("clipsig",
                        StringComparison.Ordinal)
                    || renderer.name.StartsWith("Bullet",
                        StringComparison.Ordinal))
                {
                    renderer.enabled = false;
                }
            }
        }

        private void SetWeapon(string requested)
        {
            if (requested == "knife"
                && GenesisWeaponLoadout.Melee == GenesisWeaponLoadout.HandAxe)
                requested = "axe";
            else if (requested == "knife"
                && GenesisWeaponLoadout.Melee == GenesisWeaponLoadout.Nepal)
                requested = "nepal";
            if (requested == "pistol" || requested == "knife"
                || requested == "axe" || requested == "nepal"
                || requested == "grenade" || requested == "m16"
                || requested == "ak74m" || requested == "an94"
                || requested == "m249" || requested == "famas"
                || requested == "microgalil_baxi"
                || requested == "gatling"
                || requested == "auga1"
                || requested == "ak47_bingzuan"
                || requested == "awp")
                weapon = requested;
            else if (requested == "shotgun" || requested == "shotgun01")
                weapon = "shotgun";
            else
                weapon = "rifle";
            foreach (var pair in weaponProps)
                pair.Value.SetActive(pair.Key == weapon);
        }

        private void ApplyHoldingPose()
        {
            if (IsRifleWeapon(weapon))
            {
                SetArm(leftArm, leftArmRest, -48f, 34f, 18f);
                SetArm(rightArm, rightArmRest, -55f, -8f, -14f);
            }
            else if (weapon == "shotgun")
            {
                SetArm(leftArm, leftArmRest, -54f, 38f, 22f);
                SetArm(rightArm, rightArmRest, -58f, -10f, -16f);
            }
            else if (weapon == "pistol")
            {
                SetArm(leftArm, leftArmRest, -38f, 20f, 12f);
                SetArm(rightArm, rightArmRest, -62f, -5f, -10f);
            }
            else if (IsMeleeWeapon(weapon))
            {
                SetArm(leftArm, leftArmRest, -12f, 8f, 5f);
                SetArm(rightArm, rightArmRest, -44f, -18f, -26f);
            }
            else
            {
                SetArm(leftArm, leftArmRest, -28f, 18f, 10f);
                SetArm(rightArm, rightArmRest, -52f, -12f, -18f);
            }
        }

        private void ApplyCombatAction()
        {
            if (string.IsNullOrEmpty(action))
                return;
            if (!hasActionBase)
                CaptureActionBase();
            var elapsed = Time.time - actionStartedAt;
            if (elapsed >= actionDuration)
            {
                RestoreActionBase();
                action = null;
                return;
            }
            var normalized = Mathf.Clamp01(elapsed / actionDuration);
            var wave = Mathf.Sin(normalized * Mathf.PI);
            if (action == "reload")
            {
                PoseActionArm(leftArm, actionBaseLeft,
                    -58f * wave, 44f * wave, 22f * wave);
                PoseActionArm(rightArm, actionBaseRight,
                    -22f * wave, -18f * wave, -12f * wave);
            }
            else if (action == "knife")
            {
                PoseActionArm(rightArm, actionBaseRight,
                    -92f * wave, 24f * wave, -48f * wave);
                PoseActionArm(leftArm, actionBaseLeft,
                    -18f * wave, -12f * wave, 8f * wave);
            }
            else if (action == "throw")
            {
                PoseActionArm(rightArm, actionBaseRight,
                    -118f * wave, 12f * wave, -25f * wave);
                PoseActionArm(leftArm, actionBaseLeft,
                    -48f * wave, -18f * wave, 18f * wave);
            }
            else if (action == "equip")
            {
                var lower = wave * 38f;
                PoseActionArm(leftArm, actionBaseLeft,
                    lower, 0f, -lower * 0.3f);
                PoseActionArm(rightArm, actionBaseRight,
                    lower, 0f, lower * 0.3f);
            }
            else
            {
                PoseActionArm(leftArm, actionBaseLeft,
                    8f * wave, 0f, -4f * wave);
                PoseActionArm(rightArm, actionBaseRight,
                    -14f * wave, 0f, 7f * wave);
            }
        }

        private void CaptureActionBase()
        {
            actionBaseLeft = Rotation(leftArm);
            actionBaseRight = Rotation(rightArm);
            hasActionBase = true;
        }

        private void RestoreActionBase()
        {
            if (!hasActionBase)
                return;
            if (leftArm != null)
                leftArm.localRotation = actionBaseLeft;
            if (rightArm != null)
                rightArm.localRotation = actionBaseRight;
            hasActionBase = false;
        }

        private void ApplyHitReaction()
        {
            var elapsed = Time.time - hitStartedAt;
            if (elapsed < 0f || elapsed >= 0.26f)
                return;
            var wave = Mathf.Sin(elapsed / 0.26f * Mathf.PI);
            visual.localRotation = visualRestRotation
                * Quaternion.Euler(-7f * wave, 0f, 11f * wave);
            RotateArm(leftArm, 18f * wave, 0f, -12f * wave);
            RotateArm(rightArm, 12f * wave, 0f, 16f * wave);
        }

        private void ApplyDeath()
        {
            var progress = Mathf.Clamp01(
                (Time.time - deathStartedAt) / 0.72f);
            progress = progress * progress * (3f - 2f * progress);
            visual.localRotation = visualRestRotation
                * Quaternion.Euler(8f * progress, 0f, 86f * progress);
            visual.localPosition = visualRestPosition
                + Vector3.down * (0.44f * progress)
                + Vector3.right * (0.15f * progress);
        }

        private void BuildWeaponProps()
        {
            if (rightHand == null)
                return;
            AddProp("rifle", "OriginalGame/M4A1", null, 0.72f,
                new Vector3(0.02f, 0.02f, 0.38f),
                Quaternion.Euler(0f, 180f, 0f));
            AddProp("m16", "OriginalGame/FirstPerson/M16ViewmodelCandidate",
                new[] { "Recovered_M16_Candidate" }, 0.72f,
                new Vector3(0.02f, 0.02f, 0.38f),
                Quaternion.Euler(0f, 180f, 0f));
            AddProp("ak74m", "OriginalGame/FirstPerson/AK74MViewmodelCandidate",
                new[] { "Recovered_AK74M_Candidate" }, 0.72f,
                new Vector3(0.02f, 0.02f, 0.38f),
                Quaternion.Euler(0f, 180f, 0f));
            AddProp("an94", "OriginalGame/FirstPerson/AN94ViewmodelCandidate",
                new[] { "Recovered_AN94_Candidate" }, 0.74f,
                new Vector3(0.02f, 0.02f, 0.39f),
                Quaternion.Euler(0f, 180f, 0f));
            AddProp("m249", "OriginalGame/M249", null, 0.82f,
                new Vector3(0.025f, 0.015f, 0.43f),
                Quaternion.Euler(90f, 180f, 0f));
            AddProp("famas", "OriginalGame/FAMASTP", null, 0.74f,
                new Vector3(0.02f, 0.02f, 0.39f),
                Quaternion.Euler(0f, 180f, 0f));
            AddProp("microgalil_baxi", "OriginalGame/MicroGalilBaxiTP",
                null, 0.7f,
                new Vector3(0.02f, 0.02f, 0.37f),
                Quaternion.Euler(0f, 180f, 0f));
            AddProp("gatling", "OriginalGame/GatlingTP", null, 0.86f,
                new Vector3(0.025f, 0.015f, 0.43f),
                Quaternion.Euler(90f, 180f, 0f));
            AddProp("auga1", "OriginalGame/AUGA1TP", null, 0.74f,
                new Vector3(0.02f, 0.02f, 0.39f),
                Quaternion.Euler(90f, 180f, 0f));
            AddProp("ak47_bingzuan", "OriginalGame/AK47IceTP", null, 0.74f,
                new Vector3(0.02f, 0.02f, 0.39f),
                Quaternion.Euler(90f, 180f, 0f));
            GameObject gatlingProp;
            if (weaponProps.TryGetValue("gatling", out gatlingProp)
                && gatlingProp != null)
            {
                var motor = gatlingProp.AddComponent<GenesisGatlingBarrelMotor>();
                motor.Configure(gatlingProp.transform);
            }
            AddProp("awp", "OriginalGame/AWMTP",
                null, 0.8f,
                new Vector3(0.02f, 0.02f, 0.42f),
                Quaternion.Euler(0f, 180f, 0f));
            AddProp("shotgun",
                "OriginalGame/FirstPerson/RecoveredClosures/Shotgun01/"
                    + "GameObject/Shotgun01",
                new[] { "WeaponMainMesh", "PumpMesh", "TriggerMesh_0",
                    "InsertMesh", "ReloadMesh_0" }, 0.78f,
                new Vector3(0.02f, 0.015f, 0.4f),
                Quaternion.identity);
            AddProp("pistol", "OriginalGame/FirstPerson/Pistol/Pistol01",
                new[] { "ClipMesh", "HammerMesh", "MainMesh",
                    "LeftSwitchMesh", "RightSwitchMesh", "TopPartMesh",
                    "TriggerMesh" }, 0.25f,
                new Vector3(0f, 0.01f, 0.1f),
                Quaternion.Euler(0f, 180f, 0f));
            AddMeshProp("knife",
                "OriginalGame/FirstPerson/Meshes/Knife",
                "OriginalGame/FirstPerson/Materials/Knife01",
                0.28f,
                new Vector3(0f, 0.01f, 0.15f),
                Quaternion.Euler(-18f, 18f, 92f));
            AddProp("axe", "OriginalGame/HandAxe", null, 0.78f,
                new Vector3(0f, 0.015f, 0.18f),
                Quaternion.Euler(90f, 0f, 0f));
            AddProp("nepal", "OriginalGame/NepalTP", null, 0.82f,
                new Vector3(0f, 0.015f, 0.2f),
                Quaternion.Euler(90f, 0f, 0f));
            AddProp("grenade",
                "OriginalGame/FirstPerson/RecoveredClosures/Grenade01/"
                    + "GameObject/Grenade01",
                new[] { "GrenadeMesh", "RingMesh", "SprintMesh" }, 0.14f,
                new Vector3(0f, 0f, 0.06f),
                Quaternion.Euler(0f, 90f, 90f));
        }

        private void AddProp(
            string id,
            string resourcePath,
            string[] allowedRendererNames,
            float desiredLength,
            Vector3 handOffset,
            Quaternion handRotation)
        {
            var source = Resources.Load<GameObject>(resourcePath);
            if (source == null)
                return;
            var instance = UnityEngine.Object.Instantiate(source, weaponSocket);
            instance.name = "ThirdPerson_" + id;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = handRotation;
            instance.transform.localScale = Vector3.one;
            foreach (var component in instance.GetComponentsInChildren<Animator>(true))
                component.enabled = false;
            foreach (var component in instance.GetComponentsInChildren<Animation>(true))
                component.enabled = false;
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;

            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                renderer.enabled = allowedRendererNames == null
                    || IsRendererAllowed(
                        renderer.transform,
                        instance.transform,
                        allowedRendererNames);
            }
            Bounds bounds;
            if (TryGetBounds(renderers, out bounds))
            {
                var longest = Mathf.Max(
                    bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                if (longest > 0.0001f)
                    instance.transform.localScale *= desiredLength / longest;
                if (TryGetBounds(renderers, out bounds))
                    instance.transform.position += weaponSocket.position - bounds.center;
            }
            instance.transform.localPosition += handOffset;
            weaponProps[id] = instance;
            if (IsRifleWeapon(id) || id == "shotgun" || id == "pistol")
                muzzleAnchors[id] = CreateMuzzleAnchor(instance);
        }

        private void AddMeshProp(
            string id,
            string meshResourcePath,
            string materialResourcePath,
            float desiredLength,
            Vector3 handOffset,
            Quaternion handRotation)
        {
            var mesh = Resources.Load<Mesh>(meshResourcePath);
            var material = Resources.Load<Material>(materialResourcePath);
            if (mesh == null || material == null || weaponSocket == null)
                return;
            var instance = new GameObject(
                "ThirdPerson_" + id,
                typeof(MeshFilter),
                typeof(MeshRenderer));
            instance.transform.SetParent(weaponSocket, false);
            instance.GetComponent<MeshFilter>().sharedMesh = mesh;
            instance.GetComponent<MeshRenderer>().sharedMaterial = material;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = handRotation;
            instance.transform.localScale = Vector3.one;
            var longest = Mathf.Max(
                mesh.bounds.size.x,
                Mathf.Max(mesh.bounds.size.y, mesh.bounds.size.z));
            if (longest > 0.0001f)
                instance.transform.localScale *= desiredLength / longest;
            var renderer = instance.GetComponent<Renderer>();
            instance.transform.position += weaponSocket.position
                - renderer.bounds.center;
            instance.transform.localPosition += handOffset;
            instance.SetActive(false);
            weaponProps[id] = instance;
        }

        private Transform CreateMuzzleAnchor(GameObject prop)
        {
            var recovered = FindDeepChild(prop.transform, "Muzzle")
                ?? FindDeepChild(prop.transform, "FireLocator");
            if (recovered != null)
                return recovered;
            Bounds bounds;
            if (!TryGetBounds(prop.GetComponentsInChildren<Renderer>(true), out bounds))
                return prop.transform;
            var anchor = new GameObject("ThirdPersonMuzzle").transform;
            anchor.SetParent(prop.transform, true);
            var forward = visual == null ? Vector3.forward : visual.forward;
            anchor.position = bounds.center + forward
                * Mathf.Max(bounds.extents.x,
                    Mathf.Max(bounds.extents.y, bounds.extents.z));
            anchor.rotation = Quaternion.LookRotation(forward, Vector3.up);
            return anchor;
        }

        private void PlayPresentationEffect(GenesisCombatActionCommand command)
        {
            lastPresentationEffect = command.Kind.ToString().ToLowerInvariant();
            lastAudioResource = AudioResourceFor(command);
            if (command.Kind == GenesisCombatActionKind.Fire
                && command.PresentationWeapon != "knife")
                PlayMuzzleFlash(weapon);
            if (command.Kind == GenesisCombatActionKind.Fire
                && command.Weapon == "gatling")
            {
                GameObject gatling;
                if (weaponProps.TryGetValue("gatling", out gatling)
                    && gatling != null)
                {
                    var motor = gatling.GetComponent<GenesisGatlingBarrelMotor>();
                    if (motor != null)
                        motor.Pulse(0.32f);
                }
            }
            if (string.IsNullOrEmpty(lastAudioResource) || spatialAudio == null)
                return;
            var clip = Resources.Load<AudioClip>(lastAudioResource);
            if (clip == null)
                return;
            deferredAudio.Play(clip, 1f);
        }

        private void PlayMuzzleFlash(string presentationWeapon)
        {
            Transform muzzle;
            if (!muzzleAnchors.TryGetValue(presentationWeapon, out muzzle)
                || muzzle == null)
                return;
            var flash = new GameObject("RemoteMuzzleFlash");
            flash.transform.position = muzzle.position;
            flash.transform.rotation = muzzle.rotation;
            var particles = flash.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.duration = 0.06f;
            main.loop = false;
            main.startLifetime = 0.055f;
            main.startSpeed = 0.7f;
            main.startSize = presentationWeapon == "shotgun" ? 0.22f : 0.14f;
            main.startColor = new Color(1f, 0.68f, 0.24f, 0.94f);
            var emission = particles.emission;
            emission.rateOverTime = 0f;
            particles.Emit(presentationWeapon == "shotgun" ? 8 : 5);
            var particleRenderer =
                particles.GetComponent<ParticleSystemRenderer>();
            var particleMaterial = Resources.Load<Material>(
                "OriginalGame/Effects/BulletImpact");
            if (particleRenderer != null && particleMaterial != null)
                particleRenderer.material = particleMaterial;
            var light = flash.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = presentationWeapon == "shotgun" ? 3.5f : 2.4f;
            light.intensity = presentationWeapon == "shotgun" ? 2.1f : 1.5f;
            light.color = new Color(1f, 0.58f, 0.2f);
            particles.Play();
            UnityEngine.Object.Destroy(flash, 0.1f);
        }

        private static string AudioResourceFor(GenesisCombatActionCommand command)
        {
            if (command.Kind == GenesisCombatActionKind.Throw)
                return "OriginalGame/Audio/CF2/Grenade/fire_in_the_hole";
            if (command.PresentationWeapon == "pistol")
                return command.Kind == GenesisCombatActionKind.Fire
                    ? "music/fire"
                    : command.Kind == GenesisCombatActionKind.Reload
                        ? "music/reload"
                        : "music/deploy";
            if (command.PresentationWeapon == "knife")
                return command.Kind == GenesisCombatActionKind.Fire
                    ? GenesisWeaponLoadout.Melee == GenesisWeaponLoadout.HandAxe
                        ? "OriginalGame/Audio/HandAxe/slash"
                        : GenesisWeaponLoadout.Melee == GenesisWeaponLoadout.Nepal
                            ? "OriginalGame/Audio/Nepal/slash"
                        : "music/slash"
                    : GenesisWeaponLoadout.Melee == GenesisWeaponLoadout.HandAxe
                        ? "OriginalGame/Audio/HandAxe/deploy"
                        : GenesisWeaponLoadout.Melee == GenesisWeaponLoadout.Nepal
                            ? "OriginalGame/Audio/Nepal/deploy"
                            : string.Empty;
            var family = command.Weapon == "m16"
                ? "M16"
                : command.Weapon == "an94"
                    ? "AN94"
                : command.Weapon == "m249"
                    ? "M249"
                : command.Weapon == "famas"
                    ? "FAMAS"
                : command.Weapon == "microgalil_baxi"
                    ? "MicroGalilBaxi"
                : command.Weapon == "gatling"
                    ? "Gatling"
                : command.Weapon == "auga1"
                    ? "AUGA1"
                : command.Weapon == "ak47_bingzuan"
                    ? "AK47Ice"
                : command.PresentationWeapon == "shotgun"
                    ? "Shotgun01"
                    : "M4A1";
            var actionName = command.Kind == GenesisCombatActionKind.Fire
                ? "fire"
                : command.Kind == GenesisCombatActionKind.Reload
                    ? "reload"
                    : "deploy";
            return "OriginalGame/Audio/" + family + "/" + actionName;
        }

        private static bool IsRifleWeapon(string value)
        {
            return value == "rifle" || value == "m16"
                || value == "ak74m" || value == "an94" || value == "m249"
                || value == "famas" || value == "microgalil_baxi"
                || value == "gatling" || value == "auga1"
                || value == "ak47_bingzuan"
                || value == "awp";
        }

        private static bool IsMeleeWeapon(string value)
        {
            return value == "knife" || value == "axe" || value == "nepal";
        }

        private static bool IsRendererAllowed(
            Transform renderer,
            Transform instanceRoot,
            string[] allowedNames)
        {
            var current = renderer;
            while (current != null)
            {
                if (Array.IndexOf(allowedNames, current.name) >= 0)
                    return true;
                if (current == instanceRoot)
                    break;
                current = current.parent;
            }
            return false;
        }

        private void UpdateWeaponSocket()
        {
            if (weaponSocket == null || rightHand == null || visual == null)
                return;
            weaponSocket.position = rightHand.position;
            // One-handed props must inherit the animated hand orientation so
            // the blade/grenade follows equip and attack poses instead of only
            // hovering at the hand position with a root-space rotation.
            weaponSocket.rotation = IsMeleeWeapon(weapon)
                || weapon == "grenade"
                ? rightHand.rotation
                : visual.parent == null
                    ? visual.rotation
                    : visual.parent.rotation;
        }

        private void ApplyWeaponHandContacts()
        {
            if (leftHand == null || leftForearm == null || leftArm == null
                || rightHand == null || rightForearm == null || rightArm == null)
                return;
            if (weapon != "rifle" && weapon != "shotgun"
                && weapon != "pistol")
                return;
            GameObject prop;
            if (!weaponProps.TryGetValue(weapon, out prop) || prop == null)
                return;
            Bounds bounds;
            if (!TryGetBounds(
                    prop.GetComponentsInChildren<Renderer>(true), out bounds))
                return;
            var rootForward = visual.forward;
            Vector3 rightTarget;
            Vector3 leftTarget;
            if (weapon == "pistol")
            {
                rightTarget = bounds.center + Vector3.down * bounds.extents.y * 0.5f
                    + rootForward * bounds.extents.z * 0.15f;
                leftTarget = bounds.center + Vector3.down * bounds.extents.y * 0.15f
                    - visual.right * bounds.extents.x * 0.4f;
            }
            else
            {
                rightTarget = bounds.center + Vector3.down * bounds.extents.y * 0.55f
                    + rootForward * bounds.extents.z * 0.28f;
                leftTarget = bounds.center + Vector3.down * bounds.extents.y * 0.15f
                    - rootForward * bounds.extents.z * 0.42f;
            }
            SolveHandContact(rightArm, rightForearm, rightHand, rightTarget);
            SolveHandContact(leftArm, leftForearm, leftHand, leftTarget);
        }

        private static void SolveHandContact(
            Transform upperArm,
            Transform forearm,
            Transform hand,
            Vector3 target)
        {
            for (var iteration = 0; iteration < 16; iteration += 1)
            {
                RotateJointToward(forearm, hand, target, 18f);
                RotateJointToward(upperArm, hand, target, 12f);
            }
        }

        private static void RotateJointToward(
            Transform joint,
            Transform end,
            Vector3 target,
            float maximumDegrees)
        {
            var current = end.position - joint.position;
            var desired = target - joint.position;
            if (current.sqrMagnitude <= 0.000001f
                || desired.sqrMagnitude <= 0.000001f)
                return;
            var delta = Quaternion.FromToRotation(current, desired);
            delta = Quaternion.RotateTowards(
                Quaternion.identity, delta, maximumDegrees);
            joint.rotation = delta * joint.rotation;
        }

        private static bool TryGetBounds(Renderer[] renderers, out Bounds bounds)
        {
            bounds = default(Bounds);
            var found = false;
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled)
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
            return found;
        }

        private static void SetArm(
            Transform arm, Quaternion rest, float x, float y, float z)
        {
            if (arm != null)
                arm.localRotation = rest * Quaternion.Euler(x, y, z);
        }

        private static void RotateArm(Transform arm, float x, float y, float z)
        {
            if (arm != null)
                arm.localRotation *= Quaternion.Euler(x, y, z);
        }

        private static void PoseActionArm(
            Transform arm, Quaternion baseRotation, float x, float y, float z)
        {
            if (arm != null)
                arm.localRotation = baseRotation * Quaternion.Euler(x, y, z);
        }

        private static Quaternion Rotation(Transform target)
        {
            return target == null ? Quaternion.identity : target.localRotation;
        }

        private static Transform FindDeepChild(Transform parent, string name)
        {
            if (parent == null)
                return null;
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
}
