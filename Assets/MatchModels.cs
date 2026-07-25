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

    public int turnNumber;
    public string currentTurnUid;
    public string status;

    public string boardStateJson;
    public string bagStateJson;
    public string player1RackJson;
    public string player2RackJson;

    public long createdAtUnix;

    public string setupStatus;           // pending, done
    public string setupByUid;
    public long setupAtUnix;

    public string pendingMoveJson;       // empty when no move pending
    public string pendingMoveByUid;
    public int pendingMoveTurnNumber;    // turn being resolved
    public string turnResolutionStatus;  // idle, resolving
    public string turnResolutionByUid;
    public long turnDeadlineUnix;

    public int stateVersion;
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
public class SimPlacedTileData
{
    public string letter;
    public int points;
    public int row;
    public int col;
}

