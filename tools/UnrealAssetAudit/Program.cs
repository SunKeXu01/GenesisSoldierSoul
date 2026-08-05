using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

static string RequireOption(string[] args, string option)
{
    var index = Array.IndexOf(args, option);
    if (index < 0 || index + 1 >= args.Length)
        throw new ArgumentException($"Missing required option: {option}");
    return args[index + 1];
}

static string Sha256File(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

static object Failure(Exception error) => new
{
    status = "failed",
    error_type = error.GetType().FullName,
    error = error.Message,
};

try
{
    var closurePath = Path.GetFullPath(RequireOption(args, "--closure"));
    var extractedRoot = Path.GetFullPath(RequireOption(args, "--extracted-root"));
    var outputPath = Path.GetFullPath(RequireOption(args, "--output"));
    var jsonOutputRoot = Path.GetFullPath(RequireOption(args, "--json-output-root"));
    using var document = JsonDocument.Parse(File.ReadAllText(closurePath));
    var root = document.RootElement;
    var records = new List<object>();

    foreach (var package in root.GetProperty("packages").EnumerateArray())
    {
        if (package.GetProperty("conversion_readiness").GetString()
            != "file_set_complete_candidate")
            continue;
        var relativePath = package.GetProperty("path").GetString()
            ?? throw new FormatException("Package path is null.");
        var assetPath = Path.GetFullPath(
            Path.Combine(extractedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))
        );
        if (!assetPath.StartsWith(extractedRoot + Path.DirectorySeparatorChar,
                                  StringComparison.Ordinal))
            throw new FormatException($"Package path escapes extraction root: {relativePath}");
        if (!File.Exists(assetPath))
            throw new FileNotFoundException("Closure package is missing.", assetPath);

        object structural;
        try
        {
            var asset = new UAsset(
                assetPath,
                EngineVersion.VER_UE4_27,
                null,
                CustomSerializationFlags.SkipParsingExports
                    | CustomSerializationFlags.SkipPreloadDependencyLoading
            );
            structural = new
            {
                status = "parsed",
                asset.IsUnversioned,
                asset.HasUnversionedProperties,
                object_version = asset.ObjectVersion.ToString(),
                package_flags = $"0x{(uint)asset.PackageFlags:x8}",
                imports = asset.Imports.Count,
                exports = asset.Exports.Count,
                export_map = asset.Exports.Select(export => new
                {
                    name = export.ObjectName.ToString(),
                    representation = export.GetType().Name,
                    serial_size = export.SerialSize,
                }).ToArray(),
            };
        }
        catch (Exception error)
        {
            structural = Failure(error);
        }

        object full;
        try
        {
            var asset = new UAsset(
                assetPath,
                EngineVersion.VER_UE4_27,
                null,
                CustomSerializationFlags.SkipPreloadDependencyLoading
            );
            var jsonPath = Path.GetFullPath(
                Path.Combine(
                    jsonOutputRoot,
                    (relativePath + ".json").Replace('/', Path.DirectorySeparatorChar)
                )
            );
            if (!jsonPath.StartsWith(jsonOutputRoot + Path.DirectorySeparatorChar,
                                     StringComparison.Ordinal))
                throw new FormatException($"JSON output escapes root: {relativePath}");
            Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
            File.WriteAllText(jsonPath, asset.SerializeJson(true) + Environment.NewLine);
            full = new
            {
                status = "parsed",
                imports = asset.Imports.Count,
                exports = asset.Exports.Count,
                binary_equality_verified = asset.VerifyBinaryEquality(),
                json_output = new
                {
                    path = Path.GetRelativePath(jsonOutputRoot, jsonPath)
                        .Replace(Path.DirectorySeparatorChar, '/'),
                    bytes = new FileInfo(jsonPath).Length,
                    sha256 = Sha256File(jsonPath),
                    representation = "uassetapi_json",
                },
                export_types = asset.Exports
                    .GroupBy(export => export.GetType().Name)
                    .OrderBy(group => group.Key)
                    .ToDictionary(group => group.Key, group => group.Count()),
            };
        }
        catch (Exception error)
        {
            full = Failure(error);
        }

        records.Add(new
        {
            path = relativePath,
            bytes = new FileInfo(assetPath).Length,
            sha256 = Sha256File(assetPath),
            structural_parse = structural,
            full_parse = full,
        });
    }

    var parsedStructural = records.Count(record =>
        JsonSerializer.SerializeToElement(record)
            .GetProperty("structural_parse").GetProperty("status").GetString() == "parsed");
    var parsedFull = records.Count(record =>
        JsonSerializer.SerializeToElement(record)
            .GetProperty("full_parse").GetProperty("status").GetString() == "parsed");
    var report = new
    {
        tool = "tools/UnrealAssetAudit",
        tool_version = "2",
        dependency = new
        {
            name = "UAssetAPI",
            version = typeof(UAsset).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? typeof(UAsset).Assembly.GetName().Version?.ToString(),
            license = "MIT",
        },
        engine_version = "4.27",
        source = new
        {
            closure_manifest = closurePath,
            closure_manifest_sha256 = Sha256File(closurePath),
            source_pak = root.GetProperty("source").GetString(),
            source_pak_sha256 = root.GetProperty("source_sha256").GetString(),
        },
        semantics = "Structural parsing reads package maps with exports kept raw. Full parsing attempts UObject deserialization without mappings; failures remain explicit.",
        summary = new
        {
            candidates = records.Count,
            structural_parsed = parsedStructural,
            structural_failed = records.Count - parsedStructural,
            full_parsed = parsedFull,
            full_failed = records.Count - parsedFull,
        },
        records,
    };
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    var options = new JsonSerializerOptions { WriteIndented = true };
    File.WriteAllText(outputPath, JsonSerializer.Serialize(report, options) + Environment.NewLine);
    Console.WriteLine(JsonSerializer.Serialize(report.summary));
    return parsedStructural == records.Count ? 0 : 2;
}
catch (Exception error)
{
    Console.Error.WriteLine($"{error.GetType().Name}: {error.Message}");
    return 1;
}
