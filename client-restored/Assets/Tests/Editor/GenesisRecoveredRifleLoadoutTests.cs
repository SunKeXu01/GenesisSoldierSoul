using System;
using System.Reflection;
using NUnit.Framework;

public sealed class GenesisRecoveredRifleLoadoutTests
{
    [TestCase("m16", "OriginalGame/FirstPerson/M16ViewmodelCandidate")]
    [TestCase("ak74m", "OriginalGame/FirstPerson/AK74MViewmodelCandidate")]
    [TestCase("awp", "OriginalGame/FirstPerson/AWPViewmodelCandidate")]
    [TestCase("an94", "OriginalGame/FirstPerson/AN94ViewmodelCandidate")]
    [TestCase("m249", "OriginalGame/M249")]
    [TestCase("famas", "OriginalGame/FAMAS")]
    [TestCase("microgalil_baxi", "OriginalGame/MicroGalilBaxi")]
    [TestCase("gatling", "OriginalGame/Gatling")]
    [TestCase("auga1", "OriginalGame/AUGA1")]
    [TestCase("ak47_bingzuan", "OriginalGame/AK47Ice")]
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
