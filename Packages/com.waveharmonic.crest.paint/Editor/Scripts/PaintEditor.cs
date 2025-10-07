// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using WaveHarmonic.Crest.Editor;

namespace WaveHarmonic.Crest.Paint.Editor
{
    [CustomEditor(typeof(LodInput), editorForChildClasses: true)]
    partial class PaintableEditor : Inspector
    {
        LodInput Input { get; set; }
        PaintLodInputData _InputData;
        PaintLodInputData InputData => _InputData ??= Input.Data as PaintLodInputData;

        static readonly string[] s_ButtonDropDownOptions = new string[]
        {
            "Clear",
            "Rebuild",
#if CREST_DEBUG
            "Attach Debugger",
#endif
        };

        static readonly GUIContent s_StartPainting = new("Start Painting", "Enable painting for this input.");
        static readonly GUIContent s_StopPainting = new("Stop Painting", "Disable painting for this input.");

        // Set to initiate bake indirectly.
        internal static LodInput s_BakeInput;

        protected override void OnEnable()
        {
            base.OnEnable();
            Undo.undoRedoEvent -= OnUndoRedo;
            Undo.undoRedoEvent += OnUndoRedo;

            Input = (LodInput)target;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Undo.undoRedoEvent -= OnUndoRedo;

            if (ToolManager.IsActiveTool(Tool))
            {
                ToolManager.RestorePreviousTool();
            }
        }

        public override bool RequiresConstantRepaint()
        {
            return base.RequiresConstantRepaint() || ToolManager.IsActiveTool(Tool);
        }

        protected override void RenderInspectorGUI()
        {
            base.RenderInspectorGUI();

            // If the target is not in the right mode, turn off painting and return.
            if (Input.Mode != LodInputMode.Paint || !Input.isActiveAndEnabled)
            {
                // Check both global and local tool.
                if (InputPaintingEditorTool.Active && ToolManager.IsActiveTool(Tool))
                {
                    ToolManager.RestorePreviousTool();
                }
            }
        }

        protected override void RenderBottomButtons()
        {
            base.RenderBottomButtons();

            if (Input.Mode != LodInputMode.Paint || InputData == null)
            {
                return;
            }

            {
                EditorGUILayout.Space();
                var paddedStyle = new GUIStyle(EditorStyles.helpBox);
                paddedStyle.padding = new RectOffset(8, 8, 8, 8);
                EditorGUILayout.BeginVertical(paddedStyle);

                var information = new List<string>();
                UpdateInformationString(information);
                var label = new GUIStyle(EditorStyles.label);
                label.fontSize = EditorStyles.helpBox.fontSize;
                GUILayout.Label(string.Join("\n", information), label);

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }

            var isActiveTool = ToolManager.IsActiveTool(Tool);

            EditorGUILayout.BeginHorizontal();

            if (InputData.Data != null && GUILayout.Button("Bake"))
            {
                Bake(InputData);
            }

            if (EditorHelpers.Button(isActiveTool ? s_StopPainting : s_StartPainting, out var choice, InputData._Data == null ? null : s_ButtonDropDownOptions))
            {
                switch (choice)
                {
                    case -1:
                        InputData.IsPainting = !isActiveTool;

                        if (isActiveTool)
                        {
                            ToolManager.RestorePreviousTool();
                        }
                        else
                        {
                            ToolManager.SetActiveTool(Tool);
                        }
                        break;
                    case 0:
                        ClearData();
                        break;
                    case 1:
                        InputData.Rebuild();
                        SceneView.RepaintAll();
                        break;
#if CREST_DEBUG
                    case 2:
                        Undo.AddComponent<PaintDebug>(Input.gameObject);
                        break;
#endif
                }
            }

            EditorGUILayout.EndHorizontal();

            // For other processes like validators.
            if (s_BakeInput == Input)
            {
                s_BakeInput = null;
                Bake(InputData);
            }
        }

        void UpdateInformationString(List<string> entries)
        {
            entries.Add(InputData.HelpText);
            entries.Add($"Resolution: {InputData.Resolution.x} x {InputData.Resolution.y}");
            entries.Add($"Texel Density: {InputData._TexelDensity}");
        }

        internal void ClearData()
        {
            Graphics.Blit(Texture2D.blackTexture, _InputData.TargetRT);

            Undo.RegisterCompleteObjectUndo(_InputData.Data, k_ClearUndoLabel);

            _InputData.Data._Layers.Clear();
            MarkAsDirty();
            SceneView.RepaintAll();
        }

        internal void MarkAsDirty()
        {
            EditorHelpers.MarkCurrentStageAsDirty();
            if (_InputData.Data != null) EditorUtility.SetDirty(_InputData.Data);
        }
    }

    partial class PaintableEditor
    {
        internal const string k_PaintUndoLabel = "Paint Water Data";
        internal const string k_ClearUndoLabel = "Clear Painted Data";
        internal const string k_ChangeUndoLabel = "Change Painted Data";

        static readonly string[] s_UndoRebuildLabels = new string[]
        {
            k_PaintUndoLabel,
            k_ClearUndoLabel,
            k_ChangeUndoLabel,
            PaintLodInputData.k_ResizeUndoLabel,
        };

        // NOTE: Using on OnChange might be for most properties. Hopefully only needed for data if that.
        void OnUndoRedo(in UndoRedoInfo info)
        {
            if (ArrayUtility.Contains(s_UndoRebuildLabels, info.undoName))
            {
                // Undo can undo the creation of the data.
                // Handle if data asset is deleted.
                if (InputData.Data == null)
                {
                    if (InputData.TargetRT != null)
                    {
                        InputData.TargetRT.Release();
                        Helpers.Destroy(InputData.TargetRT);
                    }

                    // Handle if data asset is deleted.
                    if (InputData._Cache != null)
                    {
                        Helpers.Destroy(InputData._Cache);
                    }
                }

                InputData.Rebuild();
                MarkAsDirty();
            }
        }
    }

    [EditorTool("Crest Input Painting", typeof(LodInput))]
    sealed class InputPaintingEditorTool : EditorTool
    {
        // All of these properties are for the global tool which has not been implemented.
        GUIContent _ToolbarIcon;
        public override GUIContent toolbarIcon => _ToolbarIcon ??= new(AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.waveharmonic.crest.paint/Editor/Icons/Paint.png"), "Crest Input Painting");
        public static bool Active => ToolManager.activeToolType == typeof(InputPaintingEditorTool);
        public override bool IsAvailable() => false;

        public override void OnActivated()
        {
            base.OnActivated();
            Helpers.SetGlobalBoolean(ShaderIDs.s_DrawBoundaryXZ, true);
        }

        public override void OnWillBeDeactivated()
        {
            base.OnWillBeDeactivated();
            Helpers.SetGlobalBoolean(ShaderIDs.s_DrawBoundaryXZ, false);
        }
    }
}
