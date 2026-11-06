using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MovesetForge.Core;

public sealed record WeaponProfile(string Id,string Label,int? Type=null,string Keyword="",string Form="")
{
    public override string ToString()=>Label;
    public int Skill=>Type is 5 or 6 or 10?7:Type is 7 or 9?8:6;
}
public static class WeaponProfiles
{
    public static readonly WeaponProfile Source=new("source","Keep source weapon conditions");
    public static readonly WeaponProfile Custom=new("keyword","Custom weapon keyword");
    public static readonly List<WeaponProfile> All=[Source,
        ..Conditions.Weapons.Select(p=>new WeaponProfile("vanilla."+p.Key,p.Value,p.Key)),
        new("aa.rapier","AA · Rapier",1,"WeapTypeRapier","801"),
        new("aa.pike","AA · Pike",5,"WeapTypePike","E457E"),
        new("aa.spear","AA · Spear",5,"WeapTypeSpear","E457F"),
        new("aa.halberd","AA · Halberd",6,"WeapTypeHalberd","E4580"),
        new("aa.quarterstaff","AA · Quarterstaff",6,"WeapTypeQtrStaff","E4581"),
        new("aa.claw","AA · Claws",2,"WeapTypeClaw","19AAB4"),
        new("aa.cestus","AA · Cestus",2,"WeapTypeCestus","19AAB3"),
        new("aa.whip","AA · Whip",4,"WeapTypeWhip","31BD77"),
        new("aa.katana","AA · Katana",1,"WeapTypeKatana","344733"),Custom];
    public static WeaponProfile? Find(string id)=>All.FirstOrDefault(p=>p.Id==id);
    public static WeaponProfile ForRule(Rule rule)=>Find(rule.WeaponProfile.Length==0?"vanilla."+rule.WeaponType:rule.WeaponProfile)??throw new InvalidDataException("Unknown weapon profile: "+rule.WeaponProfile);
    public static WeaponProfile? FromKeyword(JsonNode? keyword)
    {
        if(keyword is not JsonObject o)return null;
        if(o["editorID"]?.GetValue<string>() is {} id)
        {
            var known=All.FirstOrDefault(p=>p.Keyword.Length>0&&p.Keyword.Equals(id,StringComparison.OrdinalIgnoreCase));
            if(known!=null)return known;
            if(id.Equals("WeapTypeQuarterstaff",StringComparison.OrdinalIgnoreCase))return Find("aa.quarterstaff");
            if(id.Equals("WeapTypeClaws",StringComparison.OrdinalIgnoreCase))return Find("aa.claw");
            if(id.StartsWith("WeapType",StringComparison.OrdinalIgnoreCase))return Custom;
        }
        if(o["form"] is JsonObject f&&string.Equals(f["pluginName"]?.GetValue<string>(),"NewArmoury.esp",StringComparison.OrdinalIgnoreCase))
        {
            string number=f["formID"]?.GetValue<string>()?.Replace("0x","",StringComparison.OrdinalIgnoreCase)??"";
            if(uint.TryParse(number,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out uint value))return All.FirstOrDefault(p=>p.Form.Length>0&&uint.Parse(p.Form,NumberStyles.HexNumber)==value);
        }
        return null;
    }
}

public sealed record WeaponDetection(string ProfileId,string Hand,string Keyword,string Confidence,string Explanation,int Skill)
{
    public WeaponProfile Profile=>WeaponProfiles.Find(ProfileId)??WeaponProfiles.Source;
    public string Summary=>$"{Profile.Label} · {Hand} · {Confidence}\n{Explanation}";
}

public static class WeaponDetector
{
    private sealed record Candidate(string Id,string Keyword);
    private sealed record Evidence(List<Candidate> Candidates,bool Uncertain=false,bool WeaponClause=false);
    private static IEnumerable<Candidate> Distinct(IEnumerable<Candidate> candidates)=>candidates.DistinctBy(c=>(c.Id,c.Keyword.ToUpperInvariant()));
    private static bool Same(Candidate? a,Candidate? b)=>a!=null&&b!=null&&a.Id==b.Id&&a.Keyword.Equals(b.Keyword,StringComparison.OrdinalIgnoreCase);

