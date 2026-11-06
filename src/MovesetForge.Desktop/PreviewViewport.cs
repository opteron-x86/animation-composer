using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MovesetForge.Core;

namespace MovesetForge.Desktop;

public sealed class PreviewViewport:Control
{
    public PreviewClip? Animation {get;set;}
    public double Time {get;set;}
    public bool InPlace {get;set;}=true;
    public bool ShowWeapon {get;set;}=true;
    public string EmptyTitle {get;set;}="Select a moveset to inspect its animations";
    public string EmptyDetail {get;set;}="Choose your skeleton once, then select a clip.";
    public string Caption {get;set;}="Selected clip";
    public string TimeLabel {get;set;}="";
    private double yaw=-0.5,pitch=0.22,zoom=1;
    private Point? pointer;
    public PreviewViewport()
    {
        MinHeight=140;ClipToBounds=true;
        PointerPressed+=(_,e)=>{pointer=e.GetPosition(this);e.Pointer.Capture(this);};
        PointerReleased+=(_,e)=>{pointer=null;e.Pointer.Capture(null);};
        PointerMoved+=(_,e)=>{if(pointer is {} p){var q=e.GetPosition(this);yaw+=(q.X-p.X)*.008;pitch=Math.Clamp(pitch+(q.Y-p.Y)*.005,-.6,1);pointer=q;InvalidateVisual();}};
        PointerWheelChanged+=(_,e)=>{zoom=Math.Clamp(zoom*Math.Exp(e.Delta.Y*.1),.2,4);InvalidateVisual();};
    }
    public void ResetCamera(){yaw=-.5;pitch=.22;zoom=1;InvalidateVisual();}
    public override void Render(DrawingContext context)
    {
        base.Render(context);var bounds=new Rect(Bounds.Size);context.FillRectangle(Brush.Parse("#10171F"),bounds);
        var clip=Animation;
        if(clip==null)
        {
            DrawText(context,EmptyTitle,new Point(28,Bounds.Height/3),18,"#AEB9C5",Bounds.Width-56);
            DrawText(context,EmptyDetail,new Point(28,Bounds.Height/3+42),13,"#A6B6C6",Bounds.Width-56);return;
        }
        var world=clip.Sample(Time,false);var rest=BoneTransform.ComputeWorld(clip.Skeleton.Rest,clip.Skeleton.Parents);
        float minZ=rest.Min(p=>p.Translation.Z),maxZ=rest.Max(p=>p.Translation.Z);double height=Math.Max(50,maxZ-minZ),scale=Math.Min(Bounds.Width/(height*1.25),Bounds.Height/(height*1.55))*zoom;
        Vector3 offset=InPlace?new(world[0].Translation.X,world[0].Translation.Y,0):Vector3.Zero;
        (Point Point,double Depth) Project(Vector3 v)
        {
            v-=offset;double x=v.X*Math.Cos(yaw)-v.Y*Math.Sin(yaw),y=v.X*Math.Sin(yaw)+v.Y*Math.Cos(yaw),z=v.Z-minZ;
            double py=z*Math.Cos(pitch)-y*Math.Sin(pitch),depth=y*Math.Cos(pitch)+z*Math.Sin(pitch);
            return(new Point(Bounds.Width/2+x*scale,Bounds.Height*.86-py*scale),depth);
        }
        var gridPen=new Pen(Brush.Parse("#253342"),1);
        for(int n=-5;n<=5;n++){var a=Project(new Vector3(n*35,-175,minZ)+offset).Point;var b=Project(new Vector3(n*35,175,minZ)+offset).Point;context.DrawLine(gridPen,a,b);a=Project(new Vector3(-175,n*35,minZ)+offset).Point;b=Project(new Vector3(175,n*35,minZ)+offset).Point;context.DrawLine(gridPen,a,b);}
        var links=new List<(int Index,int Parent,double Depth)>();
        bool humanoid=clip.Skeleton.Bones.Any(n=>n.StartsWith("NPC Pelvis [",StringComparison.Ordinal));
        bool VisibleBone(int i)
        {
            string name=clip.Skeleton.Bones[i];
            return !humanoid||(name.StartsWith("NPC ",StringComparison.Ordinal)&&new[]{"Pelvis [","Spine [","Spine1 [","Spine2 [","Neck [","Head [","Clavicle [","UpperArm [","Forearm [","Hand [","Thigh [","Calf [","Foot [","Toe0 ["}.Any(name.Contains));
        }
        for(int i=0;i<world.Length;i++)
        {if(!VisibleBone(i))continue;int parent=clip.Skeleton.Parents[i];while(parent>=0&&!VisibleBone(parent))parent=clip.Skeleton.Parents[parent];if(parent>=0)links.Add((i,parent,Project(world[i].Translation).Depth));}
        foreach(var link in links.OrderBy(l=>l.Depth))
        {
            string name=clip.Skeleton.Bones[link.Index];bool main=true;
            var a=Project(world[link.Parent].Translation).Point;var b=Project(world[link.Index].Translation).Point;
            if(Math.Abs(a.X-b.X)+Math.Abs(a.Y-b.Y)<.1)continue;
            string color=main?"#C8D4DC":"#60788A";
            double width=main?Math.Clamp(scale*3,2,11):1.2;
            context.DrawLine(new Pen(Brush.Parse(color),width, lineCap:PenLineCap.Round),a,b);
            if(main)context.DrawEllipse(Brush.Parse("#E5BD71"),null,b,Math.Clamp(scale*1.6,1.5,5),Math.Clamp(scale*1.6,1.5,5));
        }
        int head=Array.FindIndex(clip.Skeleton.Bones,n=>n.StartsWith("NPC Head ["));
        if(head>=0){var position=Vector3.Transform(new Vector3(0,0,3),world[head]);context.DrawEllipse(Brush.Parse("#AFC0CE"),new Pen(Brush.Parse("#E5BD71"),1),Project(position).Point,scale*5,scale*7);}
        if(ShowWeapon)
        {
            int bone=Array.FindIndex(clip.Skeleton.Bones,n=>n.Equals("Weapon",StringComparison.OrdinalIgnoreCase));
            if(bone<0)bone=Array.FindIndex(clip.Skeleton.Bones,n=>n.Contains("R Hand"));
            if(bone>=0){var t=world[bone];var tip=Vector3.Transform(new Vector3(0,70,0),t);context.DrawLine(new Pen(Brush.Parse("#EDC47C"),3),Project(t.Translation).Point,Project(tip).Point);}
        }
        DrawText(context,"DRAG TO ORBIT   ·   SCROLL TO ZOOM",new Point(18,18),10,"#7C8D9C");
        DrawText(context,Caption,new Point(18,38),11,"#E5BD71");
        DrawText(context,TimeLabel,new Point(18,Bounds.Height-31),13,"#C8D4DC");
        DrawText(context,"Skeletal preview · weapon guide",new Point(18,Bounds.Height-52),11,"#7C8D9C");
    }
    private static void DrawText(DrawingContext c,string text,Point point,double size,string color,double width=0)
    {var formatted=new FormattedText(text,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Inter"),size,Brush.Parse(color));if(width>0)formatted.MaxTextWidth=width;c.DrawText(formatted,point);}
}
