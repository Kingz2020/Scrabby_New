using System;
using System.Collections.Generic;

[Serializable]
public class StoredRoundResult
{
    public int RoundNumber;
    public string WinningPlayerId;
    public string SubmittedWord;
    public int Score;
    public float TimeUsedSeconds;
    public bool WasTie;
    public bool WasValidWord;
    public List<string> SharedRackLetters;
    public long CompletedAtUnix;

    public StoredRoundResult()
    {
        RoundNumber = 0;
        WinningPlayerId = string.Empty;
        SubmittedWord = string.Empty;
        Score = 0;
        TimeUsedSeconds = 0f;
        WasTie = false;
        WasValidWord = false;
        SharedRackLetters = new List<string>();
        CompletedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
