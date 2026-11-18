using System;
namespace EarthUnscripted.Core
{
    // A sound-only road-V8 automatic approximation, independent of tyre forces.
    // Six ratios and hysteresis avoid the off-road rev profile and shift chatter.
    public sealed class RoadV8EngineRpm
    {
        static readonly float[] Ratios={4.17f,2.34f,1.52f,1.14f,.86f,.69f};
        static readonly float[] Up={12,24,38,52,65},LoadRange={12,14,16,17,20};
        public int Gear{get;private set;}
        public void Reset(){Gear=0;}
        public float Target(float speed,float throttle,float wheelRadius)
        {
            if(float.IsNaN(speed)||float.IsInfinity(speed))speed=0;
            if(float.IsNaN(throttle)||float.IsInfinity(throttle))throttle=0;
            if(float.IsNaN(wheelRadius)||float.IsInfinity(wheelRadius))wheelRadius=.375f;
            speed=Math.Abs(speed);throttle=Math.Max(0,Math.Min(1,throttle));wheelRadius=Math.Max(.2f,Math.Min(1,wheelRadius));
            float mph=speed*2.23694f;
            while(Gear<5&&mph>Up[Gear]+LoadRange[Gear]*throttle)Gear++;
            while(Gear>0&&mph<Up[Gear-1]+LoadRange[Gear-1]*throttle-7)Gear--;
            float wheelRpm=speed*60/(2*(float)Math.PI*wheelRadius);
            return Math.Max(750+throttle*650,Math.Min(6200,wheelRpm*3.42f*Ratios[Gear]+throttle*180));
        }
    }
}
