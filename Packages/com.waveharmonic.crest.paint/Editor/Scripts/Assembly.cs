// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if !d_CrestValid
#error "Your Crest package needs to be updated to be compatible with this version of <i>Crest - Paint</i>."
#endif

using System.Runtime.CompilerServices;

// Inherits from paint inspector if present.
[assembly: InternalsVisibleTo("WaveHarmonic.Crest.CPUQueries.Editor")]

// Define empty namespaces for when assemblies are not present.
namespace UnityEditor.EditorTools { }
