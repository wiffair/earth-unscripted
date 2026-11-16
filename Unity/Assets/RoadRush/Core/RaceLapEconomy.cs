using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RoadRush.Core
{
    /// <summary>Career schema7 model/course PB, separate from legacy setup/condition records.</summary>
    [Serializable] public sealed class ModelLayoutLapRecord
    {
        public string layoutIdentity="",key="";public int model;public double seconds;
        public ModelLayoutLapBest Read()
        {var identity=new ModelLayoutLapKey(model,layoutIdentity);if(identity.Identity!=key)throw new InvalidDataException("Model/course record identity differs");return new ModelLayoutLapBest(identity,seconds);}
        public static ModelLayoutLapRecord From(ModelLayoutLapBest best)=>new ModelLayoutLapRecord{layoutIdentity=best.Key.LayoutIdentity,key=best.Key.Identity,model=best.Key.Model,seconds=best.Seconds};
    }
    /// <summary>Separate economy-PB key. Layout identity must describe the actual physical course,
    /// excluding event/rules/setup/weather/mode identity. No legacy record migration is implied.</summary>
    public sealed class ModelLayoutLapKey
    {
        public readonly int Model;
        public readonly string LayoutIdentity, Identity;
        public ModelLayoutLapKey(int model, string layoutIdentity)
        {
            RaceLapEconomy.BasePriceForModel(model);
            if(string.IsNullOrWhiteSpace(layoutIdentity)||layoutIdentity.Length>512)
                throw new ArgumentException("A bounded real physical circuit/layout identity is required",nameof(layoutIdentity));
            foreach(char c in layoutIdentity)if(char.IsControl(c))throw new ArgumentException("Layout identity control character");
            Model=model;LayoutIdentity=layoutIdentity;
            using(var bytes=new MemoryStream())
            {
                using(var writer=new BinaryWriter(bytes,new UTF8Encoding(false,true),true))
                {writer.Write("RoadRushModelLayoutLap1");writer.Write(model);writer.Write(layoutIdentity);}
                using(var sha=SHA256.Create())Identity=BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-","").ToLowerInvariant();
            }
        }
        public bool Matches(ModelLayoutLapKey other)=>other!=null&&Model==other.Model&&LayoutIdentity==other.LayoutIdentity;
    }
    public sealed class ModelLayoutLapBest
    {
        public readonly ModelLayoutLapKey Key;
        public readonly double Seconds;
        public ModelLayoutLapBest(ModelLayoutLapKey key,double seconds)
        {if(key==null)throw new ArgumentNullException(nameof(key));RaceLapEconomy.ValidateTime(seconds);Key=key;Seconds=seconds;}
    }
    public readonly struct RaceLapCredits
    {
        public readonly int NewBaseModelPrice, HireFee, GrossReward, NetCredits;
        public readonly bool Rented, Clean, NewPersonalBest;
        internal RaceLapCredits(int price,int fee,int gross,bool rented,bool clean,bool pb)
        {NewBaseModelPrice=price;HireFee=fee;GrossReward=gross;NetCredits=checked(gross-fee);Rented=rented;Clean=clean;NewPersonalBest=clean&&pb;}
    }
    public sealed class RaceLapAssessment
    {
        public readonly bool ShouldUpdateBest;
        public readonly ModelLayoutLapBest ProposedBest;
        public readonly RaceLapCredits Credits;
        internal RaceLapAssessment(bool update,ModelLayoutLapBest best,RaceLapCredits credits)
        {ShouldUpdateBest=update;ProposedBest=best;Credits=credits;}
    }
    /// <summary>Pure quote/proposed-record calculation. Does not debit, award, save, rent or authorize a car.</summary>
    public static class RaceLapEconomy
    {
        public const string PricingIdentity="race-lap-economy1-half-up";
        internal static int BasePriceForModel(int model)
        {
            if(model<0||model>=GarageRules.Catalogue.Length||GarageRules.Catalogue[model]==null)
                throw new ArgumentOutOfRangeException(nameof(model));
            int price=GarageRules.Catalogue[model].Price;
            if(price<=0)throw new InvalidDataException("Game catalogue new model price must be positive");return price;
        }
        internal static void ValidateTime(double seconds)
        {if(double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<=0)throw new ArgumentOutOfRangeException(nameof(seconds));}
        static int Percentage(int price,int denominator)
        {
            if(price<=0)throw new ArgumentOutOfRangeException(nameof(price));
            // Positive nearest-integer, exact half rounds up. Int64 prevents overflow at Int32.MaxValue.
            return checked((int)(((long)price+denominator/2)/denominator));
        }
        // Internal value primitive for bounded integer checks, not a pack-supplied price API.
        internal static RaceLapCredits QuoteBasePrice(int price,bool rented,bool clean,bool newPersonalBest)
        {
            if(price<=0)throw new ArgumentOutOfRangeException(nameof(price));
            int fee=rented?Percentage(price,200):0;
            int gross=!clean?0:Percentage(price,newPersonalBest?100:400);
            return new RaceLapCredits(price,fee,gross,rented,clean,newPersonalBest);
        }
        public static RaceLapCredits QuoteForModel(int model,bool rented,bool clean,bool newPersonalBest)
            =>QuoteBasePrice(BasePriceForModel(model),rented,clean,newPersonalBest);
        public static RaceLapAssessment AssessLap(ModelLayoutLapKey key,double seconds,bool clean,bool rented,ModelLayoutLapBest previous)
        {
            if(key==null)throw new ArgumentNullException(nameof(key));ValidateTime(seconds);
            if(previous!=null&&!key.Matches(previous.Key))throw new ArgumentException("PB is from another model/layout",nameof(previous));
            bool pb=clean&&(previous==null||seconds<previous.Seconds);
            return new RaceLapAssessment(pb,pb?new ModelLayoutLapBest(key,seconds):previous,
                QuoteForModel(key.Model,rented,clean,pb));
        }
    }
}
