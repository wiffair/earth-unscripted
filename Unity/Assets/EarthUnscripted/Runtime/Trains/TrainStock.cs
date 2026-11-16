using System;
using System.Linq;
using UnityEngine;
using Pose=EarthUnscripted.Candidates.Articulated.Pose;
using EarthUnscripted.Candidates.Articulated;
using EarthUnscripted.Candidates.Compatibility;
namespace EarthUnscripted
{
    public enum TrainBodyStyle { Regional, Civity, Intercity, Locomotive, Container }
    public sealed class TrainVehicle
    {
        public readonly string Id;public readonly TrainBodyStyle Style;public readonly float Length,Width,Height,BogieHalfBase,WheelRadius,Mass;public readonly int Axles;
        public TrainVehicle(string id,TrainBodyStyle style,float length,float width,float height,float halfBase,float radius,float mass,int axles=2)
        {Id=id;Style=style;Length=length;Width=width;Height=height;BogieHalfBase=halfBase;WheelRadius=radius;Mass=mass;Axles=axles;}
    }
    public sealed class TrainFormation
    {
        public readonly string Id,Name,Description;public readonly TrainVehicle[] Vehicles;public readonly float Gap,MaximumSpeed,Acceleration,Brake,Gauge;public readonly Color Body,Accent;
        public float Length=>Vehicles.Sum(v=>v.Length)+Gap*(Vehicles.Length-1);
        public TrainFormation(string id,string name,string description,TrainVehicle[] cars,float gap,float mph,float acceleration,float brake,Color body,Color accent,float gauge=1.435f)
        {Id=id;Name=name;Description=description;Vehicles=cars;Gauge=gauge;Gap=gap;MaximumSpeed=mph/2.2369363f;Acceleration=acceleration;Brake=brake;Body=body;Accent=accent;}
        public Pose Pose(RailRoute route,double head,int direction=1)
        {
            double tail=head-direction*Length;if(Math.Min(head,tail)<.4||Math.Max(head,tail)>route.Length-.4)return new Pose("retirement-needed",head,tail,FrozenList<Car>.Own(Array.Empty<Car>()));
            var result=new Car[Vehicles.Length];double offset=0;
            for(int i=0;i<result.Length;i++)
            {
                var spec=Vehicles[i];double station=head-direction*(offset+spec.Length*.5);offset+=spec.Length+Gap;
                var front=route.At(station+direction*spec.BogieHalfBase);var rear=route.At(station-direction*spec.BogieHalfBase);
                var frame=Frame.From(Vec.Mul(Vec.Add(front,rear),.5),Vec.Unit(Vec.Sub(front,rear)));var bogies=new Bogie[2];
                for(int b=0;b<2;b++)
                {
                    double at=station+direction*(b==0?-spec.BogieHalfBase:spec.BogieHalfBase);var bf=route.AtFrame(at,1.25,direction);var axles=new Axle[spec.Axles];
                    for(int a=0;a<axles.Length;a++)
                    {
                        double wheelStation=at+direction*(a-(axles.Length-1)*.5)*(axles.Length==3?1.8:2.5);var wf=route.AtFrame(wheelStation,.2,direction);var wheels=new Wheel[2];
                        for(int w=0;w<2;w++){double x=w==0?-Gauge*.5:Gauge*.5;wheels[w]=new Wheel(wf.Local(new Vec3(x,0,0)),wf.Local(new Vec3(x,spec.WheelRadius,0)),direction*wheelStation/spec.WheelRadius);}
                        axles[a]=new Axle(wheelStation,wf,FrozenList<Wheel>.Own(wheels));
                    }
                    bogies[b]=new Bogie(at,bf,FrozenList<Axle>.Own(axles));
                }
                var corners=new Vec3[8];int n=0;
                for(int x=0;x<2;x++)for(int y=0;y<2;y++)for(int z=0;z<2;z++)corners[n++]=frame.Local(new Vec3((x==0?-.5:.5)*spec.Width,y==0?.12:spec.Height,(z==0?-.5:.5)*spec.Length));
                result[i]=new Car(station,frame,FrozenList<Bogie>.Own(bogies),FrozenList<Vec3>.Own(new[]{frame.Local(new Vec3(0,.8,-spec.Length*.5)),frame.Local(new Vec3(0,.8,spec.Length*.5))}),Envelope.From(corners));
            }
            var gaps=new double[result.Length-1];for(int i=1;i<result.Length;i++)gaps[i-1]=Vec.Norm(Vec.Sub(result[i-1].couplers[0],result[i].couplers[1]));
            return new Pose("active",head,tail,FrozenList<Car>.Own(result),FrozenList<double>.Own(gaps),Envelope.From(result.SelectMany(c=>new[]{c.envelope.min,c.envelope.max})));
        }
    }
    public static class TrainStock
    {
        public const string Version="1.0.0";
        // Manufacturer/operator dimensions anchor the family silhouettes. Body
        // internals, liveries and performance are game approximations, not exact assets.
        static readonly TrainVehicle regional=new TrainVehicle("158",TrainBodyStyle.Regional,23,2.72f,3.82f,8,.42f,38000);
        static readonly TrainVehicle civityEnd=new TrainVehicle("197-end",TrainBodyStyle.Civity,23.626f,2.75f,3.95f,8.3f,.43f,42000);
        static readonly TrainVehicle civityMiddle=new TrainVehicle("197-mid",TrainBodyStyle.Civity,22.55f,2.75f,3.95f,7.8f,.43f,40000);
        static readonly TrainVehicle iet=new TrainVehicle("800",TrainBodyStyle.Intercity,25.6f,2.70f,3.95f,8.7f,.43f,43000);
        static readonly TrainVehicle loco=new TrainVehicle("66",TrainBodyStyle.Locomotive,21.4f,2.64f,3.9f,6.1f,.53f,129600,3);
        static readonly TrainVehicle wagon=new TrainVehicle("container",TrainBodyStyle.Container,19.8f,2.6f,3.90f,6.8f,.46f,60000);
        static TrainVehicle[] Repeat(TrainVehicle v,int count)=>Enumerable.Repeat(v,count).ToArray();
        public static readonly TrainFormation[] All={
            new TrainFormation("gb.158.2","Class 158 regional diesel","Two-car regional passenger train",Repeat(regional,2),1,90,.55f,.8f,new Color(.66f,.68f,.69f),new Color(.63f,.06f,.08f)),
            new TrainFormation("gb.197.2","Class 197 Civity","Two-car modern regional diesel",Repeat(civityEnd,2),.8f,100,.65f,.85f,new Color(.72f,.73f,.72f),new Color(.65f,.05f,.07f)),
            new TrainFormation("gb.197.3","Class 197 Civity · three car","Three-car modern regional diesel",new[]{civityEnd,civityMiddle,civityEnd},.8f,100,.65f,.85f,new Color(.72f,.73f,.72f),new Color(.65f,.05f,.07f)),
            new TrainFormation("gb.800.5","Class 800 intercity","Five-car bi-mode intercity train",Repeat(iet,5),.5f,125,.70f,1,new Color(.025f,.105f,.075f),new Color(.68f,.69f,.60f)),
            new TrainFormation("gb.66.freight","Class 66 container freight","Six-axle diesel locomotive and six container wagons",new[]{loco,wagon,wagon,wagon,wagon,wagon,wagon},.8f,75,.23f,.55f,new Color(.58f,.035f,.025f),new Color(.78f,.62f,.15f))
        };
        static readonly TrainFormation[] generic=new[]{1435,1524,1600}.Select(g=>new TrainFormation("generic."+g,"Regional train · estimated type","Generic two-car approximation; actual unit allocation unknown",Repeat(regional,2),1,90,.55f,.8f,new Color(.65f,.67f,.69f),new Color(.15f,.25f,.4f),g/1000f)).ToArray();
        public static TrainFormation Generic(float gauge)=>generic.FirstOrDefault(t=>Mathf.Abs(t.Gauge*1000-gauge)<.1f);
        public static TrainFormation Find(string id)=>All.Concat(generic).FirstOrDefault(t=>t.Id==id);
        public static TrainFormation Local(int ordinal)=>All[new[]{0,1,3,4,2}[Math.Abs(ordinal)%5]];
    }
}
