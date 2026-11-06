using System.Text.RegularExpressions;

namespace MovesetForge.Core;

public enum AttackKind { Normal, Power }
public enum SequenceOrder { Annotations, Numbered }
public sealed record AttackSlot(string Framework,AttackKind Kind,int Number,bool Variant)
{
    public static AttackSlot? FromPath(string path)
    {
        var parts=path.Replace('\\','/').Split('/');
        for(int i=parts.Length-1;i>=0;i--)
        {
            var m=Regex.Match(parts[i],@"^(?<variant>_variants_)?(?<framework>BFCO|MCO)_(?<kind>PowerAttack|Attack)(?<number>\d+)(?:\.hkx)?$",RegexOptions.IgnoreCase);
            if(!m.Success||!int.TryParse(m.Groups["number"].Value,out int n)||n<1)continue;
            return new(m.Groups["framework"].Value.ToUpperInvariant(),m.Groups["kind"].Value.Equals("Attack",StringComparison.OrdinalIgnoreCase)?AttackKind.Normal:AttackKind.Power,n,m.Groups["variant"].Success);
        }
        return null;
    }
}

public sealed record PreviewStep(AnimationFile File,PreviewClip Clip,double Start,double Length,string Note);
public sealed class AttackSequence
{
    public required string Name {get;init;}
    public required List<PreviewStep> Steps {get;init;}
    public List<string> Notes {get;init;}=[];
    public double Duration=>Steps.Sum(s=>s.Length);
    public string Report=>Name+"\n\n"+string.Join('\n',Steps.Select((s,i)=>$"{i+1}. {s.File.RelativePath}   {s.Start:0.000}–{s.Start+s.Length:0.000}s\n   {s.Note}"))+"\n\n"+string.Join('\n',Notes);
    public (int Index,PreviewStep Step,double LocalTime) At(double time)
    {
        double t=Math.Clamp(time,0,Duration);
        for(int i=0;i<Steps.Count;i++)if(t<Steps[i].Start+Steps[i].Length||i==Steps.Count-1)return(i,Steps[i],Math.Clamp(t-Steps[i].Start,0,Steps[i].Length));
        throw new InvalidOperationException("Empty preview sequence.");
    }
    public static AttackSequence Single(AnimationFile file,PreviewClip clip)=>new(){Name="Selected clip",Steps=[new(file,clip,0,clip.Duration,"Complete clip")]};

