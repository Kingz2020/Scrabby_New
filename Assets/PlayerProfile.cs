using System;

[Serializable]
public class PlayerProfile
{
    public string PlayerId;
    public string DisplayName;
    public int Rating;
    public int GamesPlayed;
    public int Wins;
    public int Losses;
    public int Draws;
    public int TotalScore;
    public long CreatedAtUnix;
    public long UpdatedAtUnix;

    public PlayerProfile()
    {
        PlayerId = string.Empty;
        DisplayName = string.Empty;
        Rating = 1200;
        GamesPlayed = 0;
        Wins = 0;
        Losses = 0;
        Draws = 0;
        TotalScore = 0;
        CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        UpdatedAtUnix = CreatedAtUnix;
    }

    public void MarkUpdated()
    {
        UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}