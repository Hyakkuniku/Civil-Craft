using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class BarCreator : MonoBehaviour, IPointerDownHandler
{
    bool barCreationStarted = false;

    public Bar currentBar;
    public GameObject barToInstantiate;
    public Transform barParent;

    public Point currentStartPoint;
    public Point currentEndPoint;
    public GameObject pointToInstantiate;
    public Transform pointParent;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!barCreationStarted)
        {
            barCreationStarted = true;

            Vector2 startPos = Vector2Int.RoundToInt(
                Camera.main.ScreenToWorldPoint(eventData.position)
            );

            StartBarCreation(startPos);
        }
        else
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                FinishBarCreation();
            }
            else if (eventData.button == PointerEventData.InputButton.Right)
            {
                barCreationStarted = false;
                DeleteCurrentBar();
            }
        }
    }

    void StartBarCreation(Vector2 startPosition)
    {
        GameObject newBar = Instantiate(barToInstantiate, barParent);
        currentBar = newBar.GetComponent<Bar>();
        currentBar.StartPosition = startPosition;

        currentStartPoint = Instantiate(
            pointToInstantiate,
            startPosition,
            Quaternion.identity,
            pointParent
        ).GetComponent<Point>();

        currentEndPoint = Instantiate(
            pointToInstantiate,
            startPosition,
            Quaternion.identity,
            pointParent
        ).GetComponent<Point>();
    }

    void FinishBarCreation()
    {
        currentStartPoint.ConnectedBars.Add(currentBar);
        currentEndPoint.ConnectedBars.Add(currentBar);

        Vector2 nextStart = currentEndPoint.transform.position;

        StartBarCreation(nextStart);
    }

    void DeleteCurrentBar()
    {
        Destroy(currentBar.gameObject);

        if (currentStartPoint.ConnectedBars.Count == 0 && currentStartPoint.Runtime)
            Destroy(currentStartPoint.gameObject);

        if (currentEndPoint.ConnectedBars.Count == 0 && currentEndPoint.Runtime)
            Destroy(currentEndPoint.gameObject);
    }

    private void Update()
    {
        if (barCreationStarted)
        {
            Vector2 mousePos = Vector2Int.RoundToInt(
                Camera.main.ScreenToWorldPoint(Input.mousePosition)
            );

            currentEndPoint.transform.position = mousePos;
            currentBar.UpdateCreatingBar(currentEndPoint.transform.position);

        }
    }
}