// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEngine;
using UnityEngine.Rendering;

namespace WaveHarmonic.Crest
{
    /// <summary>
    /// FFT wave shape.
    /// </summary>
    [AddComponentMenu(Constants.k_MenuPrefixInputs + "Shape FFT")]
    public sealed partial class ShapeFFT : ShapeWaves
    {
        // Waves

        [Tooltip("Whether to use the wind turbulence on this component rather than the global wind turbulence.\n\nGlobal wind turbulence comes from the Water Renderer component.")]
        [@GenerateAPI]
        [@InlineToggle(order = -3), SerializeField]
        bool _OverrideGlobalWindTurbulence;

        [Tooltip("How turbulent/chaotic the waves are.")]
        [@Predicated(nameof(_OverrideGlobalWindTurbulence), hide: true)]
        [@ShowComputedProperty(nameof(WindTurbulence))]
        [@Range(0, 1, order = -4)]
        [@GenerateAPI(Getter.Custom)]
        [SerializeField]
        float _WindTurbulence = 0.145f;

        [Tooltip("How aligned the waves are with wind.")]
        [@Range(0, 1, order = -5)]
        [@GenerateAPI]
        [SerializeField]
        float _WindAlignment;


        // Generation

        [Tooltip("FFT waves will loop with a period of this many seconds.")]
        [@Range(4f, 128f, Range.Clamp.Minimum)]
        [@GenerateAPI]
        [SerializeField]
        float _TimeLoopLength = Mathf.Infinity;


        [Header("Culling")]

        [Tooltip("Maximum amount the surface will be displaced vertically from sea level.\n\nIncrease this if gaps appear at bottom of screen.")]
        [@GenerateAPI]
        [SerializeField]
        float _MaximumVerticalDisplacement = 10f;

        [Tooltip("Maximum amount a point on the surface will be displaced horizontally by waves from its rest position.\n\nIncrease this if gaps appear at sides of screen.")]
        [@GenerateAPI]
        [SerializeField]
        float _MaximumHorizontalDisplacement = 15f;


        [@Heading("Collision Data Baking")]

#if !d_WaveHarmonic_Crest_CPUQueries
        [HideInInspector]
#endif

        [Tooltip("Enable running this FFT with baked data.\n\nThis makes the FFT periodic (repeating in time).")]
        [@Predicated(nameof(_Mode), inverted: true, nameof(LodInputMode.Global), hide: true)]
        [@DecoratedField, SerializeField]
        internal bool _EnableBakedCollision = false;

#if !d_WaveHarmonic_Crest_CPUQueries
        [HideInInspector]
#endif

        [Tooltip("Frames per second of baked data.\n\nLarger values may help the collision track the surface closely at the cost of more frames and increase baked data size.")]
        [@Predicated(nameof(_EnableBakedCollision))]
        [@Predicated(nameof(_Mode), inverted: true, nameof(LodInputMode.Global), hide: true)]
        [@DecoratedField, SerializeField]
        internal int _TimeResolution = 4;

#if !d_WaveHarmonic_Crest_CPUQueries
        [HideInInspector]
#endif

        [Tooltip("Smallest wavelength required in collision.\n\nTo preview the effect of this, disable power sliders in spectrum for smaller values than this number. Smaller values require more resolution and increase baked data size.")]
        [@Predicated(nameof(_EnableBakedCollision))]
        [@Predicated(nameof(_Mode), inverted: true, nameof(LodInputMode.Global), hide: true)]
        [@DecoratedField, SerializeField]
        internal float _SmallestWavelengthRequired = 2f;

#if !d_WaveHarmonic_Crest_CPUQueries
        [HideInInspector]
#endif

        [Tooltip("FFT waves will loop with a period of this many seconds.\n\nSmaller values decrease data size but can make waves visibly repetitive.")]
        [@Predicated(nameof(_EnableBakedCollision))]
        [@Predicated(nameof(_Mode), inverted: true, nameof(LodInputMode.Global), hide: true)]
        [@Range(4f, 128f)]
        [SerializeField]
        internal float _BakedTimeLoopLength = 32f;

        internal float LoopPeriod =>
#if d_WaveHarmonic_Crest_CPUQueries
            _EnableBakedCollision ? _BakedTimeLoopLength :
#endif
            _TimeLoopLength;

        private protected override int MinimumResolution => 16;
        private protected override int MaximumResolution => int.MaxValue;

        FFTCompute _FFTCompute;

        FFTCompute.Parameters _OldFFTParameters;
        internal FFTCompute.Parameters GetFFTParameters(float gravity) => new
        (
            _ActiveSpectrum,
            Resolution,
            _TimeLoopLength,
            WindSpeedMPS,
            WindDirRadForFFT,
            WindTurbulence,
            _WindAlignment,
            gravity
        );

        private protected override void OnUpdate(WaterRenderer water)
        {
            base.OnUpdate(water);

            // We do not filter FFTs.
            _FirstCascade = 0;
            _LastCascade = k_CascadeCount - 1;

            ReportMaxDisplacement(water);

            // If geometry is being used, the water input shader will rotate the waves to align to geo
            var parameters = GetFFTParameters(water.Gravity);

            // Don't create tons of generators when values are varying. Notify so that existing generators may be adapted.
            if (parameters.GetHashCode() != _OldFFTParameters.GetHashCode())
            {
                FFTCompute.OnGenerationDataUpdated(_OldFFTParameters, parameters);
            }

#if UNITY_EDITOR
            _FFTCompute = FFTCompute.GetInstance(parameters);
#endif

            _OldFFTParameters = parameters;
        }

        internal override void Draw(Lod lod, CommandBuffer buffer, RenderTargetIdentifier target, int pass = -1, float weight = 1, int slice = -1)
        {
            if (_LastGenerateFrameCount != Time.frameCount)
            {
                // Parameters will unlikely change as our Update is called in LateUpdate with Draw
                // not too far after.
                var parameters = GetFFTParameters(lod.Water.Gravity);

                _WaveBuffers = FFTCompute.GenerateDisplacements
                (
                    buffer,
                    lod.Water.CurrentTime,
                    parameters,
                    UpdateDataEachFrame
                );

#if UNITY_EDITOR
                _FFTCompute = FFTCompute.GetInstance(parameters);
#endif

                _LastGenerateFrameCount = Time.frameCount;
            }

            base.Draw(lod, buffer, target, pass, weight, slice);
        }

        private protected override void SetRenderParameters<T>(WaterRenderer water, T wrapper)
        {
            base.SetRenderParameters(water, wrapper);

            // If using geometry, the primary wave direction is used by the input shader to
            // rotate the waves relative to the geo rotation. If not, the wind direction is
            // already used in the FFT generation.
            var waveDir = (Mode is LodInputMode.Spline or LodInputMode.Paint) ? PrimaryWaveDirection : Vector2.right;
            wrapper.SetVector(ShaderIDs.s_AxisX, waveDir);
        }

        private protected override void ReportMaxDisplacement(WaterRenderer water)
        {
            if (!Enabled) return;

            // Apply weight or will cause popping due to scale change.
            MaximumReportedHorizontalDisplacement = _MaximumHorizontalDisplacement * Weight;
            MaximumReportedVerticalDisplacement = MaximumReportedWavesDisplacement = _MaximumVerticalDisplacement * Weight;

            if (Mode == LodInputMode.Global)
            {
                water.ReportMaximumDisplacement(MaximumReportedHorizontalDisplacement, MaximumReportedVerticalDisplacement, MaximumReportedVerticalDisplacement);
            }
        }

        float WindDirRadForFFT
        {
            get
            {
                // These input types use a wave direction provided by geometry or the painted user direction
                if (Mode is LodInputMode.Spline or LodInputMode.Paint)
                {
                    return 0f;
                }

                return WaveDirectionHeadingAngle * Mathf.Deg2Rad;
            }
        }

        float GetWindTurbulence()
        {
            return _OverrideGlobalWindTurbulence || WaterRenderer.Instance == null ? _WindTurbulence : WaterRenderer.Instance.WindTurbulence;
        }

#if UNITY_EDITOR
        void OnGUI()
        {
            if (_DrawSlicesInEditor)
            {
                _FFTCompute?.OnGUI();
            }
        }
#endif
    }

    partial class ShapeFFT
    {
        static int s_InstanceCount;

        private protected override void Awake()
        {
            base.Awake();
            s_InstanceCount++;
        }

        private protected override void OnDestroy()
        {
            base.OnDestroy();

            if (--s_InstanceCount <= 0)
            {
                FFTCompute.CleanUpAll();
            }
        }
    }

    partial class ShapeFFT : ISerializationCallbackReceiver
    {
        [SerializeField, HideInInspector]
#pragma warning disable 414
        int _Version = 2;
#pragma warning restore 414

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            _Version = MigrateV1(_Version);

            if (_Version < 2)
            {
                _OverrideGlobalWindTurbulence = true;
            }

            _Version = MigrateV2(_Version);
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            // Empty.
        }
    }

#if UNITY_EDITOR
    partial class ShapeFFT
    {
        private protected override void Reset()
        {
            base.Reset();

            if (_Mode != LodInputMode.Global)
            {
                _OverrideGlobalWindTurbulence = true;
            }
        }
    }
#endif
}
