using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MovesetForge.Core;

// Explicit mappings based on OAR's legacy factories and component serialization.
// An unrecognized expression fails the entire import; never export a partial rule.
public static class LegacyConditions
{
    private static readonly Dictionary<string,string> Forms=new()
    {
        ["IsForm"]="Form",["IsActorBase"]="Actor base",["IsRace"]="Race",["HasMagicEffect"]="Magic effect",
        ["HasPerk"]="Perk",["HasSpell"]="Spell",["IsInFaction"]="Faction",["IsWorn"]="Form",
        ["IsEquippedPower"]="Power",["CurrentWeather"]="Weather",["IsInLocation"]="Location",
        ["IsParentCell"]="Cell",["IsWorldSpace"]="Worldspace",["IsVoiceType"]="Voice type",["IsCombatStyle"]="Combat style",
        ["IsCurrentPackage"]="Package"
    };
    private static readonly HashSet<string> Booleans=["IsInCombat","IsFemale","IsChild","IsPlayerTeammate","IsInInterior","IsUnique",
        "IsSneaking","IsSprinting","IsAttacking","IsWeaponDrawn","IsInAir","IsBlocking","IsRunning"];

    public static JsonArray Parse(string source)
    {
        var output=new JsonArray();JsonArray? or=null;int number=0;
        foreach(var raw in source.Split('\n'))
        {
            number++;var line=raw.Trim().TrimStart('\uFEFF');
            if(line.Length==0||line.StartsWith(';')||line.StartsWith('#'))continue;
            // A semicolon in a quoted plugin name is not a comment delimiter.
            bool quoted=false;for(int i=0;i<line.Length;i++){if(line[i]=='"')quoted=!quoted;if(!quoted&&line[i]==';'){line=line[..i].TrimEnd();break;}}
            bool moreOr=Regex.IsMatch(line,@"\s+OR$");line=Regex.Replace(line,@"\s+(AND|OR)$","");
            var match=Regex.Match(line,@"^(NOT\s+)?([A-Za-z0-9_]+)\s*(?:\((.*)\))?$");
            try
            {
                if(!match.Success)throw new InvalidDataException("Unsupported DAR syntax: "+line);
                var n=Convert(match.Groups[2].Value,match.Groups[3].Value.Trim());
                if(match.Groups[1].Success)n["negated"]=true;
                // DAR groups consecutive OR lines, then ANDs those groups.
                if(moreOr||or!=null){or??=new();or.Add(n);if(!moreOr){var group=Conditions.Named("OR");group["Conditions"]=or;output.Add(group);or=null;}}
                else output.Add(n);
            }
            catch(Exception e) when(e is InvalidDataException or FormatException or OverflowException)
            {throw new InvalidDataException($"DAR line {number}: {e.Message}",e);}
        }
        if(or!=null)throw new InvalidDataException("DAR condition ends with OR.");return output;
    }

    private static JsonObject Convert(string name,string arg)
    {
        bool left=name.Contains("Left",StringComparison.Ordinal);
        if(name is "IsEquippedRightType" or "IsEquippedLeftType")
        {var n=Conditions.Named("IsEquippedType");n["Type"]=Numeric(arg);n["Left hand"]=left;return n;}
        if(name is "IsEquippedRight" or "IsEquippedLeft")
        {var n=Conditions.Named("IsEquipped");n["Form"]=Form(arg);n["Left hand"]=left;return n;}
        if(name is "IsEquippedRightHasKeyword" or "IsEquippedLeftHasKeyword")
        {var n=Conditions.Named("IsEquippedHasKeyword");n["Keyword"]=new JsonObject{["form"]=Form(arg)};n["Left hand"]=left;return n;}
        if(name is "HasKeyword" or "IsWornHasKeyword" or "HasMagicEffectWithKeyword")
        {var n=Conditions.Named(name);n["Keyword"]=new JsonObject{["form"]=Form(arg)};if(name=="HasMagicEffectWithKeyword")n["Active effects only"]=false;return n;}
        if(name=="IsEquippedShout")name="IsEquippedPower";
        if(Forms.TryGetValue(name,out var key))
        {var n=Conditions.Named(name);n[key]=Form(arg);if(name=="HasMagicEffect")n["Active effects only"]=false;return n;}
        if(Booleans.Contains(name))
        {if(arg.Length>0)throw new InvalidDataException("Unexpected arguments for "+name);return Conditions.Named(name);}
        var comparison=Regex.Match(name,@"^(Value|IsActorValue(Base|Max|Percentage)?)(EqualTo|LessThan)$");
        if(comparison.Success)
        {
            var parts=SplitPair(arg);var n=Conditions.Named("CompareValues");
            n["Value A"]=comparison.Groups[1].Value=="Value"?Numeric(parts[0]):new JsonObject{["actorValue"]=int.Parse(parts[0],CultureInfo.InvariantCulture),["actorValueType"]=comparison.Groups[2].Value is ""?"Value":comparison.Groups[2].Value};
            n["Value B"]=Numeric(parts[1]);n["Comparison"]=comparison.Groups[3].Value=="EqualTo"?"==":"<";return n;
        }
        if(name is "IsLevelLessThan" or "CurrentGameTimeLessThan")
        {var n=Conditions.Named(name=="IsLevelLessThan"?"Level":"CurrentGameTime");n["Numeric value"]=Numeric(arg);n["Comparison"]="<";return n;}
        if(name is "IsFactionRankEqualTo" or "IsFactionRankLessThan")
        {var parts=SplitPair(arg);var n=Conditions.Named("FactionRank");n["Numeric value"]=Numeric(parts[0]);n["Faction"]=Form(parts[1]);n["Comparison"]=name.EndsWith("EqualTo")?"==":"<";return n;}
        if(name=="IsMovementDirection"){var n=Conditions.Named(name);n["Direction"]=Numeric(arg);return n;}
        if(name=="Random")
        {
            var n=Conditions.Named(name);n["requiredVersion"]="2.3.0.0";n["Numeric value"]=Numeric(arg);n["Comparison"]="<=";
            n["Minimum random value"]=new JsonObject{["value"]=0};n["Maximum random value"]=new JsonObject{["value"]=1};
            n["State"]=new JsonObject{["shouldResetOnLoopOrEcho"]=false};return n;
        }
        throw new InvalidDataException("Condition needs manual OAR conversion: "+name);
    }
    private static string[] SplitPair(string arg)
    {
        bool quoted=false;for(int i=0;i<arg.Length;i++){if(arg[i]=='"')quoted=!quoted;if(!quoted&&arg[i]==',')return [arg[..i].Trim(),arg[(i+1)..].Trim()];}
        throw new InvalidDataException("Expected two comma-separated arguments: "+arg);
    }
    private static JsonObject Numeric(string arg)
    {
        if(double.TryParse(arg,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)&&double.IsFinite(v))return new(){["value"]=v};
        return new(){["form"]=Form(arg)};
    }
    private static JsonObject Form(string arg)
    {
        var m=Regex.Match(arg,"^\"([^\"]+)\"\\s*\\|\\s*((?:0[xX])?[0-9a-fA-F]{1,8})$");
        if(!m.Success)throw new InvalidDataException("Unsupported DAR form: "+arg);
        return Conditions.Form(m.Groups[1].Value,m.Groups[2].Value);
    }
}
