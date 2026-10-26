using System;
// SDX_EXECUTION_LOG_BEGIN
using SDx.SimStudio.Logging;
// SDX_EXECUTION_LOG_END
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Mujoco;
using UnityEngine;

namespace SDx.Scenario1 {
  /// <summary>
  /// Compile compatibility fix 1.0.1: checked int dimensions for native model counts.
  /// Fixed-layout, closed-loop physical book transfer. No book parenting, weld,
  /// runtime root-transform animation, external base wrench, or collision disabling.
  /// qpos is written only during reset; scratch qpos is used only for IK.
  /// </summary>
  [DefaultExecutionOrder(-5000)]
  [DisallowMultipleComponent]
  public sealed unsafe partial class SDxScenario1Controller : MonoBehaviour {
    [Header("Assigned by Prepare Current Scene")]
    public Transform RobotRoot;
    public Transform BookRoot;
    public Transform ShelfRoot;
    public MjFreeJoint RobotFreeJoint;
    public MjFreeJoint BookFreeJoint;
    public MjBody BookBody;
    public MjBody LeftHand;
    public MjGeom BookCollision;
    public MjHingeJoint[] LeftJoints = new MjHingeJoint[7];
    public MjHingeJoint[] RightJoints = new MjHingeJoint[7];
    public MjSlideJoint[] Fingers = new MjSlideJoint[4];
    public MjSlideJoint[] DoorSlides = new MjSlideJoint[2];
    public MjActuator[] LeftMotors = new MjActuator[7];
    public MjActuator[] RightMotors = new MjActuator[7];
    public MjActuator[] WheelMotors = new MjActuator[4];
    public MjActuator LeftGripper, RightGripper;

    [Header("Verified fixed-layout preset (Unity axes)")]
    public Vector3 RobotStart = new Vector3(.581f, 0, 1.635f);
    // Body center, NOT the imported wrapper pivot. 2.31 mm lift avoids initial overlap.
    public Vector3 BookCenterStart = new Vector3(1.588f, .50111069f, 1.644f);
    public Vector3 TargetBookCenter = new Vector3(5.23f, .924364f, -4.5f);
    public bool ShowStatus = true;
    [SerializeField] string status = "Waiting for Play";
    public string Status => status;
    public bool IsRunning => ready && stage >= 0 && stage < 17;
    public int CompletedRuns { get; private set; }

    MjScene scene;
    MujocoLib.mjModel_* model;
    MujocoLib.mjData_* data;
    MujocoLib.mjData_* scratch;
    bool ready, resetRequested, faulted;
    // Public scenario consolidation 1.0.0: ONE scenario, always includes door 001.
    // Legacy Scenario2Plan filenames retain the proven door motion constants only.
    public int ActiveScenario => 1;
    public bool IsReadyForCommand => ready && isActiveAndEnabled;
    const string ScenarioTag = "[Scenario 1] ";
    int stage = -1, routeIndex, steps;
    double age, duration = 1, speed, angular, grip = 255, holdYaw;
    Vector2 holdXY;
    double[] initialQ, qgoal = new double[7], fromQ = new double[7], toQ = new double[7];
    readonly double[] kp = {600,650,550,650,250,200,150};
    readonly double[] kd = {45,45,35,35,15,14,10};
    readonly double[] torqueLimit = {87,87,87,87,12,12,12};
    Vector3 relativeBook;
    Quaternion desiredHandRotation;
    float oldCone, oldImpRatio;
    readonly List<string> milestones = new List<string>();

    // Navigation coordinates use MuJoCo X/Y (Unity X/Z). Turn entries use native yaw.
    struct Waypoint {
      public double X,Y,Yaw; public bool IsTurn, Reverse;
      public static Waypoint Drive(double x,double y,bool reverse=false) =>
        new Waypoint{X=x,Y=y,Reverse=reverse};
      public static Waypoint Turn(double yaw) => new Waypoint{IsTurn=true,Yaw=yaw};
    }
    readonly Waypoint[] route = {
      Waypoint.Drive(-.45,1.635,true), Waypoint.Turn(-Math.PI/2),
      Waypoint.Drive(-.45,-3.0), Waypoint.Turn(0),
      Waypoint.Drive(3.1,-3.15), Waypoint.Turn(-Math.PI/2),
      Waypoint.Drive(3.1,-4.5), Waypoint.Turn(0),
      Waypoint.Drive(4.18,-4.5), Waypoint.Turn(0)
    };

