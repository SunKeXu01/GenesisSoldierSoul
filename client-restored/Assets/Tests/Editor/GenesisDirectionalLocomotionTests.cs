using GenesisSoldierSoul.WeaponActions;
using NUnit.Framework;
using UnityEngine;

public sealed class GenesisDirectionalLocomotionTests
{
    [TestCase(-1f, -1f)]
    [TestCase(0f, -1f)]
    [TestCase(1f, -1f)]
    [TestCase(-1f, 0f)]
    [TestCase(1f, 0f)]
    [TestCase(-1f, 1f)]
    [TestCase(0f, 1f)]
    [TestCase(1f, 1f)]
    public void AllEightDirectionsPreserveTheirLocalOctant(float x, float z)
    {
        var blend = GenesisDirectionalLocomotion.LocalBlend(
            Quaternion.identity,
            new Vector3(x, 0f, z),
            0.75f);

        Assert.That(blend.magnitude, Is.EqualTo(0.75f).Within(0.0001f));
        Assert.That(Mathf.Sign(blend.x), Is.EqualTo(Mathf.Sign(x)));
        Assert.That(Mathf.Sign(blend.y), Is.EqualTo(Mathf.Sign(z)));
    }

    [Test]
    public void FacingRotationMapsWorldMotionIntoCharacterSpace()
    {
        var blend = GenesisDirectionalLocomotion.LocalBlend(
            Quaternion.Euler(0f, 90f, 0f),
            Vector3.forward,
            1f);

        Assert.That(blend.x, Is.EqualTo(-1f).Within(0.0001f));
        Assert.That(blend.y, Is.EqualTo(0f).Within(0.0001f));
    }

    [Test]
    public void IdleAndOverspeedInputsRemainSafe()
    {
        Assert.That(
            GenesisDirectionalLocomotion.LocalBlend(
                Quaternion.identity, Vector3.zero, 1f),
            Is.EqualTo(Vector2.zero));
        Assert.That(
            GenesisDirectionalLocomotion.LocalBlend(
                Quaternion.identity, Vector3.forward, 5f).magnitude,
            Is.EqualTo(1f).Within(0.0001f));
    }
}
