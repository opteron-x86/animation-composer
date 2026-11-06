namespace MovesetForge.Core;

public sealed record SlotConversion(string Source,string Destination,string Action);
public sealed record SetConversion(string Name,string Format,List<SlotConversion> Files,List<string> Errors,List<string> Notes);
public static class ConversionPlan
{
    public static SetConversion Inspect(Moveset set,string target)
    {
        var files=new List<SlotConversion>();var errors=new List<string>();var notes=new List<string>();var destinations=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var animation in set.Animations)
        {
            try
            {
                string dest=target=="BFCO"?Exporter.MapSlotPath(animation.RelativePath):animation.RelativePath;
                if(!destinations.Add(dest))errors.Add("Two source files map to "+dest);
                files.Add(new(animation.RelativePath,dest,dest==animation.RelativePath?"Keep":"MCO → BFCO"));
            }
            catch(InvalidDataException e){errors.Add(e.Message);files.Add(new(animation.RelativePath,"","Unsupported"));}
        }
        if(set.Format=="Invalid")errors.AddRange(set.Warnings.Count>0?set.Warnings:["Invalid source configuration."]);
        if(!string.IsNullOrEmpty(set.Config["overrideAnimationsFolder"]?.GetValue<string>()))errors.Add("This source redirects its animation folder with overrideAnimationsFolder. Resolve that folder reference before exporting; automatic redirect migration is not supported.");
        if(set.Format=="DAR")notes.Add("DAR → native OAR conditions; original source files remain unchanged.");
        if(target=="BFCO"&&set.Framework.Contains("MCO"))notes.Add("MCO slot conversion preserves HKX bytes, annotations, timing and movement. Requires a BFCO release with MCO annotation support.");
        notes.AddRange(set.Warnings);
        return new(set.Name,set.Format,files,errors.Distinct().ToList(),notes.Distinct().ToList());
    }
    public static string Describe(IEnumerable<Moveset> sources,string target)
    {
        var plans=sources.Select(s=>Inspect(s,target)).ToArray();int renamed=plans.Sum(p=>p.Files.Count(f=>f.Action=="MCO → BFCO")),dar=plans.Count(p=>p.Format=="DAR");
        string header=target=="BFCO"?$"BFCO + native OAR · {renamed} MCO slot mappings · {dar} DAR folders migrated":$"Native OAR · original animation slots · {dar} DAR folders migrated";
        return header+"\nAnimation bytes stay unchanged. Supporting plugins, effects and behaviors remain required.\n\n"+string.Join("\n\n",plans.Select(p=>p.Name+"\n"+string.Join('\n',p.Errors.Select(e=>"BLOCKED: "+e).Concat(p.Notes).Concat(p.Files.Where(f=>f.Action!="Keep").Select(f=>f.Source+" → "+(f.Destination.Length>0?f.Destination:"unsupported"))))));
    }
}
