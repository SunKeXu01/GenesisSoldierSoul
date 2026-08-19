using System.Collections;
using GenesisSoldierSoul.WeaponActions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class GenesisGatlingBarrelMotorPlayModeTests
{
    [UnityTest]
    public IEnumerator RecoveredBarrelAssemblySpoolsAndRotates()
    {
        var root = new GameObject("GatlingTestRoot");
        var barrel = new GameObject("GatlingBarrelAssembly");
        barrel.transform.SetParent(root.transform, false);
        var motor = root.AddComponent<GenesisGatlingBarrelMotor>();
        Assert.That(motor.Configure(root.transform), Is.True);
        var before = barrel.transform.localRotation;

        motor.SetInput(true);
        yield return null;
        yield return null;

        Assert.That(motor.AngularSpeed, Is.GreaterThan(0f));
        Assert.That(barrel.transform.localRotation, Is.Not.EqualTo(before));
        Object.Destroy(root);
    }
}
