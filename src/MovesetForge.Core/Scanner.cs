using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MovesetForge.Core;

public static class Scanner
{
    public static Library Scan(IEnumerable<string> roots,IProgress<string>? progress=null,CancellationToken cancel=default,bool applyProfileOverrides=false)
    {
        var result=new Library();var grouped=new Dictionary<string,Moveset>(Paths.Comparer);var seen=new HashSet<string>(Paths.Comparer);var folders=new Dictionary<string,string>(Paths.Comparer);
        var overlays=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var input in roots)
        {
            string root=Path.GetFullPath(input);
            if(!Directory.Exists(root)){result.Diagnostics.Add("Missing folder: "+root);continue;}result.Roots.Add(root);
            foreach(var path in Walk(root,result.Diagnostics,cancel))
            {
                cancel.ThrowIfCancellationRequested();if(!seen.Add(path))continue;
                string filename=Path.GetFileName(path);
                if(applyProfileOverrides&&(filename.Equals("config.json",StringComparison.OrdinalIgnoreCase)||filename.Equals("user.json",StringComparison.OrdinalIgnoreCase)||filename.Equals("_conditions.txt",StringComparison.OrdinalIgnoreCase)))
                    if(VirtualPath(path) is {} virtualPath)overlays[virtualPath]=path;
                if(path.EndsWith(".bsa",StringComparison.OrdinalIgnoreCase))
                {
                    try{progress?.Report("Reading archive: "+filename);var imported=Scan([Import.Archive(path,cancel)],progress,cancel);result.Sets.AddRange(imported.Sets);result.Skeletons.AddRange(imported.Skeletons);result.FileCount+=imported.FileCount;result.Diagnostics.AddRange(imported.Diagnostics);}
                    catch(Exception e) when(e is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException){result.Diagnostics.Add("Archive unreadable: "+path+": "+e.Message);}continue;
                }
                if(!path.EndsWith(".hkx",StringComparison.OrdinalIgnoreCase))continue;
                result.FileCount++;
                if(filename.Equals("skeleton.hkx",StringComparison.OrdinalIgnoreCase)||filename.Equals("skeleton_female.hkx",StringComparison.OrdinalIgnoreCase))result.Skeletons.Add(path);
                string parent=Path.GetDirectoryName(path)!;if(!folders.TryGetValue(parent,out string? folder))folders[parent]=folder=FindSetFolder(path,root);
                if(!grouped.TryGetValue(folder,out var set))grouped.Add(folder,set=new(){Id=Paths.Id(folder),Name=Path.GetFileName(folder),Folder=folder,SourceRoot=root});
                set.Animations.Add(new(){Path=path,RelativePath=Path.GetRelativePath(folder,path).Replace('\\','/'),Size=new FileInfo(path).Length});
                if(result.FileCount%500==0)progress?.Report($"Indexed {result.FileCount:N0} HKX files…");
            }
        }
        foreach(var set in grouped.Values)
        {
            ReadConfiguration(set,overlays);
            set.Animations.Sort((a,b)=>StringComparer.OrdinalIgnoreCase.Compare(a.RelativePath,b.RelativePath));
            bool Has(string prefix)=>set.Animations.Any(a=>a.Name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||a.RelativePath.Contains("_variants_"+prefix,StringComparison.OrdinalIgnoreCase));
            bool bfco=Has("BFCO_"),mco=Has("MCO_");set.Framework=bfco&&mco?"Mixed BFCO/MCO":bfco?"BFCO":mco?"MCO":"Other";
            if(set.Format=="DAR")SetLegacyBundle(set);
            result.Sets.Add(set);
        }
        result.Sets.Sort((a,b)=>StringComparer.OrdinalIgnoreCase.Compare(a.Name,b.Name));return result;
    }

    public static string? VirtualPath(string path)
    {string normalized=path.Replace('\\','/');int marker=normalized.IndexOf("/meshes/",StringComparison.OrdinalIgnoreCase);return marker<0?null:normalized[(marker+1)..];}
    private static string? LocalFile(string folder,string name)=>Directory.EnumerateFiles(folder).FirstOrDefault(p=>Path.GetFileName(p).Equals(name,StringComparison.OrdinalIgnoreCase));
    private static bool ActorFolder(DirectoryInfo dir)=>dir.Parent?.Parent?.Name.Equals("DynamicAnimationReplacer",StringComparison.OrdinalIgnoreCase)==true&&Regex.IsMatch(dir.Parent.Name,@"\.(esp|esm|esl)$",RegexOptions.IgnoreCase);
    private static string FindSetFolder(string path,string root)
    {
        for(var d=new DirectoryInfo(Path.GetDirectoryName(path)!);d!=null&&Paths.Within(d.FullName,root);d=d.Parent)
        {
            if(LocalFile(d.FullName,"config.json")!=null||LocalFile(d.FullName,"_conditions.txt")!=null)return d.FullName;
            if(d.Parent?.Parent?.Name.Equals("OpenAnimationReplacer",StringComparison.OrdinalIgnoreCase)==true||d.Parent?.Name.Equals("_CustomConditions",StringComparison.OrdinalIgnoreCase)==true||ActorFolder(d))return d.FullName;
        }
        var dir=new DirectoryInfo(Path.GetDirectoryName(path)!);return dir.Name.StartsWith("_variants_",StringComparison.OrdinalIgnoreCase)?dir.Parent!.FullName:dir.FullName;
    }
    private static void ReadConfiguration(Moveset set,Dictionary<string,string> overlays)
    {
        string? Resolve(string folder,string name)
        {
            string? file=null;if(VirtualPath(Path.Combine(folder,name)) is {} key)overlays.TryGetValue(key,out file);
            file??=LocalFile(folder,name);if(file!=null&&!set.ConfigurationSources.Contains(file))set.ConfigurationSources.Add(file);return file;
        }
        try
        {
            var dir=new DirectoryInfo(set.Folder);string? vpath=VirtualPath(set.Folder);
            string marker=vpath?.Contains("/DynamicAnimationReplacer/",StringComparison.OrdinalIgnoreCase)==true?"/DynamicAnimationReplacer/":"/OpenAnimationReplacer/";
            if(vpath is {} vp&&vp.IndexOf(marker,StringComparison.OrdinalIgnoreCase) is var at&&at>=0)set.MeshPrefix=vp[..at];
            bool dar=marker.StartsWith("/Dynamic")||LocalFile(set.Folder,"_conditions.txt")!=null;
            string? legacyError=null;
            if(dar)
            {
                set.Format="DAR";set.Config["priority"]=dir.Parent?.Name.Equals("_CustomConditions",StringComparison.OrdinalIgnoreCase)==true&&int.TryParse(dir.Name,out var priority)?priority:0;
                try
                {
                    if(Resolve(set.Folder,"_conditions.txt") is {} conditions)set.Config["conditions"]=Conditions.ParseLegacy(File.ReadAllText(conditions));
                    else if(ActorFolder(dir))set.Config["conditions"]=new JsonArray(Conditions.WithForm("IsActorBase","Actor base",dir.Parent!.Name,dir.Name));
                    else legacyError="DAR folder has no _conditions.txt or actor-base selector.";
                }
                catch(Exception e) when(e is InvalidDataException or FormatException or IOException){legacyError=e.Message;}
            }
            if(Resolve(set.Folder,"config.json") is {} config){set.Config=Json.ReadObject(config);if(!dar)set.Format="OAR";}
            if(Resolve(set.Folder,"user.json") is {} user)
            {
                // OAR reads author metadata, then user data instead of author data.
                // Missing user fields use defaults; they do not inherit author settings.
                var effective=Json.ReadObject(user);effective.Remove("name");effective.Remove("description");
                foreach(var key in new[]{"name","description"})if(set.Config[key]!=null)effective[key]=set.Config[key]!.DeepClone();
                if(effective["priority"] is not JsonValue priorityValue||!priorityValue.TryGetValue<int>(out _))throw new InvalidDataException("OAR user.json requires an integer priority.");
                if(!effective.ContainsKey("conditions"))effective["conditions"]=new JsonArray();
                set.Config=effective;
            }
            if(legacyError!=null)
            {
                if(set.Config["conditions"] is JsonArray)set.Warnings.Add("Using native OAR condition override; legacy text was not translatable: "+legacyError);
                else {set.Warnings.Add(legacyError);set.Format="Invalid";}
            }
            if(set.Config.ContainsKey("conditions")&&set.Config["conditions"] is not JsonArray)throw new InvalidDataException("Conditions must be an OAR JSON array.");
            set.Name=set.Config["name"]?.GetValue<string>()??dir.Name;
            if(dir.Parent?.Parent?.Name.Equals("OpenAnimationReplacer",StringComparison.OrdinalIgnoreCase)==true)
            {
                set.Format="OAR";set.ReplacerFolder=dir.Parent.FullName;
                if(Resolve(set.ReplacerFolder,"config.json") is {} root)set.ReplacerConfig=Json.ReadObject(root);
                if(Resolve(set.ReplacerFolder,"user.json") is {} rootUser)
                {
                    var effective=Json.ReadObject(rootUser);
                    foreach(var key in new[]{"name","description","author"}){effective.Remove(key);if(set.ReplacerConfig[key]!=null)effective[key]=set.ReplacerConfig[key]!.DeepClone();}
                    set.ReplacerConfig=effective;
                }
                set.Name=(set.ReplacerConfig["name"]?.GetValue<string>()??dir.Parent.Name)+" / "+set.Name;
            }
            else if(dar)set.Name=WeaponDetector.PackageName(set)+" / "+set.Name;
        }
        catch(Exception e) when(e is System.Text.Json.JsonException or InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {set.Warnings.Add("Configuration could not be read: "+e.Message);set.Format="Invalid";}
    }
    private static void SetLegacyBundle(Moveset set)
    {
        JsonNode? Equipment(JsonNode? node)
        {
            if(node is not JsonObject o)return null;string name=o["condition"]?.GetValue<string>()??"";
            if(name.StartsWith("IsEquipped",StringComparison.Ordinal))return o.DeepClone();
            if(name is "OR" or "AND"&&o["Conditions"] is JsonArray children)
            {var parts=new JsonArray();foreach(var child in children)if(Equipment(child) is {} e)parts.Add(e);if(parts.Count>0){var copy=(JsonObject)o.DeepClone();copy["Conditions"]=parts;return copy;}}
            return null;
        }
        var equipment=new JsonArray();if(set.Config["conditions"] is JsonArray conditions)foreach(var c in conditions)if(Equipment(c) is {} e)equipment.Add(e);
        if(equipment.Count==0)return;
        string signature=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(equipment.ToJsonString())))[..12];
        set.BundleKey=Directory.GetParent(set.Folder)!.FullName+"/equipment-"+signature;
        var detection=WeaponDetector.Detect([set]);string label=detection.ProfileId=="source"?"Conditional weapon group "+signature[..5]:detection.Profile.Label;
        set.BundleName=WeaponDetector.PackageName(set)+" / "+(detection.Hand=="Both"?"Dual wield · ":"")+label;set.Name=set.BundleName+" / "+Path.GetFileName(set.Folder);
    }
    public static IEnumerable<string> Walk(string root,List<string> diagnostics,CancellationToken cancel=default)
    {
        var stack=new Stack<string>();stack.Push(root);
        while(stack.Count>0)
        {
            cancel.ThrowIfCancellationRequested();var dir=stack.Pop();string[] entries;
            try{entries=Directory.GetFileSystemEntries(dir);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){diagnostics.Add(dir+": "+e.Message);continue;}
            foreach(var entry in entries)
            {
                FileAttributes attrs;try{attrs=File.GetAttributes(entry);}catch(IOException){continue;}
                if((attrs&FileAttributes.ReparsePoint)!=0){diagnostics.Add("Skipped symlink: "+entry);continue;}
                if((attrs&FileAttributes.Directory)!=0){if(Path.GetFileName(entry) is not (".git" or "bin" or "obj" or ".cache"))stack.Push(entry);}else yield return entry;
            }
        }
    }
}
