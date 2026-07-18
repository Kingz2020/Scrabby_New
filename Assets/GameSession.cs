using System;
using System.Collections.Generic;

[Serializable]
public class GameSession
{
    public string GameId;
    public string HostPlayerId;
    public List<string> PlayerIds;
    public string Status;
    public int RoundNumber;
    public string CurrentTurnPlayerId;
    public string WinnerPlayerId;
    public int MaxPlayers;
    public long CreatedAtUnix;
    public long UpdatedAtUnix;
    public List<RoundResult> RoundResults;

    public GameSession()
    {
        GameId = string.Empty;
        HostPlayerId = string.Empty;
        PlayerIds = new List<string>();
        Status = "Waiting";
        RoundNumber = 0;
        CurrentTurnPlayerId = string.Empty;
        WinnerPlayerId = string.Empty;
        MaxPlayers = 2;
        CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        UpdatedAtUnix = CreatedAtUnix;
        RoundResults = new List<RoundResult>();
    }

    public void MarkUpdated()
    {
        UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}