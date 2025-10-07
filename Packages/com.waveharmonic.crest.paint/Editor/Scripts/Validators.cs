// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEditor;
using UnityEngine;
using WaveHarmonic.Crest.Editor;
using static WaveHarmonic.Crest.Editor.ValidatedHelper;
using MessageType = WaveHarmonic.Crest.Editor.ValidatedHelper.MessageType;

namespace WaveHarmonic.Crest.Paint.Editor
{
    static class Validators
    {
        [Validator(typeof(LodInput))]
        static bool Validate(LodInput target, ShowMessage messenger)
        {
            if (target.Data is not PaintLodInputData data) return true;

            var isValid = true;

            if (SceneView.lastActiveSceneView != null && !Application.isPlaying)
            {
                var sceneView = SceneView.lastActiveSceneView;

                if (!sceneView.drawGizmos)
                {
                    messenger
                    (
                        "Gizmos are not enabled for the scene view. Painting requires gizmos to be enabled.",
                        "Enable gizmos.",
                        MessageType.Warning,
                        target,
                        (x, y) => sceneView.drawGizmos = true
                    );
                }
            }

            if (data.Data != null && data._BakedTexture == null)
            {
                messenger
                (
                    "Remember to bake for painted data to work in standalone.",
                    "Bake painted data to a texture",
                    MessageType.Info,
                    target,
                    (x, y) => PaintableEditor.s_BakeInput = target
                );
            }

            return isValid;
        }
    }
}
