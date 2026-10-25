using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;

static class Matrix
{
    const float Dt=.02f;
    static readonly AircraftSpec PSpec=new(2,9,18,240,.1f,8,.35f), ESpec=new(2,9,18,210,.1f,8,.35f);
    static readonly string[] Strategies={"Still","StraightW","CircleWA","CircleWD","DashSpam","HoldEmpty","LateDash","Managed"};
    static FlightState At(Vec2 p,float a,float s)=>new(){Position=p,Heading=a,Velocity=Vec2.FromAngle(a)*s};
    static int Main(string[] args)
    {
        string output=args.Length>0?args[0]:"matrix.json";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        var rows=new List<Row>();
        foreach(int level in new[]{1,2,3,4})
        foreach(float distance in new[]{12f,18f,24f})
        foreach(float angle in new[]{0f,MathF.PI*.5f})
        foreach(float side in new[]{-1f,1f})
        foreach(string strategy in Strategies) rows.Add(Run(level,distance,angle,side,strategy));
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        foreach(var g in rows.GroupBy(r=>(r.level,r.strategy)))
            Console.WriteLine($"L{g.Key.level} {g.Key.strategy} n={g.Count()} survivalMedian={Median(g.Select(r=>r.time)):F2} firstWarningMedian={Median(g.Select(r=>r.firstWarning<0?60:r.firstWarning)):F2} meanHits={g.Average(r=>r.hits):F2} enemyBoosts={g.Sum(r=>r.enemyBoosts)} counterDashes={g.Sum(r=>r.dashes)} overheats={g.Sum(r=>r.overheats)}");
        Console.WriteLine($"MATRIX_DONE count={rows.Count} output={output}");
        return 0;
    }
    static double Median(IEnumerable<float> source){var a=source.OrderBy(x=>x).ToArray();return (a[(a.Length-1)/2]+a[a.Length/2])*.5;}
    static Row Run(int level,float distance,float angle,float side,string strategy)
    {
        var world=new World{P=At(Vec2.Zero,angle,9),E=At(new Vec2(-distance,side*2),0,9)};
        var combat=new ContactCombatModel(5,1,.7f,1);
        var ai=new PursuitChargeInputSource(world,2,ESpec,combat,null,level);
        var pFlight=FlightModel.From(PSpec); var eFlight=FlightModel.From(ESpec); var boost=new PlayerBoostModel();
        var row=new Row{level=level,distance=distance,angle=angle,side=side,strategy=strategy};
        int lastTurnAttack=-1,lastDashAttack=-1;
        float turnUntil=-1,holdUntil=-1,turn=0,nextSpam=0;
        bool wasDash=false,wasOverheat=false,wasEnemyBoost=false;
        for(int tick=0;tick<3000 && combat.Status.IsPlayerAlive;tick++)
        {
            float now=combat.Status.ElapsedTime; var prevP=world.P;var prevE=world.E;
            var relative=world.E.Position-world.P.Position;
            ai.CanStartVisibleAttack=MathF.Abs(relative.X)<=8.15f && MathF.Abs(relative.Y)<=4.25f;
            world.Time=now;
#if !LEGACY
            world.Cue=boost.IsOverheated?VisiblePropulsionCue.Overheat:boost.IsDashing?VisiblePropulsionCue.Dash:boost.IsSustaining?VisiblePropulsionCue.Hold:VisiblePropulsionCue.None;
#endif
            var eInput=ai.ReadInput(Dt);
            if(ai.Level!=level)throw new Exception("Fixed difficulty changed");
            if(ai.Phase==AttackRunPhase.Windup && row.firstWarning<0)row.firstWarning=now;
            row.attacks=Math.Max(row.attacks,ai.AttackId);
            float throttle=strategy=="Still"?-1:1,steer=0;bool hold=false,dash=false;
            if(strategy=="CircleWA")steer=1;
            if(strategy=="CircleWD")steer=-1;
            if(strategy=="HoldEmpty")hold=true;
            if(strategy=="DashSpam" && now>=nextSpam)
            {
#if LEGACY
                dash=boost.Energy01>=.18f && boost.BurstCooldownRemaining<=0;
#else
                dash=boost.CanDash;
#endif
                if(dash)nextSpam=now+.04f;
            }
            if(strategy=="LateDash"||strategy=="Managed")
            {
                float cross=world.P.Forward.X*relative.Y-world.P.Forward.Y*relative.X;
                if(ai.Phase==AttackRunPhase.Windup && ai.PhaseRemaining<=.45f && lastTurnAttack!=ai.AttackId)
                {
                    lastTurnAttack=ai.AttackId;turn=cross>=0?-1:1;turnUntil=now+.9f;
                }
                if(ai.Phase==AttackRunPhase.Windup && ai.PhaseRemaining<=.10f && lastDashAttack!=ai.AttackId)
                {
                    lastDashAttack=ai.AttackId;dash=true;
                }
#if LEGACY
                bool canHold=boost.Energy01>=.75f;
                bool threatFar=relative.Magnitude>10;
#else
                bool canHold=boost.Energy>=50;
                bool threatFar=ai.IsPursuitBoosting && relative.Magnitude>10;
#endif
                if(strategy=="Managed" && canHold && threatFar && now>=holdUntil && ai.Phase==AttackRunPhase.Pursuit)
                {holdUntil=now+.5f;turn=cross>=0?-1:1;turnUntil=now+.7f;}
                hold=strategy=="Managed" && now<holdUntil;
                steer=now<turnUntil?turn:0;
            }
#if LEGACY
            boost.Step(dash||hold,Dt,true,throttle<0);
            if(throttle>=0 && boost.RequestsForwardThrust)throttle=1;
            pFlight.Step(ref world.P,new FlightInput(throttle,steer),Dt,boost.SpeedMultiplier);
            eFlight.Step(ref world.E,eInput,Dt,ai.SpeedMultiplier,ai.MaintainCruiseSpeed);
            bool isDash=boost.IsDashing,isOverheat=false,enemyBoost=false;
            float fuel=boost.Energy01*100;
#else
            boost.Step(dash,hold,Dt,Vec2.Dot(world.P.Velocity,world.P.Forward),true,throttle<0);
            if(throttle>=0 && boost.RequestsForwardThrust)throttle=1;
            pFlight.Step(ref world.P,new FlightInput(throttle,steer),Dt,boost.MotionCommand);
            eFlight.Step(ref world.E,eInput,Dt,ai.MotionCommand);
            bool isDash=boost.IsDashing,isOverheat=boost.IsOverheated,enemyBoost=ai.IsPursuitBoosting;
            float fuel=boost.Energy;
#endif
            if(isDash&&!wasDash)row.dashes++;
            if(isOverheat&&!wasOverheat)row.overheats++;
            if(enemyBoost&&!wasEnemyBoost)row.enemyBoosts++;
            wasDash=isDash;wasOverheat=isOverheat;wasEnemyBoost=enemyBoost;
            row.minFuel=Math.Min(row.minFuel,fuel);row.playerPeak=Math.Max(row.playerPeak,world.P.Speed);row.enemyPeak=Math.Max(row.enemyPeak,world.E.Speed);
            if(!float.IsFinite(world.P.Position.X)||!float.IsFinite(world.E.Position.X)||!float.IsFinite(world.E.Heading))throw new Exception("Non-finite flight");
            if(combat.StepContact(prevP,world.P,prevE,world.E,Dt,1,ai.CollisionDamage))
            { row.hits++;if(row.firstHit<0)row.firstHit=combat.Status.ElapsedTime;row.hitPhases.Add(ai.Phase.ToString()); }
            row.time=combat.Status.ElapsedTime;
        }
        row.hp=combat.Status.PlayerHealth;
        return row;
    }
    sealed class World:IWorldView
    {
        public FlightState P,E;public float Time;
#if !LEGACY
        public VisiblePropulsionCue Cue;
#endif
        public int AircraftCount=>2;
        public AircraftSnapshot GetAircraft(int i)
        {
#if LEGACY
            return i==0?new(1,1,P,true,true):new(2,2,E);
#else
            bool visible=MathF.Abs(E.Position.X-P.Position.X)<=8.15f&&MathF.Abs(E.Position.Y-P.Position.Y)<=4.25f;
            return i==0?new(1,1,P,true,true,Cue,true,Time):new(2,2,E);
#endif
        }
    }
    sealed class Row
    {
        public int level,hp,hits,attacks,dashes,overheats,enemyBoosts;
        public string strategy="";
        public float distance,angle,side,time,firstWarning=-1,firstHit=-1,minFuel=100,playerPeak,enemyPeak;
        public List<string> hitPhases=new();
    }
}
