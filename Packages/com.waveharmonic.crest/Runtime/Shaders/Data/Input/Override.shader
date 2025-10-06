// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

Shader "Crest/Inputs/All/Override"
{
    Properties
    {
        [Enum(ColorWriteMask)]
        _Crest_ColorMask("Color Mask", Int) = 15
        _Crest_Value("Value", Vector) = (1, 1, 1, 1)

        [HideInInspector]
        _Crest_Version("Version", Integer) = 0
    }

    SubShader
    {
        ZTest Always
        ZWrite Off

        Pass
        {
            Blend Off
            ColorMask [_Crest_ColorMask]

            CGPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "UnityCG.cginc"

            CBUFFER_START(CrestPerWaterInput)
            float3 _Crest_DisplacementAtInputPosition;
            half4 _Crest_Value;
            half _Crest_Weight;
            CBUFFER_END

            float4 Vertex(float3 positionOS : POSITION) : SV_POSITION
            {
                float3 positionWS = mul(unity_ObjectToWorld, float4(positionOS, 1.0)).xyz;
                // Correct for displacement.
                positionWS.xz -= _Crest_DisplacementAtInputPosition.xz;
                return mul(UNITY_MATRIX_VP, float4(positionWS, 1.0));
            }

            half4 Fragment() : SV_Target
            {
                return _Crest_Value * _Crest_Weight;
            }
            ENDCG
        }
    }
    CustomEditor "WaveHarmonic.Crest.Editor.CustomShaderGUI"
}
