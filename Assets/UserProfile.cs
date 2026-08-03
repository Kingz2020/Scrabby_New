using System;
using System.Collections.Generic;

[Serializable]
public class UserProfile
{
    public string email;
    public string displayName;

    public string avatarId;

    public long createdAt;
    public long lastSeenAt;

    public string presenceState;

    public List<string> activeRoomIds = new List<string>();
    public List<string> activeMatchIds = new List<string>();

    public UserProfile() { }

    public UserProfile(string email, string displayName, long timestamp)
    {
        this.email = email;
        this.displayName = displayName;
        this.avatarId = "";

        this.createdAt = timestamp;
        this.lastSeenAt = timestamp;

        this.presenceState = "online";

        this.activeRoomIds = new List<string>();
        this.activeMatchIds = new List<string>();
    }
}