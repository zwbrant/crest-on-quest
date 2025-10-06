// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef CREST_WATER_VOLUME_LIGHTING_H
#define CREST_WATER_VOLUME_LIGHTING_H

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Macros.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Globals.hlsl"
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Surface/Utility.hlsl"

m_CrestNameSpace

// Schlick phase function.
float SchlickPhase(float phaseG, float cosTheta)
{
    const float schlickK = 1.5 * phaseG - 0.5 * phaseG * phaseG * phaseG;
    const float phaseFactor = 1.0 + schlickK * cosTheta;
    return (1.0 - schlickK * schlickK) / (4.0 * PI * phaseFactor * phaseFactor);
}

half3 VolumeExtinction(const half3 i_Absorption, const half3 i_Scattering)
{
    // Extinction is light absorbed plus light scattered out.
    return i_Absorption + i_Scattering;
}

half3 VolumeOpacity(const half3 i_Extinction, const half i_WaterRayLength)
{
    // Like 'alpha' value or obscurance. Volume light needs multiplying by this value
    // to be correct in shallows.
    return 1.0 - exp(-i_Extinction * max(0.0, i_WaterRayLength));
}

half3 VolumeLighting
(
    const half3 i_Extinction,
    const half3 i_Scattering,
    const half i_PhaseG,
    const half i_DirectionalLightShadow,
    const half3 i_ViewDirectionWS,
    const half3 i_AmbientLighting,
    const half3 i_PrimaryLightDirection,
    const half3 i_PrimaryLightIntensity,
    const half3 i_AdditionalLight,
    const half i_AmbientLightingTerm,
    const half i_PrimaryLightingTerm,
    const half3 i_SunBoost,
    const half i_ShadowsAffectAmbientLightingFactor
)
{
    const half3 extinction = i_Extinction;

    const float ambientLightShadow = lerp
    (
        1.0,
        i_DirectionalLightShadow,
        saturate(min(min(extinction.x, extinction.y), extinction.z) * i_ShadowsAffectAmbientLightingFactor * g_Crest_DynamicSoftShadowsFactor)
    );

#ifdef d_IsAdditionalLight
    const float3 inscattered = i_PrimaryLightIntensity;
#else
    // Sun
    const float sunPhase = SchlickPhase(i_PhaseG, dot(i_PrimaryLightDirection, i_ViewDirectionWS));
    const float3 inScatteredSun = (1.0 + i_SunBoost) * sunPhase * i_PrimaryLightIntensity * i_PrimaryLightingTerm;
    const float3 inScatteredAmbient = i_AmbientLighting * i_AmbientLightingTerm * ambientLightShadow;

    // Total inscattered
    const float3 inscattered = (inScatteredAmbient + i_AdditionalLight + inScatteredSun * i_DirectionalLightShadow);
#endif

    const float3 scatteringAmount = saturate(i_Scattering / max(extinction, 0.00001));

    return inscattered * scatteringAmount;
}

half PinchSSS
(
    const half i_Pinch,
    const half i_Minimum,
    const half i_Maximum,
    const half i_Falloff,
    const half i_Intensity,
    const half3 i_SunDirection,
    const half i_SunDirectionFalloff,
    const half3 i_ViewDirectionWS
)
{
    half pinch = pow(saturate(InverseLerp(i_Minimum, i_Maximum, max(2.0 - i_Pinch, 0.0))), i_Falloff);
    half sun = pow(saturate(dot(i_ViewDirectionWS, -i_SunDirection)), i_SunDirectionFalloff);
    return pinch * sun * i_Intensity;
}

m_CrestNameSpaceEnd

#endif
