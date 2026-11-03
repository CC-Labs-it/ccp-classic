namespace CCP.Classic.Core.Remix;

public static partial class RemixLayouts
{
    private static RemixSlot[] Motion(RemixScene scene,int f) => scene.Layout switch
    {
        "stack"=>Stack(scene,f),"deal"=>Deal(scene,f),"carousel"=>Carousel(scene,f),"hop"=>Hop(scene,f),
        "tunnel"=>Tunnel(scene,f),"pinwheel"=>Pinwheel(scene,f),_=>throw new ArgumentOutOfRangeException(nameof(scene))
    };
    private static RemixSlot[] Stack(RemixScene s,int f)
    {
        var r=Rng(s,"stack");var output=new List<RemixSlot>();var last=Math.Min(7,(int)Math.Floor(f/(45d/8)));
        for(var k=0;k<8;k++)
        {
            var x=480*(.3+.4*r.Next());var y=270*(.32+.36*r.Next());var rot=(r.Next()*2-1)*.4;
            var t=Round(k*45d/8);var rect=V(s,x-105.5,y-79,211,158);
            if(f>=t&&f<t+5)
            {
                var p=Out((f-t)/5d);output.Add(new(k,rect,Rotation:rot,Z:-1,Gap:false,Plate:true));
                output.Add(new(k,V(s,x-105.5,y-24*(1-p)-79,211,158),Alpha:p,Rotation:rot,Scale:1.5-.5*p,Z:8,Gap:false,Plate:true));
            }
            else output.Add(new(k,rect,Rotation:rot,Z:Mod(k-last-1,8),Gap:false,Plate:true));
        }
        return output.ToArray();
    }
    private static RemixSlot[] Carousel(RemixScene s,int f)
    {
        var r=Rng(s,"carousel");var start=r.Next()*Math.Tau;var direction=r.Next()<.5?1:-1;
        return Enumerable.Range(0,8).Select(i=>
        {
            var angle=start+direction*Math.Tau*(i/8d+f/45d);var depth=(Math.Cos(angle)+1)/2;
            var h=(.36+.46*depth)*270;var w=h*1.33;var x=240+Math.Sin(angle)*480*.36;
            return new RemixSlot(i,V(s,x-w/2,135-h/2,w,h),Alpha:.35+.65*depth,Z:depth,Gap:false,Plate:true);
        }).ToArray();
    }
    private static RemixSlot[] Hop(RemixScene s,int f)
    {
        var grid=Grid(s,8);
        var groups=grid.GroupBy(rect=>Round(rect.Y*1000)).OrderBy(group=>group.Key).Select(group=>group.OrderBy(rect=>rect.X).ToArray()).ToArray();
        if(groups.Length>2)groups=grid.GroupBy(rect=>Round(rect.X*1000)).OrderBy(group=>group.Key).Select(group=>group.OrderBy(rect=>rect.Y).ToArray()).ToArray();
        var slots=groups.SelectMany((group,i)=>i%2==1?group.Reverse():group).ToArray();
        var r=Rng(s,"hop");var direction=r.Next()<.5?1:-1;var start=(int)(r.Next()*8);var lift=.06+.05*r.Next();
        var j=Math.Min(7,(int)Math.Floor(f/(45d/8)));var u=Out((f-Round(j*45d/8))/4d);
        return Enumerable.Range(0,8).Select(i=>new RemixSlot(i,Lerp(slots[Mod(i+start+direction*(j-1),8)],slots[Mod(i+start+direction*j,8)],u),
            Scale:u<1?1+lift*Math.Sin(u*Math.PI):1,Z:u<1?1:0)).ToArray();
    }
    private static RemixSlot[] Tunnel(RemixScene s,int f)
    {
        const int steps=3,rings=11;var twist=(Rng(s,"tunnel").Next()*2-1)*.22;var output=new List<RemixSlot>();
        for(var k=0;k<=rings;k++)
        {
            var d=k-steps*f/45d;var size=Math.Pow(.78,d);if(size>2.4)continue;
            output.Add(new(k%steps,new(.5-size/2,.5-size/2,size,size),Alpha:k>rings-steps?f/45d:1,Rotation:twist*d,Z:k,Gap:false));
        }
        return output.ToArray();
    }
    private static RemixSlot[] Pinwheel(RemixScene s,int f)
    {
        var r=Rng(s,"pin");var start=r.Next()*Math.Tau;var direction=r.Next()<.5?1:-1;
        const int spokes=8;const double radius=270*.38;var tw=Math.Min(270*.36,2*radius*Math.Sin(Math.PI/spokes)*.9);var th=tw/1.25;var output=new List<RemixSlot>();
        for(var k=0;k<spokes;k++)
        {
            var angle=start+direction*Math.Tau*(k+f/45d)/spokes;var x=240+Math.Cos(angle)*radius;var y=135+Math.Sin(angle)*radius;
            output.Add(new(0,V(s,x-tw/2,y-th/2,tw,th),Rotation:s.Portrait?-angle:angle+Math.PI/2,Z:1,Gap:false,Plate:true));
        }
        var hub=270*.3*(1+.05*Math.Sin(Math.Tau*f/45));output.Add(new(1,V(s,240-hub/2,135-hub/2,hub,hub),Z:2,Gap:false,Plate:true));return output.ToArray();
    }
    private static RemixSlot[] Deal(RemixScene s,int f)
    {
        var r=Rng(s,"deal");var output=new List<RemixSlot>();const double ch=270*.42,cw=ch*1.33;
        for(var k=0;k<8;k++)
        {
            var dx=(r.Next()*2-1)*3;var dy=(r.Next()*2-1)*3;var tilt=(r.Next()*2-1)*.06;
            var t=(k+.5)/8;var fx=480*(.14+.72*t);var fy=270*(.46-.16*Math.Sin(t*Math.PI));var fr=(t-.5)*.7+tilt;
            var dealt=Round(k*15d/8);var taken=28+Round((7-k)*14d/8);var x=480*.17+dx;var y=270*.68+dy;var rot=tilt;double z=7-k,p;
            if(f>=dealt&&f<dealt+5)
            {p=Out((f-dealt)/5d);x=Lerp(x,fx,p);y=Lerp(y,fy,p)-30*Math.Sin(p*Math.PI);rot=Lerp(rot,fr,p);z=8+k;}
            else if(f>=dealt+5&&f<taken){x=fx;y=fy;rot=fr;z=8+k;}
            else if(f>=taken&&f<taken+3)
            {p=1-Out(1-(f-taken)/3d);x=Lerp(fx,x,p);y=Lerp(fy,y,p)-30*Math.Sin(p*Math.PI);rot=Lerp(fr,rot,p);z=8+k;}
            output.Add(new(k,V(s,x-cw/2,y-ch/2,cw,ch),Rotation:rot,Z:z,Gap:false,Plate:true));
        }
        return output.ToArray();
    }
}
