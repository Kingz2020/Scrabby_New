using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

public class BoardDropDiagnostics : MonoBehaviour
{
    [SerializeField] private BoardGen boardGen;

    [ContextMenu("Run Board Drop Target Diagnostic")]
    public void RunBoardDropTargetDiagnostic()
    {
        if (EventSystem.current == null)
        {
            Debug.LogError(
                "[DROP DIAGNOSTIC] No EventSystem exists in the scene."
            );
            return;
        }

        if (boardGen == null)
            boardGen = FindAnyObjectByType<BoardGen>();

        if (boardGen == null)
        {
            Debug.LogError(
                "[DROP DIAGNOSTIC] Could not find BoardGen."
            );
            return;
        }

        GhostTile[] allGhostTiles = FindObjectsByType<GhostTile>(
            FindObjectsInactive.Exclude
        );

        Dictionary<string, GhostTile> tilesByCoordinate =
            new Dictionary<string, GhostTile>();

        foreach (GhostTile ghostTile in allGhostTiles)
        {
            if (ghostTile == null || ghostTile.letterPosition == null)
                continue;

            string key = Key(
                ghostTile.letterPosition.RowX,
                ghostTile.letterPosition.ColY
            );

            if (tilesByCoordinate.ContainsKey(key))
            {
                Debug.LogWarning(
                    "[DROP DIAGNOSTIC] Duplicate GhostTile at " +
                    key +
                    ": " +
                    ghostTile.name
                );
            }
            else
            {
                tilesByCoordinate.Add(key, ghostTile);
            }
        }

        int expectedCount = boardGen.RowX * boardGen.RowY;

        Debug.Log(
            "[DROP DIAGNOSTIC] START | expected=" +
            expectedCount +
            " | activeGhostTiles=" +
            allGhostTiles.Length +
            " | boardWidth=" +
            boardGen.RowX +
            " | boardHeight=" +
            boardGen.RowY
        );

        int missingCount = 0;
        int blockedCount = 0;
        int clearCount = 0;

        for (int y = 1; y <= boardGen.RowY; y++)
        {
            for (int x = 1; x <= boardGen.RowX; x++)
            {
                string key = Key(x, y);

                if (!tilesByCoordinate.TryGetValue(
                        key,
                        out GhostTile ghostTile) ||
                    ghostTile == null)
                {
                    missingCount++;

                    Debug.LogError(
                        "[DROP DIAGNOSTIC] MISSING GhostTile at " +
                        key
                    );

                    continue;
                }

                RectTransform rect =
                    ghostTile.transform as RectTransform;

                if (rect == null)
                {
                    Debug.LogError(
                        "[DROP DIAGNOSTIC] GhostTile has no RectTransform at " +
                        key +
                        " | object=" +
                        ghostTile.name
                    );

                    continue;
                }

                Vector2 screenPoint =
                    RectTransformUtility.WorldToScreenPoint(
                        null,
                        rect.TransformPoint(rect.rect.center)
                    );

                PointerEventData pointerData =
                    new PointerEventData(EventSystem.current)
                    {
                        position = screenPoint
                    };

                List<RaycastResult> results =
                    new List<RaycastResult>();

                EventSystem.current.RaycastAll(
                    pointerData,
                    results
                );

                int ghostIndex = -1;

                for (int i = 0; i < results.Count; i++)
                {
                    GameObject hitObject = results[i].gameObject;

                    if (hitObject == ghostTile.gameObject ||
                        (hitObject != null &&
                         hitObject.transform.IsChildOf(
                             ghostTile.transform)))
                    {
                        ghostIndex = i;
                        break;
                    }
                }

                if (ghostIndex == 0)
                {
                    clearCount++;
                    continue;
                }

                blockedCount++;

                StringBuilder hitList =
                    new StringBuilder();

                int maxResultsToLog =
                    Mathf.Min(results.Count, 5);

                for (int i = 0; i < maxResultsToLog; i++)
                {
                    if (i > 0)
                        hitList.Append(" > ");

                    GameObject hitObject =
                        results[i].gameObject;

                    hitList.Append(
                        hitObject != null
                            ? hitObject.name
                            : "NULL"
                    );
                }

                string state = ghostIndex < 0
                    ? "GHOST NOT HIT"
                    : "GHOST BEHIND index " + ghostIndex;

                Debug.LogWarning(
                    "[DROP DIAGNOSTIC] " +
                    state +
                    " at x=" + x +
                    " y=" + y +
                    " | ghost=" + ghostTile.name +
                    " | hits=" +
                    hitList
                );
            }
        }

        Debug.Log(
            "[DROP DIAGNOSTIC] END | clear=" +
            clearCount +
            " | blocked=" +
            blockedCount +
            " | missing=" +
            missingCount
        );
    }

    private string Key(int x, int y)
    {
        return x + "," + y;
    }
}