using System.Numerics;

namespace MovesetForge.Core;

public struct BoneTransform
{
    public Vector3 Translation;
    public Quaternion Rotation;
    public Vector3 Scale;
    public static readonly BoneTransform Identity=new(){Rotation=Quaternion.Identity,Scale=Vector3.One};

    public static BoneTransform Interpolate(BoneTransform a,BoneTransform b,float blend)=>new()
    {
        Translation=Vector3.Lerp(a.Translation,b.Translation,blend),
        Rotation=Quaternion.Slerp(a.Rotation,b.Rotation,blend),
        Scale=Vector3.Lerp(a.Scale,b.Scale,blend)
    };

    public static Matrix4x4[] ComputeWorld(BoneTransform[] local,int[] parents)
    {
        if(local.Length!=parents.Length)throw new InvalidDataException("Skeleton hierarchy does not match the pose.");
        var world=new Matrix4x4[local.Length];
        for(int i=0;i<local.Length;i++)
        {
            var bone=local[i];
            var matrix=Matrix4x4.CreateScale(bone.Scale)*Matrix4x4.CreateFromQuaternion(bone.Rotation)*Matrix4x4.CreateTranslation(bone.Translation);
            int parent=parents[i];if(parent>=i||parent<-1)throw new InvalidDataException("Invalid skeleton hierarchy.");
            // System.Numerics uses row vectors. Keep the full affine matrix so rotated
            // children retain their parent's nonuniform scale, including resulting shear.
            world[i]=parent<0?matrix:matrix*world[parent];
        }
        return world;
    }
}
