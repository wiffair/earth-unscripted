#nullable disable
using System;
using System.Collections.Generic;
namespace RoadRush.Core
{
    public sealed class CarSpec
    {
        public readonly string Name;public readonly int Price;public readonly float Mass,Power,Speed,Grip,Width,Length;
        public CarSpec(string name,int price,float mass,float power,float speed,float grip,float width,float length){Name=name;Price=price;Mass=mass;Power=power;Speed=speed;Grip=grip;Width=width;Length=length;}
    }
    [Serializable] public sealed class SavedDent {public float x,y,z,nx,ny,nz,severity;public SavedDent Clone()=>new SavedDent{x=x,y=y,z=z,nx=nx,ny=ny,nz=nz,severity=severity};}
    [Serializable] public sealed class OwnedCar
    {
        public string id="";public string packVehicleId="";public int model,paint,paintJob,wheels,bodyKit,finish,engine,gearing,tyres,brakes,suspension,differential;public float damage;
        public RaceSetup raceSetup=new RaceSetup();public RaceTyreState raceTyres=new RaceTyreState();
        public List<SavedDent> dents=new List<SavedDent>();
        public int Level(int part)=>part==0?engine:part==1?gearing:part==2?tyres:part==3?brakes:part==4?suspension:differential;
        public void SetLevel(int part,int level){if(part==0)engine=level;else if(part==1)gearing=level;else if(part==2)tyres=level;else if(part==3)brakes=level;else if(part==4)suspension=level;else differential=level;}
        public OwnedCar Clone()=>new OwnedCar{id=id,packVehicleId=packVehicleId,model=model,paint=paint,paintJob=paintJob,wheels=wheels,bodyKit=bodyKit,finish=finish,engine=engine,gearing=gearing,tyres=tyres,brakes=brakes,suspension=suspension,differential=differential,damage=damage,raceSetup=(raceSetup??new RaceSetup()).Clone(),raceTyres=(raceTyres??new RaceTyreState()).Clone(),dents=(dents??new List<SavedDent>()).ConvertAll(d=>d?.Clone())};
    }
    [Serializable] public sealed class GarageState
    {
        public string activeId="";public List<OwnedCar> cars=new List<OwnedCar>();
        public OwnedCar Active {get{foreach(var c in cars)if(c.id==activeId)return c;return cars.Count>0?cars[0]:null;}}
        public GarageState Clone(){var s=new GarageState{activeId=activeId};foreach(var c in cars)s.cars.Add(c.Clone());return s;}
    }
    public sealed class GarageTransaction
    {
        public readonly GarageState Garage;public readonly int Credits;public readonly string Message;
        public GarageTransaction(GarageState garage,int credits,string message){Garage=garage;Credits=credits;Message=message;}
    }
    public static class GarageRules
    {
        public static CarSpec[] Catalogue={new CarSpec("Brook Coupe",3200,1350,1,65,1,1,1),new CarSpec("Meridian GT",6800,1480,1.32f,78,1.08f,1.06f,1.07f),new CarSpec("Summit RS",12800,1230,1.6f,88,1.2f,1.09f,1),new CarSpec("Apex Single Seat",28000,780,1.45f,94,1.45f,1,1)};
        public static readonly string[] Parts={"ENGINE","GEARBOX","TYRES","BRAKES","SUSPENSION","DIFFERENTIAL"};
        static readonly int[] Price={240,220,180,160,200,220};
        public static GarageState CreateDefault(){var c=new OwnedCar{id=Guid.NewGuid().ToString("N")};return new GarageState{activeId=c.id,cars=new List<OwnedCar>{c}};}
        public static void Validate(GarageState state)
        {
            if(state.cars==null)state.cars=new List<OwnedCar>();state.cars.RemoveAll(c=>c==null);foreach(var car in state.cars)ImportedVehicleCatalogue.ResolveSavedCar(car);if(state.cars.Count>8)state.cars.RemoveRange(8,state.cars.Count-8);
            var ids=new HashSet<string>();foreach(var c in state.cars){if(c.raceSetup==null)c.raceSetup=new RaceSetup();c.raceSetup.Validate();if(c.raceTyres==null)c.raceTyres=new RaceTyreState();c.raceTyres.Validate();c.raceSetup.compound=c.raceTyres.compound;if(string.IsNullOrEmpty(c.id)||!ids.Add(c.id)){c.id=Guid.NewGuid().ToString("N");ids.Add(c.id);}c.paintJob=Math.Max(0,Math.Min(4,c.paintJob));c.paint=Math.Max(0,Math.Min(7,c.paint));c.wheels=Math.Max(0,Math.Min(2,c.wheels));c.bodyKit=Math.Max(0,Math.Min(2,c.bodyKit));c.finish=Math.Max(0,Math.Min(2,c.finish));for(int i=0;i<6;i++)c.SetLevel(i,Math.Max(0,Math.Min(3,c.Level(i))));c.damage=float.IsNaN(c.damage)||float.IsInfinity(c.damage)?0:Math.Max(0,Math.Min(100,c.damage));if(c.dents==null)c.dents=new List<SavedDent>();c.dents.RemoveAll(d=>d==null||float.IsNaN(d.severity)||float.IsInfinity(d.severity)||d.severity<0||d.severity>80||!Finite(d.x)||!Finite(d.y)||!Finite(d.z)||!Finite(d.nx)||!Finite(d.ny)||!Finite(d.nz));if(c.dents.Count>32)c.dents.RemoveRange(0,c.dents.Count-32);}
            if(state.cars.Count==0){var fresh=CreateDefault();state.cars=fresh.cars;state.activeId=fresh.activeId;}if(!state.cars.Exists(c=>c.id==state.activeId&&ImportedVehicleCatalogue.Available(c))){var available=state.cars.Find(c=>ImportedVehicleCatalogue.Available(c));if(available==null){available=CreateDefault().Active;state.cars.Add(available);}state.activeId=available.id;}
        }
        static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f)&&Math.Abs(f)<100;
        public static int UpgradePrice(OwnedCar car,int part)=>part<0||part>5||car.Level(part)>=3?0:Price[part]*(car.Level(part)+1);
        public static int TradeValue(OwnedCar car){int invested=0;for(int p=0;p<6;p++)for(int l=0;l<car.Level(p);l++)invested+=Price[p]*(l+1);return Math.Max(200,(int)(Catalogue[car.model].Price*.6f+invested*.3f-car.damage*12));}
        public static GarageTransaction Buy(GarageState current,int credits,int model,bool trade)
        {
            if(model<0||model>=Catalogue.Length||current.Active==null||(!trade&&current.cars.Count>=8))return null;
            int cost=Catalogue[model].Price-(trade?TradeValue(current.Active):0);if(credits<cost)return null;
            var next=current.Clone();if(trade)next.cars.RemoveAll(c=>c.id==next.activeId);
            var car=new OwnedCar{id=Guid.NewGuid().ToString("N"),model=model,packVehicleId=ImportedVehicleCatalogue.IdForModel(model),paint=model%8};next.cars.Add(car);next.activeId=car.id;
            return new GarageTransaction(next,Math.Min(10000000,credits-cost),(trade?"Traded up to ":"Purchased ")+Catalogue[model].Name);
        }
        public static GarageTransaction Upgrade(GarageState current,int credits,int part)
        {
            if(part<0||part>5||current.Active==null)return null;int cost=UpgradePrice(current.Active,part);if(cost<=0||credits<cost)return null;
            var next=current.Clone();next.Active.SetLevel(part,next.Active.Level(part)+1);return new GarageTransaction(next,credits-cost,Parts[part]+" upgraded to level "+next.Active.Level(part));
        }
        public static GarageTransaction Style(GarageState current,int credits,int paint,int wheels,int bodyKit,int finish,int paintJob=-1)
        {
            if(paint<0||paint>7||wheels<0||wheels>2||bodyKit<0||bodyKit>2||finish<0||finish>2||current.Active==null)return null;
            var old=current.Active;if(paintJob<0)paintJob=old.paintJob;if(paintJob>4)return null;if(old.model>=ImportedVehicleCatalogue.BuiltInCount)
            {
                if(wheels!=old.wheels||bodyKit!=old.bodyKit)return null;
                if(!ImportedVehicleCatalogue.Get(old.model).Settings.SupportsPaint&&(paint!=old.paint||paintJob!=old.paintJob||finish!=old.finish))return null;
            }int cost=(paint!=old.paint?150:0)+(wheels!=old.wheels?250:0)+(bodyKit!=old.bodyKit?350:0)+(finish!=old.finish?120:0)+(paintJob!=old.paintJob?180:0);if(cost==0||credits<cost)return null;
            var next=current.Clone();var c=next.Active;c.paint=paint;c.paintJob=paintJob;c.wheels=wheels;c.bodyKit=bodyKit;c.finish=finish;return new GarageTransaction(next,credits-cost,"Appearance fitted");
        }
        public static GarageTransaction Select(GarageState current,int credits,string id)
        {if(!current.cars.Exists(c=>c.id==id&&ImportedVehicleCatalogue.Available(c)))return null;var next=current.Clone();next.activeId=id;return new GarageTransaction(next,credits,"Selected "+Catalogue[next.Active.model].Name);}
        public static GarageTransaction Repair(GarageState current,int credits)
        {
            if(current.Active==null||current.Active.damage<1)return null;int cost=0;if(credits<cost)return null;
            var next=current.Clone();next.Active.damage=0;next.Active.dents.Clear();return new GarageTransaction(next,credits-cost,"Vehicle repaired");
        }
        public static float Power(OwnedCar car)=>Catalogue[car.model].Power*(1+car.engine*.13f)*(1+car.gearing*.025f);
        public static float Speed(OwnedCar car)=>Catalogue[car.model].Speed*(1+car.gearing*.06f);
        public static float Grip(OwnedCar car)=>Catalogue[car.model].Grip*(1+car.tyres*.12f);
        public static float Braking(OwnedCar car)=>1+car.brakes*.17f;
    }
}
