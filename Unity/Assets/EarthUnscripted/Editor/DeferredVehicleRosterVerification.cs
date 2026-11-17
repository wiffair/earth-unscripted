using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    // Executes the actual runtime domain, not a duplicate roster algorithm.
    // Plain roots deliberately test membership state only. They are NOT proof of
    // real factory/collider/stage provenance; wrapper tests must refuse them.
    public static class DeferredVehicleRosterVerification
    {
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static readonly Type Domain=typeof(GameSession).Assembly.GetType("EarthUnscripted.VehiclePopulation.ActualVehicleRosterDomain",true);
        static readonly Type Role=Domain.GetNestedType("Role",BindingFlags.NonPublic);
        static int checks;
        static int PoliceSlots=>(int)Domain.GetField("RoadPoliceSlots",BindingFlags.Static|BindingFlags.NonPublic).GetRawConstantValue();
        static int TrafficSlots=>(int)Domain.GetField("RoadTrafficSlots",BindingFlags.Static|BindingFlags.NonPublic).GetRawConstantValue();
        static void Check(bool valid,string message){if(!valid)throw new InvalidOperationException(message);checks++;}
        static object Call(object owner,string name,params object[] args)
        {try{return owner.GetType().GetMethod(name,Instance).Invoke(owner,args);}catch(TargetInvocationException error){throw error.InnerException??error;}}
        static object Property(object owner,string name)=>owner.GetType().GetProperty(name,Instance).GetValue(owner);
        static object NewDomain()=>Activator.CreateInstance(Domain,Instance,null,new object[]{false},null);
        static object Begin(object domain,string slot,string role)=>Call(domain,"BeginFactory",slot,Enum.Parse(Role,role));
        static object Add(object domain,string slot,string role,bool layout=true,object root=null)
        {var ticket=Begin(domain,slot,role);root=root??new object();Call(ticket,"Observe",root);Call(ticket,"CommitObservedRoot");if(layout)Call(domain,"ConfirmPhysicalLayout",root);return root;}
        static void ExpectReject(Action action,string message)
        {bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Check(rejected,message);}
        static bool TrySeal(object domain)=>(bool)Call(domain,"TrySealDeferredActualBootRoster");
        static void Unavailable(object domain,string message)
        {Check(!(bool)Property(domain,"IsComplete")&&!(bool)Property(domain,"CanSampleActualMembers"),message);}
        static object Partial()
        {var domain=NewDomain();Add(domain,"player","Player");for(int i=0;i<TrafficSlots-2;i++)Add(domain,"traffic/"+i,"Traffic");for(int i=0;i<PoliceSlots;i++)Add(domain,"police/"+i,"Police");return domain;}
        static object Complete(string wrongRole=null)
        {var domain=NewDomain();Add(domain,"player","Player");for(int i=0;i<TrafficSlots;i++)Add(domain,"traffic/"+i,i==0&&wrongRole!=null?wrongRole:"Traffic");for(int i=0;i<PoliceSlots;i++)Add(domain,"police/"+i,"Police");return domain;}
        public static int Exercise()
        {
            checks=0;
            var partial=Partial();Check((int)Property(partial,"MemberCount")==1+TrafficSlots-2+PoliceSlots,"Actual road domain has player plus all declared police and all but two declared traffic, not a reduced expected roster");
            Check(!TrySeal(partial),"Source roster missing two traffic slots actual required slots remain deferred");Unavailable(partial,"Missing source slots cannot publish or sample a complete scene");
            Check((string)Property(partial,"UnboundReason")=="BootRosterUnsealed","Deferral remains unsealed rather than poisoning the roster");
            ExpectReject(()=>Call(partial,"SealActualBootRoster"),"Old strict seal still rejectsSource roster missing two traffic slots");
            Check(!TrySeal(partial),"A refused strict seal does not silently fill missing slots");
            var ticket=Begin(partial,"traffic/"+(TrafficSlots-2),"Traffic");Check(!TrySeal(partial),"Outstanding actual factory ticket prevents deferred seal");
            var root22=new object();Call(ticket,"Observe",root22);Call(ticket,"CommitObservedRoot");
            Check(!TrySeal(partial),"Committednext identity without its physical-layout confirmation remains unavailable");
            Call(partial,"ConfirmPhysicalLayout",root22);Check(!TrySeal(partial),"One confirmed replacement cannot stand in for the final source slot");
            var root23=Add(partial,"traffic/"+(TrafficSlots-1),"Traffic",false);Check(!TrySeal(partial),"All source identities remain unsealed until final physical layout confirmation");
            Unavailable(partial,"Pending final layout never claims completeness");Call(partial,"ConfirmPhysicalLayout",root23);
            Check(TrySeal(partial),"Actual domain seals only when all required source slots and layouts are present");
            Check((bool)Property(partial,"IsComplete")&&(bool)Property(partial,"CanSampleActualMembers"),"Full validated membership becomes available to the trusted sampler");
            var revision=Property(partial,"Revision");Check(TrySeal(partial)&&revision.Equals(Property(partial,"Revision")),"Repeated deferred seal is idempotent after valid boot sealing");
            Call(partial,"Poison","actual post-seal failure");Unavailable(partial,"Poison after sealing revokes completeness even if seal is idempotent");
            var pending=Complete();var extra=Begin(pending,"race/pending","RaceOpponent");Check(!TrySeal(pending),"Pending factory prevents sealing even with all required source slots present");
            ((IDisposable)extra).Dispose();ExpectReject(()=>TrySeal(pending),"Abandoned actual factory poisons deferred completeness");Unavailable(pending,"Abandoned factory cannot publish members");
            var revoked=Partial();Call(revoked,"Revoke");ExpectReject(()=>TrySeal(revoked),"Revoked deferred domain cannot seal");Unavailable(revoked,"Revoked domain stays unavailable");
            revoked=Complete();Check(TrySeal(revoked),"Revocation case starts from a genuinely full membership");Call(revoked,"Revoke");ExpectReject(()=>TrySeal(revoked),"Revocation invalidates a previously sealed boot");
            var poisoned=Partial();Call(poisoned,"Poison","test failure");ExpectReject(()=>TrySeal(poisoned),"Poisoned unsealed domain rejects deferred seal");
            var undeclared=Complete();Add(undeclared,"traffic/not-source","Traffic");ExpectReject(()=>TrySeal(undeclared),"Undeclared pre-seal actor cannot inflate the required roster");
            var wrong=Complete("Police");ExpectReject(()=>TrySeal(wrong),"Wrong role in a required source slot fails original strict authority check");Unavailable(wrong,"Role mismatch cannot produce a complete domain");
            var strict=Complete();Call(strict,"SealActualBootRoster");Check((bool)Property(strict,"IsComplete"),"Original strict seal still accepts the complete source roster");
            var root=new GameObject("Inactive deferred-roster wrapper guard fixture");root.SetActive(false);
            try
            {
                var session=root.AddComponent<GameSession>();
                var type=typeof(GameSession).Assembly.GetType("EarthUnscripted.VehiclePopulation.SessionVehiclePopulation",true);
                var wrapper=Activator.CreateInstance(type,Instance,null,new object[]{session,false},null);
                var validated=type.GetField("actualBootActivationValidated",Instance);
                ExpectReject(()=>Call(wrapper,"ValidateAndSealActualBoot",new object[]{null}),"Empty wrapper rejects null boot activation receipt");
                Check(!(bool)validated.GetValue(wrapper),"Failed null receipt never enables deferred per-frame seal");
                ExpectReject(()=>Call(wrapper,"ValidateAndSealActualBoot",new object()),"Empty wrapper rejects caller-created non-stage object");
                Check(!(bool)validated.GetValue(wrapper),"Empty membership cannot vacuously validate stage provenance");
                var domain=Property(wrapper,"Domain");
                GameObject Identity(string slot){var item=new GameObject("Unvalidated identity "+slot);item.transform.SetParent(root.transform,false);return item;}
                Add(domain,"player","Player",true,Identity("player"));for(int i=0;i<TrafficSlots;i++)Add(domain,"traffic/"+i,"Traffic",true,Identity("traffic/"+i));for(int i=0;i<PoliceSlots;i++)Add(domain,"police/"+i,"Police",true,Identity("police/"+i));
                var phase=typeof(GameSession).Assembly.GetType("EarthUnscripted.VehiclePopulation.VehicleSamplePhase",true);
                Call(wrapper,"Sample",Enum.Parse(phase,"AfterActorFrame"));Unavailable(domain,"Full artificial membership without a genuine stage cannot bypass sampler's activation gate");
                ExpectReject(()=>Call(wrapper,"ValidateAndSealActualBoot",new object()),"Membership-only roots cannot substitute for the actual registered session player/stage");
                Check(!(bool)validated.GetValue(wrapper),"Missing actual session-player/stage receipt leaves activation gate closed");
                object[] capture={null,null};Check(!(bool)type.GetMethod("TryCapture",Instance).Invoke(wrapper,capture)&&capture[0]==null,"World-less invalid wrapper exposes no safe-looking population snapshot");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
            return checks;
        }
        public static void Run(){Debug.Log("DEFERRED VEHICLE ROSTER PASS "+Exercise());}
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
