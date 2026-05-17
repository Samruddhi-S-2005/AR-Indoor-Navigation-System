using UnityEngine;
using System.Collections.Generic;

public class NodeVisualizer : MonoBehaviour
{
    public GameObject nodeSpherePrefab;   // Assign your prefab here in Inspector
    public Pathfinder pathfinder;         // Linked at runtime from NavigationManager

    [Header("Map scale (same as NavigationManager.mapScale)")]
    public float mapScale = 1.0f;

    private List<GameObject> spheres = new List<GameObject>();

    public void CreateSpheres()
    {
        ClearSpheres();

        if (pathfinder == null || pathfinder.AllNodes == null)
        {
            Debug.LogWarning("NodeVisualizer: Pathfinder or node list missing.");
            return;
        }

        foreach (var node in pathfinder.AllNodes)
        {
            // Local position in map coordinates (scaled)
            Vector3 localPos = node.GetPosition() * mapScale;

            // Convert to world using MapRoot (this GameObject)
            Vector3 worldPos = transform.TransformPoint(localPos);

            // Instantiate the prefab at that position
            GameObject s = Instantiate(nodeSpherePrefab, worldPos, Quaternion.identity, transform);
            s.name = "NODE_" + node.nodeID;
            spheres.Add(s);
        }

        Debug.Log($"NodeVisualizer: Created {spheres.Count} spheres.");
    }

    public void ClearSpheres()
    {
        foreach (GameObject s in spheres)
            if (s != null) Destroy(s);

        spheres.Clear();
    }
}
