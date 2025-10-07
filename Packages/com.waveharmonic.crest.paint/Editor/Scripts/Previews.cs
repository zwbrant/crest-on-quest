// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using UnityEditor;
using UnityEngine;
using WaveHarmonic.Crest.Editor;
using WaveHarmonic.Crest.Internal;

namespace WaveHarmonic.Crest.Paint.Editor
{
    [CustomPreview(typeof(LodInput))]
    sealed class Preview : TexturePreview
    {
        LodInput Input => target as LodInput;
        PaintLodInputData Target => (target as LodInput).Data as PaintLodInputData;
        public override GUIContent GetPreviewTitle() => new($"Paint {Input.GetComponentIndex()}");

        RenderTexture _TemporaryRenderTexture;
        protected override Texture ModifiedTexture => _TemporaryRenderTexture;

        protected override Texture OriginalTexture
        {
            get
            {
                if (Input.Mode != LodInputMode.Paint) return null;
                return Target.SourceRT;
            }
        }

        public override void Cleanup()
        {
            base.Cleanup();

            if (_TemporaryRenderTexture != null)
            {
                RenderTexture.ReleaseTemporary(_TemporaryRenderTexture);
            }
        }

        public override void OnPreviewSettings()
        {
            base.OnPreviewSettings();

            if (_TemporaryRenderTexture != null)
            {
                // OnPreviewSettings is called after OnPreviewGUI so release here.
                RenderTexture.ReleaseTemporary(_TemporaryRenderTexture);
                _TemporaryRenderTexture = null;
            }
        }

        public override void OnPreviewGUI(Rect rect, GUIStyle background)
        {
            if (Target.Type is PaintData.StrokeMode.Direction or PaintData.StrokeMode.NormalizedDirection)
            {
                var shader = EditorHelpers.VisualizeNegativeValuesShader;
                var descriptor = OriginalTexture.GetDescriptor();
                _TemporaryRenderTexture = RenderTexture.GetTemporary(descriptor);
                Graphics.CopyTexture(OriginalTexture, _TemporaryRenderTexture);
                shader.SetTexture(0, ShaderIDs.s_Target, _TemporaryRenderTexture);
                shader.Dispatch(0, _TemporaryRenderTexture.width / 8, _TemporaryRenderTexture.height / 8, 1);
            }

            base.OnPreviewGUI(rect, background);
        }
    }
}
