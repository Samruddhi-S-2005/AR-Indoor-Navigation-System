// Pathfinder.cs
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class Pathfinder
{
    private BuildingData map;
    private Dictionary<string, Node> nodeLookup = new Dictionary<string, Node>();

    // Expose BuildingData for other scripts (MapData, etc.)
    public BuildingData BuildingData => map;

    // Public list to populate dropdowns
    public List<Node> AllNodes;

    public Pathfinder(string jsonText)
    {
        if (string.IsNullOrEmpty(jsonText))
        {
            Debug.LogError("Pathfinder constructor received empty JSON.");
            return;
        }

        map = JsonUtility.FromJson<BuildingData>(jsonText);
        if (map == null)
        {
            Debug.LogError("Failed to parse JSON into BuildingData.");
            return;
        }

        // Add reverse edges for floor edges
        foreach (Floor floor in map.buildingData)
        {
            List<Edge> reverseEdges = new List<Edge>();
            foreach (Edge edge in floor.edges)
            {
                reverseEdges.Add(new Edge
                {
                    start = edge.end,
                    end = edge.start,
                    distance = edge.distance
                });
            }
            floor.edges.AddRange(reverseEdges);
        }

        // Add reverse for inter-floor connections
        List<Edge> reverseInterFloor = new List<Edge>();
        foreach (Edge edge in map.interFloorConnections)
        {
            reverseInterFloor.Add(new Edge
            {
                start = edge.end,
                end = edge.start,
                distance = edge.distance
            });
        }
        map.interFloorConnections.AddRange(reverseInterFloor);

        // Initialize lists & lookup
        AllNodes = new List<Node>();
        foreach (Floor floor in map.buildingData)
        {
            foreach (Node node in floor.nodes)
            {
                // store the floor index in the node z coordinate for GetPosition
                if (node.coordinates == null) node.coordinates = new Coordinates();
                node.coordinates.z = floor.floorLevel;

                if (!nodeLookup.ContainsKey(node.nodeID))
                {
                    nodeLookup.Add(node.nodeID, node);
                    AllNodes.Add(node);
                }
                else
                {
                    Debug.LogWarning($"Duplicate nodeID found in JSON: {node.nodeID}");
                }
            }
        }

        Debug.Log($"Pathfinder loaded: {AllNodes.Count} nodes across {map.buildingData.Count} floors.");
    }

    // ---------------------------------------------------------
    // A* PATHFINDING
    // ---------------------------------------------------------
    public List<string> FindPath(string startNodeID, string endNodeID)
    {
        if (!nodeLookup.ContainsKey(startNodeID) || !nodeLookup.ContainsKey(endNodeID))
        {
            Debug.LogError($"FindPath: start or end node not found. start:{startNodeID} end:{endNodeID}");
            return null;
        }

        List<string> openList = new List<string> { startNodeID };
        HashSet<string> closedList = new HashSet<string>();
        Dictionary<string, string> cameFrom = new Dictionary<string, string>();

        Dictionary<string, float> gScore = new Dictionary<string, float>();
        foreach (var node in nodeLookup.Values) gScore[node.nodeID] = float.PositiveInfinity;
        gScore[startNodeID] = 0;

        Dictionary<string, float> fScore = new Dictionary<string, float>();
        foreach (var node in nodeLookup.Values) fScore[node.nodeID] = float.PositiveInfinity;
        fScore[startNodeID] = CalculateHeuristic(startNodeID, endNodeID);

        while (openList.Count > 0)
        {
            string current = GetNodeWithLowestFScore(openList, fScore);

            if (current == endNodeID)
            {
                return ReconstructPath(cameFrom, current);
            }

            openList.Remove(current);
            closedList.Add(current);

            foreach (Edge edge in GetNeighbors(current))
            {
                string neighbor = edge.end;
                if (closedList.Contains(neighbor)) continue;

                float tentative_gScore = gScore[current] + edge.distance;

                if (!openList.Contains(neighbor))
                {
                    openList.Add(neighbor);
                }
                else if (tentative_gScore >= gScore[neighbor])
                {
                    continue;
                }

                cameFrom[neighbor] = current;
                gScore[neighbor] = tentative_gScore;
                fScore[neighbor] = gScore[neighbor] + CalculateHeuristic(neighbor, endNodeID);
            }
        }

        // No path found
        return null;
    }

    // ---------------------------------------------------------
    // NEW: COMPUTE TOTAL PATH COST USING JSON DISTANCES
    // (1 unit in your JSON = 10 feet)
    // ---------------------------------------------------------
    public float ComputePathCost(List<string> pathNodeIDs)
    {
        if (pathNodeIDs == null || pathNodeIDs.Count < 2)
            return 0f;

        float total = 0f;

        for (int i = 0; i < pathNodeIDs.Count - 1; i++)
        {
            string from = pathNodeIDs[i];
            string to   = pathNodeIDs[i + 1];

            bool foundEdge = false;

            // Look for the edge from 'from' to 'to' on each floor
            foreach (var floor in map.buildingData)
            {
                foreach (var e in floor.edges)
                {
                    if (e.start == from && e.end == to)
                    {
                        total += e.distance;
                        foundEdge = true;
                        break;
                    }
                }
                if (foundEdge) break;
            }

            if (!foundEdge)
            {
                // Check inter-floor connections
                foreach (var e in map.interFloorConnections)
                {
                    if (e.start == from && e.end == to)
                    {
                        total += e.distance;
                        break;
                    }
                }
            }
        }

        return total; // still in your "distance units"
    }

    // ---------------------------------------------------------
    // INTERNAL HELPERS
    // ---------------------------------------------------------
    private float CalculateHeuristic(string nodeA_ID, string nodeB_ID)
    {
        Node nodeA = nodeLookup[nodeA_ID];
        Node nodeB = nodeLookup[nodeB_ID];
        return Vector3.Distance(nodeA.GetPosition(), nodeB.GetPosition());
    }

    private List<Edge> GetNeighbors(string nodeID)
    {
        List<Edge> neighbors = new List<Edge>();
        if (map == null) return neighbors;

        foreach (var floor in map.buildingData)
        {
            neighbors.AddRange(floor.edges.Where(edge => edge.start == nodeID));
        }

        neighbors.AddRange(map.interFloorConnections.Where(edge => edge.start == nodeID));
        return neighbors;
    }

    private List<string> ReconstructPath(Dictionary<string, string> cameFrom, string current)
    {
        List<string> totalPath = new List<string> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            totalPath.Insert(0, current);
        }
        return totalPath;
    }

    private string GetNodeWithLowestFScore(List<string> openList, Dictionary<string, float> fScore)
    {
        string bestNode = openList[0];
        float lowestScore = fScore.ContainsKey(bestNode) ? fScore[bestNode] : float.PositiveInfinity;

        foreach (string nodeID in openList)
        {
            float score = fScore.ContainsKey(nodeID) ? fScore[nodeID] : float.PositiveInfinity;
            if (score < lowestScore)
            {
                lowestScore = score;
                bestNode = nodeID;
            }
        }
        return bestNode;
    }
}
