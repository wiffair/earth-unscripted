using UnityEngine;
using UnityEngine.InputSystem;

namespace EarthUnscripted
{
    public sealed class DriveCamera : MonoBehaviour
    {
        public VehicleController Car;
        public DriverInput Input;
        public int Mode;
        public Vector3? DevelopmentCockpitEye;
        public Transform Preview;public float PreviewYaw;
        public string ModeName=>new[]{"CHASE","BONNET","COCKPIT","OVERHEAD","WIDE CHASE","BUMPER","WHEEL SIDE"}[Mathf.Clamp(Mode,0,6)];
        float yaw;
        public float ChasePitch {get;private set;}
        Camera view;
        void Awake(){view=GetComponent<Camera>();}
        void LateUpdate()
        {
            if(!Car)return;
            if(Preview)
            {
                var eye=Preview.TransformPoint(Quaternion.AngleAxis(PreviewYaw,Vector3.up)*new Vector3(7,3.2f,7));var right=Vector3.Cross(Vector3.up,(Preview.position-eye).normalized).normalized;
                transform.position=Vector3.Lerp(transform.position,eye,1-Mathf.Exp(-Time.unscaledDeltaTime*7));transform.rotation=Quaternion.LookRotation(Preview.position+Vector3.up*.8f-right*2.6f-transform.position,Vector3.up);view.fieldOfView=45;return;
            }
            if(Input.CameraPressed&&Time.timeScale>0){Mode=(Mode+1)%7;yaw=0;}
            var pad=Gamepad.current;
            if(Mode!=1&&Time.timeScale>0){float look=pad?.rightStick.x.ReadValue()??0;yaw=Mathf.MoveTowards(yaw,look*100,Time.deltaTime*140);}
            if((Mode==0||Mode==4)&&Time.timeScale>0&&!Input.Calibrating)
            {
                if(pad?.rightStickButton.wasPressedThisFrame==true)ChasePitch=0;
                float vertical=pad?.rightStick.y.ReadValue()??0;
                ChasePitch=CameraLookRules.Pitch(ChasePitch,vertical,Time.deltaTime);
            }
            var root=Car.transform;var forward=Quaternion.AngleAxis(yaw,Vector3.up)*root.forward;var p=root.position;
            Vector3 desired,target;
            if(Input.LookBack){desired=p-root.forward*2.8f+Vector3.up*1.8f;target=desired-root.forward*40;}
            else if(Mode==1){desired=root.TransformPoint(new Vector3(0,1.12f,Car.Visual.FrontCameraExtent + .15f));target=desired+root.forward*30;}
            else if(Mode==2){desired=root.TransformPoint(DevelopmentCockpitEye??Car.Visual.CockpitEye);target=desired+forward*30;}
            else if(Mode==3){desired=p+Vector3.up*36-root.forward*8;target=p+root.forward*5;}
            else if(Mode==5){desired=root.TransformPoint(new Vector3(0,.65f,Car.Visual.FrontCameraExtent + .15f));target=desired+root.forward*30;}
            else if(Mode==6){desired=root.TransformPoint(new Vector3(1.7f,.7f,.6f));target=desired+forward*30;}
            else
            {
                desired=p-forward*((Mode==4?12:7)+Mathf.Min(Car.AbsoluteSpeed*.035f,2))+Vector3.up*(Mode==4?5.2f:3.1f);target=p+Vector3.up*1.1f+root.forward*2;
                desired=target+Quaternion.AngleAxis(ChasePitch,Vector3.Cross(Vector3.up,forward).normalized)*(desired-target);
                var ray=desired-target;
                if(Physics.SphereCast(target,.28f,ray.normalized,out var hit,ray.magnitude,~(1<<8),QueryTriggerInteraction.Ignore))desired=hit.point+hit.normal*.35f;
            }
            if(Input.LookBack||Mode==1||Mode==2||Mode==5||Mode==6)transform.position=desired;else transform.position=Vector3.Lerp(transform.position,desired,1-Mathf.Exp(-Time.unscaledDeltaTime*9));
            transform.rotation=Quaternion.LookRotation(target-transform.position,Mode==1||Mode==2||Mode==5||Mode==6?root.up:Vector3.up);
            view.fieldOfView=Mathf.Lerp(view.fieldOfView,Mode==2?74:65+Mathf.Min(Car.AbsoluteSpeed*.2f,12),Time.unscaledDeltaTime*3);
        }
    }
}
