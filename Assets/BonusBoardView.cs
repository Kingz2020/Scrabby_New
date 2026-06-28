using UnityEngine;

public class BonusBoardView : MonoBehaviour
{
    public GameLogic gameLogic;

    public GameObject blankPrefab;
    public GameObject doubleLetterPrefab;
    public GameObject tripleLetterPrefab;
    public GameObject doubleWordPrefab;
    public GameObject tripleWordPrefab;

    public void DrawBonusTiles()
    {
        ClearBonusTiles();

        BonusTile[,] boardBonusTiles = gameLogic.GetBoardBonusTiles();
        GhostTile[] ghostTiles = FindObjectsByType<GhostTile>(FindObjectsInactive.Exclude);

        for (int y = 0; y < gameLogic.GetBoardSizeY(); y++)
        {
            for (int x = 0; x < gameLogic.GetBoardSizeX(); x++)
            {
                BonusTile bonusTile = boardBonusTiles[x, y];

                if (bonusTile == null)
                    continue;

                GhostTile matchingGhostTile = FindGhostTileByLocation(ghostTiles, y + 1, x + 1);

                if (matchingGhostTile == null)
                {
                    Debug.LogWarning("No GhostTile found for y=" + (y + 1) + " x=" + (x + 1));
                    continue;
                }

                GameObject prefabToSpawn = GetPrefabForBonusType(bonusTile.bonusType);

                if (prefabToSpawn == null)
                {
                    Debug.LogWarning("No prefab assigned for bonus type: " + bonusTile.bonusType);
                    continue;
                }

                GameObject newBonusTileObject = Instantiate(prefabToSpawn, matchingGhostTile.transform);
                newBonusTileObject.transform.localPosition = Vector3.zero;
                newBonusTileObject.transform.localRotation = Quaternion.identity;
                newBonusTileObject.transform.localScale = Vector3.one;
            }
        }
    }

    private GhostTile FindGhostTileByLocation(GhostTile[] ghostTiles, int y, int x)
    {
        foreach (GhostTile ghostTile in ghostTiles)
        {
            if (ghostTile.letterPosition.RowX == y && ghostTile.letterPosition.ColY == x)
            {
                return ghostTile;
            }
        }

        return null;
    }

    private GameObject GetPrefabForBonusType(BonusType bonusType)
    {
        switch (bonusType)
        {
            case BonusType.Blank:
                return blankPrefab;
            case BonusType.DoubleLetter:
                return doubleLetterPrefab;
            case BonusType.TripleLetter:
                return tripleLetterPrefab;
            case BonusType.DoubleWord:
                return doubleWordPrefab;
            case BonusType.TripleWord:
                return tripleWordPrefab;
            default:
                return null;
        }
    }

    public void ClearBonusTiles()
    {
        GhostTile[] ghostTiles = FindObjectsByType<GhostTile>(FindObjectsInactive.Exclude);

        foreach (GhostTile ghostTile in ghostTiles)
        {
            for (int i = ghostTile.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = ghostTile.transform.GetChild(i);

                if (child.GetComponent<BonusVisualMarker>() != null)
                {
                    Destroy(child.gameObject);
                }
            }
        }
    }
}