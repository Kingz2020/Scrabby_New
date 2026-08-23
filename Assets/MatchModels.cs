using System;
using System.Collections.Generic;

[Serializable]
public class TileData
{
    public string letter;
    public int value;
    public string id;


}

[Serializable]
public class BagStateData
{
    public List<TileData> tiles = new List<TileData>();
}

[Serializable]
public class RackStateData
{
    public List<TileData> tiles = new List<TileData>();
}

[Serializable]
public class BoardCellData
{
    public int x;
    public int y;
    public bool occupied;
    public TileData tile;
}

[Serializable]
public class BoardStateData
{
    public int width;
    public int height;
    public List<BoardCellData> cells = new List<BoardCellData>();
}

// One player's score entry — JsonUtility can't serialize Dictionary, so scores are a list of these
[Serializable]
public class PlayerScoreData
{
    public string uid;
    public string displayName;
    public int score;
}
[Serializable]
public class MatchData
{
    public string matchId;
    public string roomCode;

    public string hostUid;
    public string guestUid;

    public string player1Uid;
    public string player2Uid;
    public string player1DisplayName;
    public string player2DisplayName;

    public int player1Score;
    public int player2Score;

    public string status; // "active" | "finished"

    public int currentRoundNumber;

    public string boardStateJson;
    public string bagStateJson;
    public string sharedrackjson;

    public string lastRoundResultJson;
    public string roundResolutionStatus; // "idle" | "resolving" | "done"
    public string roundResolutionByUid;

    public long createdAtUnix;

    public string setupStatus;
    public string setupByUid;
    public long setupAtUnix;

    public int stateVersion;

    public string bonusBoardJson;

    public List<RoundScoreLine> roundScores = new List<RoundScoreLine>();

    public List<OnlineRoundHistoryEntry> roundHistory = new List<OnlineRoundHistoryEntry>();

    public int totalRounds;
}

// One player's submission for a given round
[Serializable]
public class SubmissionData
{
    public string uid;
    public string word;
    public int score;
    public bool isValid;
    public string placedTilesJson;     // serialized List<SimPlacedTileData> — see below
    public long submittedAt;
}


[Serializable]
public class RoundSubmissionData
{
    public string uid;
    public string word;
    public int score;
    public bool isValid;
    public string simulatedTilesJson;
    public long submittedAtUnix;
    public int secondsRemaining;
}

[Serializable]
public class RoundResultData
{
    public int roundNumber;
    public string winnerUid;
    public string winnerDisplayName;
    public string winnerWord;
    public int winnerScore;
    public bool anyValidMove;

    public string winningTilesJson;
}

[Serializable]
public class SimPlacedTileData
{
    public string letter;
    public int points;
    public int row;
    public int col;
}

[Serializable]
public class SimTileListWrapper
{
    public List<SimPlacedTileData> tiles = new List<SimPlacedTileData>();
}

[Serializable]
public class BonusCellData
{
    public int x;
    public int y;
    public string bonusType; // "DoubleLetter" | "TripleLetter" | "DoubleWord" | "TripleWord"
}

[Serializable]
public class BonusBoardData
{
    public List<BonusCellData> cells = new List<BonusCellData>();
}

[Serializable]
public class OnlineRoundHistoryEntry
{
    public int roundNumber;

    // Exact board and bonus state before either player’s move is applied.
    public string preRoundBoardStateJson;
    public string roundBonusBoardJson;

    // Player 1 submission.
    public string player1Word;
    public int player1Score;
    public bool player1Valid;
    public string player1SimulatedTilesJson;

    // Player 2 submission.
    public string player2Word;
    public int player2Score;
    public bool player2Valid;
    public string player2SimulatedTilesJson;

    // Resolution.
    public string winnerUid;
    public string winnerWord;
    public int winnerScore;
    public bool anyValidMove;

    public bool winnerIsPlayer1;

}