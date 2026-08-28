using UnityEngine;

// Coordinate convention used everywhere:
// x = horizontal board coordinate / column, 1-based for LetterPosition.
// y = vertical board coordinate / row, 1-based for LetterPosition.
// LetterPosition.RowX = x; LetterPosition.ColY = y.
// Board arrays use [x, y].
// Bonus arrays are 0-based: boardBonusTiles[x - 1, y - 1].

public class BoardGen: MonoBehaviour {

    public GameObject GhostGO;
    public int RowX;
    public int RowY;
    
    public void Start() {
        for (int y = 1; y <= RowY; y++) {
            for (int x = 1; x <= RowX; x++) {
                GameObject goTemp = Instantiate(GhostGO, transform);
                goTemp.GetComponent<GhostTile>().SetLocation(x, y);
            }
        }
    }
}
;