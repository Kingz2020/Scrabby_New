using System;

[Serializable]
public class UserProfile
{
    public string email;
    public string displayName;
    public long createdAt;
    public long lastSeenAt;
    public string currentRoomId;
    public string currentMatchId;
    public string presenceState;

    public UserProfile() { }

    public UserProfile(string email, string displayName, long timestamp)
    {
        this.email = email;
        this.displayName = displayName;
        this.createdAt = timestamp;
        this.lastSeenAt = timestamp;
        this.currentRoomId = "";
        this.currentMatchId = "";
        this.presenceState = "online";
    }
}