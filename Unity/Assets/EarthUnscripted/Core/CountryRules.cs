using System;
namespace EarthUnscripted.Core
{
    // Derived driving-side / speed-unit facts from country-coder; ISC notice in THIRD_PARTY.md.
    public static class CountryRules
    {
        const string Left=",AC,AG,AI,AU,BB,BD,BM,BN,BS,BT,BW,CC,CK,CQ,CX,CY,DM,EU,FJ,FK,GB,GD,GG,GS,GY,HK,HM,ID,IE,IM,IN,JE,JM,JP,KE,KI,KN,KY,LC,LK,LS,MO,MS,MT,MU,MV,MW,MY,MZ,NA,NF,NP,NR,NU,NZ,PG,PK,PN,SB,SC,SG,SH,SR,SZ,TA,TC,TH,TK,TL,TO,TT,TV,TZ,UG,UN,VC,VG,VI,WS,ZA,ZM,ZW,";
        const string Miles=",AC,AG,AI,AS,BS,BZ,CQ,DM,FK,FM,GB,GD,GG,GS,GU,IM,JE,KN,KY,LC,MH,MP,MS,PR,PW,SH,TA,TC,UM,UN,US,VC,VG,VI,";
        public static int Side(string code)=>Left.Contains(","+(code??"").ToUpperInvariant()+",")?-1:1;
        public static bool UsesMiles(string code)=>Miles.Contains(","+(code??"").ToUpperInvariant()+",");
    }
}