    void OnEnable() {
      if (!Application.isPlaying) return;
      try {
        scene = MjScene.Instance;
        scene.postInitEvent += OnInit;
        scene.preUpdateEvent += OnPreStep;
        scene.ctrlCallback += OnControl;
        scene.postUpdateEvent += OnPostStep;
        scene.preDestroyEvent += OnNativeDestroy;
        if (scene.Model != null && scene.Data != null) Bind();
      } catch (Exception e) { Fail(e); }
    }
    void OnDisable() {
      if (scene != null) {
        scene.postInitEvent -= OnInit; scene.preUpdateEvent -= OnPreStep;
        scene.ctrlCallback -= OnControl; scene.postUpdateEvent -= OnPostStep;
        scene.preDestroyEvent -= OnNativeDestroy;
        if (ready && scene.Model != null && scene.Data != null) ZeroAllControls();
      }
      FreeScratch(); ready=false; model=null; data=null;
    }
    void OnNativeDestroy(object sender, MjStepArgs args) {
      FreeScratch(); ready=false; model=null; data=null;
    }
    void FreeScratch() {
      if (scratch != null) { Scenario1Native.mj_deleteData((IntPtr)scratch); scratch=null; }
    }
    void OnInit(object sender, MjStepArgs args) { try { Bind(); } catch(Exception e) { Fail(e); } }

