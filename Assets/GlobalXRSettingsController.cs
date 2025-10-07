using UnityEngine;

#if UNITY_ANDROID
using Unity.Collections;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR.Features.Meta;
#endif

public class GlobalXRSettingsController : MonoBehaviour
{
#if UNITY_ANDROID

    [SerializeField] private bool use120HzIfAvailable;

    private void Start()
    {
#if !UNITY_EDITOR
        TrySetRefreshRate();
#else
        Application.targetFrameRate = 90;
#endif
    }

    private void TrySetRefreshRate()
    {
        var subsystem = XRGeneralSettings.Instance.Manager.activeLoader.GetLoadedSubsystem<XRDisplaySubsystem>();

        if (subsystem.TryGetSupportedDisplayRefreshRates(Allocator.Temp, out var rates))
        {
            if (use120HzIfAvailable && rates.Contains(120f))
            {
                if (!subsystem.TryRequestDisplayRefreshRate(120f))
                    Debug.LogError("Failed to set refresh rate to 120hz");
                else
                    Debug.Log($"Refresh rate set to 120hz");
                return;
            }

            if (!subsystem.TryRequestDisplayRefreshRate(90f))
                Debug.LogError("Failed to set refresh rate to 90hz");
            else
                Debug.Log($"Refresh rate set to 90hz");
        }
        else
        {
            Debug.LogError("Failed to get supported refresh rates");
        }
    }

#endif
}