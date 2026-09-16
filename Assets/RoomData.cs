using System;

[Serializable]
public class RoomData
{
    public string code;

    public string hostUid;
    public string hostDisplayName;

    public string guestUid;
    public string guestDisplayName;

    // Who was invited, before they have answered. The guest fields stay empty
    // until someone actually joins, so without these the host's own list could
    // only say "(waiting)" - not who for, and not which of two pending invites
    // is which.
    public string invitedUid;
    public string invitedDisplayName;

    public string status;      // waiting, full, in_game, finished

    public string matchId;

    public int playerCount;
    public int totalRounds;
    public int turnTimeMinutes;

    public long createdAtUnix;
}