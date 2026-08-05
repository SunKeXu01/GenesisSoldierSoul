using GenesisSoldierSoul.WeaponActions;
using NUnit.Framework;
using UnityEngine;

public sealed class GenesisAimAlignmentTests
{
    [Test]
    public void ShotRayUsesTheExactNormalisedCrosshairDirection()
    {
        var ray = GenesisAimAlignment.ShotRay(
            new Vector3(3f, 2f, -4f),
            new Vector3(0.2f, -0.1f, 2f));

        Assert.That(ray.direction.magnitude, Is.EqualTo(1f).Within(0.00001f));
        Assert.That(
            Vector3.Angle(ray.direction, new Vector3(0.2f, -0.1f, 2f)),
            Is.LessThan(0.001f));
    }

    [Test]
    public void MuzzlePresentationConvergesOnActualHitRay()
    {
        var ray = GenesisAimAlignment.ShotRay(
            new Vector3(0f, 1.7f, 0f),
            new Vector3(0.08f, 0.03f, 1f));
        var muzzle = new Vector3(0.32f, 1.34f, 0.75f);
        const float range = 100f;

        var direction = GenesisAimAlignment.MuzzleDirection(
            muzzle, ray, range);
        var visualPoint = muzzle + direction
            * Vector3.Distance(muzzle, ray.GetPoint(range));

        Assert.That(
            Vector3.Distance(visualPoint, ray.GetPoint(range)),
            Is.LessThan(0.0001f));
    }

    [Test]
    public void ZeroDirectionFallsBackToForwardWithoutNaN()
    {
        var ray = GenesisAimAlignment.ShotRay(Vector3.zero, Vector3.zero);
        var direction = GenesisAimAlignment.MuzzleDirection(
            Vector3.zero, ray, 0f);

        Assert.That(ray.direction, Is.EqualTo(Vector3.forward));
        Assert.That(direction, Is.EqualTo(Vector3.forward));
    }
}
