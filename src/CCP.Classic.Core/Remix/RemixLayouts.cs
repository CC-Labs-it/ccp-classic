namespace CCP.Classic.Core.Remix;

public readonly record struct RemixRect(double X, double Y, double Width, double Height);
public readonly record struct RemixCrop(double X, double Y, double Zoom);
public sealed record RemixSlot(int Tile, RemixRect Rect, int FrameOffset = 0, double Alpha = 1, double Rotation = 0,
    double Scale = 1, double Z = 0, bool Gap = true, bool Plate = false, RemixCrop? Crop = null);

// Translated from pinned public engine/layout.js, rects.js and layouts/*.js. Canvas fractions only.
public static partial class RemixLayouts
{
    private static readonly RemixRect Full = new(0, 0, 1, 1);
    public static IReadOnlyList<RemixSlot> At(RemixScene scene, int frame)
    {
        frame = Mod(frame, RemixScene.Frames);
        return scene.Layout switch
        {
            "grow" or "flat" or "shuffle" or "mirror" => Basic(scene, frame),
            "spread" => Spread(scene, frame), "slide" => Slide(scene, frame), "ripple" => Ripple(scene, frame),
            "swallow" => Swallow(scene, frame), "cover" => Cover(scene, frame), "kenburns" => KenBurns(scene, frame),
            _ => Motion(scene, frame)
        };
    }
    public static bool HasBackdrop(string mode) => mode is "stack" or "deal" or "carousel" or "pinwheel";
    private static int Round(double value) => RemixScene.Round(value);
    private static int Mod(int value, int length) => (value % length + length) % length;
    private static double Mod(double value, double length) => (value % length + length) % length;
    private static double Out(double value) => 1 - Math.Pow(1 - Math.Clamp(value, 0, 1), 3);
    private static double InOut(double value) { var c = Math.Clamp(value, 0, 1); return c < .5 ? 4 * c * c * c : 1 - Math.Pow(-2 * c + 2, 3) / 2; }
    private static double Lerp(double a, double b, double p) => a + (b - a) * p;
    private static RemixRect Lerp(RemixRect a, RemixRect b, double p) => new(Lerp(a.X,b.X,p),Lerp(a.Y,b.Y,p),Lerp(a.Width,b.Width,p),Lerp(a.Height,b.Height,p));
    private static RemixRect Exit(RemixRect r)
    {
        var cx = r.X + r.Width / 2; var cy = r.Y + r.Height / 2; double[] d = [cx, 1 - cx, cy, 1 - cy];
        var side = Array.IndexOf(d, d.Min());
        return side switch { 0 => r with { X = -r.Width }, 1 => r with { X = 1 }, 2 => r with { Y = -r.Height }, _ => r with { Y = 1 } };
    }
    private static RemixRect V(RemixScene s, double x, double y, double w, double h) => s.Portrait
        ? new(y/270,x/480,h/270,w/480) : new(x/480,y/270,w/480,h/270);
    private static RemixRandom Rng(RemixScene scene, string tag) => new(scene.Seed, "motion:" + tag);
    private static RemixRect[] Grid(RemixScene scene, int n)
    {
        var result = new List<RemixRect>();
        if (n == 3) result.AddRange([new(0,0,.5,1),new(.5,0,.5,.5),new(.5,.5,.5,.5)]);
        else
        {
            var rows = Rows(n);
            for (var row = 0; row < rows.Length; row++)
                for (var col = 0; col < rows[row]; col++) result.Add(new(col/(double)rows[row],row/(double)rows.Length,1d/rows[row],1d/rows.Length));
        }
        return result.Select(rect => scene.Portrait ? new RemixRect(rect.Y, rect.X, rect.Height, rect.Width) : rect).ToArray();
    }
    private static int[] Rows(int n) => n switch { 1 => [1], 2 => [2], 4 => [2,2], 5 => [2,3], 6 => [3,3], 7 => [3,4], _ => [4,4] };
    private sealed record Node(int Tile, bool Row = true, Node? A = null, Node? B = null, double Ratio = .5);
    private static Node Even(int[] ids, bool row)
    { if (ids.Length == 1) return new(ids[0]); var k = ids.Length / 2; return new(-1, row, Even(ids[..k],row),Even(ids[k..],row),k/(double)ids.Length); }
    private static Node? Prune(Node? node, int count)
    {
        if (node is null) return null; if (node.Tile >= 0) return node.Tile < count ? node : null;
        var a = Prune(node.A, count); var b = Prune(node.B, count);
        return a is null ? b : b is null ? a : node with { A = a, B = b };
    }
    private static RemixRect[] Tree(RemixScene scene, int count)
    {
        var root = new Node(-1, false, Even([0,1,2,3], true), Even([4,5,6,7], true));
        var result = new RemixRect[count];
        void Walk(Node node, RemixRect box)
        {
            if (node.Tile >= 0) { result[node.Tile] = scene.Portrait ? new(box.Y,box.X,box.Height,box.Width) : box; return; }
            if (node.Row)
            { Walk(node.A!, box with { Width = box.Width * node.Ratio }); Walk(node.B!, new(box.X+box.Width*node.Ratio,box.Y,box.Width*(1-node.Ratio),box.Height)); }
            else
            { Walk(node.A!, box with { Height = box.Height * node.Ratio }); Walk(node.B!, new(box.X,box.Y+box.Height*node.Ratio,box.Width,box.Height*(1-node.Ratio))); }
        }
        Walk(Prune(root,count)!, Full); return result;
    }
    private static RemixSlot[] Basic(RemixScene scene, int f)
    {
        var rects = Tree(scene, 8); var mode = scene.Layout;
        if (mode is "flat" or "mirror") return rects.Select((r,i) => new RemixSlot(mode == "mirror" ? 0 : i,r,mode == "mirror" ? i*6 : 0)).ToArray();
        var stage = Math.Min(7, f / 6); var u = stage > 0 ? Out((f - stage*6)/4d) : 1;
        if (mode == "shuffle")
        {
            var now = stage <= 0 ? Enumerable.Range(0,8).ToArray() : new RemixRandom(scene.Seed,"shuffle:"+stage).Shuffle(Enumerable.Range(0,8));
            var previous = stage <= 1 ? Enumerable.Range(0,8).ToArray() : new RemixRandom(scene.Seed,"shuffle:"+(stage-1)).Shuffle(Enumerable.Range(0,8));
            return Enumerable.Range(0,8).Select(i => new RemixSlot(i,Lerp(rects[previous[i]],rects[now[i]],u))).ToArray();
        }
        var to = Tree(scene, stage+1); var from = stage > 0 ? Tree(scene,stage) : to;
        return to.Select((r,i) => new RemixSlot(i,u >= 1 ? r : Lerp(i < from.Length ? from[i] : new(r.X+r.Width/2,r.Y+r.Height/2,0,0),r,u))).ToArray();
    }
    private static RemixSlot[] Spread(RemixScene s, int f)
    {
        if (f >= 40)
        { var u = InOut((f-40+1)/5d); return Grid(s,8).Select((r,i) => new RemixSlot(i,Lerp(r,i==0?Full:Exit(r),u),Z:i==0?8:i,Gap:u<1)).ToArray(); }
        const int step = 4; var stage = Math.Min(7,f/step); var to = Grid(s,stage+1); var from = stage>0?Grid(s,stage):to;
        var p = stage>0?Out((f-stage*step)/4d):1;
        return to.Select((r,i) => new RemixSlot(i,Lerp(i<from.Length?from[i]:Exit(r),r,p),Z:i)).ToArray();
    }
    private static RemixSlot[] Slide(RemixScene s, int f)
    {
        var r=Rng(s,"slide"); var sign=r.Next()<.5?1:-1; var output=new List<RemixSlot>(); const double rh=67.5; const int tw=78;
        for(var row=0;row<4;row++)
        {
            var tiles=new[]{row,row+4}; const int length=156; var direction=(row%2==1?-1:1)*sign;
            var start=Mod(direction*f/45d*length+r.Next()*length,length); var y=row*rh+(row>0?2:0); var h=rh-(row>0?2:0)-(row<3?2:0);
            for(var x0=start-length;x0<480;x0+=length)
                for(var i=0;i<2;i++) { var x=x0+i*tw; if(x+tw<=0||x>=480)continue; output.Add(new(tiles[i],V(s,x+2,y,tw-4,h),Gap:false)); }
        }
        return output.ToArray();
    }
    private static RemixSlot[] Ripple(RemixScene s,int f)
    {
        var r=Rng(s,"ripple"); var corner=(int)(r.Next()*4); var lean=.35+.3*r.Next(); var lift=.13+.06*r.Next(); const int span=17;
        return Grid(s,8).Select((rect,i)=>
        {
            var cx=rect.X+rect.Width/2;var cy=rect.Y+rect.Height/2;var d=((corner&1)!=0?1-cx:cx)*lean+((corner&2)!=0?1-cy:cy)*(1-lean);
            var p=(f-Round(d*span))/8d;var wave=p>0&&p<1?Math.Sin(p*Math.PI):0;
            return new RemixSlot(i,rect,Rotation:((corner&1)!=0?-1:1)*.05*wave,Scale:1+lift*wave,Z:wave>0?1:0);
        }).ToArray();
    }
    private static RemixSlot[] Swallow(RemixScene s,int f)
    {
        var order=Rng(s,"swallow").Shuffle(Enumerable.Range(0,8)).Take(3).ToArray(); const int per=15;
        var j=Math.Min(2,f/per);var local=f-j*per; const int hold=7;
        var p=local<3?Out(local/3d):local<3+hold?1:local<6+hold?1-Math.Pow(Math.Clamp((local-3-hold)/3d,0,1),3):0;
        return Grid(s,8).Select((r,i)=>i!=order[j]||p<=0?new RemixSlot(i,r):new(i,Lerp(r,new(.07,.07,.86,.86),p),Z:5,Gap:p<1,Plate:true)).ToArray();
    }
    private static RemixSlot[] Cover(RemixScene s,int f)
    {
        var direction=Rng(s,"cover").Next()<.5?1:-1; var p=Math.Clamp((f/45d-.08)/.84,0,1);p=p*p*(3-2*p);
        var output=Grid(s,7).Select((r,i)=>new RemixSlot(i+1,r)).ToList(); var x=direction>0?480-p*864:-384+p*864;
        output.Add(new(0,V(s,x,27,384,216),Z:5,Gap:false,Plate:true));return output.ToArray();
    }
    private static RemixSlot[] KenBurns(RemixScene s,int f)
    {
        var r=Rng(s,"kb");return Grid(s,8).Select((rect,i)=>
        {
            var zoom=1.25+r.Next()*.4;var phase=r.Next()*Math.Tau;var direction=r.Next()<.5?1:-1;var breath=.05+r.Next()*.05;
            var z=zoom+breath*Math.Sin(Math.Tau*f/45+phase*.7);var reach=(1-1/z)/2*.9;var a=direction*Math.Tau*f/45+phase;
            return new RemixSlot(i,rect,Crop:new(.5+reach*Math.Cos(a),.5+reach*Math.Sin(a),z));
        }).ToArray();
    }
}
