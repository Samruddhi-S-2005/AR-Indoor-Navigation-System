using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class PathDrawer : MonoBehaviour
{
    [Header("Arrow Visual")]
    public GameObject arrowPrefab;
    public float arrowSpacing = 0.15f;
    public Vector3 arrowScale = new Vector3(0.05f, 0.05f, 0.10f);

    [Header("AR (optional)")]
    public ARAnchorManager anchorManager;       // ideal: drag ARAnchorManager here
    [Tooltip("If the ARAnchorManager field doesn't appear, drag the XR Origin GameObject here.")]
    public GameObject anchorManagerGameObject;  // fallback: drag XR Origin here

    private readonly List<GameObject> spawnedArrows = new List<GameObject>();
    private readonly List<ARAnchor> spawnedAnchors = new List<ARAnchor>();

    // reflection cache
    private MethodInfo m_tryAddAnchor = null;
    private MethodInfo m_addAnchor = null;

    void Awake()
    {
        // resolve anchorManager if only GameObject provided
        if (anchorManager == null && anchorManagerGameObject != null)
            anchorManager = anchorManagerGameObject.GetComponent<ARAnchorManager>();

        // Prepare reflection for available methods (if any)
        if (anchorManager != null)
        {
            var t = anchorManager.GetType();
            // Try 'TryAddAnchor' (AR Foundation 6+ style)
            m_tryAddAnchor = t.GetMethod("TryAddAnchor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            // Try 'AddAnchor' (older API)
            m_addAnchor = t.GetMethod("AddAnchor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
    }

    public void ClearPath()
    {
        foreach (var a in spawnedArrows) if (a != null) Destroy(a);
        spawnedArrows.Clear();

        // Destroy anchor gameObjects (this removes anchors from AR session)
        foreach (var anchor in spawnedAnchors)
        {
            if (anchor != null)
            {
                try { Destroy(anchor.gameObject); }
                catch { } // safe-guard
            }
        }
        spawnedAnchors.Clear();
    }

    public void DrawAnchoredPath(List<Vector3> worldPoints)
    {
        ClearPath();

        if (worldPoints == null || worldPoints.Count < 2)
        {
            Debug.LogWarning("[PathDrawer] No world points provided!");
            return;
        }

        if (arrowPrefab == null)
        {
            Debug.LogError("[PathDrawer] Arrow prefab missing!");
            return;
        }

        // Try to (re)resolve anchorManager if needed (in case inspector changed at runtime)
        if (anchorManager == null && anchorManagerGameObject != null)
        {
            anchorManager = anchorManagerGameObject.GetComponent<ARAnchorManager>();
            if (anchorManager != null)
            {
                var t = anchorManager.GetType();
                m_tryAddAnchor = t.GetMethod("TryAddAnchor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                m_addAnchor = t.GetMethod("AddAnchor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
        }

        Debug.Log($"[PathDrawer] Drawing path with {worldPoints.Count} points. anchorManager present: {anchorManager != null}");

        for (int i = 0; i < worldPoints.Count - 1; i++)
        {
            Vector3 start = worldPoints[i];
            Vector3 end = worldPoints[i + 1];
            Vector3 flatDir = new Vector3(end.x - start.x, 0f, end.z - start.z).normalized;

            float dist = Vector3.Distance(start, end);
            int arrowCount = Mathf.Max(1, Mathf.FloorToInt(dist / arrowSpacing));

            for (int j = 0; j < arrowCount; j++)
            {
                float t = (j + 0.5f) / arrowCount;
                Vector3 pos = Vector3.Lerp(start, end, t);
                Quaternion rot = Quaternion.LookRotation(flatDir);

                // If we have an AR anchor manager, prefer creating anchors
                if (anchorManager != null)
                {
                    ARAnchor createdAnchor = TryCreateAnchor(anchorManager, pos, rot);
                    if (createdAnchor != null)
                    {
                        GameObject arrow = Instantiate(arrowPrefab, createdAnchor.transform);
                        arrow.transform.localPosition = Vector3.zero;
                        arrow.transform.localRotation = Quaternion.identity;
                        arrow.transform.localScale = arrowScale;

                        spawnedArrows.Add(arrow);
                        spawnedAnchors.Add(createdAnchor);
                        continue;
                    }
                    else
                    {
                        // If anchor creation failed, fall through to non-anchored spawn.
                        Debug.LogWarning("[PathDrawer] Anchor creation failed, instantiating non-anchored arrow.");
                    }
                }

                // Editor/device fallback: instantiate directly in world under this transform
                GameObject fallbackArrow = Instantiate(arrowPrefab, pos, rot, this.transform);
                fallbackArrow.transform.localScale = arrowScale;
                spawnedArrows.Add(fallbackArrow);
            }
        }

        Debug.Log($"[PathDrawer] Spawned arrows: {spawnedArrows.Count} anchors: {spawnedAnchors.Count}");
    }

    // Tries multiple anchor-creation strategies and returns created ARAnchor or null
    private ARAnchor TryCreateAnchor(ARAnchorManager manager, Vector3 pos, Quaternion rot)
    {
        if (manager == null) return null;

        // 1) Try reflection call for TryAddAnchor(pose, out ARAnchor anchor)
        if (m_tryAddAnchor != null)
        {
            try
            {
                // signature: bool TryAddAnchor(Pose pose, out ARAnchor anchor)
                object[] parameters = new object[] { new Pose(pos, rot), null };
                bool ok = (bool)m_tryAddAnchor.Invoke(manager, parameters);
                if (ok && parameters[1] is ARAnchor anchorObj)
                {
                    return anchorObj;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PathDrawer] TryAddAnchor reflection failed: " + ex.Message);
            }
        }

        // 2) Try reflection call for AddAnchor(pose) -> ARAnchor
        if (m_addAnchor != null)
        {
            try
            {
                object[] parameters = new object[] { new Pose(pos, rot) };
                object result = m_addAnchor.Invoke(manager, parameters);
                if (result is ARAnchor anchorObj)
                {
                    return anchorObj;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PathDrawer] AddAnchor reflection failed: " + ex.Message);
            }
        }

        // 3) As a final fallback, create a GameObject and attach ARAnchor component directly (works in many ARFoundation versions)
        try
        {
            GameObject go = new GameObject("runtime_anchor");
            go.transform.position = pos;
            go.transform.rotation = rot;
            // attach ARAnchor component
            ARAnchor anchor = go.AddComponent<ARAnchor>();
            // Some AR subsystems may require additional setup; this works on many devices
            return anchor;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[PathDrawer] Fallback ARAnchor creation failed: " + ex.Message);
            return null;
        }
    }
}
