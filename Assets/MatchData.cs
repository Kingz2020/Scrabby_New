using System;
using System.Collections.Generic;

[Serializable]
public class MatchData
{
    public string status;
    public long createdAt;
    public long startedAt;
    public long finishedAt;
    public long updatedAt;
    public string roomCode;
    public string hostUid;
    public string guestUid;
    public string currentTurnUid;
    public int turnNumber;
    public string winnerUid;
    public string endReason;
    public int boardSize;
    public int rackSize;
    public string dictionary;
    public int turnSeconds;

    public MatchData() { }

    public MatchData(string roomCode, string hostUid, string guestUid, long now)
    {
        this.status = "active";
        this.createdAt = now;
        this.startedAt = now;
        this.finishedAt = 0;
        this.updatedAt = now;
        this.roomCode = roomCode;
        this.hostUid = hostUid;
        this.guestUid = guestUid;
        this.currentTurnUid = hostUid;
        this.turnNumber = 1;
        this.winnerUid = "";
        this.endReason = "";
        this.boardSize = 9;
        this.rackSize = 7;
        this.dictionary = "en_default";
        this.turnSeconds = 0;
    }
}