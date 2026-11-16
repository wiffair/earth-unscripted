using System;
using RoadRush.Core;
using UnityEngine;
using UnityEngine.InputSystem;
namespace RoadRush
{
    /// <summary>Current-circuit stock hire; the session owns placement, save/debit and eligibility.</summary>
    public sealed class CircuitHireMenu
    {
        int model;GUIStyle title,body,small,button;
        public void Update(GameSession session,Action back)
        {
            if(!session.Menu)return;var k=Keyboard.current;var pad=Gamepad.current;
            if(k?.downArrowKey.wasPressedThisFrame==true||pad?.dpad.down.wasPressedThisFrame==true)model=(model+1)%GarageRules.Catalogue.Length;
            if(k?.upArrowKey.wasPressedThisFrame==true||pad?.dpad.up.wasPressedThisFrame==true)model=(model+GarageRules.Catalogue.Length-1)%GarageRules.Catalogue.Length;
            if(k?.enterKey.wasPressedThisFrame==true||pad?.buttonSouth.wasPressedThisFrame==true)session.RentCircuitCar(model);
            if(k?.spaceKey.wasPressedThisFrame==true||pad?.buttonNorth.wasPressedThisFrame==true)session.ResumeDrive();
            if(k?.backspaceKey.wasPressedThisFrame==true||pad?.buttonEast.wasPressedThisFrame==true)back();
        }
        public void Draw(GameSession session,Action back)
        {
            if(title==null)
            {
                title=new GUIStyle(GUI.skin.label){fontSize=24,fontStyle=FontStyle.Bold};body=new GUIStyle(GUI.skin.label){fontSize=17,wordWrap=true};
                small=new GUIStyle(body){fontSize=14};button=new GUIStyle(GUI.skin.button){fontSize=17,alignment=TextAnchor.MiddleLeft};
                title.normal.textColor=body.normal.textColor=small.normal.textColor=Color.white;
            }
            var old=GUI.color;GUI.color=new Color(.035f,.055f,.07f,.99f);GUI.DrawTexture(new Rect(35,240,1370,620),Texture2D.whiteTexture);GUI.color=old;
            GUI.Label(new Rect(65,265,1250,40),"CIRCUIT CAR HIRE",title);
            GUI.Label(new Rect(65,315,1250,65),"Hire a stock car while stopped. Your owned car stays in the garage. Each lap attempt is paid before driving; restarting or abandoning an attempt keeps its fee. Practice has no reward.",body);
            for(int i=0;i<GarageRules.Catalogue.Length;i++)
            {
                var quote=RaceLapEconomy.QuoteForModel(i,true,false,false);
                if(ControllerMenuGUI.Button(new Rect(65,400+i*75,650,60),(model==i?"SELECTED · ":"")+GarageRules.Catalogue[i].Name+" · "+quote.HireFee+" / lap",button))model=i;
            }
            var clean=RaceLapEconomy.QuoteForModel(model,true,true,false);var best=RaceLapEconomy.QuoteForModel(model,true,true,true);
            GUI.Label(new Rect(785,395,545,45),GarageRules.Catalogue[model].Name,title);
            GUI.Label(new Rect(785,455,545,180),"New model value: "+best.NewBaseModelPrice+" credits\nLap hire fee: "+best.HireFee+" (0.5%)\nClean lap reward: "+clean.GrossReward+" · net cost "+(-clean.NetCredits)+"\nClean car/course personal best: "+best.GrossReward+" · net gain "+best.NetCredits+"\nBad lap: no reward · fee retained",body);
            GUI.Label(new Rect(785,645,545,90),(session.IsRenting?"DRIVING HIRED: ":"DRIVING OWNED: ")+GarageRules.Catalogue[session.DrivenCar.model].Name+"\nOwned car: "+GarageRules.Catalogue[session.Profile.garage.Active.model].Name+"\nBalance: "+session.Economy.Credits+" credits",body);
            GUI.Label(new Rect(65,715,650,70),session.Notice,body);
            GUI.Label(new Rect(65,765,1250,30),"Up/down: choose · Enter / A / Cross: hire · Space / Y / Triangle: start · Backspace / B / Circle: back",small);
            if(ControllerMenuGUI.Button(new Rect(285,805,295,40),"HIRE SELECTED CAR",button))session.RentCircuitCar(model);
            if(ControllerMenuGUI.Button(new Rect(600,805,300,40),"START / RESUME",button))session.ResumeDrive();
            bool enabled=GUI.enabled;GUI.enabled=enabled&&session.IsRenting;
            if(ControllerMenuGUI.Button(new Rect(920,805,410,40),"RETURN TO OWNED CAR",button))session.ReturnCircuitHire();GUI.enabled=enabled;
            if(ControllerMenuGUI.Button(new Rect(65,805,200,40),"BACK",button))back();
        }
    }
}
