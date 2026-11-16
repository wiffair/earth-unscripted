// DATA-only visibility proposal. Detach only tyre/rim/spokes/fasteners; keep mounted brake/hub.
// Leaf activeSelf changes preserve Renderer.enabled presentation masking and all wheel poses.
// Wet channels follow requested compound AND wheel visibility. Native proof remains pending.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadRush
{
    // Original single-seat mesh factory used by the model3 CarVisual branch.
    // Root is ground plane at design ride height, NOT road-car suspension root.
    public sealed class SingleSeatCandidate : MonoBehaviour
    {
        [Serializable]
        public sealed class Pack
        {
            public string recipe;
            public bool metres;
            public Part[] parts;
            public float[] cockpitEye;
            public float wheelbase, track;
        }

        [Serializable]
        public sealed class Part
        {
            public string name, material, attachment;
            public int wheelIndex;
            public float[] anchor, vertices;
            public int[] triangles;
        }

        public readonly List<Transform[]> WheelPivots = new List<Transform[]>();
        TextMesh speedReadout, yokeReadout;
        int displayedMph = -1;
        public string DashboardReadout => speedReadout ? speedReadout.text : "";
        public Renderer BodyRenderer { get; private set; }

        readonly List<Mesh> panels = new List<Mesh>();
        readonly List<Vector3[]> originals = new List<Vector3[]>();
        readonly List<Transform> panelTransforms = new List<Transform>();
        readonly List<Renderer> wetTread = new List<Renderer>();
        readonly List<Transform> steering = new List<Transform>();
        public Vector3 CockpitEye { get; private set; }

        readonly List<Transform[]> uprightPivots = new List<Transform[]>();
        Material rimPalette;
        static bool IsRimPart(string name) => name != null && name.Length == 4 && name.StartsWith("Rim", StringComparison.Ordinal) && name[3] >= '0' && name[3] <= '3';
        readonly List<Transform> detachableWheelParts = new List<Transform>();
        bool wheelPartsVisible = true, wetCompoundChannels;
        static readonly HashSet<string> DetachableWheelNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Wheel0",
            "Rim0",
            "WheelSpokes0",
            "WheelFasteners0",
            "Wheel1",
            "Rim1",
            "WheelSpokes1",
            "WheelFasteners1",
            "Wheel2",
            "Rim2",
            "WheelSpokes2",
            "WheelFasteners2",
            "Wheel3",
            "Rim3",
            "WheelSpokes3",
            "WheelFasteners3"
        };
        readonly List<UnityEngine.Object> generated = new List<UnityEngine.Object>();
        static Color Colour(string key)
        {
            switch (key)
            {
                case "paint":
                    return new Color(.025f, .48f, .59f);
                case "rubber":
                    return new Color(.045f, .048f, .055f);
                case "metal":
                    return new Color(.48f, .54f, .6f);
                case "screen":
                    return new Color(.18f, .8f, .58f);
                case "red":
                    return new Color(.8f, .08f, .035f);
                case "carbon":
                    return new Color(.065f, .075f, .09f);
                default:
                    throw new ArgumentException("Unknown material");
            }
        }

        public static SingleSeatCandidate Create(Transform parent)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(parent);
            var root = new GameObject("Original single seat geometry");
            root.transform.SetParent(parent, false);
            var candidate = root.AddComponent<SingleSeatCandidate>();
            try
            {
                candidate.Build();
                return candidate;
            }
            catch
            {
                if (Application.isPlaying)
                    Destroy(root);
                else
                    DestroyImmediate(root);
                throw;
            }
        }

        void Build()
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(transform);
            var materials = new Dictionary<string, Material>();
            var lods = new LOD[3];
            for (int level = 0; level < 3; level++)
            {
                var asset = Resources.Load<TextAsset>("Vehicles/single-seat-lod" + level);
                if (asset == null || asset.text.Length > 1000000)
                    throw new InvalidOperationException("Missing/big single seat pack");
                var pack = JsonUtility.FromJson<Pack>(asset.text);
                if (pack == null || pack.recipe != "original-single-seat-v1" || !pack.metres || pack.parts == null || pack.parts.Length > 128 || pack.cockpitEye == null || pack.cockpitEye.Length != 3)
                    throw new InvalidOperationException("Invalid single seat contract");
                CockpitEye = new Vector3(pack.cockpitEye[0], pack.cockpitEye[1], pack.cockpitEye[2]);
                var lodRoot = new GameObject("LOD" + level).transform;
                lodRoot.SetParent(transform, false);
                var wheels = new Transform[4];
                var uprights = new Transform[4];
                for (int i = 0; i < 4; i++)
                {
                    wheels[i] = new GameObject("WheelPivot" + i).transform;
                    wheels[i].SetParent(lodRoot, false);
                }

                WheelPivots.Add(wheels);
                for (int i = 0; i < 4; i++)
                {
                    uprights[i] = new GameObject("Wheel upright" + i).transform;
                    uprights[i].SetParent(lodRoot, false);
                }

                uprightPivots.Add(uprights);
                var renderers = new List<Renderer>();
                foreach (var part in pack.parts)
                {
                    if (part.vertices == null || part.vertices.Length % 3 != 0 || part.vertices.Length > 30000 || part.triangles == null || part.triangles.Length % 3 != 0 || part.triangles.Length > 60000 || part.anchor == null || part.anchor.Length != 3)
                        throw new InvalidOperationException("Invalid mesh");
                    for (int j = 0; j < 3; j++)
                    {
                        float value = part.anchor[j];
                        if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 10)
                            throw new InvalidOperationException("Invalid anchor");
                    }

                    var vertices = new Vector3[part.vertices.Length / 3];
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            float value = part.vertices[i * 3 + j];
                            if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 10)
                                throw new InvalidOperationException("Invalid coordinate");
                        }

                        vertices[i] = new Vector3(part.vertices[i * 3], part.vertices[i * 3 + 1], part.vertices[i * 3 + 2]);
                    }

                    foreach (int index in part.triangles)
                        if (index < 0 || index >= vertices.Length)
                            throw new InvalidOperationException("Invalid triangle");
                    string paletteKey = part.material == "metal" && IsRimPart(part.name) ? "rim-metal" : part.material;
                    if (!materials.TryGetValue(paletteKey, out var material))
                    {
                        material = GameGraphics.Create(Shader.Find("Universal Render Pipeline/Lit"), _rrMaterial => { _rrMaterial.name = "Single seat " + paletteKey; _rrMaterial.color = Colour(part.material); _rrMaterial.enableInstancing = true; });
                        material.SetFloat("_Metallic", part.material == "rubber" ? 0 : part.material == "metal" ? .85f : part.material == "paint" ? .35f : .05f);
                        material.SetFloat("_Smoothness", part.material == "rubber" ? .08f : part.material == "carbon" ? .23f : part.material == "metal" ? .55f : .3f);
                        materials.Add(paletteKey, material);
                        generated.Add(material);
                        if (paletteKey == "rim-metal")
                            rimPalette = material;
                    }

                    var obj = new GameObject(part.name);
                    obj.transform.SetParent(lodRoot, false);
                    var anchor = new Vector3(part.anchor[0], part.anchor[1], part.anchor[2]);
                    if (part.attachment == "upright")
                    {
                        if (part.wheelIndex < 0 || part.wheelIndex > 3)
                            throw new InvalidOperationException("Invalid upright");
                        uprights[part.wheelIndex].localPosition = anchor;
                        obj.transform.SetParent(uprights[part.wheelIndex], false);
                    }
                    else if (part.name.StartsWith("Wheel") || part.name.StartsWith("Rim"))
                    {
                        int index;
                        if (!int.TryParse(part.name.Substring(part.name.Length - 1), out index) || index < 0 || index > 3)
                            throw new InvalidOperationException("Invalid wheel index");
                        wheels[index].localPosition = anchor;
                        obj.transform.SetParent(wheels[index], false);
                    }
                    else
                        obj.transform.localPosition = anchor;
                    var mesh = new Mesh
                    {
                        name = part.name
                    };
                    mesh.vertices = vertices;
                    mesh.triangles = part.triangles;
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                    generated.Add(mesh);
                    obj.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = obj.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderers.Add(renderer);
                    if (DetachableWheelNames.Contains(part.name))
                        detachableWheelParts.Add(obj.transform);
                    if (part.material == "paint")
                    {
                        if (BodyRenderer == null)
                            BodyRenderer = renderer;
                        panels.Add(mesh);
                        originals.Add(mesh.vertices);
                        panelTransforms.Add(obj.transform);
                    }

                    if (part.name == "Steering wheel")
                        steering.Add(obj.transform);
                }

                lods[level] = new LOD(level == 0 ? .16f : level == 1 ? .07f : .015f, renderers.ToArray());
            }

            var group = gameObject.AddComponent<LODGroup>();
            group.SetLODs(lods);
            group.RecalculateBounds();
            BuildWetTread();
            speedReadout = Geometry.FittedLabel("000", transform, new Vector3(-.015f, .773f, .103f), Quaternion.identity, .13f, .021f, Color.white);
            speedReadout.name = "Race dashboard speed";
            Geometry.FittedLabel("MPH", transform, new Vector3(.09f, .773f, .102f), Quaternion.identity, .04f, .010f, Color.white).name = "Race dashboard speed unit";
            var instrumentRoot = new GameObject("Steering-relative instruments").transform;
            instrumentRoot.SetParent(transform, false);
            instrumentRoot.localPosition = new Vector3(0, .71f, .04f);
            steering.Add(instrumentRoot);
            yokeReadout = Geometry.FittedLabel("000", instrumentRoot, new Vector3(0, .020f, -.039f), Quaternion.identity, .075f, .019f, Color.white);
            yokeReadout.name = "Race yoke speed";
            Geometry.FittedLabel("MPH", instrumentRoot, new Vector3(0, .002f, -.040f), Quaternion.identity, .026f, .006f, Color.white).name = "Race yoke speed unit";
        }

        // Factory/demo wheel-pose API; it does not drive suspension links.
        // Production suspension is wired through CarVisual.FollowWheel -> FollowWheel.
        // Wheel order: left rear/front, right rear/front.
        public void PoseWheels(float frontSteerDegrees, float spinDegrees)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);
            foreach (var wheels in WheelPivots)
                for (int i = 0; i < 4; i++)
                    wheels[i].localRotation = Quaternion.Euler(0, i % 2 == 1 ? frontSteerDegrees : 0, 0) * Quaternion.Euler(spinDegrees, 0, 0);
            foreach (var uprights in uprightPivots)
                for (int i = 0; i < 4; i++)
                    uprights[i].localRotation = Quaternion.Euler(0, i % 2 == 1 ? frontSteerDegrees : 0, 0);
        }

        public void SetWheelsVisible(bool visible)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);
            wheelPartsVisible = visible;
            foreach (var part in detachableWheelParts)
                part.gameObject.SetActive(visible);
            foreach (var renderer in wetTread)
                renderer.gameObject.SetActive(visible && wetCompoundChannels);
        }

        public void FollowWheel(int index, Transform source, float steer = 0)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);
            foreach (var wheels in WheelPivots)
            {
                wheels[index].position = source.position;
                wheels[index].rotation = source.rotation;
            }

            foreach (var uprights in uprightPivots)
            {
                uprights[index].position = source.position;
                uprights[index].rotation = source.parent.rotation * Quaternion.Euler(0, steer, 0);
            }
        }

        public void Instruments(float speed)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);
            int mph = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(speed) * Core.DrivingMath.MphPerMetreSecond), 0, 999);
            if (mph == displayedMph)
                return;
            displayedMph = mph;
            string text = mph.ToString("000");
            if (speedReadout)
                speedReadout.text = text;
            if (yokeReadout)
                yokeReadout.text = text;
        }

        public void Style(int wheelStyle, int bodyKit)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);
            if (rimPalette)
                rimPalette.color = wheelStyle == 1 ? new Color(.14f, .17f, .2f) : wheelStyle == 2 ? new Color(.65f, .48f, .16f) : new Color(.48f, .54f, .6f);
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name == "Wing endplate")
                    renderer.transform.localScale = new Vector3(1, bodyKit == 1 ? 1.12f : bodyKit == 2 ? .9f : 1, 1);
            }
        }

        public void Paint(Color colour, int finish)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                if (renderer.sharedMaterial.name == "Single seat paint")
                {
                    renderer.sharedMaterial.color = colour;
                    renderer.sharedMaterial.SetFloat("_Smoothness", finish == 1 ? .18f : finish == 2 ? .9f : .55f);
                }
        }

        public void Steering(float degrees)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);
            foreach (var t in steering)
                t.localRotation = Quaternion.Euler(0, 0, degrees * .3f);
        }

        public void SetCompound(Core.RaceCompound compound)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);
            wetCompoundChannels = compound == Core.RaceCompound.Intermediate || compound == Core.RaceCompound.Wet;
            foreach (var renderer in wetTread)
                renderer.gameObject.SetActive(wetCompoundChannels && wheelPartsVisible);
        }

        void BuildWetTread()
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(transform);
            var material = GameGraphics.Create(Shader.Find("Universal Render Pipeline/Lit"), _rrMaterial => { _rrMaterial.name = "Wet tread channels"; _rrMaterial.color = new Color(.012f, .014f, .018f); });
            generated.Add(material);
            var group = GetComponent<LODGroup>();
            var lods = group.GetLODs();
            for (int level = 0; level < WheelPivots.Count; level++)
                for (int wheel = 0; wheel < 4; wheel++)
                {
                    var wheels = WheelPivots[level];
                    var obj = new GameObject("Wet circumferential grooves");
                    obj.transform.SetParent(wheels[wheel], false);
                    var vertices = new List<Vector3>();
                    var triangles = new List<int>();
                    float width = wheel % 2 == 0 ? .38f : .305f;
                    for (int band = -1; band <= 1; band++)
                        for (int i = 0; i < 48; i++)
                        {
                            float a = i * Mathf.PI * 2 / 48, b = (i + 1) * Mathf.PI * 2 / 48;
                            int start = vertices.Count;
                            foreach (var q in new[]
                            {
                                new Vector2(a, -.008f),
                                new Vector2(a, .008f),
                                new Vector2(b, .008f),
                                new Vector2(b, -.008f)
                            }

                            )
                                vertices.Add(new Vector3(band * width * .25f + q.y, Mathf.Sin(q.x) * .361f, Mathf.Cos(q.x) * .361f));
                            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3, start + 2, start + 1, start, start + 3, start + 2, start });
                        }

                    var mesh = new Mesh
                    {
                        name = "Original wet tread"
                    };
                    mesh.SetVertices(vertices);
                    mesh.SetTriangles(triangles, 0);
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                    generated.Add(mesh);
                    obj.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = obj.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    wetTread.Add(renderer);
                    var renderers = new List<Renderer>(lods[level].renderers);
                    renderers.Add(renderer);
                    lods[level].renderers = renderers.ToArray();
                    obj.SetActive(false);
                }

            group.SetLODs(lods);
            group.RecalculateBounds();
        }

        public void Dent(Vector3 world, Vector3 inward, float impulse)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(transform);
            for (int p = 0; p < panels.Count; p++)
            {
                using var appearanceMeshWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceMesh(panels[p]);
                var t = panelTransforms[p];
                var v = panels[p].vertices;
                for (int i = 0; i < v.Length; i++)
                    v[i] = PanelDeformation.Displace(v[i], originals[p][i], t.InverseTransformPoint(world), t.InverseTransformDirection(inward), impulse);
                panels[p].vertices = v;
                panels[p].RecalculateNormals();
                panels[p].RecalculateBounds();
            }
        }

        public void Repair()
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(transform);
            for (int p = 0; p < panels.Count; p++)
            {
                using var appearanceMeshWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceMesh(panels[p]);
                panels[p].vertices = (Vector3[])originals[p].Clone();
                panels[p].RecalculateNormals();
                panels[p].RecalculateBounds();
            }
        }

        public float DeformationExtent
        {
            get
            {
                float extent = 0;
                for (int p = 0; p < panels.Count; p++)
                {
                    var v = panels[p].vertices;
                    for (int i = 0; i < v.Length; i++)
                        extent = Mathf.Max(extent, Vector3.Distance(v[i], originals[p][i]));
                }

                return extent;
            }
        }

        void OnDestroy()
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(transform);
            foreach (var obj in generated)
            {
                using var appearanceMeshWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceMesh(obj as Mesh);
                if (obj != null)
                {
                    if (Application.isPlaying)
                        Destroy(obj);
                    else
                        DestroyImmediate(obj);
                }
            }
        }
    }
}
