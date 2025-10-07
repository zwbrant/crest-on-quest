// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEditor;
using UnityEngine;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest.Paint.Editor
{
    static class Visualizers
    {
        [DrawGizmo(GizmoType.Selected)]
        static void DrawGizmos(LodInput target, GizmoType type)
        {
            if (target.Data is not PaintLodInputData input) return;

            var position = target.transform.position;

            // Draw gizmo at sea level.
            var water = WaterRenderer.Instance;
            if (water != null) position.y = water.SeaLevel;

            var oldMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.Translate(position) * Matrix4x4.Scale(input.WorldSize.XNZ(1f));
            Gizmos.color = InputPaintingEditorTool.Active ? new(1f, 0f, 1f, 1f) : target.GizmoColor;

            Gizmos.DrawWireCube(Vector3.zero, new(1f, 0f, 1f));

            if (input.IsPainting)
            {
                Gizmos.DrawWireCube(Vector3.up * 0.01f, new(1f, 0f, 1f));
                Gizmos.DrawWireCube(Vector3.up * -0.01f, new(1f, 0f, 1f));

                var inputPosition = target.transform.position.XZ();
                var inputSize = input._WorldSize;
                Shader.SetGlobalVector(ShaderIDs.s_BoundaryXZ, inputPosition.XYNN(inputSize));
            }

            Gizmos.matrix = oldMatrix;
        }
    }
}
