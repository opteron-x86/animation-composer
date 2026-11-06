using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MovesetForge.Core;

public sealed record BuildIssue(string Severity,string Message);
public sealed record BuildResult(string Folder,string Archive,int AnimationCount,List<BuildIssue> Issues);
public static class Exporter
{
    public static List<BuildIssue> Validate(Project project,Library library)
    {
        var issues=new List<BuildIssue>();
        if(project.TargetFramework is not ("BFCO" or "OAR"))issues.Add(new("Error","Unknown export target: "+project.TargetFramework));
        if(project.Rules.Count==0)issues.Add(new("Error","Add at least one complete moveset."));
        foreach(var rule in project.Rules)
        {
            if(rule.SetIds.Count==0)issues.Add(new("Error",rule.Name+": no source moveset."));
            if(project.TargetFramework=="BFCO"&&!library.Sets.Any(s=>rule.SetIds.Contains(s.Id)&&s.Animations.Any(a=>AttackSlot.FromPath(a.RelativePath)!=null)))issues.Add(new("Error",rule.Name+": no recognized BFCO/MCO attack slots. Use native OAR export to migrate original-slot animations; renaming vanilla attacks does not create a BFCO moveset."));
            try {Conditions.For(rule);}catch(Exception e){issues.Add(new("Error",rule.Name+": "+e.Message));}
            if(rule.MinimumLevel>rule.MaximumLevel||rule.MinimumSkill>rule.MaximumSkill)issues.Add(new("Error",rule.Name+": minimum exceeds maximum."));
            if(rule.Priority<0||rule.Priority>int.MaxValue-10000)issues.Add(new("Error",rule.Name+": priority outside supported range."));
            foreach(var id in rule.SetIds)
            {
                var set=library.Sets.FirstOrDefault(s=>s.Id==id);
                if(set==null){issues.Add(new("Error",rule.Name+": source missing; rescan the saved source folders."));continue;}
                if(set.Format=="Invalid")issues.Add(new("Error",set.Name+": invalid source configuration."));
                if(project.TargetFramework=="OAR"&&set.Format=="Loose")issues.Add(new("Error",set.Name+": native OAR migration requires an existing DAR/OAR folder structure. Loose HKX files can still be previewed or used in a recognized BFCO/MCO moveset."));
                if(set.MeshPrefix.Contains("_1stperson",StringComparison.OrdinalIgnoreCase))issues.Add(new("Error",set.Name+": first-person sets are outside this third-person project."));
                if(set.Format=="DAR"&&set.Config["conditions"] is not JsonArray)issues.Add(new("Error",set.Name+": DAR conditions could not be translated."));
                if(set.Animations.Any(a=>!File.Exists(a.Path)))issues.Add(new("Error",set.Name+": an animation is missing."));
                var conversion=ConversionPlan.Inspect(set,project.TargetFramework);foreach(var error in conversion.Errors)issues.Add(new("Error",set.Name+": "+error));
                var sourcePriority=set.Config["priority"]?.GetValue<int>()??0;
                if(sourcePriority>=rule.Priority)issues.Add(new("Warning",set.Name+": source priority is "+sourcePriority+"; raise the output priority or disable the source animation replacer after installation."));
                foreach(var note in conversion.Notes)issues.Add(new("Info",set.Name+": "+note));
                if(set.Format=="DAR"&&set.Config.ToJsonString().Contains("\"Random\""))issues.Add(new("Info",set.Name+": migrated Random conditions require OAR 2.3+ and use OAR's default legacy behavior (keep results on loops). Review if you changed that OAR setting."));
                foreach(var p in Dependencies(set.Config).Concat(Dependencies(set.ReplacerConfig)).Distinct())issues.Add(new("Dependency",set.Name+": "+p));
                if(rule.Actors!="Source"&&set.Config["conditions"] is JsonArray ca && ca.Any(Conditions.IsPlayerSelector))issues.Add(new("Info",set.Name+": the original top-level player/NPC selector will be replaced by your actor target. Other conditions are preserved."));
            }
        }
        for(int i=0;i<project.Rules.Count;i++)for(int j=i+1;j<project.Rules.Count;j++)
        {
            var a=project.Rules[i];var b=project.Rules[j];
            if(a.WeaponType==b.WeaponType && (a.Actors==b.Actors||a.Actors=="Both"||b.Actors=="Both"))
            {
                bool disjoint=(a.MaximumLevel is {} am && b.MinimumLevel is {} bn && am<bn)||(b.MaximumLevel is {} bm && a.MinimumLevel is {} an && bm<an)||(a.MaximumSkill is {} sm && b.MinimumSkill is {} sn && a.SkillActorValue==b.SkillActorValue && sm<sn)||(b.MaximumSkill is {} tm && a.MinimumSkill is {} tn && a.SkillActorValue==b.SkillActorValue && tm<tn);
                bool overlappingPriority=(long)a.Priority+a.SetIds.Count>b.Priority&&(long)b.Priority+b.SetIds.Count>a.Priority;
                if(!disjoint)issues.Add(new(overlappingPriority?"Error":"Warning",a.Name+" and "+b.Name+": conditions may overlap. "+(overlappingPriority?"Use nonoverlapping priority ranges.":"The higher-priority matching submod wins for each slot.")));
            }
        }
        return issues.Distinct().ToList();
    }

