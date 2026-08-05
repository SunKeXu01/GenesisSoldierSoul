#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class GenesisCharacterAnimationAudit
{
    private const string ControllerPath =
        "Assets/Resources/OriginalGame/Character/RemotePlayer.controller";
    private const string PrefabPath =
        "Assets/Resources/OriginalGame/RemotePlayer.prefab";

    private static readonly string[] ExpectedLocomotionClips =
    {
        "StandIdleOneHand",
        "WalkForward",
        "Run",
        "WalkBackwardOneHand",
        "RunBackwardOneHand",
        "WalkStrafeLeftOneHand",
        "RunStrafeLeftOneHand",
        "WalkStrafeRightOneHand",
        "RunStrafeRightOneHand"
    };

    private static readonly string[] RequiredCombatBones =
    {
        "Marine_L_UpperArm",
        "Marine_R_UpperArm",
        "Marine_R_Hand"
    };

    private static readonly string[] RequiredWeaponPrefabs =
    {
        "Assets/Resources/OriginalGame/M4A1.prefab",
        "Assets/Resources/OriginalGame/M9.prefab",
        "Assets/Resources/OriginalGame/FirstPerson/RecoveredClosures/"
            + "Shotgun01/GameObject/Shotgun01.prefab",
        "Assets/Resources/OriginalGame/FirstPerson/Knife/Knife01.prefab",
        "Assets/Resources/OriginalGame/FirstPerson/RecoveredClosures/"
            + "Grenade01/GameObject/Grenade01.prefab"
    };

    [Serializable]
    private sealed class CharacterAnimationReport
    {
        public string generatedAtUtc;
        public string unityVersion;
        public bool passed;
        public bool prefabLoaded;
        public bool humanoidAnimatorFound;
        public bool controllerLoaded;
        public bool usesDirectional2DBlend;
        public bool jumpStateFound;
        public bool landStateFound;
        public bool jumpTransitionsToLand;
        public bool landTransitionsToLocomotion;
        public bool runtimeEightDirectionSamplesPassed;
        public bool runtimeJumpSamplePassed;
        public bool runtimeLandSamplePassed;
        public string[] runtimeDirectionSamples;
        public string[] parameters;
        public string[] locomotionClips;
        public string[] missingClips;
        public string[] combatBones;
        public string[] missingCombatBones;
        public string[] combatWeaponPrefabs;
        public string[] missingCombatWeaponPrefabs;
    }

    [MenuItem("Genesis/Characters/Validate Recovered Locomotion")]
    public static void ValidateRecoveredLocomotion()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var animator = prefab == null
            ? null
            : prefab.GetComponentInChildren<Animator>(true);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
            ControllerPath);
        var parameters = controller == null
            ? Array.Empty<string>()
            : controller.parameters.Select(item => item.name).ToArray();

        BlendTree locomotionTree = null;
        var jumpStateFound = false;
        AnimatorState locomotionState = null;
        AnimatorState jumpState = null;
        AnimatorState landState = null;
        if (controller != null && controller.layers.Length > 0)
        {
            foreach (var state in controller.layers[0].stateMachine.states)
            {
                if (state.state.name == "Locomotion")
                {
                    locomotionState = state.state;
                    locomotionTree = state.state.motion as BlendTree;
                }
                if (state.state.name == "Jump"
                    && state.state.motion is AnimationClip)
                {
                    jumpState = state.state;
                    jumpStateFound = true;
                }
                if (state.state.name == "Land"
                    && state.state.motion is AnimationClip)
                    landState = state.state;
            }
        }
        var jumpTransitionsToLand = jumpState != null
            && landState != null
            && jumpState.transitions.Any(transition =>
                transition.destinationState == landState
                && transition.conditions.Any(condition =>
                    condition.parameter == "Grounded"
                    && condition.mode == AnimatorConditionMode.If));
        var landTransitionsToLocomotion = landState != null
            && locomotionState != null
            && landState.transitions.Any(transition =>
                transition.destinationState == locomotionState
                && transition.hasExitTime);

        var clipNames = new List<string>();
        CollectClipNames(locomotionTree, clipNames);
        var missingClips = ExpectedLocomotionClips
            .Where(expected => !clipNames.Contains(expected))
            .ToArray();
        var directionalBlend = locomotionTree != null
            && locomotionTree.blendType == BlendTreeType.FreeformCartesian2D
            && locomotionTree.blendParameter == "MoveX"
            && locomotionTree.blendParameterY == "MoveZ";
        var requiredParameters = new[] { "MoveX", "MoveZ", "Grounded" };
        var transforms = prefab == null
            ? Array.Empty<Transform>()
            : prefab.GetComponentsInChildren<Transform>(true);
        var combatBones = RequiredCombatBones
            .Where(required => transforms.Any(item => item.name == required))
            .ToArray();
        var missingCombatBones = RequiredCombatBones
            .Except(combatBones)
            .ToArray();
        var combatWeaponPrefabs = RequiredWeaponPrefabs
            .Where(path => AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            .ToArray();
        var missingCombatWeaponPrefabs = RequiredWeaponPrefabs
            .Except(combatWeaponPrefabs)
            .ToArray();
        string[] runtimeDirectionSamples;
        bool runtimeEightDirectionSamplesPassed;
        bool runtimeJumpSamplePassed;
        bool runtimeLandSamplePassed;
        SampleRuntimeLocomotion(
            prefab,
            controller,
            out runtimeDirectionSamples,
            out runtimeEightDirectionSamplesPassed,
            out runtimeJumpSamplePassed,
            out runtimeLandSamplePassed);
        var passed = prefab != null
            && animator != null
            && controller != null
            && directionalBlend
            && jumpStateFound
            && landState != null
            && jumpTransitionsToLand
            && landTransitionsToLocomotion
            && runtimeEightDirectionSamplesPassed
            && runtimeJumpSamplePassed
            && runtimeLandSamplePassed
            && missingClips.Length == 0
            && missingCombatBones.Length == 0
            && missingCombatWeaponPrefabs.Length == 0
            && requiredParameters.All(parameters.Contains);

        var report = new CharacterAnimationReport
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            unityVersion = Application.unityVersion,
            passed = passed,
            prefabLoaded = prefab != null,
            humanoidAnimatorFound = animator != null,
            controllerLoaded = controller != null,
            usesDirectional2DBlend = directionalBlend,
            jumpStateFound = jumpStateFound,
            landStateFound = landState != null,
            jumpTransitionsToLand = jumpTransitionsToLand,
            landTransitionsToLocomotion = landTransitionsToLocomotion,
            runtimeEightDirectionSamplesPassed =
                runtimeEightDirectionSamplesPassed,
            runtimeJumpSamplePassed = runtimeJumpSamplePassed,
            runtimeLandSamplePassed = runtimeLandSamplePassed,
            runtimeDirectionSamples = runtimeDirectionSamples,
            parameters = parameters,
            locomotionClips = clipNames.Distinct().OrderBy(name => name).ToArray(),
            missingClips = missingClips,
            combatBones = combatBones,
            missingCombatBones = missingCombatBones,
            combatWeaponPrefabs = combatWeaponPrefabs,
            missingCombatWeaponPrefabs = missingCombatWeaponPrefabs
        };
        var reportPath = Path.GetFullPath(Path.Combine(
            Application.dataPath,
            "../../recovery/recovered-character-animation-validation.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true) + "\n");
        Debug.Log(string.Format(
            "[GenesisAnimation] Validation passed={0}; directionalClips={1}/{2}; "
            + "jump={3}; land={4}; jumpToLand={5}; landToMove={6}; "
            + "runtime8Way={7}; runtimeJump={8}; runtimeLand={9}; "
            + "combatBones={10}/{11}; weaponProps={12}/{13}; report={14}",
            report.passed,
            report.locomotionClips.Length,
            ExpectedLocomotionClips.Length,
            report.jumpStateFound,
            report.landStateFound,
            report.jumpTransitionsToLand,
            report.landTransitionsToLocomotion,
            report.runtimeEightDirectionSamplesPassed,
            report.runtimeJumpSamplePassed,
            report.runtimeLandSamplePassed,
            report.combatBones.Length,
            RequiredCombatBones.Length,
            report.combatWeaponPrefabs.Length,
            RequiredWeaponPrefabs.Length,
            reportPath));
        if (!passed)
            throw new InvalidOperationException(
                "Recovered character locomotion validation failed. See "
                + reportPath);
    }

    private static void SampleRuntimeLocomotion(
        GameObject prefab,
        RuntimeAnimatorController controller,
        out string[] samples,
        out bool eightDirectionsPassed,
        out bool jumpPassed,
        out bool landPassed)
    {
        samples = Array.Empty<string>();
        eightDirectionsPassed = false;
        jumpPassed = false;
        landPassed = false;
        if (prefab == null || controller == null)
            return;

        var instance = UnityEngine.Object.Instantiate(prefab);
        instance.name = "Runtime Locomotion Audit";
        try
        {
            var animator = instance.GetComponentInChildren<Animator>(true);
            if (animator == null)
                return;
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
            animator.enabled = true;
            var directions = new[]
            {
                new Vector2(-1f, -1f), new Vector2(0f, -1f),
                new Vector2(1f, -1f), new Vector2(-1f, 0f),
                new Vector2(1f, 0f), new Vector2(-1f, 1f),
                new Vector2(0f, 1f), new Vector2(1f, 1f),
            };
            var results = new List<string>();
            var passedCount = 0;
            foreach (var direction in directions)
            {
                animator.Rebind();
                animator.SetBool("Grounded", true);
                animator.SetFloat("MoveX", direction.normalized.x);
                animator.SetFloat("MoveZ", direction.normalized.y);
                animator.Update(0f);
                animator.Update(0.08f);
                var clipInfo = animator.GetCurrentAnimatorClipInfo(0);
                var finiteWeights = clipInfo.Length > 0
                    && clipInfo.All(item =>
                        !float.IsNaN(item.weight)
                        && !float.IsInfinity(item.weight));
                var locomotion = StateMatches(animator, "Locomotion");
                if (locomotion && finiteWeights)
                    passedCount += 1;
                results.Add(string.Format(
                    "({0:0.00},{1:0.00}):state={2},clips={3}",
                    direction.normalized.x,
                    direction.normalized.y,
                    locomotion ? "Locomotion" : "Unexpected",
                    string.Join("+", clipInfo
                        .Where(item => item.weight > 0.001f)
                        .Select(item => item.clip.name
                            + "@" + item.weight.ToString("0.00")))));
            }
            samples = results.ToArray();
            eightDirectionsPassed = passedCount == directions.Length;

            animator.Rebind();
            animator.SetBool("Grounded", true);
            animator.Update(0f);
            animator.SetBool("Grounded", false);
            for (var step = 0; step < 4; step += 1)
                animator.Update(0.05f);
            jumpPassed = StateMatches(animator, "Jump");
            animator.SetBool("Grounded", true);
            for (var step = 0; step < 4; step += 1)
                animator.Update(0.05f);
            landPassed = StateMatches(animator, "Land");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static bool StateMatches(Animator animator, string stateName)
    {
        if (animator.GetCurrentAnimatorStateInfo(0).IsName(stateName))
            return true;
        return animator.IsInTransition(0)
            && animator.GetNextAnimatorStateInfo(0).IsName(stateName);
    }

    private static void CollectClipNames(Motion motion, ICollection<string> names)
    {
        if (motion is AnimationClip clip)
        {
            names.Add(clip.name);
            return;
        }
        if (!(motion is BlendTree tree))
            return;
        foreach (var child in tree.children)
            CollectClipNames(child.motion, names);
    }
}
#endif
