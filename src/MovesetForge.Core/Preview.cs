using System.Numerics;
using HKX2;

namespace MovesetForge.Core;

public sealed record SkeletonData(string Name,string[] Bones,int[] Parents,BoneTransform[] Rest);
public sealed record EventMarker(float Time,string Text);
public sealed class PreviewClip
{
    public required SkeletonData Skeleton {get;init;}
    public required BoneTransform[][] Frames {get;init;}
    public float Duration {get;init;}
    public float FrameDuration {get;init;}
    public List<EventMarker> Events {get;init;}=[];
    public string Description {get;init;}="";
    public Matrix4x4[] Sample(double time,bool loop=true)=>BoneTransform.ComputeWorld(SampleLocal(time,loop),Skeleton.Parents);
    public BoneTransform[] SampleLocal(double time,bool loop=true)
    {
        double t=loop&&Duration>0?((time%Duration)+Duration)%Duration:Math.Clamp(time,0,Duration);
        double f=FrameDuration>0?t/FrameDuration:0;int a=Math.Clamp((int)f,0,Frames.Length-1),b=Math.Min(a+1,Frames.Length-1);float blend=(float)Math.Clamp(f-a,0,1);
        var locals=new BoneTransform[Skeleton.Bones.Length];
        for(int i=0;i<locals.Length;i++)locals[i]=BoneTransform.Interpolate(Frames[a][i],Frames[b][i],blend);
        return locals;
    }
}

public static class Preview
{
    public static hkaAnimationContainer Read(string file)
    {
        if(new FileInfo(file).Length>128*1024*1024)throw new InvalidDataException("HKX exceeds the 128 MiB preview limit.");
        using var stream=File.OpenRead(file);
        var root=new PackFileDeserializer().Deserialize(new BinaryReaderEx(stream));
        if(root is hkaAnimationContainer direct)return direct;
        if(root is hkRootLevelContainer container)
            return container.m_namedVariants.Select(n=>n?.m_variant).OfType<hkaAnimationContainer>().FirstOrDefault()??throw new InvalidDataException("This HKX contains behavior or physics data, not a playable animation/skeleton.");
        throw new InvalidDataException("No animation container in this HKX.");
    }
    public static SkeletonData Skeleton(string file)
    {
        var s=Read(file).m_skeletons.OrderByDescending(s=>s.m_bones.Count).FirstOrDefault()??throw new InvalidDataException("No skeleton in the selected HKX.");
        if(s.m_bones.Count is 0 or >4096||s.m_referencePose.Count!=s.m_bones.Count||s.m_parentIndices.Count!=s.m_bones.Count)throw new InvalidDataException("Skeleton bone tables disagree.");
        for(int i=0;i<s.m_bones.Count;i++)if(s.m_parentIndices[i]>=i||s.m_parentIndices[i]<-1)throw new InvalidDataException("Invalid skeleton hierarchy.");
        return new(s.m_name,s.m_bones.Select(b=>b.m_name).ToArray(),s.m_parentIndices.Select(i=>(int)i).ToArray(),s.m_referencePose.Select(Transform).ToArray());
    }
    private static BoneTransform Transform(Matrix4x4 m)
    {
        return new() {Translation=new(m.M11,m.M12,m.M13),Rotation=Quaternion.Normalize(new(m.M21,m.M22,m.M23,m.M24)),Scale=new(m.M31,m.M32,m.M33)};
    }
    public static PreviewClip Load(string file,SkeletonData skeleton)
    {
        var container=Read(file);var animation=container.m_animations.FirstOrDefault()??throw new InvalidDataException("No playable animation in this HKX.");
        if(!float.IsFinite(animation.m_duration)||animation.m_duration<=0)throw new InvalidDataException("Animation has no positive finite duration.");
        var binding=container.m_bindings.FirstOrDefault(b=>ReferenceEquals(b.m_animation,animation))??container.m_bindings.FirstOrDefault();
        if(binding?.m_blendHint!=0 && binding!=null)throw new NotSupportedException("Additive animation needs a base pose; standalone preview is not supported for this clip.");
        int tracks=animation.m_numberOfTransformTracks;
        if(tracks<=0||tracks>4096)throw new InvalidDataException("Invalid transform track count.");
        BoneTransform[][] raw;float frameDuration;
        if(animation is hkaSplineCompressedAnimation s)
        {
            if(s.m_numFrames<=0||s.m_numFrames>18000||(long)s.m_numFrames*tracks>6000000)throw new InvalidDataException("Clip exceeds preview frame limits.");
            var data=s.m_data.ToArray();
            foreach(var start in s.m_blockOffsets.DefaultIfEmpty(0u))
            {
                if(start+(long)tracks*4>data.Length)throw new InvalidDataException("Truncated spline mask table.");
                for(int i=0;i<tracks;i++)
                {
                    int at=checked((int)start+i*4);int q=(data[at]>>2)&15;
                    if(data[at+2]!=0&&q is not (0 or 1 or 2 or 5))throw new NotSupportedException($"Quaternion compression {q} is not supported by the preview decoder. Export still preserves this file.");
                }
            }
            raw=HavokSplineDecoder.DecodeBlocks(data,s.m_numFrames,s.m_maskAndQuantizationSize,s.m_numBlocks,s.m_maxFramesPerBlock,s.m_blockOffsets.ToArray(),tracks);
            frameDuration=s.m_frameDuration>0?s.m_frameDuration:animation.m_duration/Math.Max(1,raw.Length-1);
        }
        else if(animation is hkaInterleavedUncompressedAnimation inter)
        {
            if(inter.m_transforms.Count%tracks!=0||inter.m_transforms.Count==0||inter.m_transforms.Count>6000000)throw new InvalidDataException("Invalid uncompressed track table.");
            raw=inter.m_transforms.Chunk(tracks).Select(f=>f.Select(Transform).ToArray()).ToArray();frameDuration=animation.m_duration/Math.Max(1,raw.Length-1);
        }
        else throw new NotSupportedException("Preview does not support "+animation.GetType().Name+".");
        int[] map=binding?.m_transformTrackToBoneIndices.Count>0?binding.m_transformTrackToBoneIndices.Select(n=>(int)n).ToArray():Enumerable.Range(0,tracks).ToArray();
        if(map.Length!=tracks||map.Any(i=>i<0||i>=skeleton.Bones.Length))throw new InvalidDataException($"Skeleton mismatch: this clip has {tracks} transform tracks and requires bone indices through {map.DefaultIfEmpty(-1).Max()}; the selected skeleton has {skeleton.Bones.Length} bones. Choose its matching skeleton.hkx or skeleton_female.hkx.");
        var frames=new BoneTransform[raw.Length][];
        for(int f=0;f<raw.Length;f++) {frames[f]=(BoneTransform[])skeleton.Rest.Clone();for(int t=0;t<tracks;t++)frames[f][map[t]]=raw[f][t];}
        var events=animation.m_annotationTracks.SelectMany(t=>t.m_annotations.Select(a=>new EventMarker(a.m_time,a.m_text))).OrderBy(e=>e.Time).ToList();
        return new() {Skeleton=skeleton,Frames=frames,Duration=animation.m_duration,FrameDuration=frameDuration,Events=events,Description=$"{animation.m_duration:0.00}s · {raw.Length} frames · {tracks} tracks · {events.Count} events"};
    }
}
