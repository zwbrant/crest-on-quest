// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#if d_UnityHDRP

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace WaveHarmonic.Crest
{
    class CustomPass : UnityEngine.Rendering.HighDefinition.CustomPass
    {
        internal GameObject _GameObject;
        internal CustomPassVolume _Volume;
    }

    static class CustomPassHelpers
    {
        internal static List<CustomPassVolume> s_Volumes = new();

        // Create or update Game Object.
        public static GameObject CreateOrUpdate
        (
            Transform parent,
            string name,
            bool hide = true
        )
        {
            GameObject gameObject = null;

            // Find the existing custom pass volume.
            // During recompiles, the reference will be lost so we need to find the game object. It could be limited to
            // the editor if it is safe to do so, but there is a potential for leaking game objects.
            if (gameObject == null)
            {
                var transform = parent.Find(name);
                if (transform != null)
                {
                    gameObject = transform.gameObject;
                }
            }

            // Create or update the custom pass volume.
            if (gameObject == null)
            {
                gameObject = new()
                {
                    name = name,
                    hideFlags = hide ? HideFlags.HideAndDontSave : HideFlags.DontSave,
                };
                // Place the custom pass under the water renderer since it is easier to find later. Transform.Find can
                // find inactive game objects unlike GameObject.Find.
                gameObject.transform.parent = parent;
            }
            else
            {
                gameObject.hideFlags = hide ? HideFlags.HideAndDontSave : HideFlags.DontSave;
                gameObject.SetActive(true);
            }

            return gameObject;
        }

        // Create or update Custom Pass Volume.
        public static void CreateOrUpdate<T>
        (
            GameObject gameObject,
            ref T pass,
            string name,
            CustomPassInjectionPoint injectionPoint,
            int priority = 0
        )
            where T : CustomPass, new()
        {
            CustomPassVolume volume = null;
            gameObject.GetComponents(s_Volumes);

            foreach (var v in s_Volumes)
            {
                if (v.injectionPoint == injectionPoint)
                {
                    volume = v;
                    break;
                }
            }

            // Create the custom pass volume if it does not exist.
            if (volume == null)
            {
                // It appears that this is currently the only way to add a custom pass.
                volume = gameObject.AddComponent<CustomPassVolume>();
                volume.injectionPoint = injectionPoint;
                volume.isGlobal = true;
                volume.priority = priority;
            }

            // Create custom pass.
            pass ??= new()
            {
                name = name,
                targetColorBuffer = UnityEngine.Rendering.HighDefinition.CustomPass.TargetBuffer.None,
                targetDepthBuffer = UnityEngine.Rendering.HighDefinition.CustomPass.TargetBuffer.None,
            };

            // Add custom pass.
            if (!volume.customPasses.Contains(pass))
            {
                volume.customPasses.Add(pass);
            }

            pass._GameObject = gameObject;
            pass._Volume = volume;
        }

        public static void Update<T>(GameObject go, T pass, CustomPassInjectionPoint point, int priority = 0) where T : CustomPass
        {
            CustomPassVolume volume = null;
            go.GetComponents(s_Volumes);

            foreach (var v in s_Volumes)
            {
                if (v.customPasses.Contains(pass))
                {
                    volume = v;
                    break;
                }
            }

            volume.injectionPoint = point;
            volume.priority = priority;
        }
    }
}

#endif
