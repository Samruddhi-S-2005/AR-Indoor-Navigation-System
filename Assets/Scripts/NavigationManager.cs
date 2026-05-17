using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;
using System.Linq;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class NavigationManager : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_Dropdown endDropdown;
    public Button pathButton;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI instructionText;

    [Header("AR Components")]
    public PathDrawer pathDrawer;
    public ARTrackedImageManager trackedImageManager;
    public Transform arCameraTransform;

    [Header("Map Setup")]
    public Transform mapRoot;
    private NodeVisualizer nodeVisualizer;

    [Header("Settings")]
    public float mapScale = 1.0f; // Corrected to 1.0 for meters

    [Tooltip("Set to 0.0 if marker is on Floor. Set to 0.8 if marker is on Table.")]
    public float markerHeightFromFloor = 0.0f;

    public float reachThreshold = 2.5f;

    // --- State Variables ---
    private Pathfinder pathfinder;
    private List<Node> destinationNodes;
    private bool mapIsAligned = false;
    private string currentStartNodeID = "";

    // --- Navigation State ---
    private List<string> currentPathIDs;
    private int currentTargetIndex = 0;
    private bool isNavigating = false;

    void Start()
    {
        TextAsset mapJson = Resources.Load<TextAsset>("MAP");
        if (mapJson == null) { Debug.LogError("MAP.json missing"); return; }

        pathfinder = new Pathfinder(mapJson.text);

        var mapData = FindObjectOfType<MapData>();
        if (mapData != null) mapData.SetBuildingData(pathfinder.BuildingData);

        if (mapRoot != null)
        {
            nodeVisualizer = mapRoot.GetComponent<NodeVisualizer>();
            if (nodeVisualizer != null)
            {
                nodeVisualizer.pathfinder = pathfinder;
                nodeVisualizer.mapScale = mapScale;
            }
        }

        destinationNodes = pathfinder.AllNodes
            .Where(n => n.type != "junction" && n.type != "stairs" && n.type != "lift")
            .OrderBy(n => n.name).ToList();

        PopulateDropdowns();

        if (pathButton != null) pathButton.onClick.AddListener(StartNavigation);

        if (statusText != null) statusText.text = "Ready. Scan any marker.";
        if (instructionText != null) instructionText.text = "";
    }

    void PopulateDropdowns()
    {
        if (endDropdown == null) return;
        endDropdown.ClearOptions();
        endDropdown.AddOptions(destinationNodes.Select(n => n.name).ToList());
    }

    void OnEnable() { if (trackedImageManager) trackedImageManager.trackedImagesChanged += OnTrackedImagesChanged; }
    void OnDisable() { if (trackedImageManager) trackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged; }

    // --- 1. INSTANT SCAN LOGIC ---
    void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs args)
    {
        if (isNavigating) return;

        foreach (var marker in args.added) AlignMap(marker);
        foreach (var marker in args.updated)
        {
            if (marker.trackingState == TrackingState.Tracking) AlignMap(marker);
        }
    }

    void AlignMap(ARTrackedImage marker)
    {
        string id = marker.referenceImage.name;
        Node startNode = pathfinder.AllNodes.FirstOrDefault(n => n.nodeID == id);

        if (startNode == null) return;

        mapRoot.rotation = Quaternion.Euler(0, marker.transform.eulerAngles.y, 0);

        Vector3 nodeLocal = startNode.GetPosition() * mapScale;
        Vector3 nodeWorld = mapRoot.TransformPoint(nodeLocal);

        Vector3 diff = marker.transform.position - nodeWorld;
        diff.y -= markerHeightFromFloor;

        mapRoot.position += diff;

        mapIsAligned = true;
        currentStartNodeID = id;

        if (statusText != null) statusText.text = $"Snapped to: {startNode.name}";
    }

    // --- 2. NAVIGATION LOGIC ---
    public void StartNavigation()
    {
        if (!mapIsAligned)
        {
            if (statusText) statusText.text = "Scan a marker first!";
            return;
        }

        if (string.IsNullOrEmpty(currentStartNodeID))
        {
            if (statusText) statusText.text = "Error: Start point lost. Rescan.";
            return;
        }

        string endID = destinationNodes[endDropdown.value].nodeID;

        if (currentStartNodeID == endID)
        {
            if (statusText) statusText.text = "You are already at this location.";
            return;
        }

        currentPathIDs = pathfinder.FindPath(currentStartNodeID, endID);

        if (currentPathIDs != null && currentPathIDs.Count > 0)
        {
            isNavigating = true;
            currentTargetIndex = 1;

            // Draw anchored arrows for the entire path
            var worldPoints = ConvertPathIDsToWorldPoints(currentPathIDs);
            if (pathDrawer != null) pathDrawer.DrawAnchoredPath(worldPoints);

            UpdateNavigationStep();
        }
        else
        {
            if (statusText) statusText.text = "Path not found.";
        }
    }

    void Update()
    {
        if (!isNavigating || currentPathIDs == null || arCameraTransform == null) return;

        if (currentTargetIndex < currentPathIDs.Count)
        {
            string targetID = currentPathIDs[currentTargetIndex];
            Node targetNode = pathfinder.AllNodes.First(n => n.nodeID == targetID);
            Vector3 targetWorldPos = GetWorldPos(targetNode);

            float dist = Vector3.Distance(
                new Vector3(arCameraTransform.position.x, 0, arCameraTransform.position.z),
                new Vector3(targetWorldPos.x, 0, targetWorldPos.z)
            );

            if (dist < reachThreshold)
            {
                currentTargetIndex++;
                UpdateNavigationStep();
            }

            if (statusText) statusText.text = $"Dist to turn: {dist:F1}m";
        }
        else
        {
            // --- USER ARRIVED ---
            if (instructionText) instructionText.text = "You have arrived!";
            if (statusText) statusText.text = "Arrived. Select next destination.";
            if (pathDrawer) pathDrawer.ClearPath();

            // Update Start Node to Current Location
            if (currentPathIDs != null && currentPathIDs.Count > 0)
            {
                currentStartNodeID = currentPathIDs[currentPathIDs.Count - 1];
            }

            isNavigating = false;
            currentTargetIndex = 0;
        }
    }

    // --- 3. AUTO-SQUARING NAVIGATION UPDATE ---
    void UpdateNavigationStep()
    {
        if (currentTargetIndex >= currentPathIDs.Count) return;

        string currentID = currentPathIDs[currentTargetIndex - 1];
        string nextID = currentPathIDs[currentTargetIndex];

        Node nextNode = pathfinder.AllNodes.First(n => n.nodeID == nextID);
        Node currentNode = pathfinder.AllNodes.First(n => n.nodeID == currentID);

        // 1. GENERATE INSTRUCTION
        Vector3 userPos = new Vector3(arCameraTransform.position.x, 0, arCameraTransform.position.z);
        Vector3 targetPos = new Vector3(GetWorldPos(nextNode).x, 0, GetWorldPos(nextNode).z);

        float distToTarget = Vector3.Distance(userPos, targetPos);
        string instruction = $"Go to {nextNode.name} ({distToTarget:F1}m)";

        // 2. TURN LOGIC
        if (currentTargetIndex + 1 < currentPathIDs.Count)
        {
            string futureID = currentPathIDs[currentTargetIndex + 1];
            Node futureNode = pathfinder.AllNodes.First(n => n.nodeID == futureID);

            Vector3 dirNow = (GetWorldPos(nextNode) - GetWorldPos(currentNode)).normalized;
            Vector3 dirNext = (GetWorldPos(futureNode) - GetWorldPos(nextNode)).normalized;

            float angle = Vector3.SignedAngle(dirNow, dirNext, Vector3.up);

            if (angle > 20) instruction += "\nThen Turn RIGHT";
            else if (angle < -20) instruction += "\nThen Turn LEFT";
            else instruction += "\nThen Go STRAIGHT";
        }
        else
        {
            instruction += "\nDestination Ahead!";
        }

        if (instructionText) instructionText.text = instruction;

        // 3. GENERATE "SQUARED" PATH (for the current step visualization)
        List<Vector3> pointsToDraw = new List<Vector3>();

        Vector3 p1 = GetWorldPos(currentNode);
        Vector3 p2 = GetWorldPos(nextNode);

        pointsToDraw.Add(p1);

        float dx = Mathf.Abs(p1.x - p2.x);
        float dz = Mathf.Abs(p1.z - p2.z);

        if (dx > 0.01f && dz > 0.01f)
        {
            Vector3 cornerPoint;
            if (dx > dz) cornerPoint = new Vector3(p2.x, p1.y, p1.z);
            else cornerPoint = new Vector3(p1.x, p1.y, p2.z);

            pointsToDraw.Add(cornerPoint);
        }

        pointsToDraw.Add(p2);

        if (currentTargetIndex + 1 < currentPathIDs.Count)
        {
            string futureID = currentPathIDs[currentTargetIndex + 1];
            pointsToDraw.Add(GetWorldPos(pathfinder.AllNodes.First(n => n.nodeID == futureID)));
        }

        // Draw the current step (anchored if possible)
        if (pathDrawer) pathDrawer.DrawAnchoredPath(pointsToDraw);
    }

    Vector3 GetWorldPos(Node node)
    {
        Vector3 mapPos = node.GetPosition() * mapScale;
        Vector3 worldPos = mapRoot.TransformPoint(mapPos);
        worldPos.y = mapRoot.position.y + 0.02f;
        return worldPos;
    }

    // Helper: convert path node IDs to world positions
    List<Vector3> ConvertPathIDsToWorldPoints(List<string> pathIDs)
    {
        List<Vector3> pts = new List<Vector3>();
        if (pathIDs == null) return pts;
        foreach (var id in pathIDs)
        {
            Node n = pathfinder.AllNodes.FirstOrDefault(x => x.nodeID == id);
            if (n != null) pts.Add(GetWorldPos(n));
        }
        return pts;
    }

    // --- 5. RESET FUNCTION ---
    public void ResetApp()
    {
        isNavigating = false;
        currentPathIDs = null;
        currentTargetIndex = 0;
        mapIsAligned = false;
        currentStartNodeID = "";

        if (pathDrawer != null) pathDrawer.ClearPath();
        if (instructionText != null) instructionText.text = "";
        if (statusText != null) statusText.text = "Reset complete. Scan a marker.";

        if (endDropdown != null) endDropdown.value = 0;
    }

    public void OnReScanButtonPressed()
    {
        ResetApp();  // existing logic

        if (statusText != null)
            statusText.text = "Please scan a nearby marker to re-align.";
    }
}
