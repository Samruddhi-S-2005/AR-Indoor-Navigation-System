using UnityEngine;
using UnityEngine.XR.ARFoundation;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class ARCameraPermissionManager : MonoBehaviour
{
    private ARSession arSession;

    void Start()
    {
        arSession = FindObjectOfType<ARSession>();

#if UNITY_ANDROID
        if (arSession != null && !Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            arSession.enabled = false;
            Permission.RequestUserPermission(Permission.Camera);
        }
#endif

        StartCoroutine(CheckAndEnableARSession());
    }

    System.Collections.IEnumerator CheckAndEnableARSession()
    {
#if UNITY_ANDROID
        while (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            yield return new WaitForSeconds(0.5f);
        }
#endif
        if (arSession != null && !arSession.enabled)
            arSession.enabled = true;

        yield break; // Ensures all code paths return a value
    }

    void Update()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            if (arSession != null && arSession.enabled)
            {
                arSession.enabled = false;
                Debug.LogWarning("Camera permission revoked, ARSession disabled.");
            }
        }
#endif
    }

    void OnApplicationFocus(bool hasFocus)
    {
#if UNITY_ANDROID
        if (hasFocus && !Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Permission.RequestUserPermission(Permission.Camera);
            Debug.Log("Camera permission requested again on app focus.");
        }
#endif
    }
}
