// MapData.cs
using System.Collections.Generic;
using UnityEngine;

#region Serializable Data Classes

[System.Serializable]
public class BuildingData
{
    public List<Floor> buildingData;
    public List<Edge> interFloorConnections;
}

[System.Serializable]
public class Floor
{
    public string floorName;
    public int floorLevel;
    public List<Node> nodes;
    public List<Edge> edges;
}

[System.Serializable]
public class Node
{
    public string nodeID;
    public string name;
    public string type;
    public Coordinates coordinates;

    // Returns a map-local 3D position.
    // Uses coordinates.x -> X, coordinates.y -> Z (map planar),
    // coordinates.z -> floor index (set by Pathfinder when reading JSON).
    public Vector3 GetPosition()
    {
        float floorHeight = 3.0f; // meters per floor - adjust if needed
        float yWorld = coordinates.z * floorHeight; // coordinates.z holds floorLevel
        return new Vector3(coordinates.x, yWorld, coordinates.y);
    }
}

[System.Serializable]
public class Edge
{
    public string start;
    public string end;
    public float distance;
}

[System.Serializable]
public class Coordinates
{
    public float x;
    public float y;
    public float z;
}

#endregion

/// <summary>
/// MonoBehaviour wrapper that gives us fast lookup of nodes by ID
/// and a GetNodePosition(string) method for AR recentering, etc.
/// </summary>
public class MapData : MonoBehaviour
{
    [Header("Pathfinder / JSON data source")]
    [Tooltip("Assign the BuildingData that was loaded from map.json (optionally from Pathfinder).")]
    public BuildingData buildingData;

    // Lookup table: nodeID -> Node
    private Dictionary<string, Node> _nodeLookup;

    private void Awake()
    {
        BuildLookup();
    }

    /// <summary>
    /// Call this if Pathfinder loads/updates the buildingData at runtime.
    /// </summary>
    public void SetBuildingData(BuildingData data)
    {
        buildingData = data;
        BuildLookup();
    }

    /// <summary>
    /// Build dictionary from the current buildingData.
    /// Safely skips duplicate nodeIDs and logs a warning instead of crashing.
    /// </summary>
    private void BuildLookup()
    {
        _nodeLookup = new Dictionary<string, Node>();

        if (buildingData == null || buildingData.buildingData == null)
        {
            Debug.LogWarning("[MapData] No buildingData assigned, node lookup will be empty.");
            return;
        }

        foreach (var floor in buildingData.buildingData)
        {
            if (floor == null || floor.nodes == null)
                continue;

            foreach (var node in floor.nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.nodeID))
                    continue;

                if (_nodeLookup.ContainsKey(node.nodeID))
                {
                    Debug.LogWarning($"[MapData] Duplicate nodeID '{node.nodeID}' found. Skipping duplicate.");
                    continue;
                }

                _nodeLookup.Add(node.nodeID, node);
            }
        }

        Debug.Log($"[MapData] Node lookup built. Total unique nodes: {_nodeLookup.Count}");
    }

    /// <summary>
    /// Returns the world-space position for a node ID,
    /// using the Node.GetPosition() function.
    /// </summary>
    public Vector3 GetNodePosition(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            Debug.LogWarning("[MapData] GetNodePosition called with null/empty nodeId.");
            return Vector3.zero;
        }

        if (_nodeLookup == null || _nodeLookup.Count == 0)
        {
            Debug.LogWarning("[MapData] Node lookup not built or empty.");
            return Vector3.zero;
        }

        if (_nodeLookup.TryGetValue(nodeId, out Node node))
        {
            return node.GetPosition();
        }

        Debug.LogWarning($"[MapData] Node id not found: {nodeId}");
        return Vector3.zero;
    }
}
