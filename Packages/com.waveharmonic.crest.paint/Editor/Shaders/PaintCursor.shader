// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// Draws a 3D cursor in the world

Shader "Hidden/Crest/Paint Cursor"
{
    CGINCLUDE
    #pragma vertex Vertex
    #pragma fragment Fragment

    #include "UnityCG.cginc"

    float4 _Crest_BoundaryXZ;

    struct Varyings
    {
        float4 positionOS : SV_POSITION;
        float3 positionWS : TEXCOORD0;
    };

    float4 GetColor(float2 worldXZ)
    {
        float2 p = abs(worldXZ - _Crest_BoundaryXZ.xy);
        float2 s = _Crest_BoundaryXZ.zw * 0.5;
        // Unity's terrain cursor color.
        return p.x > s.x || p.y > s.y ? float4(1.0, 0.5, 0.5, 0.75) : float4(0.5, 0.5, 1.0, 0.75);
    }

    Varyings Vertex(float4 positionOS : POSITION)
    {
        Varyings o;
        o.positionOS = UnityObjectToClipPos(positionOS);
        o.positionWS = mul(UNITY_MATRIX_M, float4(positionOS.xyz, 1.0)).xyz;
        return o;
    }
    ENDCG

    SubShader
    {
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        // In front of scene surfaces / visible
        Pass
        {
            CGPROGRAM
            float4 Fragment(Varyings i) : SV_Target
            {
                return GetColor(i.positionWS.xz);
            }
            ENDCG
        }

        // Behind scene surfaces / occluded
        Pass
        {
            ZTest Greater

            CGPROGRAM
            float4 Fragment(Varyings i) : SV_Target
            {
                // Checkerboard when occluded.
                float alpha = frac(i.positionWS.x) < 0.5;
                if (frac(i.positionWS.z) < 0.5) alpha = 1.0 - alpha;

                float4 color = GetColor(i.positionWS.xz);
                color = lerp(color, color * 1.25, alpha);
                color.a = 0.75;

                return color;
            }
            ENDCG
        }
    }
}
