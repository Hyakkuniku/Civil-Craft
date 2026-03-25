using UnityEngine;
using System.Collections.Generic;

public class HistoryAction
{
    public bool isBuildEvent; 
    public List<GameObject> affectedObjects = new List<GameObject>();
    public Dictionary<Point, Vector3> originalPositions = new Dictionary<Point, Vector3>();
    public Dictionary<Point, Vector3> newPositions = new Dictionary<Point, Vector3>();
    public bool isMoveEvent = false; 

    public void Undo()
    {
        if (isMoveEvent)
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
        if (isMoveEvent)
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
            if (redoAction.isBuildEvent && !redoAction.isMoveEvent) 
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
        
        // --- BUG FIX: ALWAYS refresh active bars on ANY Undo (Move, Build, Delete, Paste) ---
        foreach (var bar in FindObjectsOfType<Bar>()) 
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
        
        // --- BUG FIX: ALWAYS refresh active bars on ANY Redo ---
        foreach (var bar in FindObjectsOfType<Bar>()) 
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