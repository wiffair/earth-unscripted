using System;using System.IO;using UnityEngine;using UnityEditor;
namespace EarthUnscripted.Editor
{
    public static class OpenRouteVerification
    {
        public static void Run()
        {
            int checks=0;
            void Require(bool value,string reason){if(!value)throw new Exception(reason);checks++;Debug.Log("OPEN ROUTE PASS "+reason);}
            GeoRegion Region(Vector3[] points)
            {var r=new GeoRegion{route=points,roads=Array.Empty<GeoRoad>(),waters=Array.Empty<GeoWater>(),areas=Array.Empty<GeoArea>(),buildings=Array.Empty<GeoBuilding>(),minX=-100,minZ=-100,width=200,depth=200,nx=2,nz=2,heights=new float[4],landNx=2,landNz=2,landCover=new int[4]};r.Prepare();return r;}
            try
            {
                var points=new[]{new Vector3(0,0,0),new Vector3(20,2,0),new Vector3(20,4,30)};
                var region=Region(points);var route=new RoutePath(region);
                Require(!route.IsClosed,"geographic open route recognized");
                Require(route.At(-2)==points[0]&&route.At(-200)==points[0],"negative open distance clamps to start rather than wrapping from remote endpoint");
                Require(route.At(route.Length+2)==points[2]&&route.At(route.Length+200)==points[2],"open distance beyond finish clamps to finish");
                Require(Vector3.Dot(route.Forward(0),(points[1]-points[0]).normalized)>.99999f,"start tangent follows first road segment");
                Require(Vector3.Dot(route.Forward(route.Length),(points[2]-points[1]).normalized)>.99999f,"finish tangent follows final road segment");
                Require(Vector3.Distance(route.Rotation(0)*Vector3.forward,route.Forward(0))<.00001f,"initial vehicle orientation agrees with valid start tangent");
                Require(Mathf.Abs(Vector3.Distance(route.At(-2,2),points[0])-2)<.00001f,"clamped start retains lateral lane offset");
                var reverse=new RoutePath(region,true);Require(!reverse.IsClosed&&reverse.At(-2)==points[2],"reversed open guide retains correct endpoint policy");
                Require(Vector3.Dot(reverse.Forward(0),(points[1]-points[2]).normalized)>.99999f,"reversed guide start tangent follows actual first segment");
                var closedPoints=new[]{Vector3.zero,new Vector3(20,0,0),new Vector3(20,0,20),Vector3.zero};var closed=new RoutePath(Region(closedPoints));
                Require(closed.IsClosed,"closed geographic loop recognized");
                Require(Vector3.Distance(closed.At(-2),closed.At(closed.Length-2))<.00001f,"closed geographic negative distances still wrap");
                Require(Vector3.Distance(closed.At(closed.Length+2),closed.At(2))<.00001f,"closed geographic finish still wraps");
                var generated=new RoutePath();Require(generated.IsClosed&&Vector3.Distance(generated.At(-2),generated.At(generated.Length-2))<.00001f,"procedural county circuit retains original wrapping");
                Require(region.route[0]==points[0]&&region.route[2]==points[2],"sampling and reversal preserve source guide");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/open-route.txt","PASS "+checks+" endpoint, tangent, lateral, reverse and loop checks\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
