using UnityEngine;
using System.Collections.Generic;

public class HistoryAction
{
    public bool isBuildEvent; 
    public bool isMoveEvent = false; 
    public bool isMergeEvent = false; // --- NEW: Tracks Node Merges ---

    public List<GameObject> affectedObjects = new List<GameObject>();
    public Dictionary<Point, Vector3> originalPositions = new Dictionary<Point, Vector3>();
    public Dictionary<Point, Vector3> newPositions = new Dictionary<Point, Vector3>();

    // --- NEW: For tracking bar reconnections during a merge ---
    public Dictionary<Bar, Point> originalStartPoints = new Dictionary<Bar, Point>();
    public Dictionary<Bar, Point> originalEndPoints = new Dictionary<Bar, Point>();
    public Dictionary<Bar, Point> mergedStartPoints = new Dictionary<Bar, Point>();
    public Dictionary<Bar, Point> mergedEndPoints = new Dictionary<Bar, Point>();

    public void Undo()
    {
        if (isMergeEvent)
        {
            // 1. Restore the original points for all transferred bars
            foreach (var kvp in originalStartPoints) if (kvp.Key != null) { kvp.Key.startPoint = kvp.Value; if (!kvp.Value.ConnectedBars.Contains(kvp.Key)) kvp.Value.ConnectedBars.Add(kvp.Key); }
            foreach (var kvp in originalEndPoints) if (kvp.Key != null) { kvp.Key.endPoint = kvp.Value; if (!kvp.Value.ConnectedBars.Contains(kvp.Key)) kvp.Value.ConnectedBars.Add(kvp.Key); }

            // 2. Remove the bars from the target point they were merged into
            foreach (var kvp in mergedStartPoints) if (kvp.Key != null && kvp.Value != null) kvp.Value.ConnectedBars.Remove(kvp.Key);
            foreach (var kvp in mergedEndPoints) if (kvp.Key != null && kvp.Value != null) kvp.Value.ConnectedBars.Remove(kvp.Key);

            // 3. Reactivate the deleted node and teleport it back to where it started before the drag!
            foreach (GameObject obj in affectedObjects) if (obj != null) obj.SetActive(true);
            foreach (var kvp in originalPositions) if (kvp.Key != null) kvp.Key.transform.position = kvp.Value;
        }
        else if (isMoveEvent)
        {
            foreach (var kvp in originalPositions) if (kvp.Key != null) kvp.Key.transform.position = kvp.Value;
        }
        else
        {
            for (int i = affectedObjects.Count - 1; i >= 0; i--)
                if (affectedObjects[i] != null) affectedObjects[i].SetActive(!isBuildEvent);
        }
    }

    public void Redo()
    {
        if (isMergeEvent)
        {
            // 1. Re-apply the merge transfers
            foreach (var kvp in mergedStartPoints) if (kvp.Key != null) { kvp.Key.startPoint = kvp.Value; if (!kvp.Value.ConnectedBars.Contains(kvp.Key)) kvp.Value.ConnectedBars.Add(kvp.Key); }
            foreach (var kvp in mergedEndPoints) if (kvp.Key != null) { kvp.Key.endPoint = kvp.Value; if (!kvp.Value.ConnectedBars.Contains(kvp.Key)) kvp.Value.ConnectedBars.Add(kvp.Key); }

            foreach (var kvp in originalStartPoints) if (kvp.Key != null && kvp.Value != null) kvp.Value.ConnectedBars.Remove(kvp.Key);
            foreach (var kvp in originalEndPoints) if (kvp.Key != null && kvp.Value != null) kvp.Value.ConnectedBars.Remove(kvp.Key);

            // 2. Move the node to the target position and deactivate it
            foreach (var kvp in newPositions) if (kvp.Key != null) kvp.Key.transform.position = kvp.Value;
            foreach (GameObject obj in affectedObjects) if (obj != null) obj.SetActive(false);
        }
        else if (isMoveEvent)
        {
            foreach (var kvp in newPositions) if (kvp.Key != null) kvp.Key.transform.position = kvp.Value;
        }
        else
        {
            for (int i = 0; i < affectedObjects.Count; i++)
                if (affectedObjects[i] != null) affectedObjects[i].SetActive(isBuildEvent);
        }
    }
}

public class CommandManager : MonoBehaviour
{
    public static CommandManager Instance { get; private set; }

    private Stack<HistoryAction> undoStack = new Stack<HistoryAction>();
    private Stack<HistoryAction> redoStack = new Stack<HistoryAction>();

    private void Awake() 
    { 
        Instance = this; 
    }

    public void RecordAction(HistoryAction action)
    {
        undoStack.Push(action);
        foreach (var redoAction in redoStack) 
        {
            if (redoAction.isBuildEvent && !redoAction.isMoveEvent && !redoAction.isMergeEvent) 
            {
                foreach(var obj in redoAction.affectedObjects) 
                {
                    if (obj != null) Destroy(obj);
                }
            }
        }
        redoStack.Clear();
    }

    public void Undo()
    {
        BarCreator barCreator = FindObjectOfType<BarCreator>();
        if (barCreator != null && (barCreator.isSimulating || undoStack.Count == 0 || (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building))) return;
        
        if (barCreator != null) barCreator.CancelCreation(); 
        
        HistoryAction action = undoStack.Pop();
        action.Undo();
        redoStack.Push(action);
        
        RefreshAllPoints(); 
        
        foreach (var bar in FindObjectsOfType<Bar>(true)) 
        {
            if (bar.gameObject.activeSelf && bar.startPoint != null && bar.endPoint != null) 
            { 
                bar.StartPosition = bar.startPoint.transform.position; 
                bar.UpdateCreatingBar(bar.endPoint.transform.position); 
            } 
        }
        
        if (BuildUIController.Instance != null) 
        {
            BuildUIController.Instance.MarkBridgeDirty();
            BuildUIController.Instance.LogAction("Undid Last Action");
        }
    }

    public void Redo()
    {
        BarCreator barCreator = FindObjectOfType<BarCreator>();
        if (barCreator != null && (barCreator.isSimulating || redoStack.Count == 0 || (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.Building))) return;
        
        if (barCreator != null) barCreator.CancelCreation(); 
        
        HistoryAction action = redoStack.Pop();
        action.Redo();
        undoStack.Push(action);
        
        RefreshAllPoints(); 
        
        foreach (var bar in FindObjectsOfType<Bar>(true)) 
        {
            if (bar.gameObject.activeSelf && bar.startPoint != null && bar.endPoint != null) 
            { 
                bar.StartPosition = bar.startPoint.transform.position; 
                bar.UpdateCreatingBar(bar.endPoint.transform.position); 
            } 
        }
        
        if (BuildUIController.Instance != null) 
        {
            BuildUIController.Instance.MarkBridgeDirty();
            BuildUIController.Instance.LogAction("Redid Last Action");
        }
    }

    private void RefreshAllPoints() 
    { 
        foreach (Point p in Point.AllPoints) 
            if (p != null && p.gameObject.activeSelf) p.EvaluateAnchorState(); 
    }
}