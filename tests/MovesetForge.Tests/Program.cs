using System.Text.Json.Nodes;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Numerics;
using MovesetForge.Core;

int passed=0;void Assert(bool condition,string message){if(!condition)throw new Exception(message);passed++;}
void Throws(Action action,string message){try{action();}catch(Exception){passed++;return;}throw new Exception(message);}
string scratch=Path.Combine(Path.GetTempPath(),"moveset-forge-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);Import.CacheRoot=Path.Combine(scratch,"cache");
try
{
    var parent=BoneTransform.Identity;parent.Scale=new(2,3,4);parent.Translation=new(10,0,0);
    var child=BoneTransform.Identity;child.Translation=new(1,1,0);child.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,MathF.PI/2);
    var world=BoneTransform.ComputeWorld([parent,child],[-1,0]);
    Assert(Vector3.Distance(world[1].Translation,new(12,3,0))<0.0001,"Parent nonuniform scale must affect child bone position.");
    Assert(Vector3.Distance(Vector3.Transform(Vector3.UnitX,world[1]),new(12,6,0))<0.0001,"Rotated children must retain nonuniform scale and resulting shear.");
    var scaleEnd=parent;scaleEnd.Scale=new(4,5,6);Assert(BoneTransform.Interpolate(parent,scaleEnd,.5f).Scale==new Vector3(3,4,5),"Scale interpolation collapsed its three axes.");
    using(var memory=new MemoryStream())
    {
        using(var writer=new BinaryWriter(memory,System.Text.Encoding.UTF8,true)){writer.Write(new byte[]{0,0,0,7});writer.Write(2f);writer.Write(3f);writer.Write(4f);}
        var scaled=HavokSplineDecoder.DecodeBlocks(memory.ToArray(),2,4,1,256,[0],1);
        Assert(scaled[0][0].Scale==new Vector3(2,3,4)&&scaled[1][0].Scale==new Vector3(2,3,4),"Spline decoding must retain all scale components.");
    }
    var deserializer=new HKX2.PackFileDeserializer{_header=HKX2.HKXHeader.SkyrimSE()};
    var emptyArray=new HKX2.BinaryReaderEx(false,true,new byte[16]);deserializer.ReadEmptyArray(emptyArray);Assert(emptyArray.Position==16,"Zero-capacity empty HKX array rejected.");
    var invalidArray=new byte[16];invalidArray[8]=1;Throws(()=>deserializer.ReadEmptyArray(new HKX2.BinaryReaderEx(false,true,invalidArray)),"Nonempty array with invalid capacity accepted.");
    var testSkeleton=new SkeletonData("test",["root"],[-1],[BoneTransform.Identity]);
    PreviewClip Clip(params EventMarker[] markers)=>new(){Skeleton=testSkeleton,Frames=[[BoneTransform.Identity],[BoneTransform.Identity]],Duration=2,FrameDuration=2,Events=markers.ToList()};
    var attackFiles=Enumerable.Range(1,3).Select(i=>new AnimationFile{Path="/source/MCO_Attack"+i+".hkx",RelativePath="MCO_Attack"+i+".hkx"}).ToArray();
    var attackClips=new[]{Clip(new(0,"PIE.@SGVI|MCO_NextAttack|3"),new(.5f,"MCO_WinOpen")),Clip(),Clip(new(0,"BFCO_NextIsAttack1"),new(.8f,"BFCO_NextWinStart"))};
    PreviewClip LoadAttack(AnimationFile file)=>attackClips[AttackSlot.FromPath(file.RelativePath)!.Number-1];
    var annotated=AttackSequence.Build(attackFiles,AttackKind.Normal,SequenceOrder.Annotations,LoadAttack);
    Assert(annotated.Steps.Select(s=>s.File.Name).SequenceEqual(new[]{"MCO_Attack1.hkx","MCO_Attack3.hkx"}),"Annotated branches must override numeric order and stop cycles.");
    Assert(Math.Abs(annotated.Steps[0].Length-.5)<0.00001&&Math.Abs(annotated.Duration-2.5)<0.00001,"Chain transition timing or final recovery is wrong.");
    Assert(annotated.At(.5).Index==1&&annotated.At(2.5).LocalTime==2,"Scrubbing across a chain boundary selects the wrong pose.");
    var numbered=AttackSequence.Build(attackFiles,AttackKind.Normal,SequenceOrder.Numbered,LoadAttack);
    Assert(numbered.Steps.Count==3&&numbered.Duration==6,"Numbered mode must include every slot at full length.");
    var missing=AttackSequence.Build(attackFiles.Take(2),AttackKind.Normal,SequenceOrder.Annotations,LoadAttack);
    Assert(missing.Steps.Count==1&&missing.Notes.Any(n=>n.Contains("absent")),"Missing annotated targets must be reported, not silently substituted.");
    var powerFiles=Enumerable.Range(1,2).Select(i=>new AnimationFile{Path="/source/BFCO_PowerAttack"+i+".hkx",RelativePath="BFCO_PowerAttack"+i+".hkx"}).ToArray();
    var power=AttackSequence.Build(powerFiles,AttackKind.Power,SequenceOrder.Annotations,f=>f.Name.EndsWith("1.hkx")?Clip(new(0,"BFCO_NextIsPowerAttack2"),new(.9f,"BFCO_NextPowerWinStart")):Clip());
    Assert(power.Steps.Count==2&&Math.Abs(power.Steps[0].Length-.9)<0.0001,"Power chain must use its separate input window and target.");
    var variantSlot=AttackSlot.FromPath("_variants_BFCO_Attack10/alternative.hkx");Assert(variantSlot is {Number:10,Variant:true,Kind:AttackKind.Normal},"OAR variant directory does not resolve to its attack slot.");
    Assert(AttackSlot.FromPath("BFCO_PowerAttackLoop1.hkx")==null,"Charged loop must not enter ordinary numbered chains.");
    var legacy=Conditions.ParseLegacy("IsEquippedRightType(1) OR\nIsEquippedRightType(3) AND\nNOT IsEquippedLeftType(1)");
    Assert(legacy.Count==2&&legacy[0]?["condition"]?.GetValue<string>()=="OR"&&legacy[0]?["Conditions"]?.AsArray().Count==2,"DAR OR groups must follow OAR semantics.");
    Assert(legacy[1]?["negated"]?.GetValue<bool>()==true,"DAR negation lost.");
    Assert(Conditions.SuggestedWeapons(legacy).SequenceEqual(new[]{1,3})&&!Conditions.SuggestedWeapons(legacy,true).Any(),"Equipment suggestions must include OR choices and exclude negative conditions.");
    Throws(()=>Conditions.ParseLegacy("UnknownPluginCondition(5)"),"Unknown legacy conditions must stop export.");
    Throws(()=>Conditions.ParseLegacy("IsInCombat() OR"),"Dangling OR must fail.");
    Throws(()=>Paths.Under(scratch,"../escape.hkx"),"Parent traversal accepted.");
    Throws(()=>Paths.Under(scratch,"C:/escape.hkx"),"Windows path traversal accepted.");
    Assert(Exporter.MapSlotPath("_variants_mco_attack1/variant.hkx")=="_variants_BFCO_attack1/variant.hkx","Variant folder mapping changed variant filenames.");
    Assert(Exporter.MapSlotPath("MCO_SprintPowerAttack.hkx")=="BFCO_SprintAttackPower.hkx","Sprint slot mapped incorrectly.");
    string source=Path.Combine(scratch,"sources"),root=Path.Combine(source,"meshes/actors/character/animations/OpenAnimationReplacer/Example"),a=Path.Combine(root,"Base"),b=Path.Combine(root,"Special");Directory.CreateDirectory(a);Directory.CreateDirectory(b);
    Json.Write(Path.Combine(root,"config.json"),new JsonObject { ["name"]="Original",["conditionPresets"]=new JsonArray(new JsonObject{["name"]="Shared",["conditions"]=new JsonArray()}) });
    var player=Conditions.WithForm("IsActorBase","Actor base","Skyrim.esm","7");var equipment=Conditions.Equipped(1,false);var effect=Conditions.WithForm("HasMagicEffect","Magic effect","Source.esp","801");
    Json.Write(Path.Combine(a,"config.json"),new JsonObject{["name"]="Base",["priority"]=100,["conditions"]=new JsonArray(player,equipment,effect)});
    Json.Write(Path.Combine(b,"config.json"),new JsonObject{["name"]="Special",["priority"]=105,["conditions"]=new JsonArray()});
    byte[] bytes=Enumerable.Range(0,256).Select(i=>(byte)i).ToArray();File.WriteAllBytes(Path.Combine(a,"MCO_Attack1.hkx"),bytes);File.WriteAllBytes(Path.Combine(b,"MCO_PowerAttack1.hkx"),bytes.Reverse().ToArray());
    string variant=Path.Combine(a,"_variants_mco_attack2");Directory.CreateDirectory(variant);File.WriteAllBytes(Path.Combine(variant,"option.hkx"),bytes);
    var lib=Scanner.Scan([source]);Assert(lib.FileCount==3&&lib.Sets.Count==2,"Scanner failed to retain variant membership.");
    var rule=new Rule{Name="Complete sword",SetIds=lib.Sets.Select(s=>s.Id).ToList(),Actors="Both",WeaponType=1,Priority=20000,MinimumSkill=30};var project=new Project{Name="Test Build",SourceRoots=[source],Rules=[rule]};
    var result=Exporter.Build(project,lib,Path.Combine(scratch,"out"));Assert(result.AnimationCount==3,"Exporter omitted part of the bundle.");
    var exported=Directory.GetFiles(result.Folder,"*.hkx",SearchOption.AllDirectories);Assert(exported.Length==3,"Unexpected exported animation count.");
    var manifest=JsonNode.Parse(File.ReadAllText(Path.Combine(result.Folder,"moveset-forge.manifest.json")))!.AsArray();
    foreach(var entry in manifest){string src=entry!["source"]!.GetValue<string>(),dst=Paths.Under(result.Folder,entry["destination"]!.GetValue<string>());Assert(File.ReadAllBytes(src).SequenceEqual(File.ReadAllBytes(dst)),"Animation bytes were modified.");}
    var configs=Directory.GetFiles(result.Folder,"config.json",SearchOption.AllDirectories).Select(Json.ReadObject).ToList();var outputBase=configs.Single(c=>c["name"]?.GetValue<string>()=="Base");var outputSpecial=configs.Single(c=>c["name"]?.GetValue<string>()=="Special");
    Assert(outputBase["priority"]!.GetValue<int>()<outputSpecial["priority"]!.GetValue<int>(),"Source submod priority order changed.");
    var conditions=outputBase["conditions"]!.AsArray();Assert(!conditions.Any(Conditions.IsPlayerSelector),"Both still restricted to player.");Assert(conditions.Any(c=>c?["condition"]?.GetValue<string>()=="HasMagicEffect"),"Source gameplay dependency was dropped.");Assert(configs.Any(c=>c.ContainsKey("conditionPresets")),"Shared source presets were dropped.");
    var npc=Conditions.For(new Rule{Actors="NPCs"});Assert(npc.Any(n=>n?["condition"]?.GetValue<string>()=="IsForm"&&n?["negated"]?.GetValue<bool>()==true),"NPC condition not emitted.");
    var skill=conditions.Single(c=>c?["condition"]?.GetValue<string>()=="CompareValues");Assert(skill?["Value A"]?["actorValueType"]?.GetValue<string>()=="Base","Skill comparison must use documented actor value semantics.");
    project.Rules.Add(new Rule{Name="Overlapping",SetIds=rule.SetIds,WeaponType=1,Priority=20001});Assert(Exporter.Validate(project,lib).Any(i=>i.Severity=="Error"&&i.Message.Contains("priority")),"Overlapping priority ranges went undetected.");project.Rules.RemoveAt(1);
    Throws(()=>Exporter.Build(project,lib,Path.Combine(source,"out")),"Exporter wrote into the source tree.");
    string badZip=Path.Combine(scratch,"bad.zip");using(var z=ZipFile.Open(badZip,ZipArchiveMode.Create)){using var stream=z.CreateEntry("../evil.hkx").Open();stream.Write(bytes);}
    Throws(()=>Import.Archive(badZip),"Archive traversal accepted.");
    string aliasBsa=Path.Combine(scratch,"aliases.bsa");
    byte[] folderBytes=System.Text.Encoding.ASCII.GetBytes("meshes\\actors\\character\\animations\0"),nameBytes=System.Text.Encoding.ASCII.GetBytes("a.hkx\0b.hkx\0");
    uint payload=(uint)(36+24+1+folderBytes.Length+32+nameBytes.Length);
    using(var w=new BinaryWriter(File.Create(aliasBsa)))
    {
        w.Write(0x00415342u);w.Write(105u);w.Write(36u);w.Write(3u);w.Write(1u);w.Write(2u);w.Write((uint)folderBytes.Length);w.Write((uint)nameBytes.Length);w.Write(0u);
        w.Write(0ul);w.Write(2u);w.Write(0u);w.Write(60ul+(ulong)nameBytes.Length);w.Write((byte)folderBytes.Length);w.Write(folderBytes);
        for(int i=0;i<2;i++){w.Write((ulong)i);w.Write((uint)bytes.Length);w.Write(payload);}w.Write(nameBytes);w.Write(bytes);
    }
    var aliasMembers=BoundedBsa.Read(aliasBsa);Assert(aliasMembers.Count==2,"Shared BSA payload lost a logical file.");
    foreach(var member in aliasMembers){using var s=member.Open();using var m=new MemoryStream();s.CopyTo(m);Assert(m.ToArray().SequenceEqual(bytes),"Shared BSA payload decoded incorrectly.");}
    string duplicateJson=Path.Combine(scratch,"duplicate.json");File.WriteAllText(duplicateJson,"{\"interruptible\":false,\"interruptible\":true}");Assert(Json.ReadObject(duplicateJson)["interruptible"]!.GetValue<bool>()==false,"OAR duplicate-member semantics changed.");
    ImportExportTests.Run(Assert,Throws,scratch);
    if(args.Length>0)
    {
        string mods=args[0],skeleton=Path.Combine(mods,"XP32 Maximum Skeleton Special Extended/meshes/actors/character/character assets/skeleton.hkx");var skel=Preview.Skeleton(skeleton);
        int good=0,bad=0;var failures=new List<string>();
        foreach(var mod in new[]{"BFCO Elden Rim","For Honor in Skyrim"})
        {
            var clips=Directory.GetFiles(Path.Combine(mods,mod),"*",SearchOption.AllDirectories).Where(p=>p.EndsWith(".hkx",StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(p).Contains("attack",StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach(var file in clips)
            {
                try{var clip=Preview.Load(file,skel);foreach(double t in new[]{0.0,clip.Duration/2,clip.Duration}) {var pose=clip.Sample(t,false);Assert(pose.All(p=>float.IsFinite(p.Translation.X)&&float.IsFinite(p.Translation.Y)&&float.IsFinite(p.Translation.Z)),"Nonfinite bone coordinates.");}good++;}
                catch(Exception e){bad++;if(failures.Count<15)failures.Add(Path.GetFileName(file)+": "+e.Message);}
            }
        }
        Console.WriteLine($"Installed attack previews: {good} decoded, {bad} unsupported/failed.");foreach(var f in failures)Console.WriteLine(f);Assert(good>20&&bad==0,"Real animation preview regression.");
        var elden=Scanner.Scan([Path.Combine(mods,"BFCO Elden Rim")]);var axes=elden.Sets.Where(s=>Path.GetFileName(s.Folder) is "15006" or "15007").ToArray();
        Assert(axes.Length==2&&axes[0].BundleKey.Length>0&&axes[0].BundleKey==axes[1].BundleKey,"Conditional axe variants must stay in one complete bundle.");
        var warlord=Scanner.Scan([Path.Combine(mods,"For Honor in Skyrim")]).Sets.Single(s=>s.Folder.EndsWith("/Warlord/8071020"));
        var normalRoute=AttackSequence.Build(warlord.Animations,AttackKind.Normal,SequenceOrder.Annotations,f=>Preview.Load(f.Path,skel));
        Assert(normalRoute.Steps.Select(s=>AttackSlot.FromPath(s.File.RelativePath)!.Number).SequenceEqual(new[]{1,2,3,4,5,6}),"Warlord normal route must follow its authored links.");
        var powerRoute=AttackSequence.Build(warlord.Animations,AttackKind.Power,SequenceOrder.Annotations,f=>Preview.Load(f.Path,skel));
        Assert(powerRoute.Steps.Count>=4&&powerRoute.Steps[1].File.Name.Equals("MCO_PowerAttack7.hkx",StringComparison.OrdinalIgnoreCase),"Warlord power route must follow the branch to attack 7.");
    }
    Console.WriteLine($"PASS: {passed} assertions.");return 0;
}
catch(Exception e){Console.Error.WriteLine("FAIL: "+e);return 1;}
finally{Directory.Delete(scratch,true);}