    // Only positive, enabled weapon restrictions can identify a weapon. OR branches
    // with an unrestricted/unknown alternative must not become a mandatory filter.
    private static Evidence Read(JsonNode? node,bool? left,JsonObject presets,HashSet<string>? visiting=null)
    {
        if(node is JsonArray a)return And(a.Select(c=>Read(c,left,presets,visiting)));
        if(node is not JsonObject o||o["disabled"]?.GetValue<bool>()==true||o["negated"]?.GetValue<bool>()==true)return new([]);
        string name=o["condition"]?.GetValue<string>()??"";
        if(name is "AND" or "OR" && o["Conditions"] is JsonArray children)
        {
            var parts=children.Select(c=>Read(c,left,presets,visiting)).ToArray();
            if(name=="AND")return And(parts);
            var candidates=Distinct(parts.SelectMany(p=>p.Candidates)).ToList();
            return new(candidates,parts.Any(p=>p.Uncertain||p.Candidates.Count==0)&&parts.Any(p=>p.WeaponClause),parts.Any(p=>p.WeaponClause));
        }
        if(name=="PRESET"&&o["Preset"]?.GetValue<string>() is {} preset)
        {
            visiting??=[];if(!visiting.Add(preset))return new([],true,true);
            var found=(presets["conditionPresets"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(p=>p["name"]?.GetValue<string>()==preset);
            var result=found==null?new Evidence([],true,true):Read(found["conditions"],left,presets,visiting);visiting.Remove(preset);return result;
        }
        if(left.HasValue&&(o["Left hand"]?.GetValue<bool>()==true)!=left.Value)return new([]);
        if(name=="IsEquippedType"&&double.TryParse(o["Type"]?["value"]?.ToString(),NumberStyles.Float,CultureInfo.InvariantCulture,out double type)&&type==(int)type)
        {
            var p=WeaponProfiles.Find("vanilla."+(int)type);return p==null?new([]):new([new(p.Id,"")],WeaponClause:true);
        }
        if(name=="IsEquippedHasKeyword")
        {
            var p=WeaponProfiles.FromKeyword(o["Keyword"]);
            if(p!=null)return new([new(p.Id,o["Keyword"]?["editorID"]?.GetValue<string>()??p.Keyword)],WeaponClause:true);
            // Armor keywords on the offhand (shields) are not weapon classifications.
            return new([],WeaponClause:o["Keyword"]?["form"]!=null);
        }
        if(name=="IsEquipped")return new([],true,true);
        return new([]);
    }
    private static bool HasWeaponCondition(JsonNode? node)
    {
        if(node is JsonObject o){string name=o["condition"]?.GetValue<string>()??"";return name.StartsWith("IsEquipped")||name=="PRESET"||o.Any(p=>HasWeaponCondition(p.Value));}
        return node is JsonArray a&&a.Any(HasWeaponCondition);
    }
    private static Evidence And(IEnumerable<Evidence> values)
    {
        var parts=values.ToArray();var candidates=Distinct(parts.SelectMany(p=>p.Candidates)).ToList();
        // A mandatory explicit restriction still identifies the weapon if a separate
        // OR clause is broad. It is retained, never inferred from a negative branch.
        var certain=Distinct(parts.Where(p=>!p.Uncertain&&p.Candidates.Count==1).SelectMany(p=>p.Candidates)).ToList();
        if(certain.Count>0){var specific=certain.Where(c=>!c.Id.StartsWith("vanilla.")).ToList();candidates=specific.Count>0?specific:certain;}
        return new(candidates,certain.Count==0&&parts.Any(p=>p.Uncertain),parts.Any(p=>p.WeaponClause));
    }
    private static Candidate? Unique(Evidence evidence)
    {
        if(evidence.Uncertain)return null;
        var candidates=Distinct(evidence.Candidates).ToArray();
        return candidates.Length==1?candidates[0]:null;
    }
    public static WeaponDetection Detect(IEnumerable<Moveset> sources,string name="")
    {
        var sets=sources.ToArray();var right=sets.Select(s=>Read(s.Config["conditions"],false,s.ReplacerConfig)).ToArray();var left=sets.Select(s=>Read(s.Config["conditions"],true,s.ReplacerConfig)).ToArray();
        var chosen=right.Any(p=>p.WeaponClause)?right:left;string hand=ReferenceEquals(chosen,right)?"Right":"Left";
        var withWeapon=chosen.Where(e=>e.WeaponClause).ToArray();var candidates=withWeapon.Select(Unique).ToArray();
        var distinct=Distinct(candidates.OfType<Candidate>()).ToArray();
        if(distinct.Length!=1||candidates.Any(c=>c==null)||right.Where((e,i)=>!e.WeaponClause&&left[i].WeaponClause).Any())
        {
            var either=sets.Select(s=>Read(s.Config["conditions"],null,s.ReplacerConfig)).Where(e=>e.WeaponClause).ToArray();
            var all=either.Select(Unique).ToArray();var unique=Distinct(all.OfType<Candidate>()).ToArray();
            if(unique.Length==1&&all.All(c=>c!=null)){distinct=unique;candidates=all;hand="Either";}
        }
        if(distinct.Length==1&&candidates.All(c=>c!=null))
        {
            var selected=distinct[0]!;var p=WeaponProfiles.Find(selected.Id)!;
            if(hand=="Right"&&right.Where((e,i)=>e.WeaponClause&&!Same(Unique(left[i]),selected)).Any()==false)hand="Both";
            int skill=p.Skill;var skills=sets.SelectMany(s=>Conditions.SuggestedWeapons(s.Config["conditions"],hand=="Left")).Select(t=>WeaponProfiles.Find("vanilla."+t)!.Skill).Distinct().ToArray();if(skills.Length==1)skill=skills[0];
            return new(p.Id,hand,selected.Keyword,"Detected from conditions",selected.Keyword.Length>0?"Equipped weapon keyword: "+selected.Keyword:"Positive equipped weapon-type condition.",skill);
        }
        if(withWeapon.Length>0)return new("source","Right","","Mixed or conditional","Source weapon alternatives are preserved. Choose a narrower class only if you intend to restrict them.",6);
        if(sets.Any(s=>HasWeaponCondition(s.Config["conditions"])))return new("source","Right","","Not identified","Source weapon clauses do not positively identify a class. Keep them intact or review the source rules.",6);
        string text=name+" "+string.Join(' ',sets.Select(s=>s.Name+" "+Path.GetFileName(s.Folder)+" "+PackageName(s)));
        var hints=new List<WeaponProfile>();
        foreach(var (id,pattern) in new[]{("aa.rapier",@"\brapiers?\b"),("aa.pike",@"\bpikes?\b"),("aa.spear",@"\bspears?\b"),("aa.halberd",@"\bhalberds?\b"),("aa.quarterstaff",@"\bquarter[ _-]?staffs?\b"),("aa.claw",@"\bclaws?\b"),("aa.whip",@"\bwhips?\b"),("aa.katana",@"\bkatanas?\b"),("vanilla.5",@"\bgreatswords?\b"),("vanilla.10",@"\bwarhammers?\b"),("vanilla.6",@"\bbattle[ _-]?axes?\b"),("vanilla.1",@"\bswords?\b"),("vanilla.2",@"\bdaggers?\b")})
            if(Regex.IsMatch(text,pattern,RegexOptions.IgnoreCase))hints.Add(WeaponProfiles.Find(id)!);
        var specific=hints.Where(p=>p.Id.StartsWith("aa.")).Distinct().ToArray();if(specific.Length>0)hints=specific.ToList();
        if(hints.Distinct().ToArray() is [var hint])return new(hint.Id,"Right",hint.Keyword,"Name hint — review","Suggested from source names; the HKX motion itself does not identify a weapon class.",hint.Skill);
        return new("source","Right","","Not identified","No unambiguous weapon restriction was found. Existing source conditions remain active.",6);
    }
    public static string PackageName(Moveset set)
    {for(var d=new DirectoryInfo(set.Folder);d!=null;d=d.Parent)if(d.Name.Equals("meshes",StringComparison.OrdinalIgnoreCase))return d.Parent?.Name??set.Name;return Path.GetFileName(set.SourceRoot);}
    public static Rule Draft(string name,List<Moveset> sets,IEnumerable<Rule> existing)
    {
        var detection=Detect(sets,name);long priority=Math.Max(20000L,sets.Max(s=>(long)(s.Config["priority"]?.GetValue<int>()??0))+1000);
        foreach(var rule in existing.OrderBy(r=>r.Priority))if(priority<rule.Priority+Math.Max(1000,rule.SetIds.Count)&&priority+sets.Count>rule.Priority)priority=(long)rule.Priority+Math.Max(1000,rule.SetIds.Count);
        return new(){Name=name,SetIds=sets.Select(s=>s.Id).ToList(),WeaponProfile=detection.ProfileId,WeaponType=detection.Profile.Type??1,WeaponKeyword=detection.Keyword,Hand=detection.Hand,SkillActorValue=detection.Skill,Priority=(int)Math.Clamp(priority,0,int.MaxValue-10000)};
    }
}
