using TMPro;
using UnityEngine;

public class WorldlistTitleHolder: MonoBehaviour {
    
    public TextMeshProUGUI headerText;
    public int currentWordCount;
    public int maxWordCount;

    public void ResetAll() {
        currentWordCount = 0;
        maxWordCount = 0;
        headerText.SetText("Total words (0/0)");
    }

    public void SetMaxWords(int maxWords) {
        maxWordCount = maxWords;
        headerText.SetText($"Total words ({currentWordCount}/{maxWordCount})");
    }

    public void WordFound() {
        currentWordCount++;
        headerText.SetText($"Total words ({currentWordCount}/{maxWordCount})");
    }
    
}
