// GPL-3.0. Decoder adapted from the documented PyNifly anim_fo4.py algorithms.
// See THIRD_PARTY_NOTICES.md for the pinned sources and upstream attribution.
using System.Numerics;

namespace MovesetForge.Core;

public static class HavokSplineDecoder
{
    public static BoneTransform[][] DecodeBlocks(byte[] data,int frames,int maskSize,int blocks,int maxFrames,uint[] offsets,int tracks)
    {
        if(blocks<1||blocks>10000||offsets.Length<blocks||maskSize<tracks*4||maxFrames<2)throw new InvalidDataException("Invalid spline block table.");
        var output=new BoneTransform[frames][];int stride=maxFrames-1;
        for(int block=0;block<blocks;block++)
        {
            int start=checked((int)offsets[block]);int end=block+1<blocks?checked((int)offsets[block+1]):data.Length;
            if(start<0||end>data.Length||end<=start||start+maskSize>end)throw new InvalidDataException("Spline block outside file.");
            int count=Math.Min(block==blocks-1?maxFrames:stride,frames-block*stride);if(count<=0)throw new InvalidDataException("Excess spline blocks.");
            var r=new Reader(data.AsSpan(start,end-start).ToArray()){At=maskSize};r.Align(4);
            var local=new BoneTransform[count][];for(int f=0;f<count;f++)local[f]=new BoneTransform[tracks];
            for(int t=0;t<tracks;t++)
            {
                int mask=start+t*4;byte quant=data[mask];int pq=quant&3,rq=(quant>>2)&15,sq=(quant>>6)&3;
                if(pq>1||sq>1)throw new NotSupportedException("Unsupported scalar compression.");
                var positions=Vectors(r,data[mask+1],pq,count,0);r.Align(4);
                var rotations=Rotations(r,data[mask+2],rq,count);r.Align(4);
                var scales=Vectors(r,data[mask+3],sq,count,1);r.Align(4);
                for(int f=0;f<count;f++)
                {
                    local[f][t]=new() {Translation=positions[f],Rotation=rotations[f],Scale=scales[f]};
                }
            }
            for(int f=0;f<count;f++)output[block*stride+f]=local[f];
        }
        if(output.Any(f=>f==null))throw new InvalidDataException("Incomplete spline frames.");return output;
    }
    private static Vector3[] Vectors(Reader r,byte flags,int quant,int frames,float identity)
    {
        var output=new Vector3[frames];bool dynamic=(flags&0x70)!=0;int cps=0,degree=0;byte[] knots=[];
        if(dynamic){cps=r.U16()+1;degree=r.U8();knots=r.Bytes(cps+degree+1);CheckCurve(cps,degree,knots);r.Align(4);}
        float[] lo=new float[3],hi=new float[3];var values=new float[3][];
        for(int axis=0;axis<3;axis++)
        {
            if((flags&(0x10<<axis))!=0){lo[axis]=r.F32();hi[axis]=r.F32();values[axis]=new float[cps];}
            else lo[axis]=(flags&(1<<axis))!=0?r.F32():identity;
        }
        for(int cp=0;cp<cps;cp++)for(int axis=0;axis<3;axis++)if(values[axis]!=null)values[axis][cp]=lo[axis]+(hi[axis]-lo[axis])*(quant==0?r.U8()/255f:r.U16()/65535f);
        for(int f=0;f<frames;f++) {float Value(int a)=>values[a]==null?lo[a]:Evaluate(values[a],knots,degree,f);output[f]=new(Value(0),Value(1),Value(2));}
        return output;
    }
    private static Quaternion[] Rotations(Reader r,byte flags,int quant,int frames)
    {
        var output=new Quaternion[frames];if(flags==0){Array.Fill(output,Quaternion.Identity);return output;}
        int align=quant switch{0 or 5=>4,2 or 4=>2,_=>1};
        if((flags&0xf0)==0){r.Align(align);Array.Fill(output,ReadQuaternion(r,quant));return output;}
        int cps=r.U16()+1,degree=r.U8();var knots=r.Bytes(cps+degree+1);CheckCurve(cps,degree,knots);r.Align(align);
        var components=new[]{new float[cps],new float[cps],new float[cps],new float[cps]};Quaternion previous=Quaternion.Identity;
        for(int i=0;i<cps;i++)
        {
            var q=ReadQuaternion(r,quant);if(i>0&&Quaternion.Dot(previous,q)<0)q=-q;previous=q;
            components[0][i]=q.X;components[1][i]=q.Y;components[2][i]=q.Z;components[3][i]=q.W;
        }
        for(int f=0;f<frames;f++)output[f]=Quaternion.Normalize(new(Evaluate(components[0],knots,degree,f),Evaluate(components[1],knots,degree,f),Evaluate(components[2],knots,degree,f),Evaluate(components[3],knots,degree,f)));
        return output;
    }
    public static Quaternion ReadQuaternion(Reader r,int type)
    {
        if(type==5)return Quaternion.Normalize(new(r.F32(),r.F32(),r.F32(),r.F32()));
        float[] small=new float[3];int omitted;bool sign;
        if(type==1)
        {
            ulong bits=0;for(int i=0;i<5;i++)bits|=(ulong)r.U8()<<(i*8);
            for(int i=0;i<3;i++)small[i]=((int)((bits>>(i*12))&4095)-2049)*0.000345436f;
            omitted=(int)(bits>>36)&3;sign=((bits>>38)&1)!=0;
        }
        else if(type==2)
        {
            int x=r.U16(),y=r.U16(),z=r.U16();omitted=((y>>14)&2)|((x>>15)&1);sign=(z&0x8000)!=0;
            small[0]=((x&32767)-16383)*0.000043161f;small[1]=((y&32767)-16383)*0.000043161f;small[2]=((z&32767)-16383)*0.000043161f;
        }
        else if(type==0)
        {
            uint bits=r.U32();double w=1-Math.Pow(((bits>>18)&1023)/1023.0,2);double encoded=bits&0x3ffff,phi=Math.Floor(Math.Sqrt(encoded));double theta=phi>0?Math.PI/4*(encoded-phi*phi)/phi:0;phi*=Math.PI/2/511;
            double len=Math.Sqrt(Math.Max(0,1-w*w));float[] q=[(float)(Math.Sin(phi)*Math.Cos(theta)*len),(float)(Math.Sin(phi)*Math.Sin(theta)*len),(float)(Math.Cos(phi)*len),(float)w];for(int i=0;i<4;i++)if((bits&(1u<<(28+i)))!=0)q[i]=-q[i];return Quaternion.Normalize(new(q[0],q[1],q[2],q[3]));
        }
        else throw new NotSupportedException("Quaternion compression "+type+" is not supported.");
        float large=MathF.Sqrt(Math.Max(0,1-small.Sum(v=>v*v)));if(sign)large=-large;
        float[] result=new float[4];for(int i=0,j=0;i<4;i++)result[i]=i==omitted?large:small[j++];return Quaternion.Normalize(new(result[0],result[1],result[2],result[3]));
    }
    private static void CheckCurve(int count,int degree,byte[] knots)
    {if(degree>8||count<=degree||count>65536||knots.Where((v,i)=>i>0&&v<knots[i-1]).Any())throw new InvalidDataException("Invalid spline knots.");}
    private static float Evaluate(float[] cp,byte[] knots,int degree,int time)
    {
        int n=cp.Length-1,span=degree;
        if(time>=knots[n+1])span=n;else {int lo=degree,hi=n;while(lo<hi){int m=(lo+hi+1)/2;if(knots[m]<=time)lo=m;else hi=m-1;}span=lo;}
        Span<float> d=stackalloc float[9];for(int j=0;j<=degree;j++)d[j]=cp[Math.Clamp(span-degree+j,0,n)];
        for(int level=1;level<=degree;level++)for(int j=degree;j>=level;j--){float low=knots[j+span-degree],high=knots[j+span-level+1];float a=high>low?Math.Clamp((time-low)/(high-low),0,1):0;d[j]=(1-a)*d[j-1]+a*d[j];}
        return d[degree];
    }
    public sealed class Reader(byte[] bytes)
    {
        public int At;
        public byte U8(){Need(1);return bytes[At++];}
        public ushort U16(){Need(2);ushort v=System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(At));At+=2;return v;}
        public uint U32(){Need(4);uint v=System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(At));At+=4;return v;}
        public float F32(){float v=BitConverter.UInt32BitsToSingle(U32());if(!float.IsFinite(v))throw new InvalidDataException("Non-finite spline value.");return v;}
        public byte[] Bytes(int size){Need(size);var v=bytes.AsSpan(At,size).ToArray();At+=size;return v;}
        public void Align(int size){At=(At+size-1)&~(size-1);Need(0);}
        private void Need(int n){if(n<0||At<0||At>bytes.Length-n)throw new InvalidDataException("Truncated spline payload.");}
    }
}
