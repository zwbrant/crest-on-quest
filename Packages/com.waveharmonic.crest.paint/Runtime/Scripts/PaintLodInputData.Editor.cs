// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// These painting operations need to be part of the main class, so they can
// rebuild without an active inspector.

#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.SceneManagement;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest.Paint
{
    [@ExecuteDuringEditMode]
    partial class PaintLodInputData
    {
        internal const string k_ResizeUndoLabel = "Resize Painted Water Data";

        const int k_PointsBufferStride = sizeof(float) * (4 + 4);
        const int k_SegmentBufferLength = 2;
        const int k_ConstantBufferStride = sizeof(float) * (1 + 1 + (2));
        const int k_ConstantBufferLength = 1;

        // Use highest precision for now as water level needs it.
        internal const GraphicsFormat k_GraphicsFormat = GraphicsFormat.R32G32B32A32_SFloat;

        public static class ShaderIDs
        {
            public static readonly int s_PositionAB = Shader.PropertyToID("_PositionAB");
            public static readonly int s_BrushRadius = Shader.PropertyToID("_BrushRadius");
            public static readonly int s_BrushWeight = Shader.PropertyToID("_BrushWeight");
            public static readonly int s_BrushColour = Shader.PropertyToID("_BrushColour");
            public static readonly int s_Canvas = Shader.PropertyToID("_Canvas");
            public static readonly int s_CanvasWidth = Shader.PropertyToID("_CanvasWidth");
            public static readonly int s_CanvasHeight = Shader.PropertyToID("_CanvasHeight");

            public static readonly int s_Stroke = Shader.PropertyToID("_Stroke");
            public static readonly int s_StrokeLength = Shader.PropertyToID("_StrokeLength");

            public static readonly int s_Properties = Shader.PropertyToID("_Properties");
        }

        struct StrokePoint
        {
            public Vector4 _Position; // position (2) + padding (2).
            public Vector4 _Value;
        }

        struct Properties
        {
            public float _BrushRadius;
            public float _BrushWeight; // 2 x 4 = 8 bytes.
            readonly Vector2 _Padding; // Pad to 16 bytes.
        }

        internal enum Kernel
        {
            Add,
            Interpolate,
            Saturate,
            Saturate2,
            Towards,
            Towards2,
        }

        // A background process will listen to this. Allows a request a bake without
        // needing an assembly reference (which it cannot get due to circular reference).
        internal static System.Action<PaintLodInputData> OnBakeRequest { get; set; }

        internal PaintData Data => _Data;
        bool HasData => Data != null && Data._Layers.Count > 0;
        public Vector2Int Resolution { get; private set; }

        readonly Properties[] _Properties = new Properties[k_ConstantBufferLength] { new() };
        ComputeBuffer _ConstantBuffer;

        StrokePoint[] _StrokePoints;
        readonly StrokePoint[] _SegmentPoints = new StrokePoint[k_SegmentBufferLength];
        ComputeBuffer _SegmentBuffer;

        static ComputeShader s_PaintCS;
        static ComputeShader PaintCS => s_PaintCS != null ? s_PaintCS
            : (s_PaintCS = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.waveharmonic.crest.paint/Editor/Shaders/Paint.compute"));

        public RenderTexture TargetRT { get; private set; }

        // Cached values (not related to _Cached).
        internal Vector2 _TexelDensity;
        Vector2 _TextureOffset;

        void Enable()
        {
            UpdateResolution();

            if (_Cache == null)
            {
                LoadCache();
            }

            // Do not unsubscribe in OnDisable.
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= OnSavingScene;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += OnSavingScene;

            if (_Cache == null && TargetRT == null && HasData)
            {
                Rebuild();
                // Save cache so if others share this data, they will use it instead of rebuilding.
                SaveCache();
            }
        }

        void Disable()
        {
            SaveCache();

            // We could check IsValid to detect if released, but there is no benefit.
            _ConstantBuffer?.Release();
            _ConstantBuffer = null;
            _SegmentBuffer?.Release();
            _SegmentBuffer = null;
        }

        internal void UpdateTexelDensity()
        {
            _TexelDensity = new(TargetRT.width / _WorldSize.x, TargetRT.height / _WorldSize.y);
            _TextureOffset = new Vector2(TargetRT.width, TargetRT.height) * 0.5f;
        }

        void UpdateRenderTextureResolution()
        {
            var descriptor = TargetRT.descriptor;
            descriptor.width = Resolution.x;
            descriptor.height = Resolution.y;
            RenderTexture.active = null;
            TargetRT.Release();
            TargetRT.descriptor = descriptor;
            TargetRT.Create();

            if (_PreviousTargetRT != null)
            {
                RenderTexture.active = null;
                _PreviousTargetRT.Release();
                _PreviousTargetRT.descriptor = descriptor;
                _PreviousTargetRT.Create();
            }

            UpdateTexelDensity();
        }

        void UpdateResolution()
        {
            Resolution = Helpers.CalculateResolutionFromTexelDensity(_WorldSize, _RequestedTexelDensity, _MaximumResolution);
        }

        void SetPerCommandData(PaintData.PaintCommand command, PropertyWrapperComputeStandalone wrapper)
        {
            var brushRadius = command._BrushWidth * 0.5f;
            _Properties[0]._BrushRadius = brushRadius * Mathf.Max(_TexelDensity.x, _TexelDensity.y);
            _Properties[0]._BrushWeight = command._BrushStrength; // * 0.055f;
            _ConstantBuffer.SetData(_Properties);

            wrapper.SetFloat(ShaderIDs.s_CanvasWidth, TargetRT.width);
            wrapper.SetFloat(ShaderIDs.s_CanvasHeight, TargetRT.height);
        }

        Vector4 GetValue(PaintData.PaintCommand command, Vector2 oldPosition, Vector2 newPosition)
        {
            Vector4 value;

            switch (command._Type)
            {
                case PaintData.StrokeMode.Direction:
                case PaintData.StrokeMode.NormalizedDirection:
                    if (command._BlendOperation == Kernel.Towards2)
                    {
                        value = command._Value;
                    }
                    else
                    {
                        var direction = newPosition - oldPosition;

                        if (command._Type == PaintData.StrokeMode.NormalizedDirection)
                        {
                            // Normalize is faster than normalized.
                            // @Performance: A custom method would be even faster.
                            direction.Normalize();
                        }

                        value = direction;
                    }
                    break;
                case PaintData.StrokeMode.Value:
                    value = command._Value;
                    break;
                case PaintData.StrokeMode.Color:
                    value = command._Value;
                    break;
                default:
                    throw new System.NotImplementedException($"Crest: missing {command._Type} implementation.");
            }

            return value;
        }

        void OnSavingScene(Scene scene, string path)
        {
            // This can happen if OnDestroy is not called.
            if (this == null || _Data == null)
            {
                return;
            }

#if CREST_DEBUG
            Log($"OnSavingScene {this} {AssetDatabase.GetAssetPath(_Data)}");
#endif

            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(_Data)))
            {
                AssetDatabase.CreateAsset(_Data, $"Assets/PaintedWaterData_{System.Guid.NewGuid()}.asset");
            }

            AssetDatabase.SaveAssetIfDirty(_Data);

            if (_BakeOnSave && _BakedTexture != null)
            {
                OnBakeRequest?.Invoke(this);
            }
        }

        // TODO: keep?
        internal void Flush()
        {
            _PreviousKernel = -1;
            Graphics.Blit(Texture2D.blackTexture, TargetRT);
            SceneView.RepaintAll();
        }

        internal override bool InferMode(Component component, ref LodInputMode mode)
        {
            return false;
        }

        [@OnChange]
        internal override void OnChange(string path, object previous)
        {
            switch (path)
            {
                case nameof(_MaximumResolution):
                case nameof(_RequestedTexelDensity):
                case nameof(_WorldSize):
                    Undo.RecordObject(_Input, k_ResizeUndoLabel);
                    UpdateResolution();
                    if (TargetRT != null)
                    {
                        UpdateRenderTextureResolution();
                        Rebuild();
                    }
                    break;
                case nameof(_Data):
                    Rebuild();
                    break;
            }
        }
    }

    partial class PaintLodInputData
    {
        internal abstract PaintData.StrokeMode Type { get; }
        private protected abstract Kernel GetKernel(PaintData.BrushMode mode);
        private protected abstract float GetStrength(PaintData.BrushMode mode);
        private protected abstract Vector4 GetValue(PaintData.BrushMode mode);
        internal abstract string HelpText { get; }
    }


    sealed partial class ClipPaintLodInputData
    {
        internal override PaintData.StrokeMode Type => PaintData.StrokeMode.Value;
        private protected override Kernel GetKernel(PaintData.BrushMode mode) => Kernel.Saturate;
        private protected override float GetStrength(PaintData.BrushMode mode) => mode is not PaintData.BrushMode.None ? -1f : 1f;
        private protected override Vector4 GetValue(PaintData.BrushMode mode) => new(1, 0, 0, 0);
        internal override string HelpText => "Left click to clip the water surface.\nHold shift and left click to unclip the water surface.\n";
    }

    sealed partial class FlowPaintLodInputData
    {
        internal override PaintData.StrokeMode Type => _NormalizeStrokeMagnitude ? PaintData.StrokeMode.NormalizedDirection : PaintData.StrokeMode.Direction;
        private protected override Kernel GetKernel(PaintData.BrushMode mode) => mode is not PaintData.BrushMode.None ? Kernel.Towards2 : _ClampOutputMagnitude ? Kernel.Saturate2 : Kernel.Add;
        private protected override float GetStrength(PaintData.BrushMode mode) => 0.0125f;
        private protected override Vector4 GetValue(PaintData.BrushMode mode) => Vector4.zero;
        internal override string HelpText => "Left click, hold and drag to add flow orientated along the drag direction.\nHold shift and left click to remove.\n";

        public FlowPaintLodInputData()
        {
            _ClampOutputMagnitude = false;
        }
    }

    sealed partial class FoamPaintLodInputData
    {
        internal override PaintData.StrokeMode Type => PaintData.StrokeMode.Value;
        private protected override Kernel GetKernel(PaintData.BrushMode mode) => Kernel.Saturate;
        private protected override float GetStrength(PaintData.BrushMode mode) => mode is not PaintData.BrushMode.None ? -1f : 1f;
        private protected override Vector4 GetValue(PaintData.BrushMode mode) => new(0.03f, 0, 0, 0);
        internal override string HelpText => "Left click to add foam.\nHold shift and left click to remove.\n";
    }

    sealed partial class LevelPaintLodInputData
    {
        internal override PaintData.StrokeMode Type => PaintData.StrokeMode.Value;
        private protected override Kernel GetKernel(PaintData.BrushMode mode) => _SetValue || mode is PaintData.BrushMode.Remove ? Kernel.Towards : Kernel.Add;
        private protected override float GetStrength(PaintData.BrushMode mode) => mode switch
        {
            // Never do negative strength due to lerp etc.
            PaintData.BrushMode.Negate => 1f,
            PaintData.BrushMode.Remove => 0.01f,
            PaintData.BrushMode.None => 1f,
            _ => throw new System.NotImplementedException(),
        };
        private protected override Vector4 GetValue(PaintData.BrushMode mode) => mode switch
        {
            PaintData.BrushMode.Negate => new(_SetValue ? -_Value : -0.01f, 0, 0, 0),
            PaintData.BrushMode.Remove => Vector4.zero,
            PaintData.BrushMode.None => new(_SetValue ? _Value : 0.02f, 0, 0, 0),
            _ => throw new System.NotImplementedException(),
        };
        internal override string HelpText => "Left click to raise water level.\nHold control and left click to lower water level.\nHold shift and left click to remove.\n";
    }

    sealed partial class AbsorptionPaintLodInputData
    {
        internal override PaintData.StrokeMode Type => PaintData.StrokeMode.Color;
        private protected override Kernel GetKernel(PaintData.BrushMode mode) => Kernel.Interpolate;
        private protected override Vector4 GetValue(PaintData.BrushMode mode) => mode is not PaintData.BrushMode.None ? Vector4.zero : _Absorption;
        private protected override float GetStrength(PaintData.BrushMode mode) => 0.025f;
        internal override string HelpText => "Left click, hold and drag to set absorption.\nHold shift and left click to remove.\n";

        Vector4 _Absorption = WaterRenderer.UpdateAbsorptionFromColor(AbsorptionLod.s_DefaultColor);

        public AbsorptionPaintLodInputData()
        {
            _Color = AbsorptionLod.s_DefaultColor;
        }

        internal override void OnEnable()
        {
            base.OnEnable();

            _Absorption = WaterRenderer.UpdateAbsorptionFromColor(_Color);
        }

        internal override void OnChange(string path, object previous)
        {
            switch (path)
            {
                case nameof(_Color):
                    _Absorption = WaterRenderer.UpdateAbsorptionFromColor(_Color);
                    break;
            }

            base.OnChange(path, previous);
        }
    }

    sealed partial class ScatteringPaintLodInputData
    {
        internal override PaintData.StrokeMode Type => PaintData.StrokeMode.Color;
        private protected override Kernel GetKernel(PaintData.BrushMode mode) => Kernel.Interpolate;
        private protected override Vector4 GetValue(PaintData.BrushMode mode) => mode is not PaintData.BrushMode.None ? Vector4.zero : _Color.MaybeLinear();
        private protected override float GetStrength(PaintData.BrushMode mode) => 0.025f;
        internal override string HelpText => "Left click, hold and drag to add scattering color.\nHold shift and left click to remove.\n";

        public ScatteringPaintLodInputData()
        {
            _Color = ScatteringLod.s_DefaultColor;
        }
    }

    sealed partial class ShapeWavesPaintLodInputData
    {
        internal override PaintData.StrokeMode Type => _NormalizeStrokeMagnitude ? PaintData.StrokeMode.NormalizedDirection : PaintData.StrokeMode.Direction;
        private protected override Kernel GetKernel(PaintData.BrushMode mode) => mode is not PaintData.BrushMode.None ? Kernel.Towards2 : _ClampOutputMagnitude ? Kernel.Saturate2 : Kernel.Add;
        private protected override Vector4 GetValue(PaintData.BrushMode mode) => new(1, 0, 0, 0);
        private protected override float GetStrength(PaintData.BrushMode mode) => _NormalizeStrokeMagnitude ? 0.0125f : 0.025f;
        internal override string HelpText => "Left click, hold and drag to add waves orientated along the drag direction.\nHold shift and left click to remove.\n";
    }
}

#endif
