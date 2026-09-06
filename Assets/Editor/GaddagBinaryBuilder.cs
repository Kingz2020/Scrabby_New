//#if UNITY_EDITOR
/*using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class GaddagBinaryBuilder
{
    private const string OutputFolder = "Assets/StreamingAssets";
    private const string OutputFile = "gaddag.bin";

    [MenuItem("Scrabby/GADDAG/Build Binary From Selected Dictionary")]
    public static void BuildBinaryFromSelectedDictionary()
    {
        TextAsset dictionaryAsset = Selection.activeObject as TextAsset;

        if (dictionaryAsset == null)
        {
            EditorUtility.DisplayDialog(
                "No dictionary selected",
                "Select your dictionary TextAsset in the Project window, " +
                "then run Scrabby > GADDAG > Build Binary From Selected Dictionary.",
                "OK");

            return;
        }

        string[] rawWords = dictionaryAsset.text.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);

        GaddagNode.ResetCounters();

        Stopwatch timer = Stopwatch.StartNew();

        GaddagLexicon lexicon = new GaddagLexicon();
        int addedWords = 0;

        for (int i = 0; i < rawWords.Length; i++)
        {
            string word = rawWords[i];

            if (string.IsNullOrWhiteSpace(word))
                continue;

            lexicon.AddWord(word);
            addedWords++;
        }

        Directory.CreateDirectory(OutputFolder);

        string outputPath = Path.Combine(OutputFolder, OutputFile);
        lexicon.SaveToBinary(outputPath);

        timer.Stop();

        AssetDatabase.Refresh();

        long byteCount = new FileInfo(outputPath).Length;

        UnityEngine.Debug.Log(
            $"[GADDAG] Binary build complete | " +
            $"dictionary={dictionaryAsset.name} | " +
            $"sourceWords={rawWords.Length:N0} | " +
            $"addedWords={addedWords:N0} | " +
            $"nodes={GaddagNode.CreatedCount:N0} | " +
            $"fileBytes={byteCount:N0} | " +
            $"dt={timer.Elapsed.TotalMilliseconds:F2}ms | " +
            $"path={outputPath}");
    }
}*/
//#endif