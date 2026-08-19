using GenesisSoldierSoul.WeaponActions;
using NUnit.Framework;
using UnityEngine;

public sealed class GenesisCombatRulesTests
{
    [TestCase("m4a1", 30, 90, 30, 0.1f, 2.1f)]
    [TestCase("m16", 30, 90, 27, 0.085f, 2.1f)]
    [TestCase("an94", 30, 90, 31, 0.107f, 2.51f)]
    [TestCase("m249", 100, 100, 28, 0.08547f, 3.45f)]
    [TestCase("famas", 25, 75, 29, 0.09091f, 2.7f)]
    [TestCase("microgalil_baxi", 35, 105, 18, 0.07502f, 2.23f)]
    [TestCase("gatling", 150, 150, 32, 0.08f, 3.69f)]
    [TestCase("auga1", 30, 60, 27, 0.09671f, 3.96f)]
    [TestCase("ak47_bingzuan", 30, 60, 34, 0.10471f, 2.42f)]
    [TestCase("shotgun01", 8, 32, 10, 0.85f, 2.8f)]
    [TestCase("pistol", 12, 48, 34, 0.25f, 1.45f)]
    public void FormalWeaponsExposeCompleteFiniteProfiles(
        string weapon, int magazine, int reserve, int damage,
        float interval, float reload)
    {
        var profile = GenesisCombatRules.Profile(weapon);
        Assert.That(profile.MagazineSize, Is.EqualTo(magazine));
        Assert.That(profile.StartingReserve, Is.EqualTo(reserve));
        Assert.That(profile.BodyDamage, Is.EqualTo(damage));
        Assert.That(profile.FireInterval, Is.EqualTo(interval).Within(0.0001f));
        Assert.That(profile.ReloadDuration, Is.EqualTo(reload).Within(0.0001f));
        Assert.That(profile.Range, Is.GreaterThan(0f));
    }

    [Test]
    public void MovementAndRecoilIncreaseActualShotCone()
    {
        var still = GenesisCombatRules.SpreadDegrees(
            "m4a1", 0f, 0f, false);
        var moving = GenesisCombatRules.SpreadDegrees(
            "m4a1", 1f, 0f, false);
        var firing = GenesisCombatRules.SpreadDegrees(
            "m4a1", 1f, 1.5f, false);
        Assert.That(moving, Is.GreaterThan(still));
        Assert.That(firing, Is.GreaterThan(moving));

        var direction = GenesisCombatRules.ShotDirection(
            Vector3.forward, Vector3.up, "m4a1", 7, 1f, 1.5f, false);
        Assert.That(direction.magnitude, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(Vector3.Angle(Vector3.forward, direction),
            Is.GreaterThan(0.01f));
        Assert.That(Vector3.Angle(Vector3.forward, direction),
            Is.LessThanOrEqualTo(firing + 0.001f));
    }

    [Test]
    public void AUGA1ScopeReducesBaseSpread()
    {
        var hip = GenesisCombatRules.SpreadDegrees(
            "auga1", 0f, 0f, false);
        var scoped = GenesisCombatRules.SpreadDegrees(
            "auga1", 0f, 0f, true);
        Assert.That(scoped, Is.LessThan(hip));
    }

    [Test]
    public void HeadshotsAndPartialReloadsUseCanonicalRules()
    {
        Assert.That(GenesisCombatRules.Damage("pistol", false), Is.EqualTo(34));
        Assert.That(GenesisCombatRules.Damage("pistol", true), Is.EqualTo(68));
        Assert.That(GenesisCombatRules.Damage("knife", true), Is.EqualTo(50));
        Assert.That(GenesisCombatRules.ReloadTransfer(7, 3, 12), Is.EqualTo(3));
        Assert.That(GenesisCombatRules.ReloadTransfer(0, 48, 12), Is.EqualTo(12));
        Assert.That(GenesisCombatRules.ReloadTransfer(12, 48, 12), Is.Zero);
    }
}
