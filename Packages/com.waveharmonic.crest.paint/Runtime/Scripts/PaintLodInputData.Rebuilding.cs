// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

// Rebuild the output from painted data.

#if UNITY_EDITOR

using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace WaveHarmonic.Crest.Paint
{
    partial class PaintLodInputData
    {
        // Scratch
        PropertyWrapperComputeStandalone _Wrapper;
        int _Kernel;
        int _PreviousKernel = -1;
        int _SizeX;
        int _SizeY;

        internal void SetUpForRebuilding()
        {
            _SegmentBuffer ??= new(k_SegmentBufferLength, k_PointsBufferStride, ComputeBufferType.Structured);
            _ConstantBuffer ??= new(k_ConstantBufferLength, k_ConstantBufferStride, ComputeBufferType.Constant);

            UpdateResolution();

            if (TargetRT == null)
            {
                TargetRT = new(Resolution.x, Resolution.y, 0, DefaultFormat.LDR)
                {
                    name = "_Crest_Painted",
                    enableRandomWrite = true,
                    graphicsFormat = k_GraphicsFormat,
                    useDynamicScale = false,
                    isPowerOfTwo = false,
                    // filterMode = FilterMode.Point,
                    filterMode = FilterMode.Bilinear,
                };

                UpdateTexelDensity();

                TargetRT.Create();

                if (PersistentRT != null)
                {
                    Graphics.CopyTexture(PersistentRT, TargetRT);
                }
            }
        }

        internal void Rebuild()
        {
            // Clear data.
            if (TargetRT != null)
            {
                Graphics.Blit(Texture2D.blackTexture, TargetRT);
            }

            // No data.
            if (Data == null) return;

            var longest = 0;
            foreach (var layer in Data._Layers)
            {
                foreach (var command in layer._PaintCommands)
                {
                    foreach (var stroke in command._Strokes)
                    {
                        longest = Mathf.Max(longest, stroke._Points.Count);
                    }
                }
            }

            // No commands.
            if (longest == 0) return;

            SetUpForRebuilding();

            if (TargetRT.width != Resolution.x || TargetRT.height != Resolution.y)
            {
                UpdateRenderTextureResolution();
            }

            PaintCS.SetConstantBuffer(ShaderIDs.s_Properties, _ConstantBuffer, 0, _ConstantBuffer.stride);

            if (_StrokePoints == null)
            {
                _StrokePoints = new StrokePoint[longest];
            }
            else if (longest > _StrokePoints.Length)
            {
                Array.Resize(ref _StrokePoints, longest);
            }

            // @Performance: Is this the best approach or will it upload all the data?
            var buffer = new ComputeBuffer(longest, k_PointsBufferStride, ComputeBufferType.Structured);

            foreach (var layer in Data._Layers)
            {
                foreach (var command in layer._PaintCommands)
                {
                    Debug.Assert(command._Strokes.Count > 0, "There should be at least one stroke.");

                    // Changed kernel.
                    if (_PreviousKernel != (int)command._BlendOperation)
                    {
                        _Wrapper = new PropertyWrapperComputeStandalone(PaintCS, (int)command._BlendOperation);
                        _PreviousKernel = _Kernel = (int)command._BlendOperation;
                        _Wrapper.SetTexture(ShaderIDs.s_Canvas, TargetRT);
                        _SizeX = Mathf.CeilToInt(TargetRT.width / 8f);
                        _SizeY = Mathf.CeilToInt(TargetRT.height / 8f);
                    }

                    SetPerCommandData(command, _Wrapper);

                    foreach (var stroke in command._Strokes)
                    {
                        var count = stroke._Points.Count;

                        Debug.Assert(count > 1, "There should be at least two points in a stroke.");

                        // Paint Stroke
                        {
                            UnityEngine.Profiling.Profiler.BeginSample("Crest.Paint.PaintStroke");

                            var length = stroke._Points.Count - 1;

                            var oldPosition = stroke._Points[0];
                            oldPosition.x *= _TexelDensity.x;
                            oldPosition.y *= _TexelDensity.y;
                            oldPosition.x += _TextureOffset.x;
                            oldPosition.y += _TextureOffset.y;

                            for (var i = 0; i < length; i++)
                            {
                                // The first point of a stroke is not actually part of the stroke, and is just used
                                // to calculate the direction if the stroke begins at a moving start.
                                var newPosition = stroke._Points[i + 1];

                                // It is much faster to do this per component manually.
                                newPosition.x *= _TexelDensity.x;
                                newPosition.y *= _TexelDensity.y;
                                newPosition.x += _TextureOffset.x;
                                newPosition.y += _TextureOffset.y;

                                _StrokePoints[i]._Position = newPosition;
                                _StrokePoints[i]._Value = GetValue(command, oldPosition, newPosition);

                                oldPosition = newPosition;
                            }

                            buffer.SetData(_StrokePoints, 0, 0, length);

                            UnityEngine.Profiling.Profiler.EndSample();
                        }

                        _Wrapper.SetBuffer(ShaderIDs.s_Stroke, buffer);
                        _Wrapper.SetInteger(ShaderIDs.s_StrokeLength, count - 1);

                        _Wrapper.Dispatch(_SizeX, _SizeY, 1);

                    }
                }
            }

            buffer.Release();

            _PreviousKernel = -1;
        }
    }
}

#endif
