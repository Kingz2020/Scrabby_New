using System;

[Serializable]
public class RoomData
{
    public string code;

    public string hostUid;
    public string hostDisplayName;

    public string guestUid;
    public string guestDisplayName;

    public string status;      // waiting, full, in_game, finished

    public string matchId;

    public long createdAtUnix;
}