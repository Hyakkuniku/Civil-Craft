using UnityEngine;

public partial class BarCreator
{
    // Preview and release resolve the same endpoint, including late node joins
    // and bar-interior snapping. Resolving never commits geometry or logs actions.
    private Vector3 ResolveDrawingEndpoint(Vector3 rawWorldPos, ref Point existingEndPoint,
        Vector3 exactSnapPos, out Vector3 preClampPos, out Bar barToSplit)
    {
        return ResolveDrawingEndpoint(rawWorldPos, ref existingEndPoint, exactSnapPos,
            out preClampPos, out barToSplit, out _);
    }

    private Vector3 ResolveDrawingEndpoint(Vector3 rawWorldPos, ref Point existingEndPoint,
        Vector3 exactSnapPos, out Vector3 preClampPos, out Bar barToSplit, out bool stoppedByTerrain)
    {
        Vector3 finalPosition = CalculateTargetPosition(rawWorldPos, existingEndPoint, exactSnapPos);
        Vector3 startPos = currentBar.StartPosition;

        BuildTutorialDirector tutorialDirector = BuildTutorialDirector.Instance;
        if (tutorialDirector != null && tutorialDirector.isTracingStep)
        {
            Vector3 snappedPosition;
            if (tutorialDirector.TryGetSnappedEndPosition(activeMaterial, startPos, finalPosition, out snappedPosition))
            {
                finalPosition = snappedPosition;
                if (existingEndPoint != null && Vector3.Distance(existingEndPoint.transform.position, finalPosition) > 0.01f)
                    existingEndPoint = null;
            }
        }

        if (activeMaterial != null && activeMaterial.isPier) finalPosition.x = currentStartPoint.transform.position.x;
        float limit = activeMaterial != null ? activeMaterial.maxLength : 5f;

        if (BuildUIController.Instance != null && activeMaterial != null)
        {
            float remainingBudget = BuildUIController.Instance.maxBudget - BuildUIController.Instance.GetTotalCost();
            float costPerMeter = activeMaterial.costPerMeter * (activeMaterial.isDualBeam ? 2 : 1);
            float maxAffordable = Mathf.Max(0f, remainingBudget / costPerMeter);
            if (maxAffordable < limit) limit = maxAffordable;
        }

        float distanceToTarget = Vector3.Distance(startPos, finalPosition);
        if (existingEndPoint != null && distanceToTarget > limit && distanceToTarget <= limit + 0.2f) limit = distanceToTarget;

        if (Vector3.Distance(startPos, finalPosition) > limit)
        {
            Vector3 direction = (finalPosition - startPos).normalized;
            finalPosition = startPos + (direction * limit);
            if (isGridSnappingEnabled && existingEndPoint == null)
            {
                finalPosition = SnapToGridFromOrigin(finalPosition, startPos);
                if (activeMaterial != null && activeMaterial.isPier) finalPosition.x = startPos.x;
                if (Vector3.Distance(startPos, finalPosition) > limit) finalPosition = startPos + (direction * limit);
            }
            existingEndPoint = null;
        }

        preClampPos = finalPosition;
        finalPosition = ClampToEnvironment(startPos, finalPosition);
        // A retained node reference must agree with the position we actually build to.
        if (existingEndPoint != null &&
            Vector3.Distance(existingEndPoint.transform.position, finalPosition) > 0.01f)
            existingEndPoint = null;
        stoppedByTerrain = Vector3.Distance(preClampPos, finalPosition) > 0.05f;

        if (existingEndPoint == null)
        {
            // Only join very close nodes after length/terrain constraints. The
            // general selection radius is deliberately not used while drawing.
            if (FindPointNearPosition(finalPosition, out Point secondCheckNode, out Vector3 secondCheckSnapPos,
                limit, .15f, true))
            {
                if (Vector3.Distance(startPos, secondCheckSnapPos) <= limit + 0.05f)
                {
                    existingEndPoint = secondCheckNode;
                    finalPosition = secondCheckSnapPos;
                }
            }
        }

        barToSplit = null;
        if (existingEndPoint == null && activeMaterial != null && !activeMaterial.isPier)
        {
            float minBarDist = 0.5f;
            foreach (Point p in Point.AllPoints)
            {
                if (!p.gameObject.activeSelf) continue;
                foreach (Bar b in p.ConnectedBars)
                {
                    if (b != null && b.gameObject.activeSelf && b != currentBar && !b.materialData.isPier && b.startPoint != null && b.endPoint != null)
                    {
                        Vector3 closestOnBar = GetClosestPointOnLineSegment(finalPosition, b.startPoint.transform.position, b.endPoint.transform.position);
                        float dist = Vector3.Distance(finalPosition, closestOnBar);
                        if (dist < minBarDist &&
                            Vector3.Distance(closestOnBar, b.startPoint.transform.position) > 0.2f &&
                            Vector3.Distance(closestOnBar, b.endPoint.transform.position) > 0.2f)
                        {
                            minBarDist = dist;
                            barToSplit = b;
                            finalPosition = closestOnBar;
                        }
                    }
                }
            }
        }

        return finalPosition;
    }
}
