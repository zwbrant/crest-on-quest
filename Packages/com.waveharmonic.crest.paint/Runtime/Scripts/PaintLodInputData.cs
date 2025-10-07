// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// General helpers for interactive painting system.

using UnityEngine;
using UnityEngine.Rendering;
using WaveHarmonic.Crest.Internal;

#if !UNITY_EDITOR
#pragma warning disable CS0414
#endif

namespace WaveHarmonic.Crest.Paint
{
    abstract partial class PaintLodInputData : LodInputData
    {
        [Tooltip("Stores the editor paint data.")]
        [@DecoratedField, SerializeField]
        internal PaintData _Data;

        [Tooltip("Stores the standalone paint data.")]
        [@DecoratedField, SerializeField]
        internal Texture2D _BakedTexture;

        [Tooltip("Automatically re-bakes the texture on save.\n\nThis will only work after the first bake.")]
        [@DecoratedField, SerializeField]
        bool _BakeOnSave = true;

        [Tooltip("Force the input to use the baked paint data for standalone.")]
        [@DecoratedField, SerializeField]
        bool _ShowBakedTexture;

        [@Space(10)]

        [Tooltip("Change the world size of the painted area. Will not delete data when shrinking.")]
        [@Minimum(1f)]
        [@DecoratedField, SerializeField]
        internal Vector2 _WorldSize = Vector2.one * 50;

        [Tooltip("The resolution of the output.\n\nOutput texture resolution will scale to the world size based on this value - basically how many texels per unit (meters).")]
        [@Range(1, 200, Range.Clamp.Minimum)]
        [SerializeField]
        int _RequestedTexelDensity = 16;

        [Tooltip("The maximum resolution of the output.\n\nIf one dimension exceeds this value, it will be clamped, and the smaller dimension will be scaled down proportionally.")]
        [@Range(16, 4096, Range.Clamp.Minimum)]
        [@Maximum(Constants.k_MaximumTextureResolution)]
        [@WarnIfAbove(4096)]
        [SerializeField]
        int _MaximumResolution = 1024;

        [@Space(10)]

        // These two params have been around the houses. It doesn't really make sense to put them here, but it was awful having them shared across all
        // input types, and having them as statics so they'd get lost after code changes. Perhaps they belong in a dictionary based on data type,
        // with recovery after recompiles.

        [Tooltip("Size of the brush.")]
        [@Range(0.001f, 100f, Range.Clamp.Minimum)]
        [SerializeField]
        internal float _BrushSize = 2f;

        [Tooltip("Strength of the brush.")]
        [@Range(0.001f, 25f, Range.Clamp.Minimum)]
        [SerializeField]
        internal float _BrushStrength = 15f;

        internal abstract ComputeShader PaintedShader { get; }

        internal override bool IsEnabled => PaintedShader != null && SourceRT != null;
        internal override bool HasHeightRange => false;

        public Vector2 WorldSize => _WorldSize;

        internal bool IsPainting { get; set; }

        internal Texture2D PersistentRT =>
#if UNITY_EDITOR
            !_ShowBakedTexture ? _Cache :
#endif
            _BakedTexture;

        // Input renders this.
        internal Texture SourceRT =>
#if UNITY_EDITOR
            !_ShowBakedTexture && TargetRT != null ? TargetRT :
#endif
            PersistentRT;

        internal override void RecalculateRect()
        {
            _Rect = new(_Input.transform.position.XZ() - WorldSize * 0.5f, WorldSize);
        }

        internal override void RecalculateBounds()
        {
            throw new System.NotImplementedException();
        }

        // Avoid using Awake because Destroy is not reliable.
        internal override void OnEnable()
        {
#if UNITY_EDITOR
            Enable();
#endif
        }

        internal override void OnDisable()
        {
#if UNITY_EDITOR
            Disable();
#endif
        }

        internal override void OnUpdate()
        {
            // TODO: always recalculate for now, but should be cached.
            RecalculateCulling();
        }

        internal override void Draw(Lod lod, Component component, CommandBuffer buffer, RenderTargetIdentifier target, int slices)
        {
            var transform = component.transform;
            var wrapper = new PropertyWrapperCompute(buffer, PaintedShader, 0);
            wrapper.SetVector(Crest.ShaderIDs.s_TextureSize, WorldSize);
            wrapper.SetVector(Crest.ShaderIDs.s_TexturePosition, transform.position.XZ());
            wrapper.SetVector(Crest.ShaderIDs.s_TextureRotation, new(0, 1));
            wrapper.SetVector(Crest.ShaderIDs.s_Multiplier, Vector4.one);
            wrapper.SetVector(Crest.ShaderIDs.s_Resolution, new(SourceRT.width, SourceRT.height));

            wrapper.SetFloat(Crest.ShaderIDs.s_FeatherWidth, _Input.FeatherWidth);
            wrapper.SetInteger(Crest.ShaderIDs.s_Blend, (int)_Input.Blend);

            // Painted data always supports negative values.
            wrapper.SetBoolean(Crest.ShaderIDs.s_NegativeValues, true);

            wrapper.SetTexture(Crest.ShaderIDs.s_Texture, SourceRT);
            wrapper.SetTexture(Crest.ShaderIDs.s_Target, target);

            var threads = lod.Resolution / Lod.k_ThreadGroupSize;
            wrapper.Dispatch(threads, threads, slices);
        }
    }

    abstract class ValuePaintLodInputData : PaintLodInputData
    {
        [Tooltip("Whether to set or add to existing data.\n\nIf enabled, the data will move towards this value. How quickly depends on the brush strength.")]
        [DecoratedField, SerializeField]
        internal bool _SetValue;

        [Tooltip("The value to use when painting.")]
        [@Predicated(nameof(_SetValue))]
        [DecoratedField, SerializeField]
        internal float _Value = 1f;
    }

    abstract class ColorPaintLodInputData : PaintLodInputData
    {
        [Tooltip("The color to use when painting.")]
        [@DecoratedField, SerializeField]
        internal Color _Color = Color.white;
    }

    abstract class DirectionalPaintLodInputData : PaintLodInputData
    {
        [Tooltip("Whether to normalize the magnitude of the brush stroke.\n\nDirectional data will will use the magnitude of the stroke as the brush strength. With this option enabled, it will normalize, and use the Brush Strength property instead. Normalizing will give more consistent strokes.")]
        [@DecoratedField, SerializeField]
        internal bool _NormalizeStrokeMagnitude = true;

        [Tooltip("Whether to clamp the magnitude of the output.\n\nWhen disabled, the output (ie the texture) can exceed the maximum wave strength of the spectrum, as this is effectively a multiplier on a per-pixel basis. Use responsibly.")]
        [@DecoratedField, SerializeField]
        internal bool _ClampOutputMagnitude = true;
    }

#if !UNITY_EDITOR
    class PaintData : ScriptableObject
    {

    }
#endif
}
