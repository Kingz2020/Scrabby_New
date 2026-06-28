using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public class DebugManager: MonoBehaviour {

    public string json;
    public LetterBag letterBag;
    [SerializeField] private TMP_InputField lettersInput;
    [SerializeField] private TMP_InputField fixedletters;
    
    public void LoadFromJson() {
        letterBag = Singleton.Instance.GameLogic.GetTileBag().FromJsonToClass(json);
    }

    public void SaveAsJson() {
        json = Singleton.Instance.GameLogic.GetTileBag().SaveAsJson(letterBag);
    }

    public void StartNewGame()
    {
        Singleton.Instance.GameLogic.GetTileBag().ResetLetterBag(letterBag);
        Singleton.Instance.GameLogic.InitGame(6, 9, 9);
    }

    public void RefillHand() {

        Singleton.Instance.GameLogic.RefillPlayerHand();
        //Singleton.Instance.GameLogic.SaveCurrentRoundSnapshot();
        Singleton.Instance.GameLogic.RefreshRoundSnapshot();
    }

    public void ReturnTilesToHand() {
        Singleton.Instance.UIManager.ReturnTilesToHand();
    }

    public void EndTurn() {
        Singleton.Instance.GameLogic.EndTurn();
    }

    public void CheckSameLine() {
        Debug.Log(Singleton.Instance.GameLogic.AllTilesInSameLine());
    }

    public void CheckHasHoles() {
        Debug.Log(Singleton.Instance.GameLogic.HasHoles(Singleton.Instance.GameLogic.AllTilesInSameLine()));
    }

    public void CheckConnectedToOldTiles() {
        Debug.Log(Singleton.Instance.GameLogic.CheckConnectedToTiles());
    }

    public void CheckValidMove() {
        Debug.Log(Singleton.Instance.GameLogic.ValidMove());
    }

    public void CollectAllWords() {
        foreach (var wordList in Singleton.Instance.GameLogic.CollectAllWords(Singleton.Instance.GameLogic.AllTilesInSameLine())) {
            string completed = String.Empty;
            foreach (var word in wordList) {
                completed += word.letter;
            }
            Debug.Log(completed);
        }
    }

    public void ResetDisplayWords() {
        Singleton.Instance.GameLogic.ResetDisplay();
    }

    public void CheckWordValidity() {
        Debug.Log(Singleton.Instance.GameLogic.CheckWordValidity(
            Singleton.Instance.GameLogic.CollectAllWords(Singleton.Instance.GameLogic.AllTilesInSameLine())));
    }

    public void CountPointsForWord() {
        foreach (var wordList in Singleton.Instance.GameLogic.CollectAllWords(Singleton.Instance.GameLogic.AllTilesInSameLine())) {
            Debug.Log(Singleton.Instance.GameLogic.CountWordPoints(wordList));
        }
    }

    public void PrintPossibleWords() {
        string[] input = fixedletters.text.Split(',');
        List<string> letterList = lettersInput.text.ToCharArray().Select(c => c.ToString()).ToList();
        letterList.Sort();
        List<WordLetterPos> fixedLetterList = new List<WordLetterPos>();
        List<string> wordList;
        if (input.Length > 1) {
            for (int index = 0; index < input.Length; index = index + 2) {
                fixedLetterList.Add(new WordLetterPos(input[index], Int32.Parse(input[index + 1])));
            }
            wordList = Singleton.Instance.WordLookupLogic.FindWords(letterList, fixedLetterList);
        }
        else {
            wordList = Singleton.Instance.WordLookupLogic.FindWords(letterList);
        }
        Debug.LogFormat("Found {0} words", wordList.Count);
        Debug.Log(wordList.ToString());
        Debug.Log(string.Join(", ", wordList));        
    }
    
}
