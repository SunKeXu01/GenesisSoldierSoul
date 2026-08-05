using System;
using System.Reflection;
using NUnit.Framework;

public sealed class GenesisRecoveredRifleLoadoutTests
{
    [TestCase("ak74m", "OriginalGame/FirstPerson/AK74MViewmodelCandidate")]
    [TestCase("awp", "OriginalGame/FirstPerson/AWPViewmodelCandidate")]
    public void RecoveredRiflesLoadTheirAuditedDedicatedPrefabs(
        string weapon,
        string expectedPath)
    {
        var type = Type.GetType(
            "GenesisSoldierSoul.Multiplayer.GenesisWeaponLoadout, "
            + "Assembly-CSharp",
            true);
        var method = type.GetMethod(
            "FirstPersonResourcePath",
            BindingFlags.Public | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        Assert.That(
            (string)method.Invoke(null, new object[] { weapon }),
            Is.EqualTo(expectedPath));
    }
}
