using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MovesetForge.Core;

public static class Conditions
{
    public static readonly Dictionary<int,string> Weapons=new() { [0]="Unarmed",[1]="Sword",[2]="Dagger",[3]="War axe",[4]="Mace",[5]="Greatsword",[6]="Battleaxe",[10]="Warhammer",[7]="Bow",[9]="Crossbow",[8]="Staff" };
    public static JsonObject Named(string name) => new() { ["condition"]=name,["requiredVersion"]="1.0.0.0" };
    public static JsonObject Form(string plugin,string id)
    {
        if(string.IsNullOrWhiteSpace(plugin)||!Regex.IsMatch(id.Trim(),"^(0[xX])?[0-9a-fA-F]{1,8}$")) throw new InvalidDataException("Enter a plugin filename and a hexadecimal local FormID.");
        return new() { ["pluginName"]=plugin.Trim(),["formID"]=id.Trim().Replace("0x","",StringComparison.OrdinalIgnoreCase) };
    }
    public static JsonObject WithForm(string name,string key,string plugin,string id) { var n=Named(name);n[key]=Form(plugin,id);return n; }
    public static JsonObject Equipped(int type,bool left) { var n=Named("IsEquippedType");n["Type"]=new JsonObject{["value"]=type};n["Left hand"]=left;return n; }
    public static JsonObject EquippedKeyword(string keyword,bool left)
    {
        if(!Regex.IsMatch(keyword,@"^[A-Za-z_][A-Za-z0-9_]*$"))throw new InvalidDataException("Enter the weapon keyword's EditorID, such as WeapTypeRapier.");
        var n=Named("IsEquippedHasKeyword");n["Keyword"]=new JsonObject{["editorID"]=keyword};n["Left hand"]=left;return n;
    }
    // Suggestions for labels and initial selections; never replace the source condition tree.
    public static IEnumerable<int> SuggestedWeapons(JsonNode? node,bool left=false)
    {
        if(node is JsonArray array){foreach(var child in array)foreach(int type in SuggestedWeapons(child,left))yield return type;}
        else if(node is JsonObject o && o["negated"]?.GetValue<bool>()!=true && o["disabled"]?.GetValue<bool>()!=true)
        {
            string name=o["condition"]?.GetValue<string>()??"";
            if(name=="IsEquippedType" && (o["Left hand"]?.GetValue<bool>()==true)==left && double.TryParse(o["Type"]?["value"]?.ToString(),NumberStyles.Float,CultureInfo.InvariantCulture,out double type) && type==(int)type && Weapons.ContainsKey((int)type))yield return (int)type;
            if(name is "OR" or "AND")foreach(int childType in SuggestedWeapons(o["Conditions"],left))yield return childType;
        }
    }
    public static JsonObject Number(string name,string key,int value,string op) {var n=Named(name);n[key]=new JsonObject{["value"]=value};n["Comparison"]=op;return n;}
    public static JsonArray For(Rule rule)
    {
        var list=new JsonArray();
        if(rule.Hand is not ("Right" or "Left" or "Either" or "Both"))throw new InvalidDataException("Unknown weapon hand: "+rule.Hand);
        if(rule.Actors is not ("Both" or "Player" or "NPCs" or "Source"))throw new InvalidDataException("Unknown actor target: "+rule.Actors);
        void AddHands(Func<bool,JsonObject> make)
        {
            if(rule.Hand is "Either" or "Both"){var group=Named(rule.Hand=="Either"?"OR":"AND");group["Conditions"]=new JsonArray(make(false),make(true));list.Add(group);}
            else list.Add(make(rule.Hand=="Left"));
        }
        var profile=WeaponProfiles.ForRule(rule);
        if(profile.Id.StartsWith("vanilla."))AddHands(left=>Equipped(profile.Type!.Value,left));
        else if(profile.Id!="source")AddHands(left=>EquippedKeyword(string.IsNullOrWhiteSpace(rule.WeaponKeyword)?profile.Keyword:rule.WeaponKeyword.Trim(),left));
        if(rule.Actors is "Player" or "NPCs") {var player=WithForm("IsForm","Form","Skyrim.esm","14");if(rule.Actors=="NPCs")player["negated"]=true;list.Add(player);}
        if(rule.MinimumLevel is {} min)list.Add(Number("Level","Numeric value",min,">="));
        if(rule.MaximumLevel is {} max)list.Add(Number("Level","Numeric value",max,"<="));
        void Skill(int v,string op) {var n=Named("CompareValues");n["Value A"]=new JsonObject{["actorValue"]=rule.SkillActorValue,["actorValueType"]="Base"};n["Comparison"]=op;n["Value B"]=new JsonObject{["value"]=v};list.Add(n);}
        if(rule.MinimumSkill is {} smin)Skill(smin,">=");if(rule.MaximumSkill is {} smax)Skill(smax,"<=");
        if(rule.RacePlugin.Length>0||rule.RaceForm.Length>0)list.Add(WithForm("IsRace","Race",rule.RacePlugin,rule.RaceForm));
        if(rule.WeaponPlugin.Length>0||rule.WeaponForm.Length>0)
        AddHands(left=>{var n=WithForm("IsEquipped","Form",rule.WeaponPlugin,rule.WeaponForm);n["Left hand"]=left;return n;});
        foreach(var n in rule.ExtraConditions)list.Add(n?.DeepClone());
        return list;
    }
    public static bool IsPlayerSelector(JsonNode? n)
    {
        if(n is not JsonObject o)return false;
        var name=o["condition"]?.GetValue<string>();var key=name=="IsActorBase"?"Actor base":name=="IsForm"?"Form":"";
        if(key.Length==0||o[key] is not JsonObject f)return false;
        var id=f["formID"]?.GetValue<string>()?.Replace("0x","",StringComparison.OrdinalIgnoreCase);
        return string.Equals(f["pluginName"]?.GetValue<string>(),"Skyrim.esm",StringComparison.OrdinalIgnoreCase)&&uint.TryParse(id,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var num)&&num==(name=="IsForm"?0x14:7);
    }
    public static JsonArray ParseLegacy(string source)=>LegacyConditions.Parse(source);
}
