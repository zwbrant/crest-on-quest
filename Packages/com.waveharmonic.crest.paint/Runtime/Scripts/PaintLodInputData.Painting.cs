// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// These painting operations need to be part of the main class, so they can
// rebuild without an active inspector.

#if UNITY_EDITOR

using UnityEngine;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest.Paint
{
    partial class PaintLodInputData
    {
        // For managing spot painting.
        RenderTexture _PreviousTargetRT;
        bool _IsSecondPaint;

        // Initializes everything for a rebuild or painting for the first time.
        internal void SetUpForPainting()
        {
            SetUpForRebuilding();

            if (_Data == null)
            {
                _Data = ScriptableObject.CreateInstance<PaintData>();
            }

            if (_PreviousTargetRT == null)
            {
                _PreviousTargetRT = new RenderTexture(TargetRT.descriptor);
                _PreviousTargetRT.Create();
            }
        }

        // Live painting only.
        internal void Paint(Vector2 oldPosition, Vector2 newPosition, bool isRemove, bool isNegate, bool isFirst = false)
        {
            // Convert to local space.
            oldPosition -= _Input.transform.position.XZ();
            newPosition -= _Input.transform.position.XZ();

            PaintData.Layer layer;
            PaintData.PaintCommand command = null;

            if (Data._Layers.Count > 0)
            {
                layer = Data._Layers[^1];
            }
            else
            {
                layer = new PaintData.Layer();
                Data._Layers.Add(layer);
            }

            var brushMode = isNegate ? PaintData.BrushMode.Negate : isRemove ? PaintData.BrushMode.Remove : PaintData.BrushMode.None;
            var brushStrength = _BrushStrength * GetStrength(brushMode);
            var brushValue = GetValue(brushMode);
            var blendOperation = GetKernel(brushMode);

            // TODO: Boost strength of mouse down, feels much better when clicking.
            // brushStrength *= 3f;

            if (layer._PaintCommands.Count > 0)
            {
                command = layer._PaintCommands[^1];

                // TODO: Should we check approx?
                // We group strokes into a command based on these properties.
                if (Type != command._Type || _BrushSize != command._BrushWidth || brushStrength != command._BrushStrength ||
                    blendOperation != command._BlendOperation || brushValue != command._Value || brushMode != command._BrushMode)
                {
                    command = null;
                }
            }

            if (command == null)
            {
                command = new()
                {
                    _Type = Type,
                    _BlendOperation = blendOperation,
                    _BrushWidth = _BrushSize,
                    _BrushStrength = brushStrength,
                    _Value = brushValue,
                    _BrushMode = brushMode,
                };

                layer._PaintCommands.Add(command);
            }

            var wrapper = new PropertyWrapperComputeStandalone(PaintCS, (int)blendOperation);

            if (isFirst)
            {
                var stroke = new PaintData.Stroke();
                // Old position is from mouse movement so we can work out the direction.
                stroke._Points.Add(oldPosition);
                stroke._Points.Add(newPosition);
                command._Strokes.Add(stroke);

                // If this becomes a stroke, then this point will be duplicated on next paint which we don't want.
                // Revert this paint on the next paint. The point is still added to the data.
                Graphics.CopyTexture(TargetRT, _PreviousTargetRT);
                _IsSecondPaint = true;

                // Command cannot change during a stroke so set up some shader data here.
                // TODO: magic value! remove or write to value
                SetPerCommandData(command, wrapper);
                wrapper.SetTexture(ShaderIDs.s_Canvas, TargetRT);
                wrapper.SetConstantBuffer(ShaderIDs.s_Properties, _ConstantBuffer);
                wrapper.SetBuffer(ShaderIDs.s_Stroke, _SegmentBuffer);
            }
            else
            {
                if (_IsSecondPaint)
                {
                    // Revert the first paint.
                    Graphics.CopyTexture(_PreviousTargetRT, TargetRT);
                    _IsSecondPaint = false;
                }

                // This will keep painting consistent with undo/redo as mouse delta differs. Possibly due to precision.
                // if (!_Debug._HigherFrequencyMouseCapture) oldPosition = command._Strokes[^1]._Points[^1];
                command._Strokes[^1]._Points.Add(newPosition);
            }

#if CREST_DEBUG
            DrawMousePath(oldPosition, newPosition, Color.green);
#endif

            // Paint Segment
            {
                UnityEngine.Profiling.Profiler.BeginSample("Crest.Paint.PaintSegment");

                oldPosition.x *= _TexelDensity.x;
                oldPosition.y *= _TexelDensity.y;
                oldPosition.x += _TextureOffset.x;
                oldPosition.y += _TextureOffset.y;
                newPosition.x *= _TexelDensity.x;
                newPosition.y *= _TexelDensity.y;
                newPosition.x += _TextureOffset.x;
                newPosition.y += _TextureOffset.y;

                var value = GetValue(command, oldPosition, newPosition);

                if (isFirst)
                {
                    _SegmentPoints[0]._Position = newPosition;
                    _SegmentPoints[0]._Value = value;

                    _SegmentBuffer.SetData(_SegmentPoints, 0, 0, 1);
                    wrapper.SetInteger(ShaderIDs.s_StrokeLength, 1);
                }
                else
                {
                    _SegmentPoints[0]._Position = oldPosition;
                    _SegmentPoints[1]._Position = newPosition;
                    _SegmentPoints[1]._Value = value;

                    _SegmentBuffer.SetData(_SegmentPoints, 0, 0, k_SegmentBufferLength);
                    wrapper.SetInteger(ShaderIDs.s_StrokeLength, k_SegmentBufferLength);
                }

                UnityEngine.Profiling.Profiler.EndSample();
            }

            wrapper.Dispatch(Mathf.CeilToInt(TargetRT.width / 8f), Mathf.CeilToInt(TargetRT.height / 8f), 1);
        }
    }
}

#endif
