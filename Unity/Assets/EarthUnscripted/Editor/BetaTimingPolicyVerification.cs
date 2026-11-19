using System;
using System.Reflection;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class BetaTimingPolicyVerification
    {
        public static void Run()
        {
            var policy=typeof(BetaDrivingTelemetry).GetMethod("SelectLightweight",BindingFlags.Static|BindingFlags.NonPublic);
            int checks=0;
            void Check(bool proof,bool light,bool detail,bool expected,string label)
            {
                if((bool)policy.Invoke(null,new object[]{proof,light,detail})!=expected)throw new Exception("TIMING POLICY FAIL "+label);
                checks++;
            }
            Check(false,false,false,true,"ordinary driving uses lightweight collection");
            Check(true,false,false,false,"explicit isolated proof retains detailed evidence by default");
            Check(false,false,true,false,"a requested diagnostic can retain periodic reports");
            Check(true,true,false,true,"normal-like isolated comparison disables detailed work");
            Check(false,true,true,true,"explicit lightweight mode wins over detailed timing");
            Check(true,true,true,true,"comparison remains lightweight when both switches appear");
            Debug.Log("BETA TIMING POLICY PASS "+checks);
        }
    }
}
