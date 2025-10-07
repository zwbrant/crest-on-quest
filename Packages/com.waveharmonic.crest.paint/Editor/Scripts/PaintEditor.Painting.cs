// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest.Paint.Editor
{
    partial class PaintableEditor
    {
        static Texture2D s_ReadablePixelTexture;

        InputPaintingEditorTool _Tool;
        InputPaintingEditorTool Tool => _Tool != null ? _Tool : _Tool = CreateInstance<InputPaintingEditorTool>();

        static Material s_CursorMaterial;
        static Material CursorMaterial => s_CursorMaterial != null ? s_CursorMaterial : s_CursorMaterial = new Material(Shader.Find("Hidden/Crest/Paint Cursor"));

        // Store during normal mouse movement for clicks mid movement.
        Vector2 _MouseMoveDelta;
        Vector2 _DebugMousePrevious;

        // Store as shift should only register on click.
        bool _IsShift;
        bool _IsControl;

        protected virtual void OnSceneGUI()
        {
            if (InputData == null)
            {
                return;
            }

#if CREST_DEBUG
            if (InputData.Debugger != null && InputData.Debugger.DrawMousePath)
            {
                WorldPositionFromMouse(Event.current.mousePosition, out var p1);
                WorldPositionFromMouse(Event.current.mousePosition - Event.current.delta, out var p2);
                WorldPositionFromMouse(_DebugMousePrevious, out var p3);
                _DebugMousePrevious = Event.current.mousePosition;
                Debug.DrawLine(p2.XNZ(), p1.XNZ(), Color.magenta);
                Debug.DrawLine(p3.XNZ(), p1.XNZ(), Color.cyan);
                SceneView.RepaintAll();
            }
#endif

            if (ToolManager.activeToolType != typeof(InputPaintingEditorTool))
            {
                return;
            }

            InputData.IsPainting = ToolManager.IsActiveTool(Tool);

            if (!InputData.IsPainting)
            {
                return;
            }

            switch (Event.current.type)
            {
                case EventType.Repaint:
                    if (WorldPositionFromMouse(Event.current.mousePosition, out var position))
                    {
                        var inputPosition = InputData._Input.transform.position.XZ();
                        var inputSize = InputData._WorldSize;
                        CursorMaterial.SetVector(ShaderIDs.s_BoundaryXZ, inputPosition.XYNN(inputSize));

                        // Visible.
                        if (CursorMaterial.SetPass(0))
                        {
                            Graphics.DrawMeshNow
                            (
                                mesh: Helpers.SphereMesh,
                                matrix: Matrix4x4.TRS(position, Quaternion.identity, Vector3.one.XNZ(0.5f) * InputData._BrushSize)
                            );
                        }

                        // Occluded.
                        if (CursorMaterial.SetPass(1))
                        {
                            Graphics.DrawMeshNow
                            (
                                mesh: Helpers.SphereMesh,
                                matrix: Matrix4x4.TRS(position, Quaternion.identity, Vector3.one.XNZ(0.5f) * InputData._BrushSize)
                            );
                        }

                        // Add a circle outline.
                        {
                            // Color for inside or outside painting area.
                            var color = Handles.color;
                            var p = (position.XZ() - inputPosition).Absolute();
                            var s = inputSize * 0.5f;
                            Handles.color = p.x > s.x || p.y > s.y ? new(1.0f, 0.5f, 0.5f, 0.75f) : new(0.5f, 0.5f, 1.0f);
                            Handles.DrawWireArc(position, Vector3.up, Vector3.right, 360f, InputData._BrushSize * 0.5f, 3f);
                            Handles.color = color;
                        }
                    }
                    break;
                case EventType.MouseDown:
                    _IsShift = Event.current.shift;
                    _IsControl = Event.current.control;
                    goto case EventType.MouseDrag;
                case EventType.MouseDrag:
                    if (Event.current.button == 1) break;
                    Paint();
                    goto case EventType.MouseMove;
                case EventType.MouseMove:
                    _MouseMoveDelta = Event.current.delta;
                    HandleUtility.Repaint();
                    break;
                case EventType.MouseUp:
                    if (Event.current.button == 1) break;
                    MarkAsDirty();
                    break;
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                    break;
            }
        }

        void Paint()
        {
            if (!WorldPositionFromMouse(Event.current.mousePosition, out var newPosition))
            {
                return;
            }

            var isFirst = Event.current.type == EventType.MouseDown;

            var delta = Event.current.delta;
            if (isFirst)
            {
                delta = _MouseMoveDelta;
            }

            if (WorldPositionFromMouse(Event.current.mousePosition - delta, out var oldPosition))
            {
#if CREST_DEBUG
                InputData.DrawMousePath(oldPosition.XNZ(), newPosition.XNZ(), Color.red);
#endif

                // TODO: fix double undo entry by storing stoke in variable until mouse release.
                if (isFirst)
                {
                    // This can cause RenderTexture to become null on undo as it will trigger OnDisable/OnEnable
                    Undo.RecordObject(Input, k_PaintUndoLabel);
                    _InputData.SetUpForPainting();

                    Undo.RegisterCompleteObjectUndo(_InputData._Data, k_PaintUndoLabel);
                }
                else
                {
                    Undo.RegisterCompleteObjectUndo(_InputData._Data, k_PaintUndoLabel);
                }

                // @Versioning: Add versioning here: DirectionTowardsZero_V1
                InputData.Paint
                (
                    oldPosition.XZ(),
                    newPosition.XZ(),
                    _IsShift,
                    _IsControl,
                    isFirst
                );
            }
        }

        bool WorldPositionFromMouse(Vector2 mousePosition, out Vector3 position)
        {
            var water = WaterRenderer.Instance;
            var camera = water.Viewer;
            var sourceRT = water.Surface.WaterLevelDepthTexture;

            var success = false;
            position = Vector3.zero;

            if (camera && sourceRT)
            {
                Vector3 screenPosition = mousePosition * EditorGUIUtility.pixelsPerPoint;

                // GUI mouse position is flipped.
                screenPosition.y = camera.pixelHeight - screenPosition.y;

                var screenX = Mathf.RoundToInt(screenPosition.x);
                var screenY = Mathf.RoundToInt(screenPosition.y);

                // Out of bounds.
                if (screenX < 0 || screenY < 0 || screenX >= sourceRT.width || screenY >= sourceRT.height)
                {
                    return false;
                }

                if (s_ReadablePixelTexture == null)
                {
                    s_ReadablePixelTexture = new(1, 1, TextureFormat.RFloat, 0, true);
                }

                Helpers.ReadRenderTexturePixel(ref sourceRT, ref s_ReadablePixelTexture, screenX, screenY);

                var depth = s_ReadablePixelTexture.GetPixel(0, 0, 0).r;

                // Check to make sure query is working.
                if (depth is > 0.0f and < 1.0f)
                {
                    screenPosition.z = Helpers.ConvertDepthBufferValueToDistance(camera, depth);
                    position = camera.ScreenToWorldPoint(screenPosition);
                    success = true;
                }
            }

            if (!success)
            {
                var ray = HandleUtility.GUIPointToWorldRay(mousePosition);

                var planeY = water.transform.position.y;

                var heightOffset = ray.origin.y - planeY;
                var directionY = ray.direction.y;

                // Ray not going away from water plane.
                if (heightOffset * directionY < 0f)
                {
                    var distance = -heightOffset / directionY;
                    position = ray.GetPoint(distance);
                    success = true;
                }
            }

            return success;
        }
    }
}
