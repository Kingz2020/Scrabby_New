using System;

[Serializable]
public class MatchListItemData
{
    public string roomCode;

    public string matchId;

    public string opponentUid;
    public string opponentDisplayName;

    public string status;

    public int currentRound;
    public int totalRounds;

    public int myScore;
    public int opponentScore;

    public bool isRoom;

    public bool hasSubmittedThisRound;

    //public string fromDisplayName; // reuse opponentDisplayName instead if you prefer — see note below
    public bool isInvite;
}