using UnityEngine;
namespace EarthUnscripted
{
    // Presentation budgets only: source roads, terrain and physical coverage
    // remain independent of quality. UI is drawn at the display resolution.
    public static class GraphicsQualityPreset
    {
        public static readonly string[] Names={"LOW","MEDIUM","HIGH","ULTRA"};
        public static int Clamp(int value)=>Mathf.Clamp(value,0,3);
        public static float SceneScale(int level,int detail)=>level==0?.70f:level==1?.85f:detail==2?1.5f:detail==1?1.25f:1;
        public static int Samples(int level)=>level>=2?4:2;
        public static float ShadowRange(int level)=>level==0?0:level==1?90:level==2?160:220;
        public static int TreeStride(int level)=>level==0?4:level==1?2:1;
        public static float TreeRange(int level)=>level==0?900:level==1?1600:level==2?2400:3200;
        public static float TreeDetailRange(int level)=>level==0?0:level==1?18:level==2?45:65;
        public static float BuildingDetailRange(int level)=>level==0?0:level==1?200:level==2?330:450;
        public static int DrivingFrameLimit(int level)=>level<2?60:level==2?90:120;
        public static string Description(int level,int detail)=>$"Scene resolution {SceneScale(level,detail)*100:0}% · "+(level==0?"fewer trees · no shadows":level==1?"lighter scenery · short shadows":level==2?"detailed scenery · medium shadows":"full scenery · long shadows");
    }
}
