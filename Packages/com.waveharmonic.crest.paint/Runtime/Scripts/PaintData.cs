// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using UnityEngine;

using Kernel = WaveHarmonic.Crest.Paint.PaintLodInputData.Kernel;

namespace WaveHarmonic.Crest.Paint
{
    [ExecuteAlways]
    sealed class PaintData : ScriptableObject
    {
        // Used to give unique name for cache which is outside of Assets folder.
        [SerializeField]
        internal string _GUID;

        [Serializable]
        internal sealed class Stroke
        {
            [SerializeField]
            internal List<Vector2> _Points = new();
        }

        internal enum StrokeMode
        {
            NormalizedDirection,
            Color,
            Value,
            Direction,
        }

        internal enum BrushMode
        {
            None,
            Remove,
            Negate,
        }

        [Serializable]
        internal sealed class PaintCommand
        {
            // Versioning can handle everything including which kernel to use.
            [HideInInspector]
            [SerializeField]
            int _Version;

            [SerializeField]
            internal StrokeMode _Type;

            [SerializeField]
            internal BrushMode _BrushMode;

            // Benefits over string:
            // - Save some performance and storage
            // - Save GC allocations
            // - Can rename kernels
            // Problems over string:
            // - Cannot delete a kernel as indices will shift (would we ever?)
            // - Not certain kernel indices are reliable
            [SerializeField]
            internal Kernel _BlendOperation;

            [SerializeField]
            internal float _BrushWidth;

            [SerializeField]
            internal float _BrushStrength;

            [SerializeField]
            internal Vector4 _Value;

            [SerializeField]
            internal List<Stroke> _Strokes = new();
        }

        // For future layer support.
        [Serializable]
        internal sealed class Layer
        {
            [SerializeField]
            internal List<PaintCommand> _PaintCommands = new();
        }

        [SerializeField]
        internal List<Layer> _Layers = new();

        void Awake()
        {
            if (_GUID == null)
            {
                RegenerateGUID();
            }
        }

        internal void RegenerateGUID()
        {
            _GUID = Guid.NewGuid().ToString();
            // Name must match filename possibly.
            name = $"PaintedWaterData_{_GUID}";
        }
    }
}

#endif
