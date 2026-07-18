using System;
using System.Collections.Generic;
using UnityEngine;

public class WordLookupLogic: MonoBehaviour {
    
    [SerializeField] private TextAsset scrabbleWordsList;
    Dictionary<string, WordLookup> wordLetterList = new Dictionary<string, WordLookup>();

    private void Start() {
        InitWords();
    }

    public void InitWords() {
        foreach (var word in scrabbleWordsList.text.Split(new[] { "\r\n", "\r", "\n" },
            StringSplitOptions.None)) {
            AddWord(word);
        }
    }
    
    public void AddWord(string word) {
        Dictionary<string, WordLookup> tempList = wordLetterList;
        for (int index = 0; index < word.Length; index++) {
            string letter = word[index].ToString();
            if (!tempList.ContainsKey(letter)) {
                tempList[letter] = new WordLookup();
                if (index + 1 == word.Length) tempList[letter].validWord = true;
            }
            tempList = tempList[letter].wordLetterList;
        }
    }

    public List<string> FindWords(List<string> letters) {
        return FindWords(wordLetterList, letters, new List<string>(), "", 1, null, 1);
    }
    
    public List<string> FindWords(List<string> letters, List<WordLetterPos> fixedLetters) {
        letters.Sort();
        int minLength = fixedLetters[fixedLetters.Count - 1].position == fixedLetters.Count
            ? fixedLetters.Count + 1
            : fixedLetters[fixedLetters.Count - 1].position; 
        return FindWords(wordLetterList, letters, new List<string>(), "", 1, fixedLetters, minLength);
    }

    private List<string> FindWords(Dictionary<string, WordLookup> wordLookup, List<string> letters, List<string> words, string currentWord, int currentIndex, List<WordLetterPos> fixedLetters, int minWordLength)
    {
        if (fixedLetters != null && fixedLetters.Count >= 1 && fixedLetters[0].position == currentIndex)
        {
            if (!words.Contains(currentWord + fixedLetters[0].letter) &&
                wordLookup.ContainsKey(fixedLetters[0].letter))
            {
                if (wordLookup[fixedLetters[0].letter].validWord && currentWord.Length + 1 >= minWordLength)
                    words.Add(currentWord + fixedLetters[0].letter);

                List<WordLetterPos> tempList = new List<WordLetterPos>();
                for (int index = 1; index < fixedLetters.Count; index++)
                    tempList.Add(new WordLetterPos(fixedLetters[index].letter, fixedLetters[index].position));

                FindWords(
                    wordLookup[fixedLetters[0].letter].wordLetterList,
                    letters,
                    words,
                    currentWord + fixedLetters[0].letter,
                    currentIndex + 1,
                    tempList,
                    minWordLength
                );
            }
            else
            {
                return words;
            }
        }
        else
        {
            for (int index = 0; index < letters.Count; index++)
            {
                if (index > 0 && letters[index].Equals(letters[index - 1])) continue;

                string letter = letters[index];
                if (wordLookup.ContainsKey(letter))
                {
                    if (wordLookup[letter].validWord && currentWord.Length + 1 >= minWordLength)
                        if (!words.Contains(currentWord + letter))
                            words.Add(currentWord + letter);

                    List<string> copy = new List<string>(letters);
                    copy.Remove(letter);

                    FindWords(
                        wordLookup[letter].wordLetterList,
                        copy,
                        words,
                        currentWord + letter,
                        currentIndex + 1,
                        fixedLetters,
                        minWordLength
                    );
                }
            }
        }

        return words;
    }
}
