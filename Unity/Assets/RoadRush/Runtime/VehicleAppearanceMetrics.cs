using System;using System.Collections.Generic;using System.Linq;using UnityEngine;using RoadRush.Core;
namespace RoadRush
{
    // Fits appearance geometry while preserving the physical root transform.
    public static class VehicleAppearanceMetrics
    {
        const float SourceProceduralWidth=2.11f,SourceProceduralLength=4.405f,SourceProceduralCentreZ=.0174998f;
        public static Vector3 ProceduralPoint(int model,Vector3 p)
        {
            var m=VehicleDimensions.ForModel(model);p.x*=m.BodyWidth/SourceProceduralWidth;
            if(model==0){p.z=m.ColliderCentreZ+(p.z-SourceProceduralCentreZ)*m.BodyLength/SourceProceduralLength;p.y*=m.RoofLocalY/1.585f;}
            else p.z*=GarageRules.Catalogue[model].Length;
            return p;
        }
        static bool Beneath(Transform item,Transform root)=>root&&(item==root||item.IsChildOf(root));
        static void Recalculate(Mesh mesh)
        {mesh.RecalculateNormals();mesh.RecalculateBounds();if(mesh.uv.Length==mesh.vertexCount)mesh.RecalculateTangents();}
        public static void FitProcedural(Transform owner,int model,Renderer body,Transform[] wheels,Transform steering,Transform[] wipers)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(owner);

            var m=VehicleDimensions.ForModel(model);var sourceChildren=new List<Transform>();foreach(Transform t in owner)sourceChildren.Add(t);
            var rigid=new List<Transform>(wheels);if(steering)rigid.Add(steering);if(wipers!=null)foreach(var w in wipers)if(w)rigid.Add(w.parent);foreach(var light in owner.GetComponentsInChildren<Light>(true))if(light.transform.parent==owner)rigid.Add(light.transform);
            // Keep primitive meshes shared/instanced for24NPCs. An appearance-only
            // subtree applies one affine fit; never scale the physical owner.
            var appearance=new GameObject("Metric body appearance").transform;appearance.SetParent(owner,false);
            foreach(var child in sourceChildren)if(child!=body.transform&&!rigid.Contains(child))child.SetParent(appearance,false);
            float sx=m.BodyWidth/SourceProceduralWidth,sy=model==0?m.RoofLocalY/1.585f:1,sz=model==0?m.BodyLength/SourceProceduralLength:GarageRules.Catalogue[model].Length;
            appearance.localScale=new Vector3(sx,sy,sz);appearance.localPosition=model==0?new Vector3(0,0,m.ColliderCentreZ-SourceProceduralCentreZ*sz):Vector3.zero;
            var mesh=body.GetComponent<MeshFilter>().sharedMesh;
            using var appearanceMeshWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceMesh(mesh);var v=mesh.vertices;for(int i=0;i<v.Length;i++)v[i]=ProceduralPoint(model,v[i]);mesh.vertices=v;Recalculate(mesh);
            foreach(var r in rigid)if(r&&!wheels.Contains(r))r.localPosition=ProceduralPoint(model,r.localPosition);
            if(wipers!=null&&wipers.Length>1&&wipers[1]){var p=wipers[1].parent.localPosition;p.x=.24f;wipers[1].parent.localPosition=p;}
            for(int i=0;i<wheels.Length;i++)wheels[i].localPosition=new Vector3(i<2?-m.Track*.5f:m.Track*.5f,m.RestWheelLocalY(GarageRules.Catalogue[model].Mass),i%2==0?-m.Wheelbase*.5f:m.Wheelbase*.5f);
            foreach(Transform t in appearance)if(t.name=="Mirror")
            {float sign=Mathf.Sign(t.localPosition.x);var position=t.localPosition;position.x=sign*(m.MirrorSpan*.5f-.085f)/sx;t.localPosition=position;var size=t.localScale;size.x=.17f/sx;t.localScale=size;}
        }
        public static void FitImportedWheel(Transform owner,Transform wheel,Transform caliper,int model)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(owner);

            var m=VehicleDimensions.ForModel(model);var pivot=wheel.parent;var points=new List<Vector3>();foreach(var filter in wheel.GetComponentsInChildren<MeshFilter>(true))if(filter.sharedMesh)foreach(var v in filter.sharedMesh.vertices)points.Add(pivot.InverseTransformPoint(filter.transform.TransformPoint(v)));
            if(points.Count==0)throw new InvalidOperationException("Missing wheel geometry");float lo=points.Min(p=>p.x),hi=points.Max(p=>p.x),radius=points.Max(p=>new Vector2(p.y,p.z).magnitude);if(hi-lo<.01f||radius<.05f)throw new InvalidOperationException("Invalid source wheel dimensions");float axial=m.TyreWidth/(hi-lo),radial=m.Radius/radius;
            // Source wheels are detached before body nonuniform fitting. A common
            // radial factor preserves circular tyre/rim/spoke geometry.
            wheel.localScale=Vector3.Scale(wheel.localScale,new Vector3(axial,radial,radial));if(caliper)caliper.localScale=Vector3.Scale(caliper.localScale,new Vector3(axial,radial,radial));
        }
    }
}
