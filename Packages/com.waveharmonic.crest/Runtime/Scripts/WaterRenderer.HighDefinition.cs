// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityHDRP

using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest
{
    // High Definition Render Pipeline
    partial class WaterRenderer
    {
        internal static partial class ShaderIDs
        {
            // High Definition Render Pipeline
            public static readonly int s_PrimaryLightDirection = Shader.PropertyToID("g_Crest_PrimaryLightDirection");
            public static readonly int s_PrimaryLightIntensity = Shader.PropertyToID("g_Crest_PrimaryLightIntensity");
        }

        internal static bool s_CameraMSAA;

        bool _DoneHighDefinitionLighting;

        void OnBeginContextRendering(ScriptableRenderContext context, System.Collections.Generic.List<Camera> cameras)
        {
            s_CameraMSAA = false;
        }

        // This use to be in OnBeginContextRendering with comment: "Most compatible with
        // lighting options if computed here". Cannot remember what that meant.
        internal void UpdateHighDefinitionLighting(Camera camera)
        {
            if (_DoneHighDefinitionLighting)
            {
                return;
            }

            var lightDirection = Vector3.zero;
            var lightIntensity = Color.black;
            var sun = PrimaryLight;

            if (sun != null && sun.isActiveAndEnabled && sun.TryGetComponent<HDAdditionalLightData>(out var data))
            {
                lightDirection = -sun.transform.forward;
                lightIntensity = Color.clear;

                // It was reported that Light.intensity causes flickering when updated with
                // HDAdditionalLightData.SetIntensity, unless we get intensity from there.
                {
                    // Adapted from Helpers.FinalColor.
                    var light = data;
                    var linear = GraphicsSettings.lightsUseLinearIntensity;
                    var color = linear ? light.color.linear : light.color;
#if UNITY_6000_0_OR_NEWER
                    color *= sun.intensity;
#else
                    color *= light.intensity;
#endif
                    if (linear && light.useColorTemperature) color *= Mathf.CorrelatedColorTemperatureToRGB(sun.colorTemperature);
                    if (!linear) color = color.MaybeLinear();
                    lightIntensity = linear ? color.MaybeGamma() : color;
                }

                // Transmittance is for Physically Based Sky.
                var hdCamera = HDCamera.GetOrCreate(camera);
                var settings = SkyManager.GetSkySetting(hdCamera.volumeStack);
                var transmittance = settings != null
                    ? settings.EvaluateAtmosphericAttenuation(lightDirection, hdCamera.camera.transform.position)
                    : Vector3.one;

                lightIntensity *= transmittance.x;
                lightIntensity *= transmittance.y;
                lightIntensity *= transmittance.z;
            }

            Shader.SetGlobalVector(ShaderIDs.s_PrimaryLightDirection, lightDirection);
            Shader.SetGlobalVector(ShaderIDs.s_PrimaryLightIntensity, lightIntensity);

            _DoneHighDefinitionLighting = true;
        }
    }

    sealed class CrestInternalCopyToTextureCustomPass : CustomPass
    {
        const string k_Name = "Update Pyramids";

        static CrestInternalCopyToTextureCustomPass s_Instance;

        WaterRenderer _Water;

        // Wraps depth pyramid, so we can use Blitter.
        RTHandle _DepthPyramid;

        RenderTexture _DepthTexture;
        RenderTexture _DepthTextureDynamic;

        public static void Enable(WaterRenderer renderer)
        {
            var gameObject = CustomPassHelpers.CreateOrUpdate
            (
                parent: renderer.Container.transform,
                k_Name,
                hide: !renderer._Debug._ShowHiddenObjects
            );

            CustomPassHelpers.CreateOrUpdate
            (
                gameObject,
                ref s_Instance,
                WaterRenderer.k_DrawWater,
                renderer.RenderBeforeTransparency
                    ? CustomPassInjectionPoint.BeforeTransparent
                    : CustomPassInjectionPoint.BeforePostProcess,
                priority: -1
            );

            s_Instance._Water = renderer;

            RenderPipelineManager.beginCameraRendering -= s_Instance.OnBeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += s_Instance.OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= s_Instance.OnEndCameraRendering;
            RenderPipelineManager.endCameraRendering += s_Instance.OnEndCameraRendering;
        }

        public static void Disable()
        {
            if (s_Instance != null)
            {
                RenderPipelineManager.beginCameraRendering -= s_Instance.OnBeginCameraRendering;
                RenderPipelineManager.endCameraRendering -= s_Instance.OnEndCameraRendering;
            }

            // It should be safe to rely on this reference for this reference to fail.
            if (s_Instance != null && s_Instance._GameObject != null)
            {
                // Will also trigger Cleanup below.
                s_Instance._GameObject.SetActive(false);
            }
        }

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            s_Instance._Volume.injectionPoint = _Water.RenderBeforeTransparency
                ? CustomPassInjectionPoint.BeforeTransparent
                : CustomPassInjectionPoint.BeforePostProcess;
        }

        void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (this == null) return;

            if (!WaterRenderer.ShouldRender(camera, _Water.Surface.Layer))
            {
                return;
            }

            // TODO: Work out conditions where depth copy is needed when rendering during transparency.
            if (!_Water.RenderBeforeTransparency)
            {
                return;
            }

            var rt = Shader.GetGlobalTexture(Shader.PropertyToID("_CameraDepthTexture")) as RenderTexture;
            var hdCamera = HDCamera.GetOrCreate(camera);

            if (hdCamera.allowDynamicResolution && hdCamera.canDoDynamicResolution)
            {
                _DepthTextureDynamic = rt;
            }
            else
            {
                _DepthTexture = rt;
            }
        }

        protected override void Cleanup()
        {
            base.Cleanup();
            // Unset internal RT to avoid release it.
            _DepthPyramid?.SetRenderTexture(null);
            _DepthPyramid?.Release();
        }

        MipGenerator MipGenerator =>
