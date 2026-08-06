using System;
using System.Collections;
using System.Reflection;
using System.Text;
using GenesisSoldierSoul.WeaponActions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class GenesisMatchControllerPlayModeTests
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private GameObject player;
    private Camera viewCamera;
    private MonoBehaviour match;
    private Type matchType;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        matchType = Type.GetType(
            "GenesisSoldierSoul.Multiplayer.GenesisMatchController, Assembly-CSharp");
        Assert.That(matchType, Is.Not.Null);

        player = new GameObject("PlayMode Match Player");
        var cameraObject = new GameObject("PlayMode View Camera");
        cameraObject.transform.SetParent(player.transform, false);
        viewCamera = cameraObject.AddComponent<Camera>();
        match = player.AddComponent(matchType) as MonoBehaviour;
        Assert.That(match, Is.Not.Null);
        Invoke("Configure", player.transform, viewCamera, null);

        // GenesisMatchController initializes one frame after OnEnable.
        yield return null;
        yield return null;
        Assert.That(Get<GameObject>("rifle"), Is.Not.Null);
        Assert.That(Get<GameObject>("pistol"), Is.Not.Null);
        Assert.That(Get<GameObject>("knife"), Is.Not.Null);
        Assert.That(Get<GameObject>("grenade"), Is.Not.Null);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        PlayerPrefs.DeleteKey("Genesis.PrimaryWeapon");
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (player != null)
            UnityEngine.Object.Destroy(player);
        var visibilityLight = GameObject.Find("Recovered Map Visibility Light");
        if (visibilityLight != null)
            UnityEngine.Object.Destroy(visibilityLight);
        yield return null;
    }

    [UnityTest]
    public IEnumerator RuntimeViewmodelsExposeSeparatedRoleMap()
    {
        foreach (var fieldName in new[] { "rifle", "pistol", "knife", "grenade" })
        {
            var viewmodel = Get<GameObject>(fieldName);
            var structure = viewmodel.GetComponent<GenesisViewmodelRigStructure>();
            Assert.That(structure, Is.Not.Null, fieldName);
            Assert.That(structure.ViewmodelRoot,
                Is.EqualTo(viewmodel.transform), fieldName);
            Assert.That(structure.AnimationRoot, Is.Not.Null, fieldName);
            Assert.That(structure.ArmsRoot, Is.Not.Null, fieldName);
            Assert.That(structure.WeaponRoot, Is.Not.Null, fieldName);
            Assert.That(structure.EffectsRoot, Is.Not.Null, fieldName);
            Assert.That(structure.AnimationRoot.parent,
                Is.EqualTo(viewmodel.transform), fieldName);
        }
        Assert.That(
            Get<GameObject>("rifle")
                .GetComponent<GenesisViewmodelRigStructure>()
                .MuzzleAnchor,
            Is.Not.Null);
        Assert.That(
            Get<GameObject>("pistol")
                .GetComponent<GenesisViewmodelRigStructure>()
                .MuzzleAnchor,
            Is.Not.Null);
        yield return null;
    }

    [UnityTest]
    public IEnumerator ReloadInterruptedByRapidSwitchKeepsNewestViewmodel()
    {
        Set("rifleMagazine", 29);
        Set("rifleReserve", 90);
        Invoke("BeginReload");
        Assert.That(ActionState(),
            Is.EqualTo(GenesisWeaponActionState.Reloading));

        Invoke("SetWeapon", WeaponSlot.Pistol, true);
        yield return new WaitForSeconds(0.03f);
        Invoke("SetWeapon", WeaponSlot.Knife, true);

        // Wait beyond both the switch and the archived M4A1 reload duration.
        yield return new WaitForSeconds(2.35f);
        Assert.That(SelectedWeapon(), Is.EqualTo(WeaponSlot.Knife));
        Assert.That(ActionState(), Is.EqualTo(GenesisWeaponActionState.Ready));
        Assert.That(Get<int>("rifleMagazine"), Is.EqualTo(29));
        Assert.That(Get<GameObject>("rifle").activeSelf, Is.False);
        Assert.That(Get<GameObject>("pistol").activeSelf, Is.False);
        Assert.That(Get<GameObject>("knife").activeSelf, Is.True);
        Assert.That(Get<GameObject>("grenade").activeSelf, Is.False);
    }

    [UnityTest]
    public IEnumerator GrenadeThrowReturnsToPreviousRealViewmodel()
    {
        Invoke("SetWeapon", WeaponSlot.Pistol, false);
        Invoke("SetWeapon", WeaponSlot.Grenade, false);
        Assert.That(SelectedWeapon(), Is.EqualTo(WeaponSlot.Grenade));

        var routine = (IEnumerator)Invoke("ThrowGrenade");
        match.StartCoroutine(routine);
        yield return new WaitForSeconds(0.9f);

        Assert.That(SelectedWeapon(), Is.EqualTo(WeaponSlot.Pistol));
        Assert.That(ActionState(), Is.EqualTo(GenesisWeaponActionState.Ready));
        Assert.That(Get<GameObject>("pistol").activeSelf, Is.True);
        Assert.That(Get<GameObject>("grenade").activeSelf, Is.False);
        Assert.That(Get<int>("grenadeCount"), Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator GrenadeNormalizationSurvivesViewmodelProfileApplication()
    {
        Invoke("SetWeapon", WeaponSlot.Grenade, false);
        yield return null;

        var grenade = Get<GameObject>("grenade");
        var camera = Get<Camera>("weaponCamera");
        Assert.That(grenade.activeSelf, Is.True);
        Assert.That(camera, Is.Not.Null);
        var hasVisibleRenderer = false;
        foreach (var renderer in grenade.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled)
                continue;
            var viewport = camera.WorldToViewportPoint(renderer.bounds.center);
            if (viewport.z > 0f
                && viewport.x >= 0f && viewport.x <= 1f
                && viewport.y >= 0f && viewport.y <= 1f)
            {
                hasVisibleRenderer = true;
                break;
            }
        }
        Assert.That(hasVisibleRenderer, Is.True,
            "Grenade renderers must remain inside the weapon camera viewport.");
    }

    [UnityTest]
    public IEnumerator ShotgunViewmodelRemainsInsideCameraBeforeAndAfterReset()
    {
        UnityEngine.Object.Destroy(player);
        yield return null;
        PlayerPrefs.SetString("Genesis.PrimaryWeapon", "shotgun01");

        player = new GameObject("PlayMode Shotgun Match Player");
        var cameraObject = new GameObject("PlayMode Shotgun View Camera");
        cameraObject.transform.SetParent(player.transform, false);
        viewCamera = cameraObject.AddComponent<Camera>();
        match = player.AddComponent(matchType) as MonoBehaviour;
        Assert.That(match, Is.Not.Null);
        Invoke("Configure", player.transform, viewCamera, null);
        yield return null;
        yield return null;

        AssertActiveViewmodelVisible("initial spawn");
        Invoke("OnConnectionChanged", false);
        AssertAllViewmodelsHidden();
        Invoke("OnConnectionChanged", true);
        yield return null;
        AssertActiveViewmodelVisible("reconnect");
    }

    [UnityTest]
    public IEnumerator DisconnectInvalidatesReloadAndReconnectRestoresControl()
    {
        Set("rifleMagazine", 29);
        Invoke("BeginReload");
        var oldRevision = Get<int>("weaponActionRevision");

        Invoke("OnConnectionChanged", false);
        Assert.That(ActionState(), Is.EqualTo(GenesisWeaponActionState.Ready));
        Assert.That(Get<int>("weaponActionRevision"), Is.GreaterThan(oldRevision));
        Assert.That(FpsMotor().enabled, Is.False);
        AssertAllViewmodelsHidden();

        yield return new WaitForSeconds(2.25f);
        Assert.That(Get<int>("rifleMagazine"), Is.EqualTo(29));

        Invoke("OnConnectionChanged", true);
        Assert.That(FpsMotor().enabled, Is.True);
        Assert.That(SelectedWeapon(), Is.EqualTo(WeaponSlot.Rifle));
        Assert.That(Get<GameObject>("rifle").activeSelf, Is.True);
    }

    [UnityTest]
    public IEnumerator DeathRespawnAndRoundEndClearOldAnimationAndModels()
    {
        Invoke("SetWeapon", WeaponSlot.Pistol, false);
        Set("pistolMagazine", 11);
        Set("pistolReserve", 36);
        Invoke("BeginReload");
        Assert.That(ActionState(),
            Is.EqualTo(GenesisWeaponActionState.Reloading));

        Invoke("OnLocalStateChanged", LocalState(
            health: 0,
            alive: false,
            deaths: 1,
            roundState: "playing",
            serverTime: 1000,
            roundEndsAt: 61000,
            respawnAt: 4000));
        Assert.That(ActionState(), Is.EqualTo(GenesisWeaponActionState.Ready));
        AssertAllViewmodelsHidden();
        Assert.That(Get<Animation>("pistolAnimation").IsPlaying("Reload"),
            Is.False);

        Invoke("OnLocalStateChanged", LocalState(
            health: 100,
            alive: true,
            deaths: 1,
            roundState: "playing",
            serverTime: 5000,
            roundEndsAt: 61000,
            protectedUntil: 7000));
        Assert.That(SelectedWeapon(), Is.EqualTo(WeaponSlot.Pistol));
        Assert.That(Get<GameObject>("rifle").activeSelf, Is.False);
        Assert.That(Get<GameObject>("pistol").activeSelf, Is.True);
        Assert.That(Get<GameObject>("knife").activeSelf, Is.False);
        Assert.That(Get<GameObject>("grenade").activeSelf, Is.False);
        Assert.That(Get<Animation>("pistolAnimation").IsPlaying("Idle01"),
            Is.True);

        Invoke("FinishRound");
        Assert.That(ActionState(), Is.EqualTo(GenesisWeaponActionState.Ready));
        AssertAllViewmodelsHidden();
        yield return null;
    }

    private void AssertAllViewmodelsHidden()
    {
        Assert.That(Get<GameObject>("rifle").activeSelf, Is.False);
        Assert.That(Get<GameObject>("pistol").activeSelf, Is.False);
        Assert.That(Get<GameObject>("knife").activeSelf, Is.False);
        Assert.That(Get<GameObject>("grenade").activeSelf, Is.False);
    }

    private void AssertActiveViewmodelVisible(string phase)
    {
        var rifle = Get<GameObject>("rifle");
        var camera = Get<Camera>("weaponCamera");
        Assert.That(rifle.activeSelf, Is.True, phase);
        Assert.That(camera.enabled, Is.True, phase);
        Assert.That(camera.farClipPlane, Is.LessThanOrEqualTo(3.01f), phase);
        var visibleRenderers = 0;
        var minViewport = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var maxViewport = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        var rendererPositions = new StringBuilder();
        foreach (var renderer in rifle.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;
            var viewport = camera.WorldToViewportPoint(renderer.bounds.center);
            rendererPositions.Append(renderer.name)
                .Append('=')
                .Append(viewport.ToString("F3"))
                .Append(' ');
            var bounds = renderer.bounds;
            foreach (var x in new[] { bounds.min.x, bounds.max.x })
            foreach (var y in new[] { bounds.min.y, bounds.max.y })
            foreach (var z in new[] { bounds.min.z, bounds.max.z })
            {
                var corner = camera.WorldToViewportPoint(new Vector3(x, y, z));
                if (corner.z <= camera.nearClipPlane
                    || corner.z >= camera.farClipPlane)
                    continue;
                minViewport = Vector2.Min(minViewport, corner);
                maxViewport = Vector2.Max(maxViewport, corner);
            }
            if (viewport.z > camera.nearClipPlane
                && viewport.z < camera.farClipPlane
                && viewport.x >= 0f && viewport.x <= 1f
                && viewport.y >= 0f && viewport.y <= 1f)
                visibleRenderers += 1;
        }
        Assert.That(visibleRenderers, Is.GreaterThan(0),
            phase + ": an active Shotgun01 renderer must be inside the weapon camera. "
            + rendererPositions);
        Assert.That(maxViewport.x - minViewport.x, Is.GreaterThan(0.08f),
            phase + ": Shotgun01 must occupy a visible horizontal span. "
            + minViewport + " -> " + maxViewport);
        Assert.That(maxViewport.y - minViewport.y, Is.GreaterThan(0.08f),
            phase + ": Shotgun01 must occupy a visible vertical span. "
            + minViewport + " -> " + maxViewport);
    }

    private static object LocalState(
        int health,
        bool alive,
        int deaths,
        string roundState,
        long serverTime,
        long roundEndsAt,
        long respawnAt = 0,
        long protectedUntil = 0)
    {
        var stateType = Type.GetType(
            "GenesisSoldierSoul.Multiplayer.GenesisLocalState, Assembly-CSharp");
        Assert.That(stateType, Is.Not.Null);
        var state = Activator.CreateInstance(stateType);
        foreach (var value in new[]
        {
            new { Name = "health", Value = (object)health },
            new { Name = "alive", Value = (object)alive },
            new { Name = "deaths", Value = (object)deaths },
            new { Name = "roundState", Value = (object)roundState },
            new { Name = "serverTime", Value = (object)serverTime },
            new { Name = "roundEndsAt", Value = (object)roundEndsAt },
            new { Name = "respawnAt", Value = (object)respawnAt },
            new { Name = "protectedUntil", Value = (object)protectedUntil },
        })
        {
            var field = stateType.GetField(value.Name, InstanceMembers);
            Assert.That(field, Is.Not.Null, value.Name);
            field.SetValue(state, value.Value);
        }
        return state;
    }

    private MonoBehaviour FpsMotor()
    {
        return Get<MonoBehaviour>("fpsMotor");
    }

    private WeaponSlot SelectedWeapon()
    {
        var property = matchType.GetProperty("selectedWeapon", InstanceMembers);
        return (WeaponSlot)property.GetValue(match);
    }

    private GenesisWeaponActionState ActionState()
    {
        var property = matchType.GetProperty("weaponAction", InstanceMembers);
        return (GenesisWeaponActionState)property.GetValue(match);
    }

    private object Invoke(string name, params object[] arguments)
    {
        var method = matchType.GetMethod(name, InstanceMembers);
        Assert.That(method, Is.Not.Null, name);
        return method.Invoke(match, arguments);
    }

    private T Get<T>(string name)
    {
        var field = matchType.GetField(name, InstanceMembers);
        if (field != null)
            return (T)field.GetValue(match);
        var property = matchType.GetProperty(name, InstanceMembers);
        Assert.That(property, Is.Not.Null, name);
        return (T)property.GetValue(match);
    }

    private void Set(string name, object value)
    {
        var field = matchType.GetField(name, InstanceMembers);
        Assert.That(field, Is.Not.Null, name);
        field.SetValue(match, value);
    }
}
