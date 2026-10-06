#:property TargetFramework=net10.0

using System.Text.Json.Nodes;

if (args.Length != 4)
{
    return Fail("usage: validate-rid-lock.cs <canonical-lock> <rid-lock> <tfm> <rid>");
}

(string canonicalPath, string ridPath, string targetFramework, string runtimeIdentifier) =
    (args[0], args[1], args[2], args[3]);
JsonObject canonical = ReadObject(canonicalPath);
JsonObject runtime = ReadObject(ridPath);
if (canonical["version"]?.GetValue<int>() != 2 || runtime["version"]?.GetValue<int>() != 2)
{
    return Fail("version 2 package locks are required");
}

JsonObject canonicalTargets = RequiredObject(canonical, "dependencies");
JsonObject runtimeTargets = RequiredObject(runtime, "dependencies");
string runtimeTarget = $"{targetFramework}/{runtimeIdentifier}";
if (canonicalTargets[targetFramework] is not JsonObject canonicalPackages
    || runtimeTargets[targetFramework] is not JsonObject restoredCanonicalPackages
    || runtimeTargets[runtimeTarget] is not JsonObject runtimePackages)
{
    return Fail("RID lock targets are invalid");
}

if (!JsonNode.DeepEquals(canonicalPackages, restoredCanonicalPackages))
{
    return Fail("RID restore changed the target-framework package graph");
}

foreach ((string targetName, JsonNode? targetValue) in runtimeTargets)
{
    if (targetName == targetFramework)
    {
        continue;
    }

    if (!targetName.StartsWith(targetFramework + "/", StringComparison.Ordinal)
        || targetValue is not JsonObject targetPackages)
    {
        return Fail($"unexpected lock target: {targetName}");
    }

    foreach ((string packageName, JsonNode? value) in targetPackages)
    {
        if (value is not JsonObject runtimePackage
            || canonicalPackages[packageName] is not JsonObject canonicalPackage)
        {
            return Fail($"RID restore introduced package outside the canonical lock: {targetName}:{packageName}");
        }

        foreach (string field in new[] { "type", "resolved", "contentHash" })
        {
            if (!JsonNode.DeepEquals(runtimePackage[field], canonicalPackage[field]))
            {
                return Fail($"RID package field differs: {targetName}:{packageName}.{field}");
            }
        }
    }
}

Console.WriteLine($"RID_LOCK_VALID=PASS target={runtimeTarget} packages={runtimePackages.Count} targets={runtimeTargets.Count}");
return 0;

static JsonObject ReadObject(string path) =>
    JsonNode.Parse(File.ReadAllText(path)) as JsonObject
    ?? throw new InvalidDataException($"JSON root must be an object: {path}");

static JsonObject RequiredObject(JsonObject parent, string name) =>
    parent[name] as JsonObject
    ?? throw new InvalidDataException($"JSON property must be an object: {name}");

static int Fail(string message)
{
    Console.Error.WriteLine($"ERROR: {message}");
    return 1;
}
