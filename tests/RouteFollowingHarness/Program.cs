using System.Numerics;
using System.Text.Json;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Embedded;
using NWO_RegionNode;
using NWO_RegionNode.PathValidation;

var checks = new List<(string Name, bool Passed, string Detail)>();
void Check(bool ok, string name, string detail = "") { checks.Add((name, ok, detail)); if (!ok || !name.StartsWith("heading nearest")) Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}: {detail}"); }
var trace = new List<object>();
for (int tenth = 0; tenth <= 3600; tenth++)
{
    double desired = tenth / 10.0, rad = desired * Math.PI / 180;
    byte actual = LockstepMovement.HeadingToTarget(-Math.Sin(rad), -Math.Cos(rad));
    double measured = LockstepMovement.HeadingDegrees(actual), error = Circular(measured, desired);
    double exhaustive = Enumerable.Range(0,256).Min(i => Circular(LockstepMovement.HeadingDegrees((byte)i), desired));
    checks.Add(("heading nearest exhaustive " + desired.ToString("F1"), error <= exhaustive + 1e-8 && error <= 1.5001, $"byte={actual} degrees={measured:F1} err={error:F3} exhaustive={exhaustive:F3}"));
}
Check(LockstepMovement.HeadingToTarget(1000,0)==193,"east heading calibrated",$"byte={LockstepMovement.HeadingToTarget(1000,0)}");
Check(LockstepMovement.HeadingToTarget(0,-1000)==0,"north heading seam",$"byte={LockstepMovement.HeadingToTarget(0,-1000)}");
var channel = new EmbeddedChannel(new CaptureHandler()); var ctx = channel.Pipeline.LastContext()!; var provider = new FakeProvider(); const uint oid=92301, uid=92302;
MoveMent Ship(int x=0,int z=0) { var s=new MoveMent(ctx,oid,new MoveMent.nwo_Vector3(x,0,z),100,0){rot=0,targetspeed=100}; return s; }
AutopilotController Controller(MoveMent s) { NWO_RegionNode.Program.moveMentTable[oid]=s; return new AutopilotController(provider); }
void Tick(AutopilotController c, MoveMent s, int n, Func<int,int,float>? terrain=null) { for(int i=0;i<n;i++) LockstepMovement.AdvanceTick(c,new[]{s},terrain??((_,_)=>0)); }
bool Store(AutopilotController c, Vector2[] p) => c.StoreValidatedRoute(new PathPolygonIndexRequest{ProtocolVersion=2,UserId=uid,ObjectId=oid,PathPolygonIndices=Enumerable.Repeat(1,p.Length).ToArray(),Waypoints=p},ctx);
bool IsActive(AutopilotController c) => (bool)((System.Collections.IDictionary)typeof(AutopilotController).GetField("_routes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(c)!)[oid]!.GetType().GetField("Active")!.GetValue(((System.Collections.IDictionary)typeof(AutopilotController).GetField("_routes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(c)!)[oid]!)!;
var route = new[]{new Vector2(0,0),new Vector2(.390625f,0),new Vector2(.390625f,.390625f)};
var ship=Ship(); var ctl=Controller(ship); Store(ctl,route); ctl.Toggle(uid,oid,1,true,ctx);
var routePoints = new[]{(0d,0d),(500d,0d),(500d,-500d)};
double CrossTrack(int x,int z) => Math.Sqrt(Math.Min(LockstepMovement.DistanceSquaredToSegment(x,z,routePoints[0].Item1,routePoints[0].Item2,routePoints[1].Item1,routePoints[1].Item2),LockstepMovement.DistanceSquaredToSegment(x,z,routePoints[1].Item1,routePoints[1].Item2,routePoints[2].Item1,routePoints[2].Item2)));
int[] ticks={1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25};
double maxCross=0;
for(int i=0;i<ticks.Length;i++){
    Tick(ctl,ship,1); double cross=CrossTrack(ship.globalPosition.X,ship.globalPosition.Z); maxCross=Math.Max(maxCross,cross);
    bool active=IsActive(ctl); trace.Add(new{scenario="route-500-elbow",tick=ticks[i],x=ship.globalPosition.X,z=ship.globalPosition.Z,heading=ship.rot,active,crossTrack=cross});
    Check(cross<=Math.Sqrt(2)+1e-9,"route cross-track tick "+ticks[i],$"position=({ship.globalPosition.X},{ship.globalPosition.Z}) cross={cross:F3} active={active}");
}
Check(ship.globalPosition.X==500&&ship.globalPosition.Z==-500&&!IsActive(ctl),"route final tick 25 exact endpoint",$"actual=({ship.globalPosition.X},{ship.globalPosition.Z}) active={IsActive(ctl)} maxCross={maxCross:F3}");
Tick(ctl,ship,1); Check(ship.globalPosition.X==500&&ship.globalPosition.Z==-540,"route tick 26 final forward carry",$"actual=({ship.globalPosition.X},{ship.globalPosition.Z})");
Check(!IsActive(ctl),"route completed by tick25/26","Trace records all sampled positions");
ship=Ship();ctl=Controller(ship);Store(ctl,new[]{new Vector2(0,0),new Vector2(0,0),new Vector2(.0078125f,0),new Vector2(.0078125f,.0078125f),new Vector2(.015625f,.0078125f),new Vector2(.015625f,.015625f)});ctl.Toggle(uid,oid,2,true,ctx);Tick(ctl,ship,1);Check(ship.globalPosition.X==20&&ship.globalPosition.Z==-20,"tiny zero-leg route first tick",$"actual=({ship.globalPosition.X},{ship.globalPosition.Z})");
ship=Ship();ctl=Controller(ship);Store(ctl,new[]{new Vector2(0,0),new Vector2(.03125f,0)});ctl.Toggle(uid,oid,3,true,ctx);var pre=(ship.globalPosition.X,ship.globalPosition.Z);Tick(ctl,ship,1,(x,y)=>x>=15&&x<=20&&y==0?1:0);Check((ship.globalPosition.X,ship.globalPosition.Z)==pre&&ship.speed==-1,"terrain transaction barrier",$"pos=({ship.globalPosition.X},{ship.globalPosition.Z}) speed={ship.speed}");
ship=Ship();LockstepMovement.AdvanceTick(null,new[]{ship},static(_,_)=>0);Check(ship.globalPosition.Z==-40,"manual production tick moves forward",$"pos=({ship.globalPosition.X},{ship.globalPosition.Z})");
ship=Ship();ctl=Controller(ship);Store(ctl,new[]{new Vector2(0,0),new Vector2(0,2),new Vector2(2,2)});ctl.Toggle(uid,oid,4,true,ctx);Tick(ctl,ship,1);ctl.Toggle(uid,oid,5,false,ctx);ship.globalPosition.X+=100;pre=(ship.globalPosition.X,ship.globalPosition.Z);bool resume=ctl.Toggle(uid,oid,6,true,ctx);Check(!resume&&(ship.globalPosition.X,ship.globalPosition.Z)==pre,"off-segment resume rejected without movement",$"resume={resume} pos=({ship.globalPosition.X},{ship.globalPosition.Z})");
ship=Ship();ctl=Controller(ship);NWO_RegionNode.Program.moveMentTable[oid]=ship;bool v1Stored=ctl.StoreValidatedRoute(uid,oid,new[]{1,2},ctx);bool v1Toggled=ctl.Toggle(uid,oid,9,true,ctx);Tick(ctl,ship,1);Check(v1Stored&&v1Toggled&&ctl.HasRoute(oid)&&ship.globalPosition.X==0&&ship.globalPosition.Z==-40,"real V1 storage toggle first tick",$"stored={v1Stored} toggled={v1Toggled} hasRoute={ctl.HasRoute(oid)} pos=({ship.globalPosition.X},{ship.globalPosition.Z})");ctl.Toggle(uid,oid,10,false,ctx);int pauseX=ship.globalPosition.X,pauseZ=ship.globalPosition.Z;Tick(ctl,ship,1);Check(ctl.HasRoute(oid)&&ship.globalPosition.X==pauseX&&ship.globalPosition.Z==pauseZ-40,"V1 OFF pauses route and forward sails",$"hasRoute={ctl.HasRoute(oid)} pos=({ship.globalPosition.X},{ship.globalPosition.Z})");ctl.Cancel(oid);Check(!ctl.HasRoute(oid)&&ship.targetAngle==ship.rot&&ship.targetspeed==(int)MathF.Max(0,ship.speed),"V1 cancel removes route preserves forward target",$"hasRoute={ctl.HasRoute(oid)} rot={ship.rot} targetAngle={ship.targetAngle} speed={ship.speed} targetSpeed={ship.targetspeed}");Tick(ctl,ship,1);Check(ship.globalPosition.Z==pauseZ-80,"V1 canceled route resumes ordinary forward tick",$"pos=({ship.globalPosition.X},{ship.globalPosition.Z})");
ship=Ship();ctl=Controller(ship);NWO_RegionNode.Program.moveMentTable[oid]=ship;bool legacyStored=ctl.StoreValidatedRoute(uid,oid,new[]{1,2},ctx);ship.globalPosition.Z=-1200;bool legacyToggle=ctl.Toggle(uid,oid,11,true,ctx);Check(legacyStored&&legacyToggle&&!ctl.HasRoute(oid)&&ship.targetAngle==ship.rot,"legacy V1 160-unit arrival smoke",$"stored={legacyStored} toggled={legacyToggle} hasRoute={ctl.HasRoute(oid)} pos=({ship.globalPosition.X},{ship.globalPosition.Z})");
ship=Ship();ctl=Controller(ship);Store(ctl,new[]{new Vector2(2047.8f,512),new Vector2(2048.2f,512)});ctl.Toggle(uid,oid,7,true,ctx);Tick(ctl,ship,1);Check(ship.globalPosition.X==40,"dateline crosses east",$"x={ship.globalPosition.X}");
ship=Ship(42000,-88000);ctl=Controller(ship);Store(ctl,new[]{new Vector2(300,700),new Vector2(300,700.5f)});ctl.Toggle(uid,oid,8,true,ctx);Tick(ctl,ship,1);Check(ship.globalPosition.X==42000&&ship.globalPosition.Z==-88040,"translated origin first tick",$"pos=({ship.globalPosition.X},{ship.globalPosition.Z})");
NWO_RegionNode.Program.moveMentTable.Remove(oid);channel.FinishAndReleaseAll();
Directory.CreateDirectory("artifacts/route-following-20261025");File.WriteAllText("artifacts/route-following-20261025/HeadingAndMovement.ticktrace.json",JsonSerializer.Serialize(new{ticks=trace},new JsonSerializerOptions{WriteIndented=true}));var failed=checks.Count(x=>!x.Passed);File.WriteAllText("artifacts/route-following-20261025/HeadingAndMovement.results.json",JsonSerializer.Serialize(new{passed=checks.Count-failed,failed,maxCrossTrack=maxCross,crossTrackLimit=Math.Sqrt(2),checks},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"MAX CROSS-TRACK {maxCross:F6} limit={Math.Sqrt(2):F6}");Console.WriteLine($"RESULT {checks.Count-failed}/{checks.Count} failed={failed}");if(failed>0)Environment.ExitCode=1;
static double Circular(double a,double b){double d=Math.Abs(a-b)%360;return Math.Min(d,360-d);}
sealed class FakeProvider:IAuthoritativeNavMeshProvider{public string GetVersion()=>"harness";public bool TryGetCentroid(int i,out double x,out double y){if(i==1){x=0;y=0;return true;}if(i==2){x=0;y=1;return true;}x=y=0;return false;}public bool HasPolygon(int i)=>i==1||i==2;public bool CanMoveBetween(int a,int b)=>(a==1&&b==2)||(a==2&&b==1);public bool IsOnNavMesh(int i)=>HasPolygon(i);public bool ValidateGeometry(int[] p,out uint s,out uint t,out PathReasonCode r){s=t=(uint)(p.Length-1);r=PathReasonCode.VALID;return true;}}
sealed class CaptureHandler:ChannelHandlerAdapter{}
