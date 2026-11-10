// EOSAT-1 VR - additive halo, brightest at the edges (used for the antenna-jam warning).
// SatelliteView pulses _Color through a MaterialPropertyBlock.
Shader "EOSAT1/GlowAdditive"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.12, 0.08, 1)
        _Power ("Edge sharpness", Range(0.5, 8)) = 2
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Power;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 nWS : TEXCOORD0;
                float3 posWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.nWS = UnityObjectToWorldNormal(v.normal);
                o.posWS = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 N = normalize(i.nWS);
                float3 V = normalize(_WorldSpaceCameraPos.xyz - i.posWS);
                float edge = pow(1.0 - saturate(abs(dot(N, V))), _Power);
                return fixed4(_Color.rgb * (0.2 + edge), 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
