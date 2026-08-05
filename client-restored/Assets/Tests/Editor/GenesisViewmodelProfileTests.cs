using GenesisSoldierSoul.WeaponActions;
using NUnit.Framework;

public sealed class GenesisViewmodelProfileTests
{
    [Test]
    public void M4A1UsesDedicatedRifleFraming()
    {
        var m4 = GenesisViewmodelProfiles.Get(
            GenesisViewmodelKind.M4A1,
            GenesisViewmodelProfiles.ReferenceAspect);
        var genericRifle = GenesisViewmodelProfiles.Get(
            GenesisViewmodelKind.Rifle,
            GenesisViewmodelProfiles.ReferenceAspect);

        Assert.That(m4.Position, Is.Not.EqualTo(genericRifle.Position));
        Assert.That(m4.EulerAngles, Is.Not.EqualTo(genericRifle.EulerAngles));
        Assert.That(m4.FieldOfView, Is.LessThan(genericRifle.FieldOfView));
    }

    [Test]
    public void M9UsesDedicatedPistolFraming()
    {
        var m9 = GenesisViewmodelProfiles.Get(
            GenesisViewmodelKind.M9,
            GenesisViewmodelProfiles.ReferenceAspect);
        var fallback = GenesisViewmodelProfiles.Get(
            GenesisViewmodelKind.Pistol,
            GenesisViewmodelProfiles.ReferenceAspect);

        Assert.That(m9.Position, Is.Not.EqualTo(fallback.Position));
        Assert.That(m9.FieldOfView, Is.Not.EqualTo(fallback.FieldOfView));
        Assert.That(m9.NearClip, Is.EqualTo(fallback.NearClip));
    }

    [Test]
    public void RecoveredRiflesUseWeaponSpecificFraming()
    {
        var generic = GenesisViewmodelProfiles.Get(
            GenesisViewmodelKind.Rifle,
            GenesisViewmodelProfiles.ReferenceAspect);
        var ak74m = GenesisViewmodelProfiles.Get(
            GenesisViewmodelKind.AK74M,
            GenesisViewmodelProfiles.ReferenceAspect);
        var awp = GenesisViewmodelProfiles.Get(
            GenesisViewmodelKind.AWP,
            GenesisViewmodelProfiles.ReferenceAspect);

        Assert.That(ak74m.Position, Is.Not.EqualTo(generic.Position));
        Assert.That(awp.Position, Is.Not.EqualTo(generic.Position));
        Assert.That(awp.EulerAngles, Is.Not.EqualTo(ak74m.EulerAngles));
        Assert.That(awp.FieldOfView, Is.LessThan(ak74m.FieldOfView));
    }

    [Test]
    public void NarrowWindowKeepsRightEdgeInsideFrame()
    {
        var wide = GenesisViewmodelProfiles.Get(
            GenesisViewmodelKind.M4A1,
            GenesisViewmodelProfiles.ReferenceAspect);
        var narrow = GenesisViewmodelProfiles.Get(
            GenesisViewmodelKind.M4A1,
            GenesisViewmodelProfiles.MinimumSupportedAspect);

        Assert.That(narrow.Position.x, Is.LessThan(wide.Position.x));
        Assert.That(narrow.Scale, Is.LessThan(wide.Scale));
        Assert.That(narrow.Position.y, Is.EqualTo(wide.Position.y));
    }

    [TestCase(16f / 9f)]
    [TestCase(16f / 10f)]
    [TestCase(4f / 3f)]
    [TestCase(21f / 9f)]
    public void SupportedAspectProfilesRemainSafe(float aspect)
    {
        var pose = GenesisViewmodelProfiles.Get(
            GenesisViewmodelKind.M4A1, aspect);

        Assert.That(pose.Scale, Is.InRange(0.34f, 0.42f));
        Assert.That(pose.NearClip, Is.InRange(0.005f, 0.02f));
        Assert.That(pose.FieldOfView, Is.InRange(45f, 55f));
    }
}
