Shader "RoadRush/CamperSmoke"
{
    Properties { _MainTex ("Radial opacity", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 colour:COLOR; };
            struct Varying { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 colour:COLOR; };
            Varying Vert(Input input)
            {
                Varying output;
                output.vertex=UnityObjectToClipPos(input.vertex);
                output.uv=input.uv;output.colour=input.colour;
                return output;
            }
            half4 Frag(Varying input):SV_Target { return tex2D(_MainTex,input.uv)*input.colour; }
            ENDHLSL
        }
    }
}
