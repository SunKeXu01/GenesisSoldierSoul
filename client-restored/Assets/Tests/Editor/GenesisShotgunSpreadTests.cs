using GenesisSoldierSoul.WeaponActions;
using NUnit.Framework;
using UnityEngine;

public sealed class GenesisShotgunSpreadTests
{
    [Test]
    public void RecoveredShotgunUsesEightNormalizedDeterministicPellets()
    {
        var first = GenesisShotgunSpread.Directions(
            Vector3.forward);
        var second = GenesisShotgunSpread.Directions(
            Vector3.forward);

        Assert.That(first, Has.Length.EqualTo(8));
        Assert.That(second, Has.Length.EqualTo(8));
        Assert.That(first[0], Is.EqualTo(Vector3.forward));
        for (var index = 0; index < first.Length; index += 1)
        {
            Assert.That(first[index].magnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(second[index], Is.EqualTo(first[index]));
        }
        Assert.That(Vector3.Angle(first[0], first[1]), Is.GreaterThan(0.5f));
        Assert.That(Vector3.Angle(first[0], first[5]),
            Is.GreaterThan(Vector3.Angle(first[0], first[1])));
    }

    [Test]
    public void VerticalAimStillProducesFiniteSpreadBasis()
    {
        var directions = GenesisShotgunSpread.Directions(
            Vector3.up);

        Assert.That(directions, Has.Length.EqualTo(8));
        foreach (var direction in directions)
        {
            Assert.That(float.IsNaN(direction.x), Is.False);
            Assert.That(float.IsNaN(direction.y), Is.False);
            Assert.That(float.IsNaN(direction.z), Is.False);
        }
    }
}
