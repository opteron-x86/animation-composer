using System.Text.Json;
using System.Text.Json.Nodes;
using MovesetForge.Core;

public static class ImportExportTests
{
    public static void Run(Action<bool,string> check,Action<Action,string> throws,string scratch)
    {
        Moveset Set(JsonArray conditions)=>new(){Folder=Path.Combine(scratch,"plain"),Name="Plain",Config=new(){["conditions"]=conditions}};
        JsonObject Group(string kind,params JsonNode[] children){var n=Conditions.Named(kind);n["Conditions"]=new JsonArray(children);return n;}
        foreach(var p in WeaponProfiles.All.Where(p=>p.Id.StartsWith("aa.")))
        {
            var legacy=Conditions.ParseLegacy($"IsEquippedRightHasKeyword(\"NewArmoury.esp\" | 0X{p.Form})");
            var found=WeaponDetector.Detect([Set(legacy)]);
            check(found.ProfileId==p.Id&&found.Hand=="Right",$"AA keyword FormID {p.Form} was not identified.");
            check(legacy[0]?["Keyword"]?["form"]?["pluginName"]?.GetValue<string>()=="NewArmoury.esp","Legacy keyword form must be nested under Keyword.form.");
        }
        var aa=WeaponDetector.Detect([Set(new(Conditions.Equipped(1,false),Conditions.EquippedKeyword("WeapTypeRapier",false)))]);
        check(aa.ProfileId=="aa.rapier","Mandatory AA keyword should refine a generic weapon type.");
        check(WeaponDetector.Detect([Set(new(Conditions.Equipped(5,false),Conditions.EquippedKeyword("WeapTypeKatana",false)))]).Skill==7,"Two-handed katana conditions must select the two-handed skill.");
        var sameCase=WeaponDetector.Detect([Set(new(Conditions.EquippedKeyword("WeapTypeRapier",false))),Set(new(Conditions.EquippedKeyword("weaptyperapier",false)))]);
        check(sameCase.ProfileId=="aa.rapier","Keyword identity should ignore casing while preserving the literal EditorID.");
        check(WeaponDetector.Detect([Set(new(Group("OR",Conditions.EquippedKeyword("WeapTypeRapier",false),Conditions.Equipped(1,false))))]).ProfileId=="source","OR of sword and rapier must not narrow to rapier.");
        check(WeaponDetector.Detect([Set(new(Group("OR",Conditions.Equipped(1,false),Conditions.Named("IsInCombat"))))]).ProfileId=="source","An unrestricted OR branch must not become a mandatory weapon condition.");
        var excluded=Conditions.EquippedKeyword("WeapTypeRapier",false);excluded["negated"]=true;
        check(WeaponDetector.Detect([Set(new(Conditions.Equipped(1,false),excluded))]).ProfileId=="vanilla.1","Excluded keywords must not identify the weapon.");
        var negative=Conditions.EquippedKeyword("WeapTypeRapier",false);negative["negated"]=true;
        check(WeaponDetector.Detect([Set(new(negative))],"Rapier").ProfileId=="source","A name hint must not reintroduce an excluded weapon class.");
        var disabled=Conditions.EquippedKeyword("WeapTypePike",false);disabled["disabled"]=true;
        check(WeaponDetector.Detect([Set(new(disabled))]).ProfileId=="source","Disabled conditions must not identify a weapon.");
        var either=WeaponDetector.Detect([Set(new(Group("OR",Conditions.Equipped(2,false),Conditions.Equipped(2,true))))]);
        check(either.ProfileId=="vanilla.2"&&either.Hand=="Either","Either-hand alternatives were narrowed to one hand.");
        var dual=WeaponDetector.Detect([Set(new(Conditions.Equipped(2,false),Conditions.Equipped(2,true)))]);
        check(dual.Hand=="Both","Dual-wield conditions should require both hands.");
        var conditionalDual=WeaponDetector.Detect([Set(new(Conditions.Equipped(2,false))),Set(new(Conditions.Equipped(2,false),Conditions.Equipped(2,true)))]);
        check(conditionalDual.Hand=="Right","A dual-wield subfolder must not restrict a whole bundle to dual wield.");
        var crossHands=WeaponDetector.Detect([Set(new(Conditions.Equipped(2,false))),Set(new(Conditions.Equipped(2,true)))]);
        check(crossHands.Hand=="Either","Separate left/right alternatives must not be restricted to one hand.");
        var alias=WeaponDetector.Detect([Set(new(Conditions.EquippedKeyword("WeapTypeQuarterstaff",false)))]);
        check(alias.ProfileId=="aa.quarterstaff"&&alias.Keyword=="WeapTypeQuarterstaff","Detected keyword aliases must retain their literal spelling.");
        check(WeaponDetector.Detect([Set(new())],"Rapier moveset").Confidence.StartsWith("Name hint"),"Name guesses must be labeled separately from condition evidence.");
        var presetSet=Set(new(new JsonObject{["condition"]="PRESET",["Preset"]="Weapon"}));
        presetSet.ReplacerConfig=new(){["conditionPresets"]=new JsonArray(new JsonObject{["name"]="Weapon",["conditions"]=new JsonArray(Conditions.EquippedKeyword("WeapTypePike",false))})};
        check(WeaponDetector.Detect([presetSet]).ProfileId=="aa.pike","Weapon presets were ignored.");
        var custom=Conditions.For(new Rule{WeaponProfile="aa.whip",Hand="Both",Actors="Source"});
        check(custom.Count==1&&custom[0]?["condition"]?.GetValue<string>()=="AND"&&custom[0]?["Conditions"]?[1]?["Left hand"]?.GetValue<bool>()==true,"Both hands must generate AND, not OR.");
        check(Conditions.For(new Rule{WeaponProfile="source",Actors="Source"}).Count==0,"Source-only migration must add no weapon or actor restrictions.");
        check(Conditions.For(new Rule{WeaponType=3})[0]?["Type"]?["value"]?.GetValue<int>()==3,"Older numeric weapon rules lost compatibility.");
        throws(()=>Conditions.For(new Rule{WeaponProfile="keyword",WeaponKeyword=""}),"An empty custom keyword must fail.");
        var values=Conditions.ParseLegacy("NOT IsActorValuePercentageLessThan(24, 0.36) AND\nValueEqualTo(\"Globals.esp\" | 0X123, 1) AND\nRandom(0.2) AND\nIsEquippedShout(\"Shouts.esp\" | 0x55)");
        check(values[0]?["Value A"]?["actorValueType"]?.GetValue<string>()=="Percentage"&&values[0]?["negated"]?.GetValue<bool>()==true,"DAR actor-value semantics changed.");
        check(values[1]?["Value A"]?["form"]?["formID"]?.GetValue<string>()=="123","DAR globals must remain form-backed numeric values.");
        check(values[2]?["Comparison"]?.GetValue<string>()=="<="&&values[2]?["State"]?["shouldResetOnLoopOrEcho"]?.GetValue<bool>()==false,"Random migration changed legacy default behavior.");
        check(values[3]?["condition"]?.GetValue<string>()=="IsEquippedPower"&&values[3]?["Power"]!=null,"Equipped shout legacy alias was mapped incorrectly.");
        throws(()=>Conditions.ParseLegacy("IsInCombat() AND\nUnknownCondition(7)"),"Unknown DAR clauses must not yield a partial conversion.");
        check(Exporter.MapSlotPath("_variants_mco_attack1/MCO_ArbitraryAlternative.hkx")=="_variants_BFCO_attack1/MCO_ArbitraryAlternative.hkx","Variant asset names are arbitrary and must not be treated as behavior slots.");

        string source=Path.Combine(scratch,"dar-source"),folder=Path.Combine(source,"meshes/actors/character/animations/DynamicAnimationReplacer/_CustomConditions/100");Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder,"mco_attack1.hkx"),[1,2,3,4]);
        File.WriteAllText(Path.Combine(folder,"_conditions.txt"),"IsEquippedRightHasKeyword(\"NewArmoury.esp\" | 0x801) AND\nIsActorBase(\"Skyrim.esm\" | 0x7)");
        string patch=Path.Combine(scratch,"config-patch"),patchFolder=Path.Combine(patch,"Meshes/Actors/Character/Animations/DynamicAnimationReplacer/_CustomConditions/100");Directory.CreateDirectory(patchFolder);
        Json.Write(Path.Combine(patchFolder,"user.json"),new JsonObject{["priority"]=550,["conditions"]=new JsonArray(Conditions.EquippedKeyword("WeapTypeWhip",false)),["replacementAnimDatas"]=new JsonArray(new JsonObject{["path"]="Data\\meshes\\actors\\character\\animations\\DynamicAnimationReplacer\\_CustomConditions\\100\\mco_attack1.hkx",["disabled"]=true})});
        var physical=Scanner.Scan([source,patch]);var profiled=Scanner.Scan([source,patch],applyProfileOverrides:true);
        check(WeaponDetector.Detect(physical.Sets).ProfileId=="aa.rapier","All-installed scan must not guess external patch order.");
        check(WeaponDetector.Detect(profiled.Sets).ProfileId=="aa.whip"&&profiled.Sets[0].Config["priority"]!.GetValue<int>()==550,"A config-only profile patch was not applied to DAR sources.");
        check(profiled.Sets[0].ConfigurationSources.Any(p=>p.StartsWith(patch)),"Applied config provenance missing.");
        string localUser=Path.Combine(folder,"user.json");Json.Write(localUser,new JsonObject{["priority"]=150});
        var localEffective=Scanner.Scan([source]).Sets[0];check(localEffective.Config["conditions"]!.AsArray().Count==0,"OAR user data must replace author data, not merge omitted conditions.");File.Delete(localUser);
        var draft=WeaponDetector.Draft("Whip",profiled.Sets,[]);draft.Name="Custom whip";draft.MinimumLevel=10;
        var project=new Project{Name="Whip export",SourceRoots=[source,patch],ApplyProfileOverrides=true,Rules=[draft]};
        var result=Exporter.Build(project,profiled,Path.Combine(scratch,"features-out"));
        var output=Directory.GetFiles(result.Folder,"config.json",SearchOption.AllDirectories).Select(Json.ReadObject).Single(c=>c["priority"]!=null);
        string mapped=output["replacementAnimDatas"]![0]!["path"]!.GetValue<string>();
        check(mapped.Contains("OpenAnimationReplacer")&&mapped.EndsWith("BFCO_attack1.hkx"),"Per-animation OAR paths must point to exported BFCO files.");
        check(output["conditions"]!.ToJsonString().Contains("WeapTypeWhip")&&!output["conditions"]!.ToJsonString().Contains("WeapTypeRapier"),"Export reparsed stale DAR conditions instead of the effective override.");
        check(File.Exists(Path.Combine(result.Folder,"moveset-forge.conversion.json")),"Export conversion report missing.");
        foreach(var file in Directory.GetFiles(result.Folder,"*.hkx",SearchOption.AllDirectories))check(File.ReadAllBytes(file).SequenceEqual(new byte[]{1,2,3,4}),"Conversion changed HKX bytes.");
        var nativeRule=new Rule{Name="Native migration",SetIds=physical.Sets.Select(s=>s.Id).ToList(),WeaponProfile="source",Actors="Source"};
        var native=new Project{Name="Native",TargetFramework="OAR",Rules=[nativeRule]};
        var migrated=Exporter.Build(native,physical,Path.Combine(scratch,"features-out"));
        var nativeConfig=Directory.GetFiles(migrated.Folder,"config.json",SearchOption.AllDirectories).Select(Json.ReadObject).Single(c=>c["priority"]!=null);
        check(File.Exists(Directory.GetFiles(migrated.Folder,"mco_attack1.hkx",SearchOption.AllDirectories).Single()),"Native OAR migration renamed an attack slot.");
        check(nativeConfig["conditions"]!.ToJsonString()==physical.Sets[0].Config["conditions"]!.ToJsonString(),"Native source targeting changed DAR condition logic.");
        File.WriteAllBytes(Path.Combine(folder,"MCO_Unknown.hkx"),[1]);var unknown=Scanner.Scan([source]);
        check(ConversionPlan.Inspect(unknown.Sets[0],"BFCO").Errors.Count>0&&ConversionPlan.Inspect(unknown.Sets[0],"OAR").Errors.Count==0,"Unsupported MCO slots must block BFCO conversion but remain valid native OAR files.");
        File.WriteAllText(Path.Combine(folder,"_conditions.txt"),"UnknownCondition(7)");
        check(Scanner.Scan([source]).Sets[0].Format=="Invalid","Unsupported DAR input was accepted.");
        check(Scanner.Scan([source,patch],applyProfileOverrides:true).Sets[0].Format=="DAR","Valid native override should replace unsupported legacy text.");
        string actor=Path.Combine(source,"meshes/actors/character/animations/DynamicAnimationReplacer/Skyrim.esm/00000007");Directory.CreateDirectory(actor);File.WriteAllBytes(Path.Combine(actor,"idle.hkx"),[8]);
        var actorSet=Scanner.Scan([source]).Sets.Single(s=>s.Folder==actor);
        check(actorSet.Format=="DAR"&&actorSet.Config["conditions"]!.AsArray().Any(Conditions.IsPlayerSelector),"DAR plugin/actor-base directories were not imported.");
        var vanillaLib=new Library{Sets=[actorSet]};var vanillaProject=new Project{Rules=[new Rule{SetIds=[actorSet.Id],WeaponProfile="source",Actors="Source"}]};
        check(Exporter.Validate(vanillaProject,vanillaLib).Any(i=>i.Severity=="Error"&&i.Message.Contains("attack slots")),"Vanilla-slot animations cannot be presented as authored BFCO movesets.");
        vanillaProject.TargetFramework="OAR";check(!Exporter.Validate(vanillaProject,vanillaLib).Any(i=>i.Severity=="Error"),"Native migration should allow non-combat DAR slots.");
        actorSet.Format="Loose";check(Exporter.Validate(vanillaProject,vanillaLib).Any(i=>i.Severity=="Error"&&i.Message.Contains("folder structure")),"Unstructured HKX data must not be exported to an invented native OAR insertion point.");
        var fh=new Library{Sets=new[]{"FH-Warlord-Normal","FH-Warlord-Power","FH-Kensei-Normal"}.Select(n=>new Moveset{Folder=Path.Combine(scratch,"For Honor in Skyrim - BFCO/meshes/actors/character/animations/OpenAnimationReplacer/BFCO",n),ReplacerFolder="shared-bfco",Name=n}).ToList()};
        var bundles=Bundles.Create(fh);check(bundles.Count==2&&bundles.Single(b=>b.Name.EndsWith("Warlord")).Sets.Count==2,"Named For Honor families were merged or incompletely grouped.");
    }
}
