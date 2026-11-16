namespace EarthUnscripted.TerrainDraft.Core800 {
 public readonly struct SourceRoadRow {public readonly double SourceS,DerivedH;public readonly bool PreviousSide;public readonly float CenterX,CenterY,CenterZ,RightX,RightY,RightZ;internal SourceRoadRow(double sourceS,double h,float cx,float cy,float cz,float rx,float ry,float rz,bool previousSide=false){PreviousSide=previousSide;SourceS=sourceS;DerivedH=h;CenterX=cx;CenterY=cy;CenterZ=cz;RightX=rx;RightY=ry;RightZ=rz;}}
}
