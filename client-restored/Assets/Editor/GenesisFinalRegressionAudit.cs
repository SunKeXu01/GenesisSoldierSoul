#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class GenesisFinalRegressionAudit
{
    [Serializable]
    private sealed class Report
    {
        public string generatedAtUtc;
        public string unityVersion;
        public bool passed;
        public string[] completedGates;
        public string failure;
    }

    [MenuItem("Genesis/Run Final Automated Regression Gates")]
    public static void Run()
    {
        var completed = new List<string>();
        Exception failure = null;
        try
        {
            GenesisWeaponRecoveryAudit.ValidateRecoveredWeaponPrefabs();
            GenesisViewmodelStructureAudit.Run();
            completed.Add(
                "formal weapon resources, viewmodel roles, poses and anchors");
            GenesisCharacterAnimationAudit.ValidateRecoveredLocomotion();
            completed.Add("third-person locomotion, jump, combat bones and props");
            GenesisMapRecoveryAudit.AuditMapRecoveryReadiness();
            completed.Add("seven-map strict recovery readiness");
            GenesisSevenMapAcceptanceAudit.Run();
            completed.Add("seven-map unified acceptance matrix");
            GenesisRestoredBuild.ValidatePlayableRecoveredMaps();
            completed.Add("seven-map build publication gate");
            GenesisRestoredBuild.SanitizePlayableRecoveredMaps();
            GenesisRestoredBuild.AuditPlayableColliderScales();
            completed.Add("seven-map non-negative BoxCollider scales");
            GenesisRestoredBuild.AuditPlayableAudio();
            completed.Add("seven-map playable audio inventory");
            GenesisThirdPersonRuntimePreview.Audit();
            completed.Add("third-person action reset, death/respawn and weapon props");
            GenesisLobbyUiAudit.AuditCreateRoomMapSelector();
            completed.Add("create-room map selector and confirmation layout");
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        var report = new Report
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            unityVersion = Application.unityVersion,
            passed = failure == null,
            completedGates = completed.ToArray(),
            failure = failure == null ? string.Empty : failure.ToString()
        };
        var outputPath = Path.GetFullPath(Path.Combine(
            Application.dataPath,
            "../../recovery/final-automated-regression.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        File.WriteAllText(outputPath, JsonUtility.ToJson(report, true) + "\n");
        AssetDatabase.SaveAssets();
        Debug.Log("[GenesisFinalRegression] passed=" + report.passed
            + "; gates=" + completed.Count + "; report=" + outputPath);
        if (failure != null)
            throw new InvalidOperationException(
                "Final automated regression failed. See " + outputPath,
                failure);
    }
}
#endif
