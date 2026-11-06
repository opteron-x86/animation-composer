using System.IO.Compression;
using System.Security.Cryptography;

namespace MovesetForge.Core;

public static class Import
{
    public static string CacheRoot {get;set;}=Environment.GetEnvironmentVariable("MOVESETFORGE_CACHE")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MovesetForge","imports");
    public static string Archive(string file,CancellationToken cancel=default)
    {
        file=Path.GetFullPath(file);var info=new FileInfo(file);
        string key=Paths.Id(file)+"-"+info.Length+"-"+info.LastWriteTimeUtc.Ticks;
        string dest=Path.Combine(CacheRoot,Paths.Slug(Path.GetFileNameWithoutExtension(file))+"-"+key);
        if(File.Exists(Path.Combine(dest,".complete")))return dest;
        Directory.CreateDirectory(CacheRoot);string stage=dest+"-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(stage);
        long bytes=0;int count=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Wanted(string name)=>name.EndsWith(".hkx",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(name) is "config.json" or "user.json" or "_conditions.txt";
        void Extract(string name,long size,Func<Stream> open)
        {
            cancel.ThrowIfCancellationRequested();name=name.Replace('\\','/');if(!Wanted(name))return;
            string path=Paths.Under(stage,name);if(!seen.Add(name))throw new InvalidDataException("Duplicate archive path: "+name);
            if(size<0||size>128*1024*1024||(bytes+=size)>4L*1024*1024*1024||++count>100000)throw new InvalidDataException("Archive exceeds animation import limits.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);using var input=open();using var output=new FileStream(path,FileMode.CreateNew);
            var buffer=new byte[81920];long copied=0;int n;while((n=input.Read(buffer))>0){cancel.ThrowIfCancellationRequested();copied+=n;if(copied>size)throw new InvalidDataException("Archive size mismatch.");output.Write(buffer,0,n);}if(copied!=size)throw new InvalidDataException("Archive member truncated.");
        }
        try
        {
            if(file.EndsWith(".bsa",StringComparison.OrdinalIgnoreCase))foreach(var item in BoundedBsa.Read(file))Extract(item.Path,item.Size,item.Open);
            else if(file.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)) {using var zip=ZipFile.OpenRead(file);foreach(var item in zip.Entries)if(!item.FullName.EndsWith('/'))Extract(item.FullName,item.Length,item.Open);}
            else throw new NotSupportedException("Import ZIP or BSA files; extract 7z/RAR downloads to a folder first.");
            File.WriteAllText(Path.Combine(stage,".complete"),file);Directory.Move(stage,dest);return dest;
        }
        catch{Directory.Delete(stage,true);throw;}
    }
}

public sealed record ProfileInfo(string Name,string File);
public static class Profiles
{
    public static List<ProfileInfo> List(string instance)
    {
        string root=Path.GetFileName(instance).Equals("profiles",StringComparison.OrdinalIgnoreCase)?instance:Path.Combine(instance,"profiles");
        return Directory.Exists(root)?Directory.GetDirectories(root).Where(d=>File.Exists(Path.Combine(d,"modlist.txt"))).Select(d=>new ProfileInfo(Path.GetFileName(d),Path.Combine(d,"modlist.txt"))).OrderBy(p=>p.Name).ToList():[];
    }
    public static List<string> EnabledFolders(string mods,ProfileInfo profile)
    {
        var lines=File.ReadAllLines(profile.File);var folders=new List<string>();
        foreach(var line in lines.Reverse())if(line.StartsWith('+')){var name=line[1..].Trim();if(name.EndsWith("_separator",StringComparison.OrdinalIgnoreCase))continue;string path=Paths.Under(mods,name);if(Directory.Exists(path))folders.Add(path);}
        return folders;
    }
}
