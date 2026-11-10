// EOSAT-1 VR - Earth and Moon. Lit by _EOSAT_SunDir (set every frame by SpaceScene), not by
// scene lights, so the planet stays lit while the spacecraft sits in Earth's shadow.
// Day map alpha = open water (for sun glint). Works in URP and the built-in pipeline,
// single-pass stereo included.
Shader "EOSAT1/Planet"
{
    Properties
    {
        _DayTex ("Day map (RGB), water mask (A)", 2D) = "white" {}
        _NightTex ("Night lights (RGB)", 2D) = "black" {}
        _NightIntensity ("Night lights", Range(0, 4)) = 1.3
        _GlintStrength ("Sun glint on water", Range(0, 4)) = 0.9
        _GlintPower ("Glint sharpness", Range(8, 512)) = 80
        _RimColor ("Atmosphere tint", Color) = (0.35, 0.58, 1.0, 1)
        _RimStrength ("Atmosphere tint strength", Range(0, 3)) = 0.6
        _Exposure ("Exposure", Range(0.2, 4)) = 1.1
        _DarkSide ("Dark side ambient", Range(0, 0.2)) = 0.004
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Cull Back
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _DayTex;
            sampler2D _NightTex;
            float _NightIntensity;
            float _GlintStrength;
            float _GlintPower;
            float _RimStrength;
            float _Exposure;
            float _DarkSide;
            float4 _RimColor;
            float4 _EOSAT_SunDir;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 nWS : TEXCOORD1;
                float3 posWS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.nWS = UnityObjectToWorldNormal(v.normal);
                o.posWS = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 N = normalize(i.nWS);
                float3 L = normalize(_EOSAT_SunDir.xyz + float3(0.0, 1e-6, 0.0));
                float3 V = normalize(_WorldSpaceCameraPos.xyz - i.posWS);
                float ndl = dot(N, L);
                float4 day = tex2D(_DayTex, i.uv);
                float lit = saturate(ndl);
                float twilight = smoothstep(-0.08, 0.06, ndl);
                float3 col = day.rgb * (lit * 0.92 + twilight * 0.08) * _Exposure;
                col *= lerp(float3(1.0, 0.72, 0.5), float3(1.0, 1.0, 1.0), saturate(ndl * 6.0 + 0.25));
                col += day.rgb * _DarkSide;
                float night = 1.0 - smoothstep(-0.12, 0.02, ndl);
                col += tex2D(_NightTex, i.uv).rgb * night * _NightIntensity;
                float3 H = normalize(L + V);
                float spec = pow(saturate(dot(N, H)), _GlintPower) * day.a * lit;
                col += spec * _GlintStrength * float3(1.0, 0.96, 0.88);
                float rim = pow(1.0 - saturate(dot(N, V)), 3.0);
                col += _RimColor.rgb * twilight * rim * _RimStrength;
                return fixed4(col, 1.0);
            }
            ENDCG
        }

        // Depth-only pass for URP's depth prepass / depth priming (ignored by the built-in pipeline).
        Pass
        {
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target { return 0; }
            ENDCG
        }
    }
    FallBack Off
}