    void Bind() {
      ready=false; FreeScratch(); model=scene.Model; data=scene.Data;
      if (model==null || data==null) return;
      if (RobotFreeJoint==null || BookFreeJoint==null || LeftHand==null ||
          BookBody==null || BookCollision==null || LeftGripper==null || RightGripper==null)
        throw new InvalidOperationException("Run Tools > SDx > Scenario 1 > Prepare Current Scene first.");
      CheckArray(LeftJoints,7); CheckArray(RightJoints,7); CheckArray(LeftMotors,7);
      CheckArray(RightMotors,7); CheckArray(WheelMotors,4); CheckArray(Fingers,4);
      if (Math.Abs(model->opt.timestep-.001)>1e-7)
        throw new InvalidOperationException("Scenario 1 requires Fixed Timestep = 0.001.");
      if (RobotFreeJoint.QposAddress<0 || BookFreeJoint.QposAddress<0)
        throw new InvalidOperationException("MuJoCo joints are not bound to the active scene.");
      string version=Marshal.PtrToStringAnsi(Scenario1Native.mj_versionString());
      if (version!="3.16.0") Debug.LogWarning("[Scenario 1] Native validation used 3.16.0; current="+version);
      // Native count fields may be ulong; managed array/index arithmetic uses int.
      // Reject out-of-range dimensions instead of silently truncating them.
      int nq = checked((int)model->nq);
      initialQ = new double[nq];
      for(int i=0;i<initialQ.Length;i++) initialQ[i]=model->qpos0[i];
      SetFreeInitial(RobotFreeJoint,RobotStart);
      SetFreeInitial(BookFreeJoint,BookCenterStart);
      for(int i=0;i<7;i++) {
        initialQ[LeftJoints[i].QposAddress]=Scenario1Plan.home[i];
        initialQ[RightJoints[i].QposAddress]=Scenario1Plan.home[i];
      }
      foreach(var joint in Fingers) initialQ[joint.QposAddress]=.04;
      foreach(var joint in DoorSlides) if(joint!=null) initialQ[joint.QposAddress]=2.4;
      // These are runtime-only task tuning, restored naturally when Play ends.
      oldCone=model->opt.cone; oldImpRatio=(float)model->opt.impratio;
      model->opt.cone=1; model->opt.impratio=10; model->opt.noslip_iterations=0;
      TuneGripper(LeftGripper); TuneGripper(RightGripper);
      int geom=BookCollision.MujocoId;
      model->geom_friction[3*geom]=1.5; model->geom_friction[3*geom+1]=.1;
      model->geom_friction[3*geom+2]=.003;
      model->geom_solref[2*geom]=.005; model->geom_solref[2*geom+1]=1;
      model->geom_solimp[5*geom]=.99; model->geom_solimp[5*geom+1]=.999;
      model->geom_solimp[5*geom+2]=.001;
      scratch=(MujocoLib.mjData_*)Scenario1Native.mj_makeData((IntPtr)model);
      if(scratch==null) throw new InvalidOperationException("Could not allocate IK scratch data.");
      ready=true; faulted=false; ResetPhysics(false);
      Debug.Log("[Scenario 1] Ready: book transfer WITH door 001 opening. Natural-language commands are handled by the LLM bridge.",this);
      // SDX_EXECUTION_LOG_BEGIN
      SDxExecutionLog.ControllerReady();
      // SDX_EXECUTION_LOG_END
    }
    static void CheckArray<T>(T[] array,int count) where T:UnityEngine.Object {
      if(array==null || array.Length!=count || array.Any(x=>x==null))
        throw new InvalidOperationException("Scenario 1 has incomplete model references.");
    }
    void SetFreeInitial(MjFreeJoint joint,Vector3 unity) {
      int a=joint.QposAddress;
      initialQ[a]=unity.x;initialQ[a+1]=unity.z;initialQ[a+2]=unity.y;
      initialQ[a+3]=1;initialQ[a+4]=initialQ[a+5]=initialQ[a+6]=0;
    }
    void TuneGripper(MjActuator actuator) {
      int i=actuator.MujocoId;
      model->actuator_gainprm[10*i]=.1568627451;
      model->actuator_biasprm[10*i]=0;
      model->actuator_biasprm[10*i+1]=-1000;
      model->actuator_biasprm[10*i+2]=-50;
      model->actuator_forcerange[2*i]=-40;model->actuator_forcerange[2*i+1]=40;
    }
    public void SubmitCommand(string command) {
      // Only an explicit, already-approved public command can enter the controller.
      if(!SDxSingleScenarioCommand.IsScenario1(command)) return;
      RequestScenario1();
    }
    public bool RequestScenario1() {
      if(!IsReadyForCommand) return false;
      resetRequested=true;
      status="Scenario 1 reset requested (doors closed)";
      // SDX_EXECUTION_LOG_BEGIN
      SDxExecutionLog.ScenarioRequested(IsRunning);
      // SDX_EXECUTION_LOG_END
      return true;
    }
    void OnPreStep(object sender,MjStepArgs args) {
      if(!ready || !resetRequested)return;
      try {resetRequested=false;ResetPhysics(true);}catch(Exception e){Fail(e);}
    }
    void ResetPhysics(bool start) {
      if(start)ResolveDoorBindings();
      ResetDoorSequence();
      // Reset all dynamic objects, velocity, control, warm start and simulation time.
      MujocoLib.mj_resetData(model,data);
      for(int i=0;i<initialQ.Length;i++)data->qpos[i]=initialQ[i];
      if(start) {
        // Initial reset only. There is no door qpos/control write while opening.
        data->qpos[doorOneSlide.QposAddress]=0;
        data->qpos[doorTwoSlide.QposAddress]=0;
      }
      ZeroAllControls();
      Array.Copy(Scenario1Plan.home,qgoal,7);Array.Copy(qgoal,fromQ,7);Array.Copy(qgoal,toQ,7);
      holdXY=new Vector2(RobotStart.x,RobotStart.z);holdYaw=0;
      speed=angular=age=0;duration=1;grip=255;steps=routeIndex=0;faulted=false;
      stage=start?0:-1;status=start?"Reset / settle":"Ready - waiting for scenario command";
      milestones.Clear();MujocoLib.mj_forward(model,data);scene.SyncUnityToMjState();
      if(start)CheckClosedDoorCalibration();
      Debug.Log(start?ScenarioTag+"Reset complete. Both doors closed; left arm book, right arm door 001.":
        "[Scenario 1] Initial pose held; doors open while idle. No automatic task start.",this);
      // SDX_EXECUTION_LOG_BEGIN
      SDxExecutionLog.PhysicsResetCompleted(start);
      // SDX_EXECUTION_LOG_END
    }
    void ZeroAllControls() {
      foreach(var a in LeftMotors)if(a!=null)a.Control=0;
      foreach(var a in RightMotors)if(a!=null)a.Control=0;
      foreach(var a in WheelMotors)if(a!=null)a.Control=0;
      if(LeftGripper!=null)LeftGripper.Control=0;if(RightGripper!=null)RightGripper.Control=0;
      if(data!=null && model!=null) {
        int nu = checked((int)model->nu);
        for(int i=0;i<nu;i++)data->ctrl[i]=0;
      }
    }
    void SetControl(MjActuator actuator,double value) {
      if(double.IsNaN(value)||double.IsInfinity(value))throw new InvalidOperationException("Non-finite actuator input.");
      actuator.Control=(float)value;
      data->ctrl[actuator.MujocoId]=value;
    }
    void OnControl(object sender,MjStepArgs args) {
      if(!ready||faulted)return;
      try {
        double dt=model->opt.timestep,vc=0,wc=0;
        bool navigate=stage==8||stage==9;
        double x=data->qpos[RobotFreeJoint.QposAddress],y=data->qpos[RobotFreeJoint.QposAddress+1];
        double yaw=BaseYaw();
        if(stage==8 && routeIndex==2 && !doorFinished) {
          if(!DoorSequenceActive)BeginDoorSequence();
          RunDoorControl(dt,x,y,yaw,out vc,out wc,out navigate);
        } else if(stage==8) {
          var wp=route[routeIndex];bool reached;
          if(wp.IsTurn) {
            double error=Wrap(wp.Yaw-yaw);
            reached=Math.Abs(error)<.008 && Math.Abs(angular)<.015;
            wc=Math.Abs(error)<.008?0:Clamp(3*error,-.35,.35);
          } else {
            double dx=wp.X-x,dy=wp.Y-y,dist=Math.Sqrt(dx*dx+dy*dy);
            double error=Wrap(Math.Atan2(dy,dx)+(wp.Reverse?Math.PI:0)-yaw);
            reached=dist<.012 && Math.Abs(speed)<.018;
            vc=Math.Min(.28,.9*dist)*(wp.Reverse?-1:1)*Math.Max(0,Math.Cos(error));
            wc=Clamp(3*error,-.4,.4);
            if(dist<.012)vc=wc=0;
          }
          if(reached) {
            RecordMilestone("Waypoint "+routeIndex);routeIndex++;age=0;
            if(routeIndex>=route.Length)Enter(9);
            vc=wc=0;
          }
          if(age>70)throw new InvalidOperationException("The calibrated route is blocked in this scene. No transform bypass was applied.");
        } else if(stage>=0 && stage!=9 && stage<17) {
          double f=Math.Min(1,(age+dt)/duration),s=f*f*f*(10+f*(-15+6*f));
          for(int i=0;i<7;i++)qgoal[i]=fromQ[i]+(toQ[i]-fromQ[i])*s;
        }
        for(int i=0;i<7;i++) {
          SetControl(LeftMotors[i],Clamp(kp[i]*(qgoal[i]-data->qpos[LeftJoints[i].QposAddress])-kd[i]*data->qvel[LeftJoints[i].DofAddress],-torqueLimit[i],torqueLimit[i]));
          SetControl(RightMotors[i],Clamp(kp[i]*(RightArmGoal(i)-data->qpos[RightJoints[i].QposAddress])-kd[i]*data->qvel[RightJoints[i].DofAddress],-torqueLimit[i],torqueLimit[i]));
        }
        SetControl(LeftGripper,grip);SetControl(RightGripper,rightGrip);
        double vl,vr;
        if(navigate) {
          speed+=Clamp(vc-speed,-.25*dt,.25*dt);angular+=Clamp(wc-angular,-.4*dt,.4*dt);
          // Empirically calibrated skid-steer yaw gain; not a claimed physical track width.
          vl=speed-angular*2.0;vr=speed+angular*2.0;
        } else {
          vc=Clamp(2*((holdXY.x-x)*Math.Cos(yaw)+(holdXY.y-y)*Math.Sin(yaw)),-.07,.07);
          wc=Clamp(3*Wrap(holdYaw-yaw),-.2,.2);vl=vc-wc*.2854;vr=vc+wc*.2854;
        }
        SetControl(WheelMotors[0],Clamp(vl/.1651,-10,10));SetControl(WheelMotors[2],Clamp(vl/.1651,-10,10));
        SetControl(WheelMotors[1],Clamp(vr/.1651,-10,10));SetControl(WheelMotors[3],Clamp(vr/.1651,-10,10));
      }catch(Exception e){Fail(e);}
    }
    void OnPostStep(object sender,MjStepArgs args) {
      if(!ready||faulted||stage<0||stage>=17)return;
      try {
        age+=model->opt.timestep;steps++;
        if(DoorSequenceActive)AdvanceDoorAfterStep(model->opt.timestep);
        else if(stage!=8 && age>=duration-1e-8){RecordMilestone(status);Enter(stage+1);}
      }catch(Exception e){Fail(e);}
    }
    void Enter(int next) {
      stage=next;age=0;Array.Copy(qgoal,fromQ,7);Array.Copy(qgoal,toQ,7);
      switch(stage) {
        case 1: Motion("Above book",Scenario1Plan.above,5,255);break;
        case 2: Motion("Approach book",Scenario1Plan.pregrasp,4,255);break;
        case 3: Motion("Grasp pose",Scenario1Plan.pick,3,255);break;
        case 4: Motion("Close physical gripper",Scenario1Plan.pick,2,0);break;
        case 5: Motion("Lift book",Scenario1Plan.lift,4,0);break;
        case 6: Motion("Carry pose",Scenario1Plan.carry,4,0);break;
        case 7: Motion("Hold book",Scenario1Plan.carry,2,0);break;
        case 8: status="Navigate fixed route";routeIndex=0;duration=999;break;
        case 9: status="Settle at shelf";duration=1;break;
        case 10: {
          int a=RobotFreeJoint.QposAddress;holdXY=new Vector2((float)data->qpos[a],(float)data->qpos[a+1]);holdYaw=BaseYaw();speed=angular=0;
          MeasureGrip();Vector3 p=Native(TargetBookCenter)+new Vector3(-.28f,0,.018f);
          Motion("Align with center cell",Solve(p-desiredHandRotation*relativeBook,desiredHandRotation,Scenario1Plan.shelf_pre),5,0);break;
        }
        case 11: {
          Vector3 p=Native(TargetBookCenter)+new Vector3(0,0,.018f);
          Motion("Insert into center cell",Solve(p-desiredHandRotation*relativeBook,desiredHandRotation,Scenario1Plan.shelf_insert),4,0);break;
        }
        case 12: {
          MeasureGrip();Vector3 p=Native(TargetBookCenter)+new Vector3(0,0,.0005f);
          Motion("Lower onto shelf",Solve(p-desiredHandRotation*relativeBook,desiredHandRotation,Scenario1Plan.shelf_insert),3,0);break;
        }
        case 13: Motion("Release book",qgoal,2,255);break;
        case 14: {
          Pose hand=HandPose(data);
          Motion("Withdraw gripper",Solve(hand.position+new Vector3(-.28f,0,0),hand.rotation,Scenario1Plan.withdraw),4,255);break;
        }
        case 15: Motion("Return arm to home",Scenario1Plan.home,6,255);break;
        case 16: Motion("Final settle",Scenario1Plan.home,5,255);break;
        case 17: Finish();break;
      }
      // SDX_EXECUTION_LOG_BEGIN
      SDxExecutionLog.ScenarioStage(stage,status);
      // SDX_EXECUTION_LOG_END
    }
    void Motion(string text,double[] goal,double seconds,double gripper) {
      status=text;duration=seconds;grip=gripper;Array.Copy(goal,toQ,7);
    }
    void MeasureGrip() {
      Pose h=HandPose(data);Pose b=BodyPose(data,BookBody.MujocoId);
      relativeBook=Quaternion.Inverse(h.rotation)*(b.position-h.position);
      Quaternion relative=Quaternion.Inverse(h.rotation)*b.rotation;
      desiredHandRotation=Quaternion.Inverse(relative); // desired book orientation = native identity
    }
    static Vector3 Native(Vector3 unity)=>new Vector3(unity.x,unity.z,unity.y);
    static Vector3 UnityPosition(Vector3 native)=>new Vector3(native.x,native.z,native.y);
    static Pose BodyPose(MujocoLib.mjData_* d,int id) {
      return new Pose(new Vector3((float)d->xpos[3*id],(float)d->xpos[3*id+1],(float)d->xpos[3*id+2]),
        new Quaternion((float)d->xquat[4*id+1],(float)d->xquat[4*id+2],(float)d->xquat[4*id+3],(float)d->xquat[4*id]));
    }
    Pose HandPose(MujocoLib.mjData_* d) => HandPose(d,LeftHand);
    static Pose HandPose(MujocoLib.mjData_* d,MjBody handBody) {
      Pose h=BodyPose(d,handBody.MujocoId);h.position+=h.rotation*new Vector3(0,0,.1034f);return h;
    }
    double BaseYaw() {
      int a=RobotFreeJoint.QposAddress;double w=data->qpos[a+3],x=data->qpos[a+4],y=data->qpos[a+5],z=data->qpos[a+6];
      return Math.Atan2(2*(w*z+x*y),1-2*(y*y+z*z));
    }
    double[] Solve(Vector3 target,Quaternion rotation,double[] seed) =>
      SolveArm(target,rotation,seed,LeftJoints,LeftHand);
    double[] SolveArm(Vector3 target,Quaternion rotation,double[] seed,MjHingeJoint[] joints,MjBody handBody) {
      Scenario1Native.mj_copyData((IntPtr)scratch,(IntPtr)model,(IntPtr)data);
      double[] q=(double[])seed.Clone();
      int nv = checked((int)model->nv);
      int jacobianLength = checked(3 * nv);
      double[] jp=new double[jacobianLength],jr=new double[jacobianLength];
      double[,] J=new double[6,7];double[] e=new double[6];
      double posError=999,rotError=999;
      for(int it=0;it<250;it++) {
        for(int i=0;i<7;i++)scratch->qpos[joints[i].QposAddress]=q[i];
        Scenario1Native.mj_kinematics((IntPtr)model,(IntPtr)scratch);
        Scenario1Native.mj_comPos((IntPtr)model,(IntPtr)scratch);
        Pose h=HandPose(scratch,handBody);Vector3 dp=target-h.position;
        Quaternion qe=rotation*Quaternion.Inverse(h.rotation);
        if(qe.w<0)qe=new Quaternion(-qe.x,-qe.y,-qe.z,-qe.w);
        double norm=Math.Sqrt(qe.x*qe.x+qe.y*qe.y+qe.z*qe.z);
        double factor=norm<1e-10?2:2*Math.Atan2(norm,qe.w)/norm;
        Vector3 dr=new Vector3((float)(qe.x*factor),(float)(qe.y*factor),(float)(qe.z*factor));
        posError=dp.magnitude;rotError=dr.magnitude;
        if(posError<.00008 && rotError<.0006)return q;
        double[] point={h.position.x,h.position.y,h.position.z};
        fixed(double* pp=point,jpp=jp,jrp=jr)
          Scenario1Native.mj_jac((IntPtr)model,(IntPtr)scratch,jpp,jrp,pp,handBody.MujocoId);
        for(int r=0;r<3;r++)for(int c=0;c<7;c++) {
          J[r,c]=jp[r*nv+joints[c].DofAddress]*4;
          J[r+3,c]=jr[r*nv+joints[c].DofAddress];
        }
        e[0]=dp.x*4;e[1]=dp.y*4;e[2]=dp.z*4;e[3]=dr.x;e[4]=dr.y;e[5]=dr.z;
        double[,] a=new double[6,7];
        for(int r=0;r<6;r++){for(int c=0;c<6;c++){double sum=0;for(int j=0;j<7;j++)sum+=J[r,j]*J[c,j];a[r,c]=sum+(r==c?.0025:0);}a[r,6]=e[r];}
        double[] x=Solve6(a);double[] dq=new double[7];double max=0;
        for(int j=0;j<7;j++){for(int r=0;r<6;r++)dq[j]+=J[r,j]*x[r];max=Math.Max(max,Math.Abs(dq[j]));}
        double scale=max>.10?.10/max:1;
        for(int j=0;j<7;j++) {
          int id=joints[j].MujocoId;
          q[j]=Clamp(q[j]+dq[j]*scale,model->jnt_range[2*id]+.012,model->jnt_range[2*id+1]-.012);
        }
      }
      throw new InvalidOperationException("Fixed-layout IK residual: position="+posError+", rotation="+rotError+". No teleport fallback.");
    }
    static double[] Solve6(double[,] a) {
      for(int col=0;col<6;col++) {
        int pivot=col;for(int r=col+1;r<6;r++)if(Math.Abs(a[r,col])>Math.Abs(a[pivot,col]))pivot=r;
        if(Math.Abs(a[pivot,col])<1e-14)throw new InvalidOperationException("Singular IK solve.");
        if(pivot!=col)for(int j=col;j<7;j++){double temp=a[col,j];a[col,j]=a[pivot,j];a[pivot,j]=temp;}
        double d=a[col,col];for(int j=col;j<7;j++)a[col,j]/=d;
        for(int r=0;r<6;r++)if(r!=col){double s=a[r,col];for(int j=col;j<7;j++)a[r,j]-=s*a[col,j];}
      }
      var x=new double[6];for(int i=0;i<6;i++)x[i]=a[i,6];return x;
    }
    void RecordMilestone(string label) {
      Pose b=BodyPose(data,BookBody.MujocoId);string line=steps+" ms | "+label+" | book(native)="+b.position.ToString("F5");
      if(doorOneSlide!=null && doorTwoSlide!=null)
        line+=" | door001="+data->qpos[doorOneSlide.QposAddress].ToString("F5")+
          " | door002="+data->qpos[doorTwoSlide.QposAddress].ToString("F5");
      milestones.Add(line);Debug.Log(ScenarioTag+line,this);
      // SDX_EXECUTION_LOG_BEGIN
      SDxExecutionLog.NavigationMilestone(label);
      // SDX_EXECUTION_LOG_END
    }
    void Finish() {
      Pose b=BodyPose(data,BookBody.MujocoId);Vector3 error=b.position-Native(TargetBookCenter);
      float angle=Quaternion.Angle(b.rotation,Quaternion.identity);
      bool doorsVerified=doorFinished && doorOneSlide!=null && doorTwoSlide!=null &&
        data->qpos[doorOneSlide.QposAddress]>2.35 && Math.Abs(data->qpos[doorTwoSlide.QposAddress])<.02;
      bool verified=error.magnitude<.02 && angle<3 && doorsVerified;
      status=verified?"Scenario 1 complete":"Placement finished - inspect final pose";
      if(verified){CompletedRuns++;}
      string report=string.Join("\n",milestones)+"\nFinal book Unity: "+UnityPosition(b.position).ToString("F6")+
        "\nPosition error: "+error.magnitude+" m\nAngle: "+angle+" deg\nWithin final tolerance: "+verified;
      report+="\nScenario: 1 (with door opening)\nDoor condition verified: "+doorsVerified;
      string path=Path.Combine(Application.persistentDataPath,"SDxScenario1_LastRun.txt");
      try{File.WriteAllText(path,report);}catch(Exception e){Debug.LogWarning(e.Message);}
      Debug.Log(ScenarioTag+status+". Log: "+path,this);
      // SDX_EXECUTION_LOG_BEGIN
      SDxExecutionLog.ScenarioFinished(verified,error.magnitude,angle,doorsVerified,CompletedRuns);
      // SDX_EXECUTION_LOG_END
    }
    void Fail(Exception exception) {
      if(faulted)return;faulted=true;status="Scenario 1: inspect Console";
      // SDX_EXECUTION_LOG_BEGIN
      SDxExecutionLog.ScenarioFault(exception);
      // SDX_EXECUTION_LOG_END
      if(ready)ZeroAllControls();Debug.LogException(exception,this);
    }
    static double Clamp(double value,double min,double max)=>Math.Max(min,Math.Min(max,value));
    static double Wrap(double angle)=>Math.Atan2(Math.Sin(angle),Math.Cos(angle));
    void OnGUI() {
      // SDX_EXECUTION_LOG_BEGIN
      if(SDxExecutionLog.OverlayActive)return;
      // SDX_EXECUTION_LOG_END
      if(!ShowStatus||!Application.isPlaying)return;
      GUI.Box(new Rect(Mathf.Max(270,Screen.width-460),8,450,44),
        "Scenario 1 | "+status+"\nCompleted: "+CompletedRuns+"   Book transfer with door opening.");
    }
    void OnDrawGizmosSelected() {
      if(route==null)return;Vector3 prev=RobotStart;
      foreach(var w in route)if(!w.IsTurn){Vector3 next=new Vector3((float)w.X,.08f,(float)w.Y);Gizmos.DrawLine(prev,next);Gizmos.DrawWireSphere(next,.10f);prev=next;}
      Gizmos.DrawWireCube(TargetBookCenter,new Vector3(.183f,.269f,.042f));
    }
  }
}
