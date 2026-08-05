using System;
using System.Reflection;
using NUnit.Framework;

public sealed class GenesisMapRotationTests
{
    [Test]
    public void RestoredSevenMapRotationIsCompleteAndWraps()
    {
        var type = Type.GetType(
            "GenesisSoldierSoul.Multiplayer.GenesisMultiplayerBootstrap, "
            + "Assembly-CSharp",
            true);
        var next = type.GetMethod(
            "GetNextPlayableMap",
            BindingFlags.Public | BindingFlags.Static);
        var playable = type.GetMethod(
            "IsPlayableMap",
            BindingFlags.Public | BindingFlags.Static);
        Assert.That(next, Is.Not.Null);
        Assert.That(playable, Is.Not.Null);

        var expected = new[]
        {
            "Pyramid",
            "NewConstructionSite",
            "BiochemicalTown",
            "ClassicConstructionSite",
            "SteelFactory",
            "IceFireMaze",
            "RadiationDistrict",
        };
        for (var index = 0; index < expected.Length; index += 1)
        {
            Assert.That((bool)playable.Invoke(null, new object[] { expected[index] }),
                Is.True, expected[index]);
            var following = expected[(index + 1) % expected.Length];
            Assert.That((string)next.Invoke(null, new object[] { expected[index] }),
                Is.EqualTo(following));
        }
    }
}
