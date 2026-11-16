Shader "Hidden/EarthUnscripted/DrivingMap"
{
    Properties { _MainTex("Map",2D)="white"{} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _CarUv, _MapTransform;
            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 delta=(input.uv-.5)*_MapTransform.z;
                float cosine=_MapTransform.x,sine=_MapTransform.y;
                float2 uv=_CarUv.xy+float2(cosine*delta.x+sine*delta.y,-sine*delta.x+cosine*delta.y);
                return tex2D(_MainTex,uv);
            }
            ENDCG
        }
    }
}
