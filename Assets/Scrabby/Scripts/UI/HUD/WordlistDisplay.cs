using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class WordlistDisplay : MonoBehaviour {

    public List<WordlistDisplayHolder> headerLines;

    public void ResetList(List<string> letters) {
        foreach (var holder in headerLines) {
            holder.ResetAll();
        }

        List<string> allWords = Singleton.Instance.WordLookupLogic.FindWords(letters);
        foreach (var word in allWords) {
            foreach (var holder in headerLines) {
                holder.SetMaxWords(word);
            }
        }
        
        Singleton.Instance.UIManager.worldlistTitleHolder.SetMaxWords(allWords.Count);

        foreach (var holder in headerLines) {
            holder.DisableUnused();
        }
         
    }

    public void AddMissingWord(string word) {
        foreach (var holder in headerLines) {
            holder.AddMissingWord(word);
        }
    }

    public void AddWord(string word) {
        foreach (var holder in headerLines) {
            holder.AddWord(word);
        }
    }
}
