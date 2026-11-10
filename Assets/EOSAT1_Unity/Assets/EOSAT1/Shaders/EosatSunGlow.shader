// EOSAT-1 VR - the Sun: a 0.53 deg disc plus a soft glow, on a camera-facing quad.
// _DiscRadius = disc radius as a fraction of the quad's half-width (SpaceScene sizes the quad).
// Fades with _EOSAT_SunVisibility, and the Earth hides it by depth during an eclipse.
Shader "EOSAT1/SunGlow"
{
    Properties
    {
        _CoreColor ("Disc colour", Color) = (1, 0.98, 0.94, 1)
        _GlowColor ("Glow colour", Color) = (1, 0.86, 0.62, 1)
        _DiscRadius ("Disc radius (fraction of quad)", Range(0.001, 0.5)) = 0.0417
        _Intensity ("Disc intensity", Range(0, 20)) = 6
        _GlowStrength ("Glow strength", Range(0, 2)) = 0.55
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _CoreColor;
            float4 _GlowColor;
            float _DiscRadius;
            float _Intensity;
            float _GlowStrength;
            float _EOSAT_SunVisibility;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
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
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p = i.uv * 2.0 - 1.0;
                float r = length(p);
                float disc = 1.0 - smoothstep(_DiscRadius * 0.85, _DiscRadius, r);
                float glow = pow(saturate(1.0 - r), 5.0) * 0.5 + exp(-r * 9.0) * 0.5;
                float3 col = _CoreColor.rgb * disc * _Intensity + _GlowColor.rgb * glow * _GlowStrength;
                return fixed4(col * saturate(_EOSAT_SunVisibility), 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
