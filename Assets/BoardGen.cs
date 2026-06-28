using UnityEngine;

public class BoardGen: MonoBehaviour {

    public GameObject GhostGO;
    public int RowX;
    public int RowY;
    
    public void Start() {
        for (int y = 1; y <= RowY; y++) {
            for (int x = 1; x <= RowX; x++) {
                GameObject goTemp = Instantiate(GhostGO, transform);
                goTemp.GetComponent<GhostTile>().SetLocation(y, x);
            }
        }
    }
}
;