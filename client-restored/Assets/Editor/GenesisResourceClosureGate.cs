#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

public static class GenesisResourceClosureGate
{
    [Serializable]
    private sealed class ApprovalInput
    {
        public string path;
        public string sha256;
    }

    [Serializable]
    private sealed class ClosureReport
    {
        public string generatedAtUtc;
        public string policySha256;
        public bool formalBuildAllowed;
        public int formalGroupCount;
        public int formalAGradeCount;
        public ApprovalInput[] approvalInputs;
    }

    [MenuItem("Genesis/Audit/Formal Resource Closure Gate")]
    public static void EnsureFormalBuildAllowed()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            Application.dataPath, "../.."));
        var policyPath = Path.Combine(
            repositoryRoot, "recovery/resource-closure-policy.json");
        var reportPath = Path.Combine(
            repositoryRoot, "recovery/resource-closure-audit.json");
        if (!File.Exists(policyPath) || !File.Exists(reportPath))
            throw new InvalidOperationException(
                "Formal resource closure evidence is missing. Run "
                + "tools/audit_resource_closures.py before a formal build.");

        var report = JsonUtility.FromJson<ClosureReport>(
            File.ReadAllText(reportPath));
        if (report == null)
            throw new InvalidOperationException(
                "Formal resource closure report is unreadable: " + reportPath);
        var policyHash = ComputeSha256(policyPath);
        if (!string.Equals(
                report.policySha256, policyHash,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Resource policy changed after the audit. Re-run "
                + "tools/audit_resource_closures.py.");

        if (report.approvalInputs == null || report.approvalInputs.Length == 0)
            throw new InvalidOperationException(
                "Resource closure approval input hashes are missing. Re-run "
                + "tools/audit_resource_closures.py.");
        var repositoryPrefix = repositoryRoot.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        foreach (var input in report.approvalInputs)
        {
            if (input == null || string.IsNullOrEmpty(input.path)
                || string.IsNullOrEmpty(input.sha256))
                throw new InvalidOperationException(
                    "Resource closure approval input is malformed.");
            var inputPath = Path.GetFullPath(Path.Combine(
                repositoryRoot,
                input.path.Replace('/', Path.DirectorySeparatorChar)));
            if (!inputPath.StartsWith(repositoryPrefix, StringComparison.Ordinal)
                || !File.Exists(inputPath))
                throw new InvalidOperationException(
                    "Resource closure approval input is missing or escapes the repository: "
                    + input.path);
            var inputHash = ComputeSha256(inputPath);
            if (!string.Equals(
                    inputHash, input.sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Resource closure approval input changed after the audit: "
                    + input.path);
        }

        DateTime generatedAt;
        if (!DateTime.TryParse(
                report.generatedAtUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out generatedAt))
            throw new InvalidOperationException(
                "Resource closure report has an invalid timestamp.");
        var protectedRoots = new[]
        {
            Path.Combine(Application.dataPath, "Resources"),
            Path.Combine(Application.dataPath, "PlayableMaps"),
        };
        var newestAsset = protectedRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(
                root, "*", SearchOption.AllDirectories))
            .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        if (generatedAt.ToUniversalTime() < newestAsset)
            throw new InvalidOperationException(
                "Runtime resources changed after the closure audit. Re-run "
                + "tools/audit_resource_closures.py.");

        if (!report.formalBuildAllowed
            || report.formalGroupCount == 0
            || report.formalAGradeCount != report.formalGroupCount)
            throw new InvalidOperationException(
                "Formal build blocked: recovered runtime resources are not all "
                + "A grade. Diagnostic builds remain available with "
                + "GENESIS_DIAGNOSTIC=1. See " + reportPath);
    }

    private static string ComputeSha256(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var algorithm = SHA256.Create())
            return string.Concat(
                algorithm.ComputeHash(stream).Select(value => value.ToString("x2")));
    }
}
#endif
