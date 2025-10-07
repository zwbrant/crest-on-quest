// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace WaveHarmonic.Crest.Paint.Editor
{
    partial class PaintableEditor
    {
        [InitializeOnLoadMethod]
        static void OnLoad()
        {
            // Allows a bake request without referencing this assembly.
            PaintLodInputData.OnBakeRequest -= Bake;
            PaintLodInputData.OnBakeRequest += Bake;
        }

        static void Bake(PaintLodInputData input)
        {
            if (input == null) return;

            input.SaveCache();

            // TODO: Undo/Redo
            if (input._BakedTexture == null)
            {
                input._BakedTexture = new
                (
                    input.Resolution.x,
                    input.Resolution.y,
                    PaintLodInputData.k_GraphicsFormat,
                    TextureCreationFlags.None
                );
            }
            else
            {
                input._BakedTexture.Reinitialize
                (
                    input.Resolution.x,
                    input.Resolution.y,
                    PaintLodInputData.k_GraphicsFormat,
                    false
                );
            }

            var path = AssetDatabase.GetAssetPath(input._BakedTexture) ?? "";
            if (path.Length <= 0) path = $"Assets/PaintedWaterData_{System.Guid.NewGuid()}.exr";

            // Fixes Graphics.CopyTexture destination graphics texture is not initialized on
            // the GPU for HDRP.
            input._BakedTexture.Apply();

            // Cache is in correct format for CopyTexture.
            Graphics.CopyTexture(input._Cache, input._BakedTexture);

            File.WriteAllBytes(path, input._BakedTexture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
            // Required or next line will not work.
            AssetDatabase.ImportAsset(path);

            input._BakedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            importer.isReadable = true;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = false;
            // Compression will clamp negative values.
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            // TODO: Make this configurable?
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            // Values are slightly different with NPOT Scale applied.
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();

            s_BakeInput = null;
        }
    }
}
