using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GenesisSoldierSoul.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GenesisAudioEffectsAudit
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("Genesis/Audit Audio And Combat Effects")]
    public static void Audit()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var resources = AuditAudioResources();
        var mappings = AuditRemoteWeaponMappings();
        var movement = AuditMovementAudio();
        var effects = AuditCombatEffects();
        Debug.Log(string.Format(
            "[GenesisAudioEffectsAudit] resources={0}/{0}, mappings={1}/{1}, "
                + "movement={2}, effects={3}/5, deferred={4}",
            resources, mappings, movement, effects,
            typeof(GenesisDeferredOneShotAudio) != null));
        if (resources != RequiredAudio.Count || mappings != 12
            || !movement || effects != 5)
        {
            throw new InvalidOperationException(
                "Audio/effects acceptance gate did not pass completely.");
        }
    }

    private static readonly Dictionary<string, string> RequiredAudio =
        new Dictionary<string, string>
        {
            { "m4-fire", "OriginalGame/Audio/M4A1/fire" },
            { "m4-reload", "OriginalGame/Audio/M4A1/reload" },
            { "m4-deploy", "OriginalGame/Audio/M4A1/deploy" },
            { "m16-fire", "OriginalGame/Audio/M16/fire" },
            { "m16-reload", "OriginalGame/Audio/M16/reload" },
            { "m16-deploy", "OriginalGame/Audio/M16/deploy" },
            { "shotgun-fire", "OriginalGame/Audio/Shotgun01/fire" },
            { "shotgun-reload", "OriginalGame/Audio/Shotgun01/reload" },
            { "shotgun-deploy", "OriginalGame/Audio/Shotgun01/deploy" },
            { "pistol-fire", "music/fire" },
            { "pistol-reload", "music/reload" },
            { "pistol-deploy", "music/deploy" },
            { "knife-slash", "music/slash" },
            { "walk", "music/walk" },
            { "jump", "music/jump" },
            { "land", "music/step" },
            { "hit", "OriginalGame/Audio/Genesis/bullet_hit" },
            { "harmed", "OriginalGame/Audio/Genesis/harmed" },
            { "death", "OriginalGame/Audio/Genesis/die" },
            { "stage-start", "OriginalGame/Audio/CF2/stage_start" },
            { "round-win", "OriginalGame/Audio/CF2/round_win" },
            { "headshot", "OriginalGame/Audio/CF2/headshot" },
            { "double-kill", "OriginalGame/Audio/CF2/double_kill" },
            { "triple-kill", "OriginalGame/Audio/CF2/triple_kill" },
            { "multi-kill", "OriginalGame/Audio/CF2/multi_kill" },
            { "knife-kill", "OriginalGame/Audio/CF2/knife_kill" },
            { "grenade-throw", "OriginalGame/Audio/CF2/Grenade/fire_in_the_hole" },
            { "grenade-explosion", "OriginalGame/Audio/CF2/Grenade/explosion" },
        };

    private static int AuditAudioResources()
    {
        var loaded = 0;
        foreach (var pair in RequiredAudio)
        {
            var clip = Resources.Load<AudioClip>(pair.Value);
            if (clip == null)
                throw new InvalidOperationException(
                    "Missing recovered audio " + pair.Key + ": " + pair.Value);
            if (clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
            Debug.Log(string.Format(
                "[GenesisAudioEffectsAudit] audio={0}, path={1}, state={2}, "
                    + "length={3:F3}, channels={4}, frequency={5}",
                pair.Key, pair.Value, clip.loadState, clip.length,
                clip.channels, clip.frequency));
            loaded += 1;
        }
        return loaded;
    }

    private static int AuditRemoteWeaponMappings()
    {
        var prefab = Resources.Load<GameObject>("OriginalGame/RemotePlayer");
        if (prefab == null)
            throw new InvalidOperationException("RemotePlayer prefab is missing.");
        var instance = UnityEngine.Object.Instantiate(prefab);
        var driverType = typeof(GenesisNetworkClient).Assembly.GetType(
            "GenesisSoldierSoul.Multiplayer.GenesisThirdPersonActionDriver", true);
        var driver = Activator.CreateInstance(
            driverType, InstanceMembers, null,
            new object[] { instance.transform }, null);
        var play = driverType.GetMethod(
            "PlayAction", InstanceMembers, null,
            new[] { typeof(string), typeof(string) }, null);
        var field = driverType.GetField("lastAudioResource", InstanceMembers);
        var expected = new[]
        {
            new[] { "m4a1", "fire", "OriginalGame/Audio/M4A1/fire" },
            new[] { "m4a1", "reload", "OriginalGame/Audio/M4A1/reload" },
            new[] { "m4a1", "equip", "OriginalGame/Audio/M4A1/deploy" },
            new[] { "m16", "fire", "OriginalGame/Audio/M16/fire" },
            new[] { "m16", "reload", "OriginalGame/Audio/M16/reload" },
            new[] { "m16", "equip", "OriginalGame/Audio/M16/deploy" },
            new[] { "shotgun01", "fire", "OriginalGame/Audio/Shotgun01/fire" },
            new[] { "shotgun01", "reload", "OriginalGame/Audio/Shotgun01/reload" },
            new[] { "shotgun01", "equip", "OriginalGame/Audio/Shotgun01/deploy" },
            new[] { "pistol", "fire", "music/fire" },
            new[] { "knife", "fire", "music/slash" },
            new[] { "grenade", "throw", "OriginalGame/Audio/CF2/Grenade/fire_in_the_hole" },
        };
        var passed = 0;
        foreach (var row in expected)
        {
            play.Invoke(driver, new object[] { row[0], row[1] });
            var actual = field.GetValue(driver) as string;
            Debug.Log(string.Format(
                "[GenesisAudioEffectsAudit] remote={0}/{1}, audio={2}",
                row[0], row[1], actual));
            if (actual != row[2])
                throw new InvalidOperationException(
                    "Remote audio mapping mismatch: " + row[0] + "/" + row[1]);
            passed += 1;
        }
        UnityEngine.Object.DestroyImmediate(instance);
        return passed;
    }

    private static bool AuditMovementAudio()
    {
        var player = new GameObject("AudioAuditPlayer");
        player.AddComponent<CharacterController>();
        var motor = player.AddComponent<GenesisFpsMotor>();
        var type = typeof(GenesisFpsMotor);
        type.GetMethod("Awake", InstanceMembers).Invoke(motor, null);
        var walk = type.GetField("walkClip", InstanceMembers).GetValue(motor);
        var jump = type.GetField("jumpClip", InstanceMembers).GetValue(motor);
        var land = type.GetField("landClip", InstanceMembers).GetValue(motor);
        var source = player.GetComponent<AudioSource>();
        var passed = walk != null && jump != null && land != null
            && source != null && !source.playOnAwake;
        Debug.Log("[GenesisAudioEffectsAudit] movementAudio=" + passed);
        UnityEngine.Object.DestroyImmediate(player);
        return passed;
    }

    private static int AuditCombatEffects()
    {
        var root = new GameObject("AudioEffectsAuditMatch");
        var match = root.AddComponent<GenesisMatchController>();
        var type = typeof(GenesisMatchController);
        type.GetMethod("CreateAudio", InstanceMembers).Invoke(match, null);
        var muzzleObject = new GameObject("AuditMuzzle");
        muzzleObject.transform.position = new Vector3(0f, 1f, 0f);
        muzzleObject.transform.rotation = Quaternion.identity;
        type.GetMethod("CreateShellEjection", InstanceMembers).Invoke(
            match, new object[] { muzzleObject.transform, "m4a1" });
        type.GetMethod("CreateImpactEffect", InstanceMembers).Invoke(
            match, new object[] { Vector3.zero, Vector3.up, false });
        type.GetMethod("CreateImpactEffect", InstanceMembers).Invoke(
            match, new object[] { Vector3.right, Vector3.up, true });
        type.GetMethod("CreateExplosionEffect", InstanceMembers).Invoke(
            match, new object[] { Vector3.forward, 5f });
        type.GetMethod("PlayMuzzleFlash", InstanceMembers).Invoke(
            match, new object[] { muzzleObject.transform, Vector3.forward, 100f });

        var names = UnityEngine.Object.FindObjectsOfType<GameObject>()
            .Select(item => item.name).ToArray();
        var gates = new[]
        {
            names.Any(name => name.Contains("_Shell")),
            names.Contains("Recovered_BulletImpact"),
            names.Contains("Recovered_BloodImpact"),
            names.Contains("Recovered_Grenade01_Explosion"),
            names.Any(name => name.StartsWith("Recovered_WarFX_MuzzleFlash")
                || name.StartsWith("Recovered_Procedural_MuzzleFlash")),
        };
        var particles = UnityEngine.Object.FindObjectsOfType<ParticleSystem>();
        Debug.Log(string.Format(
            "[GenesisAudioEffectsAudit] shell={0}, impact={1}, blood={2}, "
                + "explosion={3}, muzzle={4}, particles={5}",
            gates[0], gates[1], gates[2], gates[3], gates[4], particles.Length));
        UnityEngine.Object.DestroyImmediate(root);
        UnityEngine.Object.DestroyImmediate(muzzleObject);
        foreach (var item in UnityEngine.Object.FindObjectsOfType<GameObject>())
        {
            if (item.name.StartsWith("Recovered_"))
                UnityEngine.Object.DestroyImmediate(item);
        }
        return gates.Count(value => value);
    }
}