    public static AttackSequence Build(IEnumerable<AnimationFile> files,AttackKind kind,SequenceOrder order,Func<AnimationFile,PreviewClip> load,AnimationFile? preferred=null)
    {
        var candidates=files.Select(f=>(File:f,Slot:AttackSlot.FromPath(f.RelativePath))).Where(x=>x.Slot?.Kind==kind).ToArray();
        if(candidates.Length==0)throw new InvalidDataException($"This source folder has no numbered {kind.ToString().ToLowerInvariant()} attacks. Choose another source folder or preview an individual clip.");
        string framework=AttackSlot.FromPath(preferred?.RelativePath??"")?.Framework??candidates.GroupBy(x=>x.Slot!.Framework).OrderByDescending(g=>g.Count()).ThenBy(g=>g.Key).First().Key;
        if(!candidates.Any(x=>x.Slot!.Framework==framework))framework=candidates[0].Slot!.Framework;
        var notes=new List<string>();var slots=new SortedDictionary<int,AnimationFile>();
        foreach(var group in candidates.Where(x=>x.Slot!.Framework==framework).GroupBy(x=>x.Slot!.Number))
        {
            var chosen=group.OrderByDescending(x=>x.File.Path==preferred?.Path).ThenBy(x=>x.Slot!.Variant).ThenByDescending(x=>x.File.Name==preferred?.Name).ThenBy(x=>x.File.RelativePath,StringComparer.OrdinalIgnoreCase).First();
            slots.Add(group.Key,chosen.File);
            if(group.Count()>1)notes.Add($"Slot {group.Key}: using {chosen.File.RelativePath}. Other OAR variants are available in the clip list.");
        }
        var steps=new List<PreviewStep>();var visited=new HashSet<int>();int current=slots.ContainsKey(1)?1:slots.Keys.First();double start=0;long poses=0;
        if(current!=1)notes.Add($"Attack 1 is absent from this folder; starting at attack {current}.");
        if(order==SequenceOrder.Numbered)notes.Add("Numbered preview plays every numbered slot in this source folder at full length. It does not infer combat transitions.");
        else notes.Add("Uses the earliest annotated input window and its active next-attack value. Stops at a repeated or missing slot. OAR conditions, input timing, behavior blending, payloads and motion extraction are not simulated.");
        while(visited.Add(current))
        {
            if(steps.Count>=128)throw new InvalidDataException("Sequence exceeds the 128-clip preview limit.");
            var file=slots[current];var clip=load(file);poses+=(long)clip.Frames.Length*clip.Skeleton.Bones.Length;
            if(poses>8000000)throw new InvalidDataException("Sequence exceeds the in-memory preview limit; preview individual clips.");
            int? next=null;double length=clip.Duration;string note="Complete clip";
            int? nextNumber=slots.Keys.Where(n=>n>current).Select(n=>(int?)n).FirstOrDefault();
            if(order==SequenceOrder.Numbered)next=nextNumber;
            else
            {
                var events=clip.Events.Where(e=>e.Time>=0&&e.Time<=clip.Duration).OrderBy(e=>e.Time).ToArray();
                string[] opens=kind==AttackKind.Normal?["BFCO_NextWinStart","MCO_WinOpen"]:["BFCO_NextPowerWinStart","MCO_PowerWinOpen"];
                var window=events.FirstOrDefault(e=>e.Time>0.0001&&opens.Contains(e.Text.Trim(),StringComparer.OrdinalIgnoreCase));
                var assignments=events.Select(e=>(Event:e,Value:NextValue(e.Text,kind))).Where(x=>x.Value!=null).ToArray();
                if(window!=null)
                {
                    var active=assignments.LastOrDefault(x=>x.Event.Time<=window.Time+0.00001);
                    next=active.Value;
                    if(next==null)
                    {
                        next=nextNumber;note="No explicit next-slot value; using the next numbered slot at the input window.";
                        notes.Add($"{file.Name}: next slot inferred from filename order.");
                    }
                    else note=$"{window.Text} at {window.Time:0.000}s → slot {next}";
                    if(next is >0&&slots.ContainsKey(next.Value)&&!visited.Contains(next.Value))length=window.Time;
                }
                else if(assignments.Length==0)
                {
                    next=nextNumber;note="No recognized chain annotations; complete-clip numbered fallback.";
                    notes.Add($"{file.Name}: no recognized chain annotations; filename order used.");
                }
                else note="No input window for this attack type; chain ends after this clip.";
            }
            if(next is <=0){notes.Add($"{file.Name}: next-slot value {next} ends the chain.");next=null;}
            if(next is {} target&&!slots.ContainsKey(target)){notes.Add($"{file.Name}: referenced slot {target} is absent from the selected folder. Preview stops here; no unrelated subfolder is substituted.");next=null;length=clip.Duration;}
            if(next is {} repeat&&visited.Contains(repeat)){notes.Add($"The chain returns to slot {repeat}. This preview shows one pass; Loop repeats the preview.");next=null;length=clip.Duration;}
            steps.Add(new(file,clip,start,length,note));start+=length;
            if(next==null)break;current=next.Value;
        }
        return new(){Name=(kind==AttackKind.Normal?"Normal":"Power")+" chain · "+(order==SequenceOrder.Annotations?"annotations":"numbered clips"),Steps=steps,Notes=notes.Distinct().ToList()};
    }
    private static int? NextValue(string text,AttackKind kind)
    {
        string suffix=kind==AttackKind.Normal?"Attack":"PowerAttack";
        var direct=Regex.Match(text.Trim(),@"^(?:BFCO_NextIs|MCO_Next)"+suffix+@"(?<number>\d+)$",RegexOptions.IgnoreCase);
        var payload=Regex.Match(text.Trim(),@"^(?:PIE\.)?@SGVI\|(?:MCO_Next|BFCO_Next)"+suffix+@"\|(?<number>\d+)$",RegexOptions.IgnoreCase);
        var match=direct.Success?direct:payload;
        return match.Success&&int.TryParse(match.Groups["number"].Value,out int n)?n:null;
    }
}
