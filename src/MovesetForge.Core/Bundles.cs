using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MovesetForge.Core;

public sealed class Bundle
{
    public required string Name {get;init;}
    public required List<Moveset> Sets {get;init;}
    public bool Combat=>Sets.Any(s=>s.Framework!="Other");
    public override string ToString()=>$"{Name}\n{Sets.Sum(s=>s.Animations.Count):N0} clips · {Sets.Count} source folders";
}
public static class Bundles
{
    public static List<Bundle> Create(Library library)
    {
        var output=new List<Bundle>();
        foreach(var group in library.Sets.GroupBy(s=>s.ReplacerFolder.Length>0?s.ReplacerFolder:s.BundleKey.Length>0?s.BundleKey:s.Folder,Paths.Comparer))
        {
            var first=group.First();string name=first.ReplacerConfig["name"]?.GetValue<string>()??(first.ReplacerFolder.Length>0?Path.GetFileName(first.ReplacerFolder):first.BundleName.Length>0?first.BundleName:first.Name);
            // This pack puts independently authored FH families in a shared BFCO
            // root. Retain every conditional subfolder within each named family.
            string Family(Moveset s){var m=Regex.Match(Path.GetFileName(s.Folder),@"^FH-(Nu-Xia|Varangian-Guard|[^-]+)-",RegexOptions.IgnoreCase);return m.Success?m.Groups[1].Value:"";}
            if(WeaponDetector.PackageName(first).Contains("For Honor",StringComparison.OrdinalIgnoreCase)&&group.Any(s=>Family(s).Length>0))
            {
                var shared=group.Where(s=>Family(s).Length==0).ToList();
                foreach(var family in group.Where(s=>Family(s).Length>0).GroupBy(Family,StringComparer.OrdinalIgnoreCase))
                    output.Add(new(){Name=WeaponDetector.PackageName(first)+" / "+family.Key,Sets=family.Concat(shared).ToList()});
            }
            else output.Add(new(){Name=name,Sets=group.ToList()});
        }
        return output.OrderBy(b=>b.Name,StringComparer.OrdinalIgnoreCase).ToList();
    }
}