    public static BuildResult Build(Project project,Library library,string outputParent)
    {
        var issues=Validate(project,library);if(issues.Any(i=>i.Severity=="Error"))throw new InvalidDataException(string.Join('\n',issues.Where(i=>i.Severity=="Error").Select(i=>i.Message)));
        outputParent=Path.GetFullPath(outputParent);
        if(library.Roots.Any(r=>Paths.Within(outputParent,r)))throw new InvalidDataException("Choose an output folder outside the source library; install the resulting ZIP through your mod manager.");
        Directory.CreateDirectory(outputParent);
        string name=Paths.Slug(project.Name)+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..4];
        string target=Path.Combine(outputParent,name),stage=Path.Combine(outputParent,".building-"+Guid.NewGuid().ToString("N"));
        var manifest=new List<object>();int count=0;
        Directory.CreateDirectory(stage);
        try
        {
            foreach(var rule in project.Rules)
            {
                var sets=rule.SetIds.Select(id=>library.Sets.Single(s=>s.Id==id)).OrderBy(s=>s.Config["priority"]?.GetValue<int>()??0).ThenBy(s=>s.Name).ToArray();
                // Preserve ordering within a source bundle using offsets; rules receive disjoint priority ranges.
                for(int i=0;i<sets.Length;i++)
                {
                    var set=sets[i];string rootId=Paths.Id(set.ReplacerFolder.Length>0?set.ReplacerFolder:set.Folder);
                    string replacerName=project.Name+" / "+rule.Name+" / "+rootId;
                    string replacerRel=set.MeshPrefix.ToLowerInvariant()+"/OpenAnimationReplacer/"+Paths.Slug(project.Name)+"-"+rule.Id+"-"+rootId;
                    string replacerDir=Paths.Under(stage,replacerRel);Directory.CreateDirectory(replacerDir);
                    var rootConfig=(JsonObject)set.ReplacerConfig.DeepClone();rootConfig["name"]=replacerName;
                    rootConfig["description"]="Complete source movesets selected with Animation Composer. "+(set.ReplacerConfig["description"]?.GetValue<string>()??"");
                    Json.Write(Path.Combine(replacerDir,"config.json"),rootConfig);
                    string subName=Path.GetFileName(set.Folder);string sub=Paths.Under(replacerDir,subName);Directory.CreateDirectory(sub);
                    var config=(JsonObject)set.Config.DeepClone();config["name"]=set.Config["name"]?.DeepClone()??JsonValue.Create(subName);config["priority"]=checked(rule.Priority+i);
                    var source=config["conditions"] as JsonArray??new JsonArray();
                    var conditions=new JsonArray();foreach(var n in source)if(rule.Actors=="Source"||!Conditions.IsPlayerSelector(n))conditions.Add(n?.DeepClone());
                    foreach(var n in Conditions.For(rule))conditions.Add(n?.DeepClone());config["conditions"]=conditions;
                    RemapSelfReferences(config,set.ReplacerConfig["name"]?.GetValue<string>(),replacerName);
                    RemapSelfReferences(rootConfig,set.ReplacerConfig["name"]?.GetValue<string>(),replacerName);Json.Write(Path.Combine(replacerDir,"config.json"),rootConfig);
                    // Animation configuration uses the original slot names, too.
                    RemapAnimationStrings(config,set,replacerRel+"/"+subName,project.TargetFramework);
                    Json.Write(Path.Combine(sub,"config.json"),config);
                    var destinations=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach(var animation in set.Animations)
                    {
                        string relative=project.TargetFramework=="BFCO"?MapSlotPath(animation.RelativePath):animation.RelativePath;
                        if(!destinations.Add(relative))throw new InvalidDataException("BFCO filename collision in "+set.Name+": "+relative);
                        string destination=Paths.Under(sub,relative);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        using var input=File.OpenRead(animation.Path);using(var output=new FileStream(destination,FileMode.CreateNew))input.CopyTo(output);
                        using var verify=File.OpenRead(destination);string hash=Convert.ToHexString(SHA256.HashData(verify));
                        manifest.Add(new {source=animation.Path,destination=Path.GetRelativePath(stage,destination).Replace('\\','/'),sha256=hash,sourceSet=set.Name});count++;
                    }
                }
            }
            Json.Write(Path.Combine(stage,"moveset-forge.project.json"),project);Json.Write(Path.Combine(stage,"moveset-forge.manifest.json"),manifest);
            var sourceSets=project.Rules.SelectMany(r=>r.SetIds).Distinct().Select(id=>library.Sets.Single(s=>s.Id==id)).ToArray();
            Json.Write(Path.Combine(stage,"moveset-forge.conversion.json"),new{target=project.TargetFramework,sets=sourceSets.Select(s=>ConversionPlan.Inspect(s,project.TargetFramework))});
            File.WriteAllText(Path.Combine(stage,"README.txt"),project.Name+"\n\nInstall through your mod manager. Requires OAR, "+(project.TargetFramework=="BFCO"?"BFCO, ":"")+"and the source mods' gameplay/behavior dependencies.\nOriginal animation bytes, events, timing, and movement are preserved. "+(project.TargetFramework=="BFCO"?"MCO filenames are mapped to BFCO slots.":"Original animation filenames are preserved; DAR conditions become native OAR configs.")+"\nKeep supporting plugins, sounds and effects installed. This package contains animation replacements, not replacement behavior graphs.\nVerify the selected animations and conditions in OAR in game.\n\n"+string.Join('\n',issues.Select(i=>i.Severity+": "+i.Message)));
            Directory.Move(stage,target);string zip=target+".zip";ZipFile.CreateFromDirectory(target,zip,CompressionLevel.Optimal,false);return new(target,zip,count,issues);
        }
        catch{if(Directory.Exists(stage))Directory.Delete(stage,true);throw;}
    }

    public static string MapSlotPath(string path)
    {
        bool withinVariant=false;
        return string.Join('/',path.Replace('\\','/').Split('/').Select(p=>
        {
            if(withinVariant)return p;
            string prefix=p.StartsWith("_variants_",StringComparison.OrdinalIgnoreCase)?p[..10]:"";string file=p[prefix.Length..];
            if(prefix.Length>0)withinVariant=true;
            var m=Regex.Match(file,@"^mco_(attack\d+|powerattack\d+|powerattackloop\d*|powerattackoutro\d*|sprintattack|sprintpowerattack|weaponart)(\.hkx)?$",RegexOptions.IgnoreCase);
            if(!m.Success) {if(file.StartsWith("mco_",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Unknown MCO slot: "+file);return p;}
            var slot=m.Groups[1].Value.ToLowerInvariant();slot=slot=="sprintpowerattack"?"SprintAttackPower":slot=="weaponart"?"PowerAttackComb":slot;
            return prefix+"BFCO_"+slot+m.Groups[2].Value;
        }));
    }
    private static IEnumerable<string> Dependencies(JsonNode? node)
    {
        if(node is JsonObject o)foreach(var p in o){if(p.Key is "pluginName" or "requiredPlugin" && p.Value is JsonValue v && v.TryGetValue<string>(out var s)&&s.Length>0)yield return s;foreach(var x in Dependencies(p.Value))yield return x;}
        else if(node is JsonArray a)foreach(var n in a)foreach(var x in Dependencies(n))yield return x;
    }
    private static void RemapSelfReferences(JsonNode? n,string? oldName,string newName)
    {
        if(n is JsonObject o)foreach(var p in o.ToList()){if(p.Key=="Replacer mod" && p.Value is JsonValue v && v.TryGetValue<string>(out var name) && name==oldName)o[p.Key]=newName;else RemapSelfReferences(p.Value,oldName,newName);}
        else if(n is JsonArray a)foreach(var child in a)RemapSelfReferences(child,oldName,newName);
    }
    private static void RemapAnimationStrings(JsonNode? n,Moveset set,string destinationPrefix,string target,bool variantMetadata=false)
    {
        if(n is JsonObject o)foreach(var p in o.ToList())
        {
            if(p.Value is JsonValue v&&v.TryGetValue<string>(out var s)&&p.Key is "animation" or "filename" or "path")
            {
                string value=s.Replace('\\','/');string? sourcePrefix=Scanner.VirtualPath(set.Folder);
                if(p.Key=="path"&&sourcePrefix!=null)
                {int start=value.IndexOf(sourcePrefix+"/",StringComparison.OrdinalIgnoreCase);if(start>=0)value=value[..start]+destinationPrefix+value[(start+sourcePrefix.Length)..];}
                if(target=="BFCO"&&!(variantMetadata&&p.Key=="filename"))value=MapSlotPath(value);
                o[p.Key]=s.Contains('\\')?value.Replace('/','\\'):value;
            }
            else RemapAnimationStrings(p.Value,set,destinationPrefix,target,variantMetadata||p.Key=="variants");
        }
        else if(n is JsonArray a)foreach(var child in a)RemapAnimationStrings(child,set,destinationPrefix,target,variantMetadata);
    }
}
