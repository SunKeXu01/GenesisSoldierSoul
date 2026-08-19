using System.Linq;
using GenesisSoldierSoul.Catalog;
using NUnit.Framework;
using UnityEngine;

public sealed class GenesisStoreCatalogTests
{
    [Test]
    public void CatalogContainsOnlyUniqueEvidenceBackedResources()
    {
        var entries = GenesisStoreCatalog.Build("m4a1", false);
        Assert.That(entries.Length, Is.EqualTo(17));
        Assert.That(entries.Select(item => item.Id).Distinct().Count(),
            Is.EqualTo(entries.Length));
        foreach (var entry in entries)
        {
            Assert.That(entry.ResourcePath, Is.Not.Empty, entry.Id);
            Assert.That(Resources.Load<GameObject>(entry.ResourcePath),
                Is.Not.Null, entry.Id + " must resolve to a recovered prefab");
        }
    }

    [Test]
    public void OfficialPrimariesAndCandidatesHaveHonestAvailability()
    {
        var entries = GenesisStoreCatalog.Build("shotgun01", false)
            .ToDictionary(item => item.Id);
        Assert.That(entries["shotgun01"].Availability,
            Is.EqualTo(GenesisCatalogAvailability.Equipped));
        Assert.That(entries["m4a1"].Availability,
            Is.EqualTo(GenesisCatalogAvailability.Available));
        Assert.That(entries["m16"].Availability,
            Is.EqualTo(GenesisCatalogAvailability.Available));
        Assert.That(entries["ak74m"].Availability,
            Is.EqualTo(GenesisCatalogAvailability.RecoveryCandidate));
        Assert.That(entries["awp"].Availability,
            Is.EqualTo(GenesisCatalogAvailability.RecoveryCandidate));
        Assert.That(entries["ak74m"].CanEquipPrimary, Is.False);
        Assert.That(entries["awp"].CanEquipPrimary, Is.False);
        Assert.That(entries["an94"].Availability,
            Is.EqualTo(GenesisCatalogAvailability.RecoveryCandidate));
        Assert.That(entries["an94"].CanEquipPrimary, Is.False);
        Assert.That(entries["m249"].Availability,
            Is.EqualTo(GenesisCatalogAvailability.RecoveryCandidate));
        Assert.That(entries["m249"].CanEquipPrimary, Is.False);
        Assert.That(entries["famas"].Availability,
            Is.EqualTo(GenesisCatalogAvailability.RecoveryCandidate));
        Assert.That(entries["famas"].CanEquipPrimary, Is.False);
        Assert.That(entries["microgalil_baxi"].Availability,
            Is.EqualTo(GenesisCatalogAvailability.RecoveryCandidate));
        Assert.That(entries["microgalil_baxi"].CanEquipPrimary, Is.False);
        Assert.That(entries["gatling"].Availability,
            Is.EqualTo(GenesisCatalogAvailability.RecoveryCandidate));
        Assert.That(entries["gatling"].CanEquipPrimary, Is.False);
        Assert.That(entries["auga1"].Availability,
            Is.EqualTo(GenesisCatalogAvailability.RecoveryCandidate));
        Assert.That(entries["auga1"].CanEquipPrimary, Is.False);
        Assert.That(entries["ak47_bingzuan"].Availability,
            Is.EqualTo(GenesisCatalogAvailability.RecoveryCandidate));
        Assert.That(entries["ak47_bingzuan"].CanEquipPrimary, Is.False);
        Assert.That(entries["axe"].Availability,
            Is.EqualTo(GenesisCatalogAvailability.Available));
        Assert.That(entries["axe"].CanEquipPrimary, Is.False);
        Assert.That(entries["nepal"].Availability,
            Is.EqualTo(GenesisCatalogAvailability.Available));
        Assert.That(entries["nepal"].CanEquipPrimary, Is.False);
    }

    [Test]
    public void ValidatedRecoveredRiflesCanBeEquippedWhenEnabled()
    {
        var entries = GenesisStoreCatalog.Build("ak74m", true)
            .ToDictionary(item => item.Id);
        Assert.That(entries["ak74m"].Availability,
            Is.EqualTo(GenesisCatalogAvailability.Equipped));
        Assert.That(entries["awp"].Availability,
            Is.EqualTo(GenesisCatalogAvailability.Available));
        Assert.That(entries["ak74m"].CanEquipPrimary, Is.True);
        Assert.That(entries["awp"].CanEquipPrimary, Is.True);
        Assert.That(entries["an94"].CanEquipPrimary, Is.True);
        Assert.That(entries["m249"].CanEquipPrimary, Is.True);
        Assert.That(entries["famas"].CanEquipPrimary, Is.True);
        Assert.That(entries["microgalil_baxi"].CanEquipPrimary, Is.True);
        Assert.That(entries["gatling"].CanEquipPrimary, Is.True);
        Assert.That(entries["auga1"].CanEquipPrimary, Is.True);
        Assert.That(entries["ak47_bingzuan"].CanEquipPrimary, Is.True);
    }

    [Test]
    public void StandardIssueItemsAreNotMisrepresentedAsPurchases()
    {
        var entries = GenesisStoreCatalog.Build("m4a1", false)
            .ToDictionary(item => item.Id);
        foreach (var id in new[] { "pistol", "knife", "grenade" })
        {
            Assert.That(entries[id].Availability,
                Is.EqualTo(GenesisCatalogAvailability.StandardIssue));
            Assert.That(entries[id].CanEquipPrimary, Is.False);
        }
    }
}
