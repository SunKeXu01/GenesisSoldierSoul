using System;
using System.Linq;
using UnityEngine;

namespace GenesisSoldierSoul.WeaponActions
{
    /// <summary>
    /// Explicit role map for a first-person rig. Recovered legacy Animation
    /// clips bind by transform path, so the archived hierarchy stays intact;
    /// these references separate runtime responsibilities without reparenting
    /// bones or meshes and invalidating those bindings.
    /// </summary>
    public sealed class GenesisViewmodelRigStructure : MonoBehaviour
    {
        [SerializeField] private Transform animationRoot;
        [SerializeField] private Transform armsRoot;
        [SerializeField] private Transform weaponRoot;
        [SerializeField] private Transform effectsRoot;
        [SerializeField] private Transform muzzleAnchor;
        [SerializeField] private Transform rightHandAnchor;
        [SerializeField] private Transform leftHandAnchor;

        public Transform ViewmodelRoot => transform;
        public Transform AnimationRoot => animationRoot;
        public Transform ArmsRoot => armsRoot;
        public Transform WeaponRoot => weaponRoot;
        public Transform EffectsRoot => effectsRoot;
        public Transform MuzzleAnchor => muzzleAnchor;
        public Transform RightHandAnchor => rightHandAnchor;
        public Transform LeftHandAnchor => leftHandAnchor;

        public void Configure(
            Transform animatedHierarchy,
            Transform explicitWeaponRoot = null,
            Transform explicitMuzzleAnchor = null)
        {
            animationRoot = animatedHierarchy;
            var hierarchy = animatedHierarchy == null
                ? Array.Empty<Transform>()
                : animatedHierarchy.GetComponentsInChildren<Transform>(true);
            rightHandAnchor = FindByNames(
                hierarchy, "RightHand", "Bip01 R Hand", "Right");
            leftHandAnchor = FindByNames(
                hierarchy, "LeftHand", "Bip01 L Hand", "Left");
            armsRoot = FindArmsRoot(animatedHierarchy, rightHandAnchor);
            weaponRoot = explicitWeaponRoot ?? FindByNames(
                hierarchy,
                "Recovered_M4A1_Sopmod",
                "Recovered_M16_Candidate",
                "Recovered_AK74M_Candidate",
                "Recovered_AN94_Candidate",
                "Recovered_M249_Candidate",
                "Recovered_AUGA1_Candidate",
                "Recovered_AK47Ice_Candidate",
                "Recovered_AWP_Candidate",
                "Recovered_M9_Candidate",
                "Recovered_Knife_Blade",
                "WeaponMainMesh",
                "GrenadeMesh",
                "MainMesh",
                "WeaponMainLocator");
            muzzleAnchor = explicitMuzzleAnchor ?? FindByNames(
                hierarchy, "Muzzle", "FireLocator");

            effectsRoot = transform.Find("EffectsRoot");
            if (effectsRoot == null)
            {
                var effectsObject = new GameObject("EffectsRoot");
                effectsRoot = effectsObject.transform;
                effectsRoot.SetParent(transform, false);
            }
        }

        private static Transform FindArmsRoot(
            Transform animatedHierarchy,
            Transform hand)
        {
            if (animatedHierarchy == null)
                return null;
            var named = animatedHierarchy
                .GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item =>
                    item.name.IndexOf("arm", StringComparison.OrdinalIgnoreCase)
                        >= 0
                    || item.name.IndexOf("hand", StringComparison.OrdinalIgnoreCase)
                        >= 0);
            if (named != null)
                return named;
            if (hand != null)
                return hand;
            var skinned = animatedHierarchy
                .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault();
            return skinned == null ? null : skinned.transform;
        }

        private static Transform FindByNames(
            Transform[] hierarchy,
            params string[] names)
        {
            foreach (var name in names)
            {
                var match = hierarchy.FirstOrDefault(item => item.name == name);
                if (match != null)
                    return match;
            }
            return null;
        }
    }
}
