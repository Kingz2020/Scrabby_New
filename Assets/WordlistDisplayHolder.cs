using System;
using TMPro;
using UnityEngine;

public class WordlistDisplayHolder: MonoBehaviour {

    public int maxWordLength;
    public int currentWordCount;
    public int maxWordCount;

    public TextMeshProUGUI headerText;
    public TextMeshProUGUI wordTest;

    public string wordList;

    public void ResetAll() {
        currentWordCount = 0;
        headerText.enabled = true;
        headerText.SetText(string.Format("{0}-Letter Words 0/ ", maxWordLength));
        wordList = "";
        wordTest.enabled = true;
        wordTest.SetText(wordList);
        maxWordCount = 0;
    }

    public void SetMaxWords(int max) {
        maxWordCount = max;
        headerText.SetText(string.Format("{0}-Letter Words 0/ {1}", maxWordLength, maxWordCount));
    }
    
    public void SetMaxWords(string word) {
        if (word.Length != maxWordLength) return;
        maxWordCount++;
        headerText.SetText($"{maxWordLength}-Letter Words 0/ {maxWordCount}");
    }

    public void AddWord(string word) {
        if (word.Length != maxWordLength) return;
        if (wordList.Contains(word)) return;
        wordList = wordList.Length < 1 ? word : wordList += " " + word;
        currentWordCount++;
        headerText.SetText(string.Format("{0}-Letter Words {1}/ {2}", maxWordLength, currentWordCount, maxWordCount));
        Singleton.Instance.UIManager.worldlistTitleHolder.WordFound();
        wordTest.SetText(wordList);
    }
    
    public void AddMissingWord(string word) {
        if (word.Length != maxWordLength) return;
        if (wordList.Contains(word)) return;
        wordList = wordList.Length < 1 ? "<color=black>" + word  + "</color=black>": wordList += "<color=black> " + word +"</color=black>";
        headerText.SetText(string.Format("{0}-Letter Words {1}/ {2}", maxWordLength, currentWordCount, maxWordCount));
        wordTest.SetText(wordList);
    }

    public void DisableUnused() {
        if (maxWordCount >= 1) return;
        headerText.enabled = false;
        wordTest.enabled = false;
    }
}
