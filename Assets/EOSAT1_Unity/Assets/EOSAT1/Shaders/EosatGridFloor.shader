// EOSAT-1 VR - a faint grid disc under the viewer's feet. Standing on nothing at 500 km
// is uncomfortable for many people; this gives the eye a floor without hiding the Earth.
Shader "EOSAT1/GridFloor"
{
    Properties
    {
        _Color ("Line colour", Color) = (0.45, 0.75, 1.0, 0.35)
        _Spacing ("Grid spacing (m)", Float) = 0.5
        _Radius ("Fade-out radius (m)", Float) = 2.2
    }
    SubShader
    {
        Tags { "Queue"="Transparent+5" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Spacing;
            float _Radius;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 posWS : TEXCOORD0;
                float3 originWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.posWS = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.originWS = mul(unity_ObjectToWorld, float4(0.0, 0.0, 0.0, 1.0)).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 w = (i.posWS.xz - i.originWS.xz) / max(_Spacing, 0.01);
                float2 fw = max(fwidth(w), 1e-4);
                float2 g = abs(frac(w - 0.5) - 0.5) / fw;
                float gridLine = 1.0 - saturate(min(g.x, g.y));
                float d = length(i.posWS.xz - i.originWS.xz);
                float rings = abs(frac(d + 0.5) - 0.5) / max(fwidth(d), 1e-4);
                float ringLine = 1.0 - saturate(rings);
                float fade = 1.0 - smoothstep(_Radius * 0.55, _Radius, d);
                float a = max(gridLine * 0.6, ringLine) * _Color.a * fade;
                return fixed4(_Color.rgb, a);
            }
            ENDCG
        }
    }
    FallBack Off
}
