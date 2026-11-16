using System;
namespace RoadRush.Core
{
    // Shared metric dimensions for physical factories and vehicle appearances.
    public sealed class VehicleMetric
    {
        public readonly int Model;public readonly float BodyWidth,MirrorSpan,Track,Wheelbase,Radius=.36f,TyreWidth=.225f;
        public readonly float ColliderWidth,ColliderLength,ColliderCentreZ,LegacyShellWidth,BodyLength,RoofLocalY;
        readonly ImportedVehicleSettings imported;
        public float BodyColliderY=>imported?.ColliderY??ColliderCentreY;
        public float BodyColliderHeight=>imported?.ColliderHeight??ColliderHeight;
        public VehicleMetric(int model,ImportedVehicleSettings settings)
        {Model=model;imported=settings;BodyWidth=settings.BodyWidth;MirrorSpan=settings.BodyWidth;Track=settings.Track;Wheelbase=settings.Wheelbase;Radius=settings.WheelRadius;TyreWidth=settings.TyreWidth;ColliderWidth=settings.ColliderWidth;ColliderLength=settings.ColliderLength;ColliderCentreZ=0;LegacyShellWidth=settings.BodyWidth;BodyLength=settings.ColliderLength;RoofLocalY=settings.RoofHeight;}
        public const float AnchorY=.58f,RayLength=.78f,MaximumCompression=.42f,Spring=38000,Damper=4200;
        public const float ColliderCentreY=.86f,ColliderHeight=.85f;
        internal VehicleMetric(int model,float width,float mirrors,float track,float wheelbase,float colliderLength,float centreZ,float legacyWidth,float length,float roof)
        {Model=model;BodyWidth=width;MirrorSpan=mirrors;Track=track;Wheelbase=wheelbase;ColliderWidth=model==3?.81f:width-.05f;TyreWidth=model==3?.305f:.225f;ColliderLength=colliderLength;ColliderCentreZ=centreZ;LegacyShellWidth=legacyWidth;BodyLength=length;RoofLocalY=roof;}
        public float SuspensionAnchorY=>imported?.AnchorY??(Model==3?.38f:AnchorY);
        public float SuspensionRayLength=>imported?.RayLength??(Model==3?.48f:RayLength);
        public float SuspensionTravel=>imported?.Travel??(Model==3?.16f:MaximumCompression);
        public float SpringRate=>imported?.SpringRate??(Model==3?65000:Spring);
        public float DamperRate=>imported?.DamperRate??(Model==3?5200:Damper);
        public float TyreWidthAt(int index)=>imported!=null?TyreWidth:Model==3?(index%2==0?.38f:.305f):TyreWidth;
        public float LegacyBodyXScale=>BodyWidth/LegacyShellWidth;
        public float RestRootHeight(float mass,float springFactor=1)
        {if(float.IsNaN(mass)||float.IsInfinity(mass)||mass<500||mass>4000||float.IsNaN(springFactor)||float.IsInfinity(springFactor)||springFactor<.5f||springFactor>2)throw new ArgumentOutOfRangeException();return SuspensionRayLength-SuspensionAnchorY-mass*9.81f/(4*SpringRate*springFactor);}
        public float RestWheelLocalY(float mass,float springFactor=1)=>Radius-RestRootHeight(mass,springFactor);
        public float FullyExtendedWheelLocalY=>SuspensionAnchorY-SuspensionRayLength+Radius;
        public (float x,float y,float z) Anchor(int index)
        {if(index<0||index>3)throw new ArgumentOutOfRangeException(nameof(index));return(index<2?-Track*.5f:Track*.5f,SuspensionAnchorY,index%2==0?-Wheelbase*.5f:Wheelbase*.5f);}
    }
    public static class VehicleDimensions
    {
        public const string Recipe="roadcar-metric-width-v1-review";
        static readonly VehicleMetric[] Data=
        {
            new VehicleMetric(0,1.85f,1.94f,1.58f,2.72f,4.15f,.1483656f,2.30311155f,4.2332008f,1.14865518f),
            new VehicleMetric(1,1.90f,2.07f,1.62f,2.91040015f,4.4405003f,.0187249f,2.2366f,4.7133502f,1.585f),
            new VehicleMetric(2,1.88f,2.06f,1.60f,2.72f,4.15f,.0174998f,2.2999f,4.405f,1.585f),
            new VehicleMetric(3,1.66f,1.19f,1.60f,3.50f,4.28f,.08f,1.66f,5.02f,1.12f)
        };
        public static VehicleMetric ForModel(int model)
        {if(model>=Data.Length)return ImportedVehicleCatalogue.Get(model).Metric;
            if(model<0)throw new ArgumentOutOfRangeException(nameof(model));return Data[model];}
        public static int Count=>GarageRules.Catalogue.Length;
    }
}
