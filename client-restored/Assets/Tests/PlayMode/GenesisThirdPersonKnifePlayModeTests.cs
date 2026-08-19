using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class GenesisThirdPersonKnifePlayModeTests
{
    private GameObject instance;

    [TearDown]
    public void TearDown()
    {
        PlayerPrefs.SetString("Genesis.MeleeWeapon", "knife");
        if (instance != null)
            UnityEngine.Object.DestroyImmediate(instance);
    }

    [Test]
    public void KnifeEquipCreatesVisibleBladeAttachedToRightHand()
    {
        var prefab = Resources.Load<GameObject>("OriginalGame/RemotePlayer");
        Assert.That(prefab, Is.Not.Null);
        instance = UnityEngine.Object.Instantiate(prefab);

        var driverType = Type.GetType(
            "GenesisSoldierSoul.Multiplayer.GenesisThirdPersonActionDriver, "
                + "Assembly-CSharp");
        Assert.That(driverType, Is.Not.Null);
        var driver = Activator.CreateInstance(
            driverType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { instance.transform },
            null);
        Assert.That(driver, Is.Not.Null);
        driverType.GetMethod("PlayAction", new[] { typeof(string), typeof(string) })
            .Invoke(driver, new object[] { "knife", "equip" });
        driverType.GetMethod("Apply").Invoke(driver, null);

        var blade = FindDeepChild(instance.transform, "ThirdPerson_knife");
        var hand = FindDeepChild(instance.transform, "Marine_R_Hand");
        Assert.That(blade, Is.Not.Null);
        Assert.That(hand, Is.Not.Null);
        Assert.That(blade.gameObject.activeInHierarchy, Is.True);
        var renderer = blade.GetComponent<Renderer>();
        Assert.That(renderer, Is.Not.Null);
        Assert.That(renderer.enabled, Is.True);
        Assert.That(Vector3.Distance(renderer.bounds.center, hand.position),
            Is.LessThan(0.65f));
        var length = Mathf.Max(renderer.bounds.size.x,
            Mathf.Max(renderer.bounds.size.y, renderer.bounds.size.z));
        Assert.That(length, Is.InRange(0.28f, 0.5f));
    }

    [Test]
    public void RecoveredRiflesUseDistinctThirdPersonModels()
    {
        var prefab = Resources.Load<GameObject>("OriginalGame/RemotePlayer");
        Assert.That(prefab, Is.Not.Null);
        instance = UnityEngine.Object.Instantiate(prefab);

        var driverType = Type.GetType(
            "GenesisSoldierSoul.Multiplayer.GenesisThirdPersonActionDriver, "
                + "Assembly-CSharp");
        Assert.That(driverType, Is.Not.Null);
        var driver = Activator.CreateInstance(
            driverType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { instance.transform },
            null);
        var playAction = driverType.GetMethod(
            "PlayAction", new[] { typeof(string), typeof(string) });
        var apply = driverType.GetMethod("Apply");
        var hand = FindDeepChild(instance.transform, "Marine_R_Hand");
        Assert.That(hand, Is.Not.Null);

        foreach (var weapon in new[]
            { "m16", "ak74m", "awp", "an94", "m249", "famas",
                "microgalil_baxi", "gatling", "auga1",
                "ak47_bingzuan" })
        {
            playAction.Invoke(driver, new object[] { weapon, "equip" });
            apply.Invoke(driver, null);
            var prop = FindDeepChild(
                instance.transform, "ThirdPerson_" + weapon);
            Assert.That(prop, Is.Not.Null, weapon);
            Assert.That(prop.gameObject.activeInHierarchy, Is.True, weapon);
            var renderers = Array.FindAll(
                prop.GetComponentsInChildren<Renderer>(true),
                item => item.enabled);
            Assert.That(renderers, Is.Not.Empty, weapon);
            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            Assert.That(Vector3.Distance(bounds.center, hand.position),
                Is.LessThan(1.2f), weapon);
            var length = Mathf.Max(bounds.size.x,
                Mathf.Max(bounds.size.y, bounds.size.z));
            Assert.That(length, Is.InRange(0.55f, 1.05f), weapon);
        }
    }

    [TestCase("axe", "ThirdPerson_axe")]
    [TestCase("nepal", "ThirdPerson_nepal")]
    public void RecoveredMeleeFollowsAnimatedRightHand(
        string melee, string propName)
    {
        PlayerPrefs.SetString("Genesis.MeleeWeapon", melee);
        var prefab = Resources.Load<GameObject>("OriginalGame/RemotePlayer");
        Assert.That(prefab, Is.Not.Null);
        instance = UnityEngine.Object.Instantiate(prefab);

        var driverType = Type.GetType(
            "GenesisSoldierSoul.Multiplayer.GenesisThirdPersonActionDriver, "
                + "Assembly-CSharp");
        Assert.That(driverType, Is.Not.Null);
        var driver = Activator.CreateInstance(
            driverType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { instance.transform },
            null);
        driverType.GetMethod("PlayAction", new[] { typeof(string), typeof(string) })
            .Invoke(driver, new object[] { "knife", "equip" });
        driverType.GetMethod("Apply").Invoke(driver, null);

        var prop = FindDeepChild(instance.transform, propName);
        var hand = FindDeepChild(instance.transform, "Marine_R_Hand");
        var socket = FindDeepChild(
            instance.transform, "ThirdPersonWeaponSocket");
        Assert.That(prop, Is.Not.Null);
        Assert.That(hand, Is.Not.Null);
        Assert.That(socket, Is.Not.Null);
        Assert.That(prop.gameObject.activeInHierarchy, Is.True);
        Assert.That(Quaternion.Angle(socket.rotation, hand.rotation),
            Is.LessThan(0.5f));

        var renderers = Array.FindAll(
            prop.GetComponentsInChildren<Renderer>(true), item => item.enabled);
        Assert.That(renderers, Is.Not.Empty);
        var bounds = renderers[0].bounds;
        for (var index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);
        Assert.That(Vector3.Distance(bounds.center, hand.position),
            Is.LessThan(0.8f));
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
