// EOSAT-1 VR - the thin blue line: glow along Earth's limb, brightest where sunlit,
// orange near sunrise/sunset and around the Earth during an eclipse.
// Drawn on a shell slightly larger than the Earth, additively. Needs the globals set by
// SpaceScene: _EOSAT_SunDir, _EOSAT_EarthCenter, _EOSAT_EarthRadius, _EOSAT_AtmoScale.
Shader "EOSAT1/Atmosphere"
{
    Properties
    {
        _Color ("Scatter colour", Color) = (0.30, 0.55, 1.0, 1)
        _Sunset ("Sunset colour", Color) = (1.0, 0.52, 0.25, 1)
        _Strength ("Strength", Range(0, 8)) = 1.8
        _Haze ("Haze over the disc", Range(0, 1)) = 0.10
    }
    SubShader
    {
        Tags { "Queue"="Transparent-20" "RenderType"="Transparent" "IgnoreProjector"="True" }
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
            float4 _Sunset;
            float _Strength;
            float _Haze;
            float4 _EOSAT_SunDir;
            float4 _EOSAT_EarthCenter;
            float _EOSAT_EarthRadius;
            float _EOSAT_AtmoScale;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 posWS : TEXCOORD0;
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
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 C = _WorldSpaceCameraPos.xyz;
                float3 D = normalize(i.posWS - C);
                float3 O = _EOSAT_EarthCenter.xyz;
                float R = max(_EOSAT_EarthRadius, 1e-3);
                float H = max(_EOSAT_AtmoScale, 1e-4);
                float3 L = normalize(_EOSAT_SunDir.xyz + float3(0.0, 1e-6, 0.0));
                float tc = dot(O - C, D);
                float3 P = C + D * tc;
                float dmin = length(P - O);
                float glow;
                float3 Np;
                if (dmin > R)
                {
                    glow = exp(-(dmin - R) / H);
                    Np = normalize(P - O);
                }
                else
                {
                    float depthIn = (R - dmin) / R;
                    glow = _Haze * (exp(-depthIn * 14.0) + 0.25);
                    Np = normalize(i.posWS - O);
                }
                float sunUp = dot(Np, L);
                float lit = smoothstep(-0.22, 0.25, sunUp);
                float towardsSun = pow(saturate(dot(D, L)), 8.0);
                float sunset = smoothstep(-0.3, 0.0, sunUp) * (1.0 - smoothstep(0.0, 0.35, sunUp));
                float3 col = _Color.rgb * lit + _Sunset.rgb * sunset * (0.6 + 2.0 * towardsSun);
                return fixed4(col * glow * _Strength, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
