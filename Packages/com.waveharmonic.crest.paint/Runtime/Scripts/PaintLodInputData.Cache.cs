// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if UNITY_EDITOR

using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace WaveHarmonic.Crest.Paint
{
    partial class PaintLodInputData
    {
        internal Texture2D _Cache;

        internal void LoadCache()
        {
            // No data, no cache.
            if (Data == null) return;

            var path = Path.Combine(Application.temporaryCachePath, Data.name);
            if (!File.Exists(path)) return;
            CreateOrUpdateCacheTexture();
            _Cache.LoadRawTextureData(File.ReadAllBytes(path));
            _Cache.Apply();

#if UNITY_EDITOR
#if CREST_DEBUG
            Log($"Loaded Cache");
#endif
#endif
        }

        internal void SaveCache()
        {
            // No data, no cache.
            if (Data == null) return;

            // TODO: Remove Application.isPlaying if support editing in play mode.
            if (Application.isPlaying || TargetRT == null) return;

            CreateOrUpdateCacheTexture();
            // Copy from GPU to CPU.
            RenderTexture.active = TargetRT;
            _Cache.ReadPixels(new Rect(0, 0, TargetRT.width, TargetRT.height), 0, 0);
            _Cache.Apply();
            RenderTexture.active = null;

            var path = Path.Combine(Application.temporaryCachePath, Data.name);
            File.WriteAllBytes(path, _Cache.GetRawTextureData());

#if CREST_DEBUG
#if UNITY_EDITOR
            Log($"Saved Cache");
#endif
#endif
        }

        void CreateOrUpdateCacheTexture()
        {
            if (_Cache == null)
            {
                _Cache = new Texture2D(Resolution.x, Resolution.y, k_GraphicsFormat, TextureCreationFlags.None);
            }
            else if (_Cache.width != Resolution.x || _Cache.height != Resolution.y)
            {
                _Cache.Reinitialize(Resolution.x, Resolution.y, k_GraphicsFormat, false);
            }
        }
    }
}

#endif
