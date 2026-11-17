using System;
using System.Collections.Generic;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>Instantiates validated, engine-independent mesh data. Every instance
    /// owns its meshes/materials, so dents and repainting cannot affect another car.
    /// Four wheel pivots use the same suspension positions as the physical controller.</summary>
    public sealed class VehiclePackAppearance : MonoBehaviour
    {
        readonly List<Material> materials = new List<Material>();
        readonly List<Mesh> deformable = new List<Mesh>();
        readonly List<Vector3[]> originals = new List<Vector3[]>();
        readonly System.Collections.Generic.List<Material> painted=new System.Collections.Generic.List<Material>();
        Texture2D texture;
        public Transform[] Wheels { get; private set; }
        public Transform Steering { get; private set; }
        public Light[] Headlights {get;private set;}
        public Renderer Body { get; private set; }
        public Vector3 CockpitEye { get; private set; }
        public float CockpitPitchDegrees {get;private set;}
        public float DeformationExtent { get; private set; }
        public static VehiclePackAppearance Create(Transform owner, VehiclePack pack, Color paint, int finish)
        {
            var root = new GameObject("Imported vehicle · " + pack.Settings.Id);
            root.transform.SetParent(owner, false);
            var appearance = root.AddComponent<VehiclePackAppearance>();
            appearance.Build(pack, paint, finish);
            return appearance;
        }
        void Build(VehiclePack pack, Color paint, int finish)
        {
            var settings = pack.Settings;
            bool camper=CamperCockpit.Supports(pack);int removedWindowCaps=0,hollowedInteriorTriangles=0,trimmedWindowReveals=0,openedQuarterlights=0;
            if (settings.Id == "earthunscripted.vw-splittie") gameObject.AddComponent<CamperTailpipeSmoke>();
            var entry = Core.ImportedVehicleCatalogue.Find(settings.Id);
            var metric = entry.Metric;
            // The original close-to-glass eye hid the period dashboard below
            // the 74-degree view. Sit above the actual front seat, behind the rim.
            CockpitEye = new Vector3(settings.CockpitX, settings.CockpitY, camper ? .95f : settings.CockpitZ);
            CockpitPitchDegrees=camper?12:0;
            if (pack.TexturePng != null)
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = settings.Id + " texture" };
                if (!ImageConversion.LoadImage(texture, pack.TexturePng, true)) throw new InvalidOperationException("Could not decode the pack texture.");
            }
            foreach (var source in pack.Materials)
            {
                var shared = Geometry.Material("Imported car base", Color.white);
                var material = source.Paint?VehiclePaintJobs.Create(shared,paint,0,finish):GameGraphics.Create(shared, _euMaterial => { _euMaterial.name = settings.Id + " material"; _euMaterial.color = source.Colour; });
                if(source.Paint)painted.Add(material);
                material.SetFloat("_Metallic", source.Metal);
                material.SetFloat("_Smoothness", source.Paint && finish == 1 ? .12f : source.Paint && finish == 2 ? .9f : source.Smoothness);
                if (texture) material.mainTexture = texture;
                // The camper's source glass is an exterior-facing single skin.
                // Keep it opaque from outside and culled from the cabin, across
                // both render pipelines, instead of covering an open aperture.
                if(camper&&source.Colour.b>source.Colour.r*2&&source.Colour.b<.3f)
                {
                    if(material.HasProperty("_Cull"))material.SetFloat("_Cull",2);
                    if(material.HasProperty("_CullMode"))material.SetFloat("_CullMode",2);
                    if(material.HasProperty("_CullModeForward"))material.SetFloat("_CullModeForward",2);
                    if(material.HasProperty("_DoubleSidedEnable"))material.SetFloat("_DoubleSidedEnable",0);
                }
                materials.Add(material);
            }
            Wheels = new Transform[4];
            for (int index = 0; index < 4; index++)
            {
                var wheel = new GameObject("Imported wheel " + index).transform;
                wheel.SetParent(transform, false);
                var anchor = metric.Anchor(index);
                wheel.localPosition = new Vector3(anchor.x, metric.RestWheelLocalY(settings.Mass), anchor.z);
                Wheels[index] = wheel;
            }
            Steering = new GameObject("Imported steering wheel").transform;
            Steering.SetParent(transform, false);
            // Steering mesh coordinates are local to this cockpit pivot.
            Steering.localPosition = new Vector3(settings.CockpitX, settings.CockpitY - .35f, settings.CockpitZ + .45f);
            Headlights=new Light[2];
            for(int side=0;side<2;side++)
            {
                var lamp=new GameObject("Imported headlamp "+side);lamp.transform.SetParent(transform,false);
                lamp.transform.localPosition=new Vector3((side==0?-1:1)*settings.BodyWidth*.30f,settings.RoofHeight*.52f,settings.ColliderLength*.5f+.35f);
                var light=lamp.AddComponent<Light>();HeadlightTuning.Configure(light);lamp.transform.localRotation=Quaternion.Euler(4,0,0);light.enabled=false;
                Headlights[side]=light;
            }
            foreach (var part in pack.Parts)
            {
                if(camper&&part.Attachment=="steering")continue;
                var adapted=part;
                if(camper){adapted=CamperCockpit.OpenCabin(part,out int removed,out int hollowed,out int reveals,out int quarters);removedWindowCaps+=removed;hollowedInteriorTriangles+=hollowed;trimmedWindowReveals+=reveals;openedQuarterlights+=quarters;}
                Transform parent = part.Attachment.StartsWith("wheel", StringComparison.Ordinal) ? Wheels[part.Attachment[5] - '0'] : part.Attachment == "steering" ? Steering : transform;
                var item = Geometry.Mesh(part.Name, parent, adapted.Vertices, adapted.Triangles, adapted.UV, materials[part.Material], false);
                if (adapted.Normals != null) item.GetComponent<MeshFilter>().sharedMesh.normals = adapted.Normals;
                if (part.Attachment == "body")
                {
                    if (!Body) Body = item.GetComponent<Renderer>();
                    // Only painted panels deform; cage, engine and tyres retain shape.
                    if (pack.Materials[part.Material].Paint)
                    {
                        var mesh = item.GetComponent<MeshFilter>().sharedMesh;
                        deformable.Add(mesh); originals.Add(mesh.vertices);
                    }
                }
            }
            if(camper)CamperCockpit.Build(transform,Steering,removedWindowCaps,hollowedInteriorTriangles,trimmedWindowReveals,openedQuarterlights);
        }
        public void SetPaintJob(int pattern){foreach(var material in painted)material.SetFloat("_PaintJob",pattern);}
        public void SetDirt(float dirt){foreach(var material in painted)material.SetFloat("_Dirt",dirt);}
        public void Dent(Vector3 worldPoint, Vector3 worldNormal, float severity)
        {
            Vector3 point = transform.InverseTransformPoint(worldPoint), normal = transform.InverseTransformDirection(worldNormal);
            DeformationExtent = 0;
            for (int part = 0; part < deformable.Count; part++)
            {
                var mesh = deformable[part];
                using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceMesh(mesh);
                var positions = mesh.vertices;
                for (int vertex = 0; vertex < positions.Length; vertex++)
                {
                    positions[vertex] = PanelDeformation.Displace(positions[vertex], originals[part][vertex], point, normal, severity);
                    DeformationExtent = Mathf.Max(DeformationExtent, Vector3.Distance(positions[vertex], originals[part][vertex]));
                }
                mesh.vertices = positions; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            }
        }
        public void Repair()
        {
            DeformationExtent = 0;
            for (int part = 0; part < deformable.Count; part++)
            {
                using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceMesh(deformable[part]);
                deformable[part].vertices = (Vector3[])originals[part].Clone();
                deformable[part].RecalculateNormals(); deformable[part].RecalculateBounds();
            }
        }
        void OnDestroy()
        {
            foreach (var material in materials) if (material) Destroy(material);
            if (texture) Destroy(texture);
        }
    }
}
