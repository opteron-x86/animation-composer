using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

namespace MovesetForge.Core;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static JsonObject ReadObject(string file)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(file),new(){AllowTrailingCommas=true,CommentHandling=JsonCommentHandling.Skip});
        // OAR uses RapidJSON FindMember, which chooses the first duplicate member.
        JsonNode? Read(JsonElement element)
        {
            if(element.ValueKind==JsonValueKind.Object){var o=new JsonObject();foreach(var p in element.EnumerateObject())if(!o.ContainsKey(p.Name))o[p.Name]=Read(p.Value);return o;}
            if(element.ValueKind==JsonValueKind.Array){var a=new JsonArray();foreach(var v in element.EnumerateArray())a.Add(Read(v));return a;}
            return JsonNode.Parse(element.GetRawText());
        }
        return Read(doc.RootElement) as JsonObject??throw new InvalidDataException($"Expected JSON object: {file}");
    }
    public static void Write(string file, object value) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!); File.WriteAllText(file, JsonSerializer.Serialize(value, Options)); }
}

public sealed class AnimationFile
{
    public string Path { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public long Size { get; set; }
    public string Name => System.IO.Path.GetFileName(RelativePath);
    public override string ToString() => RelativePath;
}

public sealed class Moveset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public string SourceRoot { get; set; } = "";
    public string ReplacerFolder { get; set; } = "";
    public string MeshPrefix { get; set; } = "meshes/actors/character/animations";
    public string Framework { get; set; } = "Other";
    public string Format { get; set; } = "Loose";
    public string BundleKey { get; set; } = "";
    public string BundleName { get; set; } = "";
    public JsonObject Config { get; set; } = new();
    public JsonObject ReplacerConfig { get; set; } = new();
    public List<AnimationFile> Animations { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> ConfigurationSources { get; set; } = [];
    public string Summary => $"{Name}  ·  {Framework}  ·  {Animations.Count} clips";
    public override string ToString() => Summary;
}

public sealed class Library
{
    public List<string> Roots { get; set; } = [];
    public List<Moveset> Sets { get; set; } = [];
    public List<string> Skeletons { get; set; } = [];
    public List<string> Diagnostics { get; set; } = [];
    public int FileCount { get; set; }
}

public sealed class Rule
{
    public string Name { get; set; } = "Sword style";
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public List<string> SetIds { get; set; } = [];
    public int WeaponType { get; set; } = 1;
    // Empty preserves the pre-0.3 project's numeric weapon type.
    public string WeaponProfile { get; set; } = "";
    public string WeaponKeyword { get; set; } = "";
    public string Actors { get; set; } = "Both";
    public string Hand { get; set; } = "Right";
    public int Priority { get; set; } = 20000;
    public int? MinimumLevel { get; set; }
    public int? MaximumLevel { get; set; }
    public int? MinimumSkill { get; set; }
    public int? MaximumSkill { get; set; }
    public int SkillActorValue { get; set; } = 6;
    public string RacePlugin { get; set; } = "";
    public string RaceForm { get; set; } = "";
    public string WeaponPlugin { get; set; } = "";
    public string WeaponForm { get; set; } = "";
    public JsonArray ExtraConditions { get; set; } = [];
    public override string ToString() => $"{Name}  ·  {Actors}  ·  priority {Priority}";
}

public sealed class Project
{
    public int Schema { get; set; } = 1;
    public string Name { get; set; } = "My Combat Movesets";
    public List<string> SourceRoots { get; set; } = [];
    public string SkeletonPath { get; set; } = "";
    public bool ApplyProfileOverrides { get; set; }
    public string TargetFramework { get; set; } = "BFCO";
    public List<Rule> Rules { get; set; } = [];
}

public static class Paths
{
    public static readonly StringComparer Comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static string Id(string path) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(path))))[..16].ToLowerInvariant();
    public static string Slug(string name) { var s = System.Text.RegularExpressions.Regex.Replace(name, "[^a-zA-Z0-9_-]+", "-").Trim('-'); return string.IsNullOrEmpty(s) ? "Movesets" : s[..Math.Min(s.Length, 60)]; }
    public static string Under(string root, string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        if (System.IO.Path.IsPathRooted(relative) || parts.Any(p => p is ".." or "." || p.Contains(':'))) throw new InvalidDataException("Unsafe relative path: " + relative);
        return System.IO.Path.Combine(root, System.IO.Path.Combine(parts));
    }
    public static bool Within(string child, string parent) => Comparer.Equals(System.IO.Path.GetFullPath(child), System.IO.Path.GetFullPath(parent)) || System.IO.Path.GetFullPath(child).StartsWith(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(parent)) + System.IO.Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
