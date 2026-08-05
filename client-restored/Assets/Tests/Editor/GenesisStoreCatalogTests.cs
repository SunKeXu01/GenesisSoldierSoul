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
        Assert.That(entries.Length, Is.EqualTo(8));
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
