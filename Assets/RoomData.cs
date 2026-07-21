using System;

[Serializable]
public class RoomData
{
    public string roomCode;
    public string type;
    public string status;
    public string createdByUid;
    public long createdAt;
    public long expiresAt;
    public string hostUid;
    public string guestUid;
    public int boardSize;
    public int rackSize;
    public int turnSeconds;
    public string dictionary;
    public string matchId;

    public RoomData() { }

    public RoomData(string roomCode, string hostUid, long now)
    {
        this.roomCode = roomCode;
        this.type = "private";
        this.status = "waiting";
        this.createdByUid = hostUid;
        this.createdAt = now;
        this.expiresAt = now + 3600000;
        this.hostUid = hostUid;
        this.guestUid = "";
        this.boardSize = 9;
        this.rackSize = 7;
        this.turnSeconds = 0;
        this.dictionary = "en_default";
        this.matchId = "";
    }
}