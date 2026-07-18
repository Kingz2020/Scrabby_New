using System.Collections.Generic;
using UnityEngine;

public class TileBag: MonoBehaviour {
 
    private List<LetterInfo> letters = new List<LetterInfo>();
    
    public List<LetterInfo> GetLetters() {
        return letters;
    }

    public void ResetLetterBag(LetterBag letterBag) {
        letters.Clear();
        foreach (LetterDistribution letterDistribution in letterBag.letters) {
            for (int amount = 0; amount < letterDistribution.amount; amount++) {
                letters.Add(new LetterInfo(letterDistribution.letterInfo.letter, letterDistribution.letterInfo.points));
            }
        }
    }

    public LetterInfo DrawLetterTileFromBag() {
        int index = Random.Range(0, letters.Count);
        LetterInfo returnTile = letters[index];
        letters.Remove(returnTile);
        return returnTile;
    }
    
    public string SaveAsJson(LetterBag letterBag) {
        return JsonUtility.ToJson(letterBag);
    }

    
    public LetterBag FromJsonToClass(string jsonString) { 
        return JsonUtility.FromJson<LetterBag>(jsonString);
    }
   

}