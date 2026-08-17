using System;

[Serializable]
public class OnlineRoundHistory
{
    public int roundNumber;

    public string player1Word;
    public int player1Score;

    public string player2Word;
    public int player2Score;
}


[Serializable]
public class RoundScoreLine
{
    public int roundNumber;
    public int player1Score;
    public int player2Score;
}