using GenesisSoldierSoul.WeaponActions;
using NUnit.Framework;
using UnityEngine;

public sealed class GenesisViewmodelRigStructureTests
{
    [Test]
    public void ConfigureMapsRolesWithoutReparentingArchivedHierarchy()
    {
        var root = new GameObject("ViewmodelRoot_Test");
        var animationRoot = new GameObject("AnimationRoot_Test");
        animationRoot.transform.SetParent(root.transform, false);
        animationRoot.AddComponent<Animation>();
        var rightHand = new GameObject("RightHand");
        rightHand.transform.SetParent(animationRoot.transform, false);
        var leftHand = new GameObject("LeftHand");
        leftHand.transform.SetParent(animationRoot.transform, false);
        var weapon = new GameObject("Recovered_M4A1_Sopmod");
        weapon.transform.SetParent(rightHand.transform, false);
        var muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(weapon.transform, false);

        try
        {
            var structure = root.AddComponent<GenesisViewmodelRigStructure>();
            structure.Configure(animationRoot.transform);

            Assert.That(structure.ViewmodelRoot, Is.EqualTo(root.transform));
            Assert.That(structure.AnimationRoot,
                Is.EqualTo(animationRoot.transform));
            Assert.That(structure.ArmsRoot, Is.Not.Null);
            Assert.That(structure.WeaponRoot, Is.EqualTo(weapon.transform));
            Assert.That(structure.MuzzleAnchor, Is.EqualTo(muzzle.transform));
            Assert.That(structure.RightHandAnchor,
                Is.EqualTo(rightHand.transform));
            Assert.That(structure.LeftHandAnchor,
                Is.EqualTo(leftHand.transform));
            Assert.That(structure.EffectsRoot, Is.Not.Null);
            Assert.That(animationRoot.transform.parent,
                Is.EqualTo(root.transform),
                "Role mapping must not alter legacy Animation binding paths.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
