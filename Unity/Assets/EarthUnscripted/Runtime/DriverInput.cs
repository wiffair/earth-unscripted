using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using EarthUnscripted.Core;

namespace EarthUnscripted
{
    public sealed class DriverInput : MonoBehaviour
    {
        public float Steer,Throttle,Brake,Handbrake;
        public bool ParkingBrakePressed;public bool ParkingInputAllowed;
        public bool ParkingBrakeRequest {get;private set;}public int ParkingBrakeCommand {get;private set;}
        bool parkingWasDown,parkingArmed,radioDownCaptured,radioUpCaptured,radioNorthCaptured;
        public void QueueParkingToggle(bool applied,int acknowledged)
        {ParkingBrakeRequest=ParkingBrakeCommand!=acknowledged?!ParkingBrakeRequest:!applied;if(ParkingBrakeCommand<int.MaxValue)ParkingBrakeCommand++;}
        public bool CameraPressed,PausePressed,ResetPressed,RepairPressed,ChallengePressed,Horn,LookBack;
        public string DeviceName { get; private set; }="Keyboard";
        public bool Calibrating=>rebinding!=null;
        public PlayerProfile Profile;
#if UNITY_EDITOR
        [NonSerialized] public bool VerificationInput;
        [NonSerialized] public Gamepad VerificationPad;
        [NonSerialized] public Keyboard VerificationKeyboard;
#endif
        InputAction bindingAction;
        InputActionRebindingExtensions.RebindingOperation rebinding;
        public float ReadAxis(string path)
        {
            if(string.IsNullOrWhiteSpace(path))return 0;
            try{var axis=InputSystem.FindControl(path) as AxisControl;return axis!=null?axis.ReadValue():0;}catch{return 0;}
        }
        public bool WheelConnected
        {
            get{try{return Profile!=null&& !string.IsNullOrEmpty(Profile.steerPath)&&InputSystem.FindControl(Profile.steerPath) is AxisControl;}catch{return false;}}
        }
        void Update()
        {
            using var timing=WorkTelemetry.Measure("frame.input",null,8);
            var k=Keyboard.current;var p=Gamepad.current;
#if UNITY_EDITOR
            if(VerificationInput){k=VerificationKeyboard;p=VerificationPad;}
#endif
            if (Calibrating && p?.buttonEast.wasPressedThisFrame == true) CancelBinding();
            bool radio=p?.leftShoulder.isPressed==true&&Time.timeScale>0;
            radioDownCaptured=p?.dpad.down.isPressed==true&&(radio||radioDownCaptured);
            radioUpCaptured=p?.dpad.up.isPressed==true&&(radio||radioUpCaptured);
            radioNorthCaptured=p?.buttonNorth.isPressed==true&&(radio||radioNorthCaptured);
            bool parkingDown=k?.pKey.isPressed==true||!radioDownCaptured&&p?.dpad.down.isPressed==true;
            ParkingBrakePressed=ParkingInputAllowed&&!Calibrating&&parkingArmed&&parkingDown&&!parkingWasDown;
            if(!ParkingInputAllowed||Calibrating)parkingArmed=false;else if(!parkingDown)parkingArmed=true;parkingWasDown=parkingDown;
            PausePressed=k?.escapeKey.wasPressedThisFrame==true||p?.startButton.wasPressedThisFrame==true;
            CameraPressed=k?.cKey.wasPressedThisFrame==true||p?.rightShoulder.wasPressedThisFrame==true;
            ResetPressed=k?.rKey.wasPressedThisFrame==true||p?.selectButton.wasPressedThisFrame==true;
            RepairPressed=k?.gKey.wasPressedThisFrame==true||!radioUpCaptured&&p?.dpad.up.wasPressedThisFrame==true;
            ChallengePressed=k?.tKey.wasPressedThisFrame==true||p?.buttonWest.wasPressedThisFrame==true;
            Horn=k?.hKey.isPressed==true||p?.buttonEast.isPressed==true;
            LookBack=k?.bKey.isPressed==true||!radioNorthCaptured&&p?.buttonNorth.isPressed==true;
            float keyboardSteer=(k?.dKey.isPressed==true||k?.rightArrowKey.isPressed==true?1:0)-(k?.aKey.isPressed==true||k?.leftArrowKey.isPressed==true?1:0);
            float gas=k?.wKey.isPressed==true||k?.upArrowKey.isPressed==true?1:0;
            float brake=k?.sKey.isPressed==true||k?.downArrowKey.isPressed==true?1:0;
            Steer=keyboardSteer;Throttle=gas;Brake=brake;Handbrake=k?.spaceKey.isPressed==true?1:0;
            if(keyboardSteer!=0||gas>0||brake>0)DeviceName="Keyboard";
            if(p!=null)
            {
                float stick=DrivingMath.Deadzone(p.leftStick.x.ReadValue(),.12f);
                if(Mathf.Abs(stick)>.01f||p.rightTrigger.ReadValue()>.02f||p.leftTrigger.ReadValue()>.02f){DeviceName=p.displayName;Steer=stick;}
                Throttle=Mathf.Max(Throttle,p.rightTrigger.ReadValue());Brake=Mathf.Max(Brake,p.leftTrigger.ReadValue());Handbrake=Mathf.Max(Handbrake,p.buttonSouth.ReadValue());
            }
            if(Profile!=null&&Profile.wheelEnabled&&WheelConnected&&!Calibrating)
            {
                Steer=DrivingMath.Deadzone(DrivingMath.Steering(ReadAxis(Profile.steerPath),Profile.steerLeft,Profile.steerCentre,Profile.steerRight),.015f);
                Throttle=DrivingMath.Calibrate(ReadAxis(Profile.gasPath),Profile.gasRest,Profile.gasFull);
                Brake=DrivingMath.Calibrate(ReadAxis(Profile.brakePath),Profile.brakeRest,Profile.brakeFull);DeviceName="Calibrated wheel";
            }
            if(Calibrating){Steer=Throttle=Brake=Handbrake=0;}
        }
        public void Bind(string which,Action complete)
        {
            CancelBinding();bindingAction=new InputAction("Wheel calibration",InputActionType.Value,expectedControlType:"Axis");bindingAction.AddBinding("<Joystick>/stick/x");
            rebinding=bindingAction.PerformInteractiveRebinding().WithExpectedControlType("Axis").WithControlsExcluding("<Mouse>").WithControlsExcluding("<Keyboard>").WithControlsExcluding("<Gamepad>")
                .WithCancelingThrough("<Keyboard>/escape").WithTimeout(12).OnMatchWaitForAnother(.15f)
                .OnComplete(op=>{string path=bindingAction.bindings[0].effectivePath;if(which=="steer")Profile.steerPath=path;else if(which=="gas")Profile.gasPath=path;else Profile.brakePath=path;CancelBinding();Profile.Save();complete?.Invoke();})
                .OnCancel(op=>{CancelBinding();complete?.Invoke();});
            rebinding.Start();
        }
        public void CancelBinding(){var old=rebinding;rebinding=null;old?.Dispose();bindingAction?.Dispose();bindingAction=null;}
        void OnDisable(){ParkingBrakePressed=false;ParkingInputAllowed=parkingArmed=false;CancelBinding();}
    }
}