#if UNITY_6000_OR_NEWER
            (RenderPipelineManager.currentPipeline as HDRenderPipeline).m_MipGenerator
#else
            s_MipGenerator.GetValue(RenderPipelineManager.currentPipeline) as MipGenerator;
        static readonly FieldInfo s_MipGenerator = typeof(HDRenderPipeline).GetField("m_MipGenerator", BindingFlags.NonPublic | BindingFlags.Instance);
#endif

        static readonly MethodInfo s_UseScaling = typeof(RTHandle).GetProperty("useScaling", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).SetMethod;
        static readonly object[] s_UseScalingParameters = { true };

        static void UseScaling(RTHandle rt)
        {
            s_UseScaling.Invoke(rt, s_UseScalingParameters);
        }

        protected override void Execute(CustomPassContext context)
        {
            // We cannot override _ColorPyramidTexture or _CameraDepthTexture with our own
            // texture, so we write to these textures instead. Getting these textures has
            // been tricky. Getting _ColorPyramidTexture with Shader.GetGlobalTexture does
            // not always work, as it is replaced with the distortion color pyramid before
            // we can grab it.

            var hdCamera = context.hdCamera;
            var camera = hdCamera.camera;
            var buffer = context.cmd;

            if (!WaterRenderer.ShouldRender(camera, _Water.Surface.Layer))
            {
                return;
            }

            if (_Water.Surface.Material == null)
            {
                return;
            }

            if (!SurfaceRenderer.IsTransparent(_Water.Surface.Material))
            {
                return;
            }

            // Our reflections do not need them.
            if (camera == WaterReflections.CurrentCamera)
            {
                return;
            }

            if (context.hdCamera.msaaEnabled)
            {
                WaterRenderer.s_CameraMSAA = true;
                return;
            }

            if (!_Water.RenderBeforeTransparency && hdCamera.frameSettings.IsEnabled(FrameSettingsField.Distortion))
            {
                return;
            }

            if (_Water.WriteToColorTexture)
            {
                var colorTexture = hdCamera.GetCurrentFrameRT((int)HDCameraFrameHistoryType.ColorBufferMipChain);

                if (colorTexture != null)
                {
                    buffer.BeginSample(WaterRenderer.k_DrawCopyColor);

                    var pyramidSize = new Vector2Int(hdCamera.actualWidth, hdCamera.actualHeight);
                    Blitter.BlitCameraTexture(buffer, context.cameraColorBuffer, colorTexture);
                    MipGenerator.RenderColorGaussianPyramid(buffer, pyramidSize, context.cameraColorBuffer, colorTexture);

                    buffer.EndSample(WaterRenderer.k_DrawCopyColor);
                }
            }

            // TODO: Work out conditions where depth copy is needed when rendering during transparency.
            if (!_Water.RenderBeforeTransparency)
            {
                return;
            }

            if (_Water.WriteToDepthTexture)
            {
                // Texture is not set yet, so we need to store it at the end of rendering.
                // Textures may be different depending on configuration.
                var depthTexture = hdCamera.allowDynamicResolution && hdCamera.canDoDynamicResolution ? _DepthTextureDynamic : _DepthTexture;

                if (depthTexture != null)
                {
                    buffer.BeginSample(WaterRenderer.k_DrawCopyDepth);

                    // Set up wrapper, so we can use Blitter.
                    _DepthPyramid ??= RTHandles.Alloc(depthTexture);
                    _DepthPyramid.SetRenderTexture(depthTexture);
                    UseScaling(_DepthPyramid);

                    // Blit to the bottom of the depth atlas.
                    Blitter.BlitCameraTexture(buffer, context.cameraDepthBuffer, _DepthPyramid, new Rect(0, 0, hdCamera.actualWidth, hdCamera.actualHeight));

                    // Regenerate the depth pyramid.
                    MipGenerator.RenderMinDepthPyramid
                    (
                        buffer,
                        depthTexture,
                        hdCamera.depthBufferMipChainInfo
#if !UNITY_6000_0_OR_NEWER
                        , mip1AlreadyComputed: false
#endif
                    );

                    buffer.EndSample(WaterRenderer.k_DrawCopyDepth);
                }
            }
        }
    }
}

#endif
