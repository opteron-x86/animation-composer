using System.Text.Json;
using MovesetForge.Core;

try
{
    if(args.Length==0) {Console.WriteLine("Animation Composer\nscan <folder> [library.json]\npreview <animation.hkx> <skeleton.hkx>\nbuild <project.json> <output-parent>\ncheck <project.json>");return 0;}
    switch(args[0])
    {
        case "scan":
            var library=Scanner.Scan([args[1]],new Progress<string>(Console.Error.WriteLine));
            if(args.Length>2)Json.Write(args[2],library);
            Console.WriteLine($"{library.FileCount} HKX files; {library.Sets.Count} source groups; {library.Skeletons.Count} skeletons.");
            foreach(var s in library.Sets.Where(s=>s.Framework!="Other"))Console.WriteLine($"{s.Id}  {s.Summary}");
            foreach(var d in library.Diagnostics.Take(20))Console.Error.WriteLine(d);break;
        case "preview":
            var clip=Preview.Load(args[1],Preview.Skeleton(args[2]));var world=clip.Sample(clip.Duration/2);
            if(world.Any(x=>!float.IsFinite(x.Translation.X)||!float.IsFinite(x.Translation.Y)||!float.IsFinite(x.Translation.Z)))throw new InvalidDataException("Non-finite preview coordinates.");
            Console.WriteLine(clip.Description+" · "+clip.Skeleton.Bones.Length+" bones");break;
        case "dump-preview":
            var sample=Preview.Load(args[1],Preview.Skeleton(args[2]));
            Json.Write(args[3],new {bones=sample.Skeleton.Bones,frames=sample.Frames.Select(f=>f.Select(p=>new {translation=new[]{p.Translation.X,p.Translation.Y,p.Translation.Z},rotation=new[]{p.Rotation.X,p.Rotation.Y,p.Rotation.Z,p.Rotation.W},scale=new[]{p.Scale.X,p.Scale.Y,p.Scale.Z}}))});break;
        case "build":case "check":
            var project=JsonSerializer.Deserialize<Project>(File.ReadAllText(args[1]),Json.Options)??throw new InvalidDataException("Empty project.");
            var lib=Scanner.Scan(project.SourceRoots,applyProfileOverrides:project.ApplyProfileOverrides);var issues=Exporter.Validate(project,lib);
            foreach(var i in issues)Console.WriteLine(i.Severity+": "+i.Message);
            if(issues.Any(i=>i.Severity=="Error"))return 2;
            if(args[0]=="build")Console.WriteLine(Exporter.Build(project,lib,args[2]).Archive);break;
        default:throw new ArgumentException("Unknown command.");
    }
    return 0;
}
catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
