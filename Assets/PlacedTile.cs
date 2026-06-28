using UnityEngine;

public class PlacedTile: MonoBehaviour {

    public LetterInfo letterInfo;
    public LetterPosition letterPosition;
    
    public override string ToString() {
        return $"{base.ToString()}, {nameof(letterInfo)}: {letterInfo}, {nameof(letterPosition)}: {letterPosition}";
    }
}
