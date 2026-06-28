using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using Random = UnityEngine.Random;

public class GameLogic : MonoBehaviour
{
    private int maxHandSize;
    private int boardSizeX;
    private int boardSizeY;
    private int currentTurn;

    private LetterInfo[,] validatedBoardTiles;
    private List<LetterInfo> playerHandTiles;
    private RoundSnapshot currentRoundSnapshot;

    private int roundRevealStep = 0;
    private RoundMove pendingPlayerMove;
    private RoundMove pendingAIMove;
    private RoundMove pendingWinningMove;
    private bool roundFlowActive = false;
    private bool roundStarted = false;
    private int humanTotalScore;
    private int aiTotalScore;
    [SerializeField] private int maxRounds = 4;
    private int currentRoundNumber = 1;

    [SerializeField] private TextAsset scrabbleWordsList;
    private List<string> scrabbleWords;
    [SerializeField] private TileBag _tileBag;
    private LetterBag letterBag;
    private BonusTile[,] boardBonusTiles;
    [SerializeField] private BonusTileBag bonusTileBag;
    [SerializeField] private BonusBag bonusBag;
    public UnityEvent hidePointTiles;
    [SerializeField] private BonusBoardView bonusBoardView;
    [SerializeField] private Timer timer;
    private TurnState currentState;
    private HashSet<string> scrabbleWordSet;
    private GaddagLexicon aiGaddagLexicon;
    private bool aiGaddagReady = false;
    public bool enableScoreDebug = true; 

    public enum TurnState
    {
        PlayerTurn,
        AITurn,
        Busy
    }

    [System.Serializable]
    public class RoundMove
    {
        public bool isValid;
        public int score;
        public string word;
        public float timeUsed;
        public bool isHuman;
        public List<PlacedTile> placedTiles;
        public List<SimPlacedTile> simulatedTiles;
    }


    public class GaddagLexicon
    {
        public const char Separator = '>';
        private readonly GaddagNode root = new GaddagNode();
        private readonly HashSet<string> words = new HashSet<string>();

        public GaddagNode Root
        {
            get { return root; }
        }

        public bool ContainsWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return false;

            return words.Contains(word.ToUpper());
        }

        public void AddWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return;

            word = word.Trim().ToUpper();

            if (words.Contains(word))
                return;

            words.Add(word);

            for (int split = 1; split <= word.Length; split++)
            {
                string prefix = word.Substring(0, split);
                string suffix = split < word.Length ? word.Substring(split) : string.Empty;
                string form = Reverse(prefix) + Separator + suffix;

                AddPath(form);
            }
        }

        private void AddPath(string form)
        {
            GaddagNode current = root;

            for (int i = 0; i < form.Length; i++)
            {
                current = current.GetOrAdd(form[i]);
            }

            current.isTerminal = true;
        }

        private string Reverse(string input)
        {
            char[] chars = input.ToCharArray();
            Array.Reverse(chars);
            return new string(chars);
        }
    }

    public class AnchorSquare
    {
        public int row;
        public int col;
        public HashSet<char> horizontalCrossChecks = new HashSet<char>();
        public HashSet<char> verticalCrossChecks = new HashSet<char>();
    }


    public class GaddagNode
    {
        public Dictionary<char, GaddagNode> edges = new Dictionary<char, GaddagNode>();
        public bool isTerminal = false;

        public GaddagNode GetOrAdd(char c)
        {
            GaddagNode next;
            if (!edges.TryGetValue(c, out next))
            {
                next = new GaddagNode();
                edges[c] = next;
            }

            return next;
        }
    }

    [System.Serializable]
    public class SimPlacedTile
    {
        public LetterInfo letterInfo;
        public LetterPosition letterPosition;
    }

    public void InitGame(int maxHandSize, int boardSizeX, int boardSizeY)
    {
        // Automatically discover active scene's BoardGen to dynamic-scale rectangular and square board dimensions
        var boardGen = UnityEngine.Object.FindAnyObjectByType<BoardGen>();
        if (boardGen != null)
        {
            boardSizeX = boardGen.RowY; // Rows
            boardSizeY = boardGen.RowX; // Columns
            Debug.Log($"[GameLogic] Auto-detected board dimensions from active BoardGen: {boardSizeY}x{boardSizeX} (columns x rows)");
        }

        this.maxHandSize = maxHandSize;
        this.boardSizeX = boardSizeX;
        this.boardSizeY = boardSizeY;

        currentTurn = 0;

        validatedBoardTiles = new LetterInfo[boardSizeX + 2, boardSizeY + 2];
        playerHandTiles = new List<LetterInfo>();
        boardBonusTiles = new BonusTile[boardSizeY, boardSizeX];

        pendingPlayerMove = null;
        pendingAIMove = null;
        pendingWinningMove = null;

        roundFlowActive = false;
        roundRevealStep = 0;
        roundStarted = false;

        if (bonusTileBag != null && bonusBag != null)
            bonusTileBag.ResetBonusBag(bonusBag);

        //scrabbleWords = new List<string>();
        scrabbleWordSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (scrabbleWordsList != null)
        {
            scrabbleWords = new List<string>(
                scrabbleWordsList.text.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries
                )
            );

            for (int i = 0; i < scrabbleWords.Count; i++)
            {
                string w = scrabbleWords[i];
                if (!string.IsNullOrWhiteSpace(w))
                    scrabbleWordSet.Add(w.Trim().ToUpper());
            }
        }
        else
        {
            scrabbleWords = new List<string>();
            scrabbleWordSet.Clear();
        }

        currentState = TurnState.PlayerTurn;

        if (timer != null)
            timer.ResetTimer();

        // reset totals and round number
        humanTotalScore = 0;
        aiTotalScore = 0;
        currentRoundNumber = 1;

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
        {
            Singleton.Instance.UIManager.UpdateRoundText(currentRoundNumber, maxRounds);
            Singleton.Instance.UIManager.ClearRoundMessage();
            Singleton.Instance.UIManager.UpdateTotalScores(humanTotalScore, aiTotalScore);
        }
    }

    public void BeginGameFromButton()
    {
        Debug.Log("BeginGameFromButton CALLED");
        StartRound();
    }

    private void StartRound()
    {
        Debug.Log("StartRound START");

        roundStarted = true;
        roundFlowActive = false;
        roundRevealStep = 0;
        pendingPlayerMove = null;
        pendingAIMove = null;
        pendingWinningMove = null;
        currentState = TurnState.PlayerTurn;
        //humanTotalScore = 0;
        //aiTotalScore = 0;

        if (playerHandTiles == null)
            playerHandTiles = new List<LetterInfo>();

        playerHandTiles.Clear();

        boardBonusTiles = new BonusTile[boardSizeY, boardSizeX];
        if (bonusTileBag != null && bonusBag != null)
            bonusTileBag.ResetBonusBag(bonusBag);

        PlaceBonusTilesOnBoard();

        if (bonusBoardView != null)
            bonusBoardView.DrawBonusTiles();

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
            Singleton.Instance.UIManager.RemoveAllHandTiles();

        RefillPlayerHand();
        ResetDisplay();
        SaveCurrentRoundSnapshot();

        if (timer != null)
        {
            timer.ResetTimer();
            timer.StartTimer();
        }

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
            Singleton.Instance.UIManager.ClearRoundMessage();

        Debug.Log("StartRound END");
    }

    public int GetMaxHandSize()
    {
        return maxHandSize;
    }

    public int GetBoardSizeX()
    {
        return boardSizeX;
    }

    public int GetBoardSizeY()
    {
        return boardSizeY;
    }

    public void SetBoardTile(PlacedTile tile)
    {
        if (tile == null || tile.letterInfo == null || tile.letterPosition == null)
            return;

        tile.letterInfo.bonusUsed = true;
        validatedBoardTiles[tile.letterPosition.RowX, tile.letterPosition.ColY] = tile.letterInfo;
    }

    public void ResetDisplay()
    {
        if (Singleton.Instance == null || Singleton.Instance.UIManager == null)
            return;

        List<string> drawnLetters = new List<string>();
        if (playerHandTiles != null)
        {
            foreach (var tile in playerHandTiles)
            {
                if (tile != null)
                    drawnLetters.Add(tile.letter);
            }
        }

        Singleton.Instance.UIManager.ResetDisplayWordList(drawnLetters);
    }

    public TileBag GetTileBag()
    {
        return _tileBag;
    }

    private List<PlacedTile> GetPlacedTilesThisTurn()
    {
        if (Singleton.Instance == null || Singleton.Instance.DropManager == null)
            return new List<PlacedTile>();

        return Singleton.Instance.DropManager.GetTilesDroppedThisTurn();
    }

    public TilePlacement AllTilesInSameLine()
    {
        List<PlacedTile> thisTurn = GetPlacedTilesThisTurn();

        if (thisTurn.Count == 0) return TilePlacement.NoTilePlaced;
        if (thisTurn.Count == 1) return TilePlacement.SingleTile;

        PlacedTile checkTile = thisTurn[0];
        bool vertical = true;
        bool horizontal = true;

        foreach (var tempTile in thisTurn)
        {
            if (tempTile.letterPosition.RowX != checkTile.letterPosition.RowX) horizontal = false;
            if (tempTile.letterPosition.ColY != checkTile.letterPosition.ColY) vertical = false;
        }

        if (vertical) return TilePlacement.Vertical;
        if (horizontal) return TilePlacement.Horizontal;
        return TilePlacement.WrongTilePlacement;
    }

    public bool HasHoles(TilePlacement orientation)
    {
        if (orientation == TilePlacement.SingleTile) return false;

        List<PlacedTile> thisTurn = GetPlacedTilesThisTurn();
        if (thisTurn.Count == 0) return false;

        int min = orientation == TilePlacement.Horizontal ? boardSizeX : boardSizeY;
        int max = -1;

        foreach (var tempTile in thisTurn)
        {
            if (orientation == TilePlacement.Horizontal)
            {
                min = Mathf.Min(min, tempTile.letterPosition.ColY);
                max = Mathf.Max(max, tempTile.letterPosition.ColY);
            }

            if (orientation == TilePlacement.Vertical)
            {
                min = Mathf.Min(min, tempTile.letterPosition.RowX);
                max = Mathf.Max(max, tempTile.letterPosition.RowX);
            }
        }

        if (max - min + 1 == thisTurn.Count) return false;

        int counter = 0;
        for (int index = min + 1; index < max; index++)
        {
            if (orientation == TilePlacement.Horizontal)
            {
                if (validatedBoardTiles[thisTurn[0].letterPosition.RowX, index] != null)
                    counter++;
            }

            if (orientation == TilePlacement.Vertical)
            {
                if (validatedBoardTiles[index, thisTurn[0].letterPosition.ColY] != null)
                    counter++;
            }
        }

        return max - min + 1 != thisTurn.Count + counter;
    }

    public bool CheckConnectedToTiles()
    {
        List<PlacedTile> thisTurn = GetPlacedTilesThisTurn();

        foreach (var tempTile in thisTurn)
        {
            if (validatedBoardTiles[tempTile.letterPosition.RowX - 1, tempTile.letterPosition.ColY] != null)
                return true;
            if (validatedBoardTiles[tempTile.letterPosition.RowX + 1, tempTile.letterPosition.ColY] != null)
                return true;
            if (validatedBoardTiles[tempTile.letterPosition.RowX, tempTile.letterPosition.ColY - 1] != null)
                return true;
            if (validatedBoardTiles[tempTile.letterPosition.RowX, tempTile.letterPosition.ColY + 1] != null)
                return true;
        }

        return false;
    }

    public void RestartGameSingleGuessTrainer()
    {
        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
            Singleton.Instance.UIManager.RemoveAllHandTiles();

        if (Singleton.Instance != null && Singleton.Instance.DebugManager != null)
            Singleton.Instance.DebugManager.StartNewGame();

        RefillPlayerHand();
        ResetDisplay();
    }

    public void FillMissingWordsInRed()
    {
        if (Singleton.Instance == null || Singleton.Instance.WordLookupLogic == null || Singleton.Instance.UIManager == null)
            return;

        List<string> drawnLetters = new List<string>();
        foreach (var tile in playerHandTiles)
        {
            if (tile != null)
                drawnLetters.Add(tile.letter);
        }

        List<string> allWords = Singleton.Instance.WordLookupLogic.FindWords(drawnLetters);
        foreach (var word in allWords)
        {
            Singleton.Instance.UIManager.AddRedWord(word);
        }
    }

    public bool ValidMove()
    {
        TilePlacement orientation = AllTilesInSameLine();

        if (orientation == TilePlacement.NoTilePlaced || orientation == TilePlacement.WrongTilePlacement)
            return false;

        bool boardHasExistingTiles = HasAnyValidatedTilesOnBoard();

        if (orientation == TilePlacement.SingleTile)
        {
            if (!boardHasExistingTiles)
                return true;

            return CheckConnectedToTiles();
        }

        bool hasHoles = HasHoles(orientation);
        if (hasHoles)
            return false;

        if (!boardHasExistingTiles)
            return true;

        return CheckConnectedToTiles();
    }

    public List<List<LetterInfo>> CollectAllWords(TilePlacement orientation)
    {
        List<List<LetterInfo>> wordList = new List<List<LetterInfo>>();
        List<PlacedTile> droppedTiles = GetPlacedTilesThisTurn();

        if (droppedTiles.Count == 0)
            return wordList;

        LetterInfo[,] tempBoard = (LetterInfo[,])validatedBoardTiles.Clone();

        foreach (var tempTile in droppedTiles)
        {
            tempBoard[tempTile.letterPosition.RowX, tempTile.letterPosition.ColY] = tempTile.letterInfo;
        }

        // Cross words created by each newly dropped tile
        foreach (var tempTile in droppedTiles)
        {
            int row = tempTile.letterPosition.RowX;
            int col = tempTile.letterPosition.ColY;

            if (orientation == TilePlacement.Horizontal || orientation == TilePlacement.SingleTile)
            {
                bool hasVerticalCross =
                    tempBoard[row - 1, col] != null ||
                    tempBoard[row + 1, col] != null;

                if (hasVerticalCross)
                {
                    int firstRow = GetFirstLetterIndex(TilePlacement.Vertical, tempBoard, row, col);
                    List<LetterInfo> crossWord = GetWordFromBoard(TilePlacement.Vertical, tempBoard, firstRow, col);

                    if (crossWord != null && crossWord.Count > 1)
                        AddWordIfNotDuplicate(wordList, crossWord);
                }
            }

            if (orientation == TilePlacement.Vertical || orientation == TilePlacement.SingleTile)
            {
                bool hasHorizontalCross =
                    tempBoard[row, col - 1] != null ||
                    tempBoard[row, col + 1] != null;

                if (hasHorizontalCross)
                {
                    int firstCol = GetFirstLetterIndex(TilePlacement.Horizontal, tempBoard, row, col);
                    List<LetterInfo> crossWord = GetWordFromBoard(TilePlacement.Horizontal, tempBoard, row, firstCol);

                    if (crossWord != null && crossWord.Count > 1)
                        AddWordIfNotDuplicate(wordList, crossWord);
                }
            }
        }

        // Main word
        PlacedTile placedTile = droppedTiles[0];
        int mainRow = placedTile.letterPosition.RowX;
        int mainCol = placedTile.letterPosition.ColY;

        if (orientation == TilePlacement.Horizontal)
        {
            int firstCol = GetFirstLetterIndex(TilePlacement.Horizontal, tempBoard, mainRow, mainCol);
            List<LetterInfo> mainWord = GetWordFromBoard(TilePlacement.Horizontal, tempBoard, mainRow, firstCol);

            if (mainWord != null && mainWord.Count > 0)
                AddWordIfNotDuplicate(wordList, mainWord);
        }
        else if (orientation == TilePlacement.Vertical)
        {
            int firstRow = GetFirstLetterIndex(TilePlacement.Vertical, tempBoard, mainRow, mainCol);
            List<LetterInfo> mainWord = GetWordFromBoard(TilePlacement.Vertical, tempBoard, firstRow, mainCol);

            if (mainWord != null && mainWord.Count > 0)
                AddWordIfNotDuplicate(wordList, mainWord);
        }
        else if (orientation == TilePlacement.SingleTile)
        {
            bool addedWord = false;

            bool hasVertical =
                tempBoard[mainRow - 1, mainCol] != null ||
                tempBoard[mainRow + 1, mainCol] != null;

            if (hasVertical)
            {
                int firstRow = GetFirstLetterIndex(TilePlacement.Vertical, tempBoard, mainRow, mainCol);
                List<LetterInfo> verticalWord = GetWordFromBoard(TilePlacement.Vertical, tempBoard, firstRow, mainCol);

                if (verticalWord != null && verticalWord.Count > 1)
                {
                    AddWordIfNotDuplicate(wordList, verticalWord);
                    addedWord = true;
                }
            }

            bool hasHorizontal =
                tempBoard[mainRow, mainCol - 1] != null ||
                tempBoard[mainRow, mainCol + 1] != null;

            if (hasHorizontal)
            {
                int firstCol = GetFirstLetterIndex(TilePlacement.Horizontal, tempBoard, mainRow, mainCol);
                List<LetterInfo> horizontalWord = GetWordFromBoard(TilePlacement.Horizontal, tempBoard, mainRow, firstCol);

                if (horizontalWord != null && horizontalWord.Count > 1)
                {
                    AddWordIfNotDuplicate(wordList, horizontalWord);
                    addedWord = true;
                }
            }

            if (!addedWord)
            {
                List<LetterInfo> singleLetterWord = new List<LetterInfo>();
                singleLetterWord.Add(tempBoard[mainRow, mainCol]);
                AddWordIfNotDuplicate(wordList, singleLetterWord);
            }
        }

        return wordList;
    }

    public int GetFirstLetterIndex(TilePlacement orientation, LetterInfo[,] board, int row, int col)
    {
        if (orientation == TilePlacement.Horizontal)
        {
            while (col > 0 && board[row, col - 1] != null)
                col--;

            return col;
        }

        if (orientation == TilePlacement.Vertical)
        {
            while (row > 0 && board[row - 1, col] != null)
                row--;

            return row;
        }

        return orientation == TilePlacement.Horizontal ? col : row;
    }

    public List<LetterInfo> GetWordFromBoard(TilePlacement orientation, LetterInfo[,] board, int row, int col)
    {
        List<LetterInfo> newWord = new List<LetterInfo>();

        if (orientation == TilePlacement.Horizontal)
        {
            while (col < board.GetLength(1) && board[row, col] != null)
            {
                newWord.Add(board[row, col]);
                col++;
            }
        }
        else if (orientation == TilePlacement.Vertical)
        {
            while (row < board.GetLength(0) && board[row, col] != null)
            {
                newWord.Add(board[row, col]);
                row++;
            }
        }

        return newWord;
    }

    public bool CheckWordValidity(List<List<LetterInfo>> inputWords)
    {
        foreach (var wordTiles in inputWords)
        {
            string word = string.Empty;
            foreach (var wordTile in wordTiles)
            {
                word += wordTile.letter;
            }

            if (scrabbleWordSet == null || !scrabbleWordSet.Contains(word.ToUpper()))
                return false;
        }

        return true;
    }
    public int CountWordPoints(List<LetterInfo> word, List<PlacedTile> placedThisTurn)
    {
        int totalLetterPoints = 0;
        int wordMultiplier = 1;

        if (enableScoreDebug)
        {
            string letters = string.Join("", word.ConvertAll(t => t.letter));
            Debug.Log($"SCORE DEBUG: Start CountWordPoints for word '{letters}'. placedThisTurn.Count={placedThisTurn.Count}");
        }

        foreach (var tile in word)
        {
            int row = -1;
            int col = -1;
            bool isNewlyPlaced = false;

            foreach (var placedTile in placedThisTurn)
            {
                if (placedTile.letterInfo == tile)
                {
                    row = placedTile.letterPosition.RowX;
                    col = placedTile.letterPosition.ColY;
                    isNewlyPlaced = true;
                    break;
                }
            }

            int letterPoints = tile.points;

            if (enableScoreDebug)
            {
                Debug.Log(
                    $"SCORE DEBUG: Letter '{tile.letter}' base={tile.points}, " +
                    $"isNewlyPlaced={isNewlyPlaced}, row={row}, col={col}");
            }

            int letterMultiplierUsed = 1;

            if (isNewlyPlaced && row >= 0 && col >= 0)
            {
                int bonusRow = row - 1;
                int bonusCol = col - 1;

                bool bonusIndexInRange =
                    bonusRow >= 0 &&
                    bonusRow < boardBonusTiles.GetLength(1) &&
                    bonusCol >= 0 &&
                    bonusCol < boardBonusTiles.GetLength(0);

                if (bonusIndexInRange)
                {
                    BonusTile bonusTile = boardBonusTiles[bonusCol, bonusRow];

                    if (bonusTile != null && !tile.bonusUsed)
                    {
                        switch (bonusTile.bonusType)
                        {
                            case BonusType.DoubleLetter:
                                letterPoints *= 2;
                                letterMultiplierUsed = 2;
                                break;
                            case BonusType.TripleLetter:
                                letterPoints *= 3;
                                letterMultiplierUsed = 3;
                                break;
                            case BonusType.DoubleWord:
                                wordMultiplier *= 2;
                                break;
                            case BonusType.TripleWord:
                                wordMultiplier *= 3;
                                break;
                        }

                        if (enableScoreDebug)
                        {
                            Debug.Log(
                                $"SCORE DEBUG: Letter '{tile.letter}' hit bonus {bonusTile.bonusType} " +
                                $"at board [{row},{col}] (bonus[{bonusCol},{bonusRow}]). " +
                                $"letterMult={letterMultiplierUsed}, wordMultiplier NOW={wordMultiplier}");
                        }
                    }
                    else if (enableScoreDebug && bonusTile != null && tile.bonusUsed)
                    {
                        Debug.Log(
                            $"SCORE DEBUG: Letter '{tile.letter}' is on bonus {bonusTile.bonusType} " +
                            $"but bonus already used; no bonus applied.");
                    }
                }
                else if (enableScoreDebug)
                {
                    Debug.Log(
                        $"SCORE DEBUG: Letter '{tile.letter}' has out-of-range bonus index " +
                        $"bonusRow={bonusRow}, bonusCol={bonusCol}, no bonus applied.");
                }
            }

            totalLetterPoints += letterPoints;

            if (enableScoreDebug)
            {
                Debug.Log(
                    $"SCORE DEBUG: After letter '{tile.letter}': letterPoints={letterPoints}, " +
                    $"totalLetterPoints={totalLetterPoints}, wordMultiplier={wordMultiplier}");
            }
        }

        int finalScore = totalLetterPoints * wordMultiplier;

        if (enableScoreDebug)
        {
            string letters = string.Join("", word.ConvertAll(t => t.letter));
            Debug.Log(
                $"SCORE DEBUG: Final word '{letters}' => baseSum={totalLetterPoints}, " +
                $"wordMultiplier={wordMultiplier}, totalScore={finalScore}");
        }

        return finalScore;
    }

    /*public int CountWordPoints(List<LetterInfo> word, List<PlacedTile> placedThisTurn)
    {
        int totalLetterPoints = 0;
        int wordMultiplier = 1;

        foreach (var tile in word)
        {
            int row = -1;
            int col = -1;
            bool isNewlyPlaced = false;

            foreach (var placedTile in placedThisTurn)
            {
                if (placedTile.letterInfo == tile)
                {
                    row = placedTile.letterPosition.RowX;
                    col = placedTile.letterPosition.ColY;
                    isNewlyPlaced = true;
                    break;
                }
            }

            int letterPoints = tile.points;

            if (isNewlyPlaced && row >= 0 && col >= 0)
            {
                int bonusRow = row - 1;
                int bonusCol = col - 1;

                bool bonusIndexInRange =
                    bonusRow >= 0 &&
                    bonusRow < boardBonusTiles.GetLength(1) &&
                    bonusCol >= 0 &&
                    bonusCol < boardBonusTiles.GetLength(0);

                if (bonusIndexInRange)
                {
                    BonusTile bonusTile = boardBonusTiles[bonusCol, bonusRow];

                    if (bonusTile != null && !tile.bonusUsed)
                    {
                        switch (bonusTile.bonusType)
                        {
                            case BonusType.DoubleLetter:
                                letterPoints *= 2;
                                break;
                            case BonusType.TripleLetter:
                                letterPoints *= 3;
                                break;
                            case BonusType.DoubleWord:
                                wordMultiplier *= 2;
                                break;
                            case BonusType.TripleWord:
                                wordMultiplier *= 3;
                                break;
                        }
                    }
                }
            }

            totalLetterPoints += letterPoints;
        }

        return totalLetterPoints * wordMultiplier;
    }*/

    public int CountWordPoints(List<LetterInfo> word)
    {
        return CountWordPoints(word, new List<PlacedTile>());
    }

    public void EndTurn()
    {
        foreach (var placedTile in GetPlacedTilesThisTurn())
        {
            playerHandTiles.Remove(placedTile.letterInfo);
            SetBoardTile(placedTile);
        }

        currentTurn++;

        if (Singleton.Instance != null && Singleton.Instance.DropManager != null)
            Singleton.Instance.DropManager.ResetLocations();

        if (Singleton.Instance != null && Singleton.Instance.GameLogic != null)
            Singleton.Instance.GameLogic.RefillPlayerHand();
    }

    public void RefillPlayerHand()
    {
        Debug.Log("RefillPlayerHand START");

        if (playerHandTiles == null)
        {
            Debug.LogError("playerHandTiles is null in RefillPlayerHand.");
            return;
        }

        if (_tileBag == null)
        {
            Debug.LogError("_tileBag is null in RefillPlayerHand.");
            return;
        }

        Debug.Log("maxHandSize = " + maxHandSize);
        Debug.Log("playerHandTiles.Count at refill start = " + playerHandTiles.Count);

        // If hand is already full or overfull, do nothing
        if (playerHandTiles.Count >= maxHandSize)
        {
            Debug.Log("Hand already full or overfull. No refill performed.");
            Debug.Log("RefillPlayerHand END");
            return;
        }

        int availableInBag = _tileBag.GetLetters().Count;
        int tilesMissing = maxHandSize - playerHandTiles.Count;
        int tilesToDraw = Mathf.Min(tilesMissing, availableInBag);

        Debug.Log("availableInBag = " + availableInBag);
        Debug.Log("tilesMissing   = " + tilesMissing);
        Debug.Log("tilesToDraw    = " + tilesToDraw);

        for (int i = 0; i < tilesToDraw; i++)
        {
            // Safety: if hand somehow reaches max during the loop, stop.
            if (playerHandTiles.Count >= maxHandSize)
            {
                Debug.Log("Safety break: hand reached maxHandSize during refill.");
                break;
            }

            // Correct source for new tiles: the serialized TileBag
            LetterInfo tile = _tileBag.DrawLetterTileFromBag();
            if (tile == null)
            {
                Debug.LogWarning("DrawLetterTileFromBag returned null. Stopping refill.");
                break;
            }

            playerHandTiles.Add(tile);
            Singleton.Instance.UIManager.AddTileToHand(tile);

            Debug.Log(
                "Drew tile letter " + tile.letter +
                ", points " + tile.points +
                ", new hand count = " + playerHandTiles.Count
            );
        }

        Debug.Log("playerHandTiles.Count at refill end = " + playerHandTiles.Count);
        Debug.Log("RefillPlayerHand END");
    }

    private void PlaceBonusTilesOnBoard()
    {
       // Debug.Log("===== PlaceBonusTilesOnBoard START =====");

        if (boardBonusTiles == null)
        {
         //   Debug.LogError("boardBonusTiles is null. Cannot place bonus tiles.");
            return;
        }

        if (bonusTileBag == null)
        {
         //   Debug.LogError("bonusTileBag is null. Cannot place bonus tiles.");
            return;
        }

        int width = boardBonusTiles.GetLength(0);   // x / column
        int height = boardBonusTiles.GetLength(1);  // y / row

        //Debug.Log("boardBonusTiles size => X = " + width + ", Y = " + height);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                boardBonusTiles[x, y] = null;
            }
        }

        //Debug.Log("Cleared old bonus tiles from array.");

        int placedCount = 0;
        int safety = 0;
        int maxSafety = width * height * 20;

        while (bonusTileBag.GetRemainingCount() > 0)
        {
            safety++;

            if (safety > maxSafety)
            {
                //Debug.LogWarning("Safety break triggered in PlaceBonusTilesOnBoard. No more legal spaces for bonus tiles.");
                break;
            }

            BonusTile bonusTile = bonusTileBag.DrawRandomBonusTile();

            if (bonusTile == null)
            {
                //Debug.LogWarning("DrawRandomBonusTile returned null. Stopping placement.");
                break;
            }

            int x = -1;
            int y = -1;
            int findSpotSafety = 0;
            bool foundSpot = false;

            do
            {
                x = Random.Range(0, width);
                y = Random.Range(0, height);
                findSpotSafety++;

                bool bonusCellOccupied = boardBonusTiles[x, y] != null;

                // boardBonusTiles uses [x, y]
                // validatedBoardTiles uses [row, col] with 1-cell padding
                bool boardLetterOccupied =
                    validatedBoardTiles != null &&
                    validatedBoardTiles[y + 1, x + 1] != null;

                if (!bonusCellOccupied && !boardLetterOccupied)
                {
                    foundSpot = true;
                    break;
                }

                if (findSpotSafety > width * height * 3)
                {
                    //Debug.LogWarning(
                     //   "Could not find empty legal location for bonus tile " + bonusTile.bonusType +
                    //    ". Placement stopped early."
                    //);
                    break;
                }
            }
            while (true);

            if (!foundSpot)
            {
                break;
            }

            boardBonusTiles[x, y] = bonusTile;
            placedCount++;

            //Debug.Log("Placed " + bonusTile.bonusType + " at [x=" + x + ", y=" + y + "]");
        }

        //Debug.Log("Total bonus tiles placed = " + placedCount);
        //Debug.Log("===== BONUS BOARD DUMP START =====");

        for (int y = 0; y < height; y++)
        {
            string line = "";

            for (int x = 0; x < width; x++)
            {
                bool boardLetterOccupied =
                    validatedBoardTiles != null &&
                    validatedBoardTiles[y + 1, x + 1] != null;

                if (boardLetterOccupied)
                {
                    line += "[WORD]";
                }
                else if (boardBonusTiles[x, y] == null)
                {
                    line += "[----]";
                }
                else
                {
                    switch (boardBonusTiles[x, y].bonusType)
                    {
                        case BonusType.Blank:
                            line += "[BLNK]";
                            break;
                        case BonusType.DoubleLetter:
                            line += "[ DL ]";
                            break;
                        case BonusType.TripleLetter:
                            line += "[ TL ]";
                            break;
                        case BonusType.DoubleWord:
                            line += "[ DW ]";
                            break;
                        case BonusType.TripleWord:
                            line += "[ TW ]";
                            break;
                        default:
                            line += "[????]";
                            break;
                    }
                }
            }

           // Debug.Log("Bonus row y=" + y + " => " + line);
        }

       // Debug.Log("===== BONUS BOARD DUMP END =====");
       // Debug.Log("===== PlaceBonusTilesOnBoard END =====");
    }

    public BonusTile[,] GetBoardBonusTiles()
    {
        return boardBonusTiles;
    }

    public void EndTurnSingleGuess()
    {
        if (!roundStarted)
        {
            StartRound();
            return;
        }

        if (roundFlowActive)
        {
            AdvanceRoundReveal();
            return;
        }

        if (currentState != TurnState.PlayerTurn)
            return;

        currentState = TurnState.Busy;
        roundFlowActive = true;
        roundRevealStep = 0;

        if (timer != null)
            timer.StopTimer();

        pendingPlayerMove = EvaluatePlayerSubmission();
        pendingAIMove = null;
        pendingWinningMove = null;

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
            Singleton.Instance.UIManager.ShowRoundMessage("Press EndTurn to reveal your result.");
    }

    private void AdvanceRoundReveal()
    {
        if (!roundFlowActive)
            return;

        switch (roundRevealStep)
        {
            case 0:
                if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
                {
                    if (pendingPlayerMove != null && pendingPlayerMove.isValid)
                    {
                        Singleton.Instance.UIManager.ShowRoundMessage(
                            "You played " + pendingPlayerMove.word + " for " + pendingPlayerMove.score + " points. Press EndTurn.");
                    }
                    else
                    {
                        Singleton.Instance.UIManager.ShowRoundMessage(
                            "Your move was invalid. Press EndTurn.");
                    }
                }

                roundRevealStep = 1;
                break;

            case 1:
                if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
                    Singleton.Instance.UIManager.ReturnTilesToHand();

                pendingAIMove = EvaluateAIMove();

                if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
                {
                    if (pendingAIMove != null && pendingAIMove.isValid)
                    {
                        Singleton.Instance.UIManager.ShowRoundMessage(
                            "AI played " + pendingAIMove.word + " for " + pendingAIMove.score + " points. Press EndTurn.");
                    }
                    else
                    {
                        Singleton.Instance.UIManager.ShowRoundMessage(
                            "AI could not make a valid move. Press EndTurn.");
                    }
                }

                roundRevealStep = 2;
                break;

            case 2:
                pendingWinningMove = CompareMoves(pendingPlayerMove, pendingAIMove);

                bool bothValid =
                    pendingPlayerMove != null && pendingPlayerMove.isValid &&
                    pendingAIMove != null && pendingAIMove.isValid;

                bool sameScore =
                    bothValid &&
                    pendingPlayerMove.score == pendingAIMove.score;

                bool sameWord =
                    sameScore &&
                    !string.IsNullOrEmpty(pendingPlayerMove.word) &&
                    !string.IsNullOrEmpty(pendingAIMove.word) &&
                    string.Equals(pendingPlayerMove.word, pendingAIMove.word, StringComparison.OrdinalIgnoreCase);

                if (sameWord)
                {
                    Singleton.Instance.UIManager.ShowRoundMessage(
                        "Both found " + pendingPlayerMove.word + " for " + pendingPlayerMove.score +
                        " points. Human wins the tie against AI. Press EndTurn."
                    );
                }
                else if (sameScore)
                {
                    Singleton.Instance.UIManager.ShowRoundMessage(
                        "Tie on score. Human wins the tie against AI. Press EndTurn."
                    );
                }
                else if (pendingWinningMove != null && pendingWinningMove.isValid)
                {
                    Singleton.Instance.UIManager.ShowRoundMessage(
                        (pendingWinningMove.isHuman ? "You win with " : "AI wins with ") +
                        pendingWinningMove.word + " (" + pendingWinningMove.score + " pts). Press EndTurn."
                    );
                }
                else
                {
                    Singleton.Instance.UIManager.ShowRoundMessage(
                        "No valid move won the round. Press EndTurn."
                    );
                }

                Debug.Log("Displayed final winner message.");
                roundRevealStep = 3;
                break;

            case 3:
                ApplyWinningMove(pendingWinningMove);

                if (IsGameOver())
                {
                    EndGame();
                }
                else
                {
                    StartNextRound();
                }

                Debug.Log("Applied winning move. Checked for game over.");
                break;
        }
    }

    private RoundMove EvaluatePlayerSubmission()
    {
        RoundMove move = new RoundMove();
        move.isHuman = true;
        move.timeUsed = GetCurrentTimeUsed();
        move.placedTiles = new List<PlacedTile>(GetPlacedTilesThisTurn());
        
        // Populate robust, cloned simulatedTiles data for the Human move
        move.simulatedTiles = new List<SimPlacedTile>();
        foreach (var pt in move.placedTiles)
        {
            if (pt != null && pt.letterInfo != null && pt.letterPosition != null)
            {
                SimPlacedTile sim = new SimPlacedTile();
                sim.letterInfo = new LetterInfo(pt.letterInfo);
                sim.letterPosition = new LetterPosition(pt.letterPosition.RowX, pt.letterPosition.ColY);
                move.simulatedTiles.Add(sim);
            }
        }

        TilePlacement orientation = AllTilesInSameLine();

        if (orientation == TilePlacement.NoTilePlaced || orientation == TilePlacement.WrongTilePlacement)
        {
            move.isValid = false;
            move.score = 0;
            move.word = "";
            return move;
        }

        bool boardHasExistingTiles = HasAnyValidatedTilesOnBoard();

        if (orientation == TilePlacement.SingleTile)
        {
            if (boardHasExistingTiles && !CheckConnectedToTiles())
            {
                move.isValid = false;
                move.score = 0;
                move.word = "";
                return move;
            }
        }
        else
        {
            if (HasHoles(orientation))
            {
                move.isValid = false;
                move.score = 0;
                move.word = "";
                return move;
            }

            if (boardHasExistingTiles && !CheckConnectedToTiles())
            {
                move.isValid = false;
                move.score = 0;
                move.word = "";
                return move;
            }
        }

        List<List<LetterInfo>> words = CollectAllWords(orientation);

        if (!CheckWordValidity(words))
        {
            move.isValid = false;
            move.score = 0;
            move.word = "";
            return move;
        }

        move.isValid = true;
        move.score = 0;
        move.word = "";

        foreach (var singleList in words)
        {
            string finalWord = "";
            foreach (var letter in singleList)
            {
                finalWord += letter.letter;
            }

            if (move.word == "")
                move.word = finalWord;

            move.score += CountWordPoints(singleList, move.placedTiles);
        }

        if (move.placedTiles.Count == maxHandSize)
            move.score += 50;

        return move;
    }

    public RoundMove TestCompareMoves(RoundMove playerMove, RoundMove aiMove) {
        return CompareMoves(playerMove, aiMove);
    }

    private RoundMove CompareMoves(RoundMove playerMove, RoundMove aiMove)
    {
        Debug.Log("===== CompareMoves START =====");

        if (playerMove == null)
        {
            Debug.Log("Player move is null. Returning AI move.");
            return aiMove;
        }

        if (aiMove == null)
        {
            Debug.Log("AI move is null. Returning player move.");
            return playerMove;
        }

        Debug.Log("Player => valid: " + playerMove.isValid +
                  ", score: " + playerMove.score +
                  ", time: " + playerMove.timeUsed +
                  ", word: " + playerMove.word);

        Debug.Log("AI => valid: " + aiMove.isValid +
                  ", score: " + aiMove.score +
                  ", time: " + aiMove.timeUsed +
                  ", word: " + aiMove.word);

        if (playerMove.isValid && !aiMove.isValid)
        {
            Debug.Log("Player wins because player is valid and AI is invalid.");
            return playerMove;
        }

        if (!playerMove.isValid && aiMove.isValid)
        {
            Debug.Log("AI wins because AI is valid and player is invalid.");
            return aiMove;
        }

        if (!playerMove.isValid && !aiMove.isValid)
        {
            Debug.Log("Neither move is valid. No winner this round.");
            return null;
        }

        if (playerMove.score > aiMove.score)
        {
            Debug.Log("Player wins on score.");
            return playerMove;
        }

        if (aiMove.score > playerMove.score)
        {
            Debug.Log("AI wins on score.");
            return aiMove;
        }

        bool sameWord =
            !string.IsNullOrEmpty(playerMove.word) &&
            !string.IsNullOrEmpty(aiMove.word) &&
            string.Equals(playerMove.word, aiMove.word, StringComparison.OrdinalIgnoreCase);

        if (sameWord)
        {
            Debug.Log("Scores tied and both sides found the same word. Human wins tie against AI.");
            return playerMove;
        }

        Debug.Log("Scores tied on different words. Human wins tie against AI because AI has a timing advantage.");
        return playerMove;
    }

    private void ApplyWinningMove(RoundMove winningMove)
    {
        Debug.Log("===== ApplyWinningMove START =====");

        if (winningMove == null)
        {
            Debug.Log("winningMove is null. No move won this round.");
            Debug.Log("===== ApplyWinningMove END =====");
            return;
        }

        Debug.Log(
            "Winning move isValid=" + winningMove.isValid +
            ", isHuman=" + winningMove.isHuman +
            ", word=" + winningMove.word +
            ", score=" + winningMove.score
        );

        if (!winningMove.isValid)
        {
            Debug.Log("Winning move invalid. Nothing applied.");
            Debug.Log("===== ApplyWinningMove END =====");
            return;
        }

        if (winningMove.isHuman)
        {
            Debug.Log("Applying HUMAN winning move.");
            ApplyHumanWinningTiles(winningMove);
        }
        else
        {
            Debug.Log("Applying AI winning move.");
            ApplyAIWinningTiles(winningMove);
        }

        AddRoundWinnerScore(winningMove);

        currentTurn++;
        RefillPlayerHand();
        RebuildHandUIFromLogicalHand();

        Debug.Log("===== ApplyWinningMove END =====");
    }


    private void ApplyHumanWinningTiles(RoundMove winningMove)
    {
        if (winningMove == null || winningMove.simulatedTiles == null)
        {
            Debug.LogWarning("Human winning move has no simulatedTiles.");
            return;
        }

        Debug.Log("ApplyHumanWinningTiles: starting with " + winningMove.simulatedTiles.Count + " tiles.");

        foreach (var simTile in winningMove.simulatedTiles)
        {
            if (simTile == null || simTile.letterInfo == null || simTile.letterPosition == null)
            {
                Debug.LogWarning("Null simTile data in human winning move.");
                continue;
            }

            bool removed = RemoveMatchingTileFromHandByLetter(
                simTile.letterInfo.letter,
                simTile.letterInfo.points
            );

            Debug.Log(
                "HUMAN removing logical hand tile => " +
                simTile.letterInfo.letter + " (" + simTile.letterInfo.points + "), removed = " + removed
            );

            // Re-apply correct validatedBoardTiles state using robust cloned data
            simTile.letterInfo.bonusUsed = true;
            validatedBoardTiles[simTile.letterPosition.RowX, simTile.letterPosition.ColY] = simTile.letterInfo;

            Debug.Log(
                "HUMAN committed board tile => " +
                simTile.letterInfo.letter + " at row " +
                simTile.letterPosition.RowX + ", col " + simTile.letterPosition.ColY
            );

            Singleton.Instance.UIManager.PlaceAITileOnBoard(
                simTile.letterInfo,
                simTile.letterPosition
            );
        }
    }

    private void ApplyAIWinningTiles(RoundMove winningMove)
    {
        if (winningMove.simulatedTiles == null)
        {
            Debug.LogWarning("AI winning move has no simulatedTiles.");
            return;
        }

        foreach (var simTile in winningMove.simulatedTiles)
        {
            if (simTile == null || simTile.letterInfo == null || simTile.letterPosition == null)
            {
                Debug.LogWarning("Null simTile data in AI winning move.");
                continue;
            }

            bool removed = RemoveMatchingTileFromHandByLetter(
                simTile.letterInfo.letter,
                simTile.letterInfo.points
            );

            Debug.Log(
                "AI removing logical hand tile => " +
                simTile.letterInfo.letter + " (" + simTile.letterInfo.points + "), removed = " + removed
            );

            simTile.letterInfo.bonusUsed = true;
            validatedBoardTiles[simTile.letterPosition.RowX, simTile.letterPosition.ColY] = simTile.letterInfo;

            Singleton.Instance.UIManager.PlaceAITileOnBoard(
                simTile.letterInfo,
                simTile.letterPosition
            );
        }
    }

    private bool RemoveMatchingTileFromHandByLetter(string letter, int points)
    {
        for (int i = 0; i < playerHandTiles.Count; i++)
        {
            if (playerHandTiles[i] != null &&
                playerHandTiles[i].letter == letter &&
                playerHandTiles[i].points == points)
            {
                playerHandTiles.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    private void StartNextRound()
    {
        roundFlowActive = false;
        roundRevealStep = 0;
        pendingPlayerMove = null;
        pendingAIMove = null;
        pendingWinningMove = null;
        currentState = TurnState.PlayerTurn;

        currentRoundNumber++;

        boardBonusTiles = new BonusTile[boardSizeY, boardSizeX];
        if (bonusTileBag != null && bonusBag != null)
            bonusTileBag.ResetBonusBag(bonusBag);

        PlaceBonusTilesOnBoard();

        if (bonusBoardView != null)
            bonusBoardView.DrawBonusTiles();

        ResetDisplay();
        SaveCurrentRoundSnapshot();

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
        {
            Singleton.Instance.UIManager.UpdateRoundText(currentRoundNumber, maxRounds);
            Singleton.Instance.UIManager.ClearRoundMessage();
        }

        if (timer != null)
        {
            timer.ResetTimer();
            timer.StartTimer();
        }
    }

    private float GetCurrentTimeUsed()
    {
        if (timer == null)
            return 0f;

        return timer.GetRoundDuration() - timer.GetRemainingTime();
    }

    private bool HasAnyValidatedTilesOnBoard()
    {
        for (int x = 0; x < validatedBoardTiles.GetLength(0); x++)
        {
            for (int y = 0; y < validatedBoardTiles.GetLength(1); y++)
            {
                if (validatedBoardTiles[x, y] != null)
                    return true;
            }
        }

        return false;
    }

    public void RefreshRoundSnapshot()
    {
        SaveCurrentRoundSnapshot();
    }

    public void SaveCurrentRoundSnapshot()
    {
        currentRoundSnapshot = new RoundSnapshot(playerHandTiles, boardBonusTiles);
    }

    private List<LetterInfo> CloneTilesForAI(List<LetterInfo> source)
    {
        List<LetterInfo> clone = new List<LetterInfo>();
        if (source == null) return clone;

        foreach (LetterInfo tile in source)
        {
            clone.Add(tile == null ? null : new LetterInfo(tile));
        }

        return clone;
    }

    private BonusTile[,] CloneBonusTilesForAI(BonusTile[,] source)
    {
        if (source == null)
            return null;

        int width = source.GetLength(0);
        int height = source.GetLength(1);
        BonusTile[,] clone = new BonusTile[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                clone[x, y] = source[x, y] == null ? null : new BonusTile(source[x, y]);
            }
        }

        return clone;
    }

    private RoundMove EvaluateAIMove()
    {
        Debug.Log("===== EvaluateAIMove START =====");

        RoundMove bestMove = null;

        if (currentRoundSnapshot == null || currentRoundSnapshot.initialTiles == null)
        {
            Debug.LogWarning("EvaluateAIMove: currentRoundSnapshot or initialTiles is null, returning PASS move.");
            return new RoundMove
            {
                isHuman = false,
                isValid = false,
                score = 0,
                word = string.Empty,
                timeUsed = 70f,
                placedTiles = null,
                simulatedTiles = new List<SimPlacedTile>()
            };
        }

        List<LetterInfo> aiTiles = CloneTilesForAI(currentRoundSnapshot.initialTiles);
        BonusTile[,] aiBonusBoard = CloneBonusTilesForAI(currentRoundSnapshot.initialBonusTiles);

        bool boardHasTiles = HasAnyValidatedTilesOnBoard();
        Debug.Log("EvaluateAIMove: boardHasTiles = " + boardHasTiles);

        if (!boardHasTiles)
        {
            Debug.Log("EvaluateAIMove: First-turn logic (empty board).");

            List<string> possibleWords = FindPossibleAIWords(aiTiles);
            Debug.Log("EvaluateAIMove: First-turn rack-only possible word count = " + possibleWords.Count);

            for (int i = 0; i < possibleWords.Count; i++)
            {
                string word = possibleWords[i];
                if (string.IsNullOrEmpty(word))
                    continue;

                RoundMove candidate = FindBestFirstTurnPlacement(word, aiTiles, aiBonusBoard);
                if (candidate == null || !candidate.isValid)
                    continue;

                if (bestMove == null || IsBetterAIMove(candidate, bestMove))
                    bestMove = candidate;
            }
        }
        else
        {
            Debug.Log("EvaluateAIMove: GADDAG round-2+ search.");
            EnsureAIGaddagReady();

            List<RoundMove> allCandidates = FindAllConnectedCandidatesGaddag(aiTiles, aiBonusBoard);
            Debug.Log("EvaluateAIMove: total GADDAG connected candidates = " + allCandidates.Count);

            if (allCandidates.Count > 0)
            {
                allCandidates.Sort(CompareAIMovesBestFirst);

                int logCount = Mathf.Min(10, allCandidates.Count);
                for (int i = 0; i < logCount; i++)
                {
                    RoundMove m = allCandidates[i];
                    Debug.Log(
                        "EvaluateAIMove: ranked " + i +
                        " => word " + m.word +
                        ", score " + m.score +
                        ", signature " + GetMoveSignature(m)
                    );
                }

                bestMove = allCandidates[0];
            }
        }

        if (bestMove == null)
        {
            Debug.LogWarning("EvaluateAIMove: No legal move found. Returning PASS move.");
            bestMove = new RoundMove
            {
                isHuman = false,
                isValid = false,
                score = 0,
                word = string.Empty,
                timeUsed = 70f,
                placedTiles = null,
                simulatedTiles = new List<SimPlacedTile>()
            };
        }
        else
        {
            bestMove.isHuman = false;
            bestMove.timeUsed = 70f;

            Debug.Log(
                "EvaluateAIMove: FINAL chosen AI move => word " +
                (string.IsNullOrEmpty(bestMove.word) ? "<EMPTY>" : bestMove.word) +
                ", score " + bestMove.score +
                ", isValid = " + bestMove.isValid
            );
        }

        Debug.Log("===== EvaluateAIMove END =====");
        return bestMove;
    }

    private RoundMove FindBestConnectedPlacement(List<LetterInfo> aiTiles, BonusTile[,] aiBonusBoard)
    {
        Debug.Log("===== FindBestConnectedPlacement START =====");

        if (aiTiles == null || aiTiles.Count == 0)
        {
            Debug.LogWarning("AI has no tiles.");
            return null;
        }

        List<string> rackLetters = new List<string>();
        foreach (LetterInfo t in aiTiles)
        {
            rackLetters.Add(t.letter);
        }
        string rackString = string.Join(",", rackLetters.ToArray());
        Debug.Log("FindBestConnectedPlacement: AI rack = [" + rackString + "]");

        List<string> possibleWords = FindPossibleAIWords(aiTiles);
        Debug.Log("Connected-placement rack-only word count = " + possibleWords.Count);

        List<string> boardWords = CollectExistingBoardWords();
        Debug.Log("Existing board words count = " + boardWords.Count);

        HashSet<string> uniqueWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string w in possibleWords)
        {
            if (!string.IsNullOrEmpty(w))
                uniqueWords.Add(w.ToUpper());
        }

        foreach (string baseWord in boardWords)
        {
            if (string.IsNullOrEmpty(baseWord))
                continue;

            List<string> extensions = FindExtensionsForBaseWord(baseWord, aiTiles);
            Debug.Log(
                "Found " + extensions.Count +
                " extension word(s) for base word '" + baseWord + "'"
            );

            foreach (string ext in extensions)
            {
                if (string.IsNullOrEmpty(ext))
                    continue;

                string upperExt = ext.ToUpper();
                if (!uniqueWords.Contains(upperExt))
                {
                    uniqueWords.Add(upperExt);
                    possibleWords.Add(upperExt);
                }
            }
        }

        Debug.Log("Total connected-placement possible words (rack + extensions) = " + possibleWords.Count);

        RoundMove bestMove = null;
        int candidateAttempts = 0;
        int validCandidates = 0;

        for (int boardRow = 1; boardRow <= boardSizeX; boardRow++)
        {
            for (int boardCol = 1; boardCol <= boardSizeY; boardCol++)
            {
                LetterInfo anchorTile = validatedBoardTiles[boardRow, boardCol];
                if (anchorTile == null || string.IsNullOrEmpty(anchorTile.letter))
                    continue;

                char anchorChar = char.ToUpper(anchorTile.letter[0]);

                Debug.Log("Testing anchor tile '" + anchorChar + "' at [" + boardRow + "," + boardCol + "]");

                foreach (string word in possibleWords)
                {
                    if (string.IsNullOrEmpty(word))
                        continue;

                    string upperWord = word.ToUpper();

                    for (int letterIndex = 0; letterIndex < upperWord.Length; letterIndex++)
                    {
                        if (upperWord[letterIndex] != anchorChar)
                            continue;

                        int horizontalStartRow = boardRow;
                        int horizontalStartCol = boardCol - letterIndex;

                        if (horizontalStartCol >= 1 &&
                            horizontalStartCol + upperWord.Length - 1 <= boardSizeY)
                        {
                            candidateAttempts++;

                            RoundMove horizontalCandidate = TryBuildConnectedAIMove(
                                upperWord,
                                horizontalStartRow,
                                horizontalStartCol,
                                TilePlacement.Horizontal,
                                aiTiles,
                                aiBonusBoard
                            );

                            if (horizontalCandidate != null && horizontalCandidate.isValid)
                            {
                                validCandidates++;
                                Debug.Log(
                                    "Valid anchored H candidate => word " + horizontalCandidate.word +
                                    ", row " + horizontalStartRow +
                                    ", startCol " + horizontalStartCol +
                                    ", score " + horizontalCandidate.score
                                );

                                if (bestMove == null || IsBetterAIMove(horizontalCandidate, bestMove))
                                {
                                    bestMove = horizontalCandidate;
                                    Debug.Log(
                                        "New best connected AI move => " +
                                        bestMove.word + " score " + bestMove.score
                                    );
                                }
                            }
                        }

                        int verticalStartRow = boardRow - letterIndex;
                        int verticalStartCol = boardCol;

                        if (verticalStartRow >= 1 &&
                            verticalStartRow + upperWord.Length - 1 <= boardSizeX)
                        {
                            candidateAttempts++;

                            RoundMove verticalCandidate = TryBuildConnectedAIMove(
                                upperWord,
                                verticalStartRow,
                                verticalStartCol,
                                TilePlacement.Vertical,
                                aiTiles,
                                aiBonusBoard
                            );

                            if (verticalCandidate != null && verticalCandidate.isValid)
                            {
                                validCandidates++;
                                Debug.Log(
                                    "Valid anchored V candidate => word " + verticalCandidate.word +
                                    ", startRow " + verticalStartRow +
                                    ", col " + verticalStartCol +
                                    ", score " + verticalCandidate.score
                                );

                                if (bestMove == null || IsBetterAIMove(verticalCandidate, bestMove))
                                {
                                    bestMove = verticalCandidate;
                                    Debug.Log(
                                        "New best connected AI move => " +
                                        bestMove.word + " score " + bestMove.score
                                    );
                                }
                            }
                        }
                    }
                }
            }
        }

        Debug.Log("Connected-placement candidate attempts = " + candidateAttempts);
        Debug.Log("Connected-placement valid candidates = " + validCandidates);

        if (bestMove == null)
            Debug.LogWarning("FindBestConnectedPlacement found no legal connected move.");
        else
            Debug.Log("FindBestConnectedPlacement chose word " + bestMove.word + " score " + bestMove.score);

        Debug.Log("===== FindBestConnectedPlacement END =====");
        return bestMove;
    }

    private List<string> FindPossibleAIWords(List<LetterInfo> aiTiles)
    {
        List<string> possibleWords = new List<string>();

        foreach (string rawWord in scrabbleWords)
        {
            if (string.IsNullOrWhiteSpace(rawWord))
                continue;

            string word = rawWord.Trim().ToUpper();

            if (word.Length < 2)
                continue;

            if (word.Length > aiTiles.Count)
                continue;

            if (CanBuildWordFromTiles(word, aiTiles))
                possibleWords.Add(word);
        }

        return possibleWords;
    }

    private bool CanBuildWordFromTiles(string word, List<LetterInfo> aiTiles)
    {
        Dictionary<char, int> availableLetters = new Dictionary<char, int>();

        foreach (LetterInfo tile in aiTiles)
        {
            if (tile == null || string.IsNullOrEmpty(tile.letter))
                continue;

            char c = char.ToUpper(tile.letter[0]);
            if (!availableLetters.ContainsKey(c))
                availableLetters[c] = 0;

            availableLetters[c]++;
        }

        foreach (char c in word)
        {
            if (!availableLetters.ContainsKey(c) || availableLetters[c] <= 0)
                return false;

            availableLetters[c]--;
        }

        return true;
    }

    private RoundMove FindBestFirstTurnPlacement(string word, List<LetterInfo> aiTiles, BonusTile[,] aiBonusBoard)
    {
        //Debug.Log("=== FindBestFirstTurnPlacement START for word '" + word + "' ===");

        RoundMove bestPlacement = null;

        if (string.IsNullOrEmpty(word))
        {
          //  Debug.LogWarning("FindBestFirstTurnPlacement: word is null or empty.");
            return null;
        }

        int wordLength = word.Length;
        int candidateAttempts = 0;
        int validCandidates = 0;

        // Horizontal placements
        for (int row = 1; row <= boardSizeX; row++)
        {
            for (int startCol = 1; startCol <= boardSizeY - wordLength + 1; startCol++)
            {
                candidateAttempts++;

                RoundMove candidate = ScoreAIFirstTurnPlacement(
                    word,
                    row,
                    startCol,
                    TilePlacement.Horizontal,
                    aiTiles,
                    aiBonusBoard
                );

                if (candidate == null || !candidate.isValid)
                    continue;

                validCandidates++;
                /*Debug.Log(
                    "FindBestFirstTurnPlacement: VALID H candidate for '" + word +
                    "' at [row=" + row + ", startCol=" + startCol +
                    "], score " + candidate.score
                );*/

                if (bestPlacement == null || IsBetterAIMove(candidate, bestPlacement))
                {
                    bestPlacement = candidate;
                    /*Debug.Log(
                        "FindBestFirstTurnPlacement: NEW BEST H placement for '" + word +
                        "' => score " + bestPlacement.score +
                        " at [row=" + row + ", startCol=" + startCol + "]"
                    );*/
                }
            }
        }

        // Vertical placements
        for (int col = 1; col <= boardSizeY; col++)
        {
            for (int startRow = 1; startRow <= boardSizeX - wordLength + 1; startRow++)
            {
                candidateAttempts++;

                RoundMove candidate = ScoreAIFirstTurnPlacement(
                    word,
                    startRow,
                    col,
                    TilePlacement.Vertical,
                    aiTiles,
                    aiBonusBoard
                );

                if (candidate == null || !candidate.isValid)
                    continue;

                validCandidates++;
                /*Debug.Log(
                    "FindBestFirstTurnPlacement: VALID V candidate for '" + word +
                    "' at [startRow=" + startRow + ", col=" + col +
                    "], score " + candidate.score
                );*/

                if (bestPlacement == null || IsBetterAIMove(candidate, bestPlacement))
                {
                    bestPlacement = candidate;
                    /*Debug.Log(
                        "FindBestFirstTurnPlacement: NEW BEST V placement for '" + word +
                        "' => score " + bestPlacement.score +
                        " at [startRow=" + startRow + ", col=" + col + "]"
                    );*/
                }
            }
        }

        /*Debug.Log("FindBestFirstTurnPlacement: candidateAttempts = " + candidateAttempts +
                  ", validCandidates = " + validCandidates);*/

        if (bestPlacement == null)
            Debug.LogWarning("FindBestFirstTurnPlacement: No valid placement found for word '" + word + "'.");
        else
            Debug.Log("FindBestFirstTurnPlacement: BEST placement for word '" + word +
                      "' => score " + bestPlacement.score);

        Debug.Log("=== FindBestFirstTurnPlacement END for word '" + word + "' ===");
        return bestPlacement;
    }

    private RoundMove ScoreAIFirstTurnPlacement(
    string word,
    int startRow,
    int startCol,
    TilePlacement orientation,
    List<LetterInfo> aiTiles,
    BonusTile[,] aiBonusBoard)
    {
        if (string.IsNullOrEmpty(word))
            return null;

        List<SimPlacedTile> placedTiles = new List<SimPlacedTile>();
        List<LetterInfo> lettersInWord = new List<LetterInfo>();
        List<LetterInfo> availableTiles = new List<LetterInfo>(aiTiles);

        int totalLetterPoints = 0;
        int wordMultiplier = 1;

        for (int i = 0; i < word.Length; i++)
        {
            char neededChar = char.ToUpper(word[i]);
            LetterInfo matchingTile = null;

            for (int t = 0; t < availableTiles.Count; t++)
            {
                if (availableTiles[t] == null || string.IsNullOrEmpty(availableTiles[t].letter))
                    continue;

                if (char.ToUpper(availableTiles[t].letter[0]) == neededChar)
                {
                    matchingTile = availableTiles[t];
                    availableTiles.RemoveAt(t);
                    break;
                }
            }

            if (matchingTile == null)
                return null;

            lettersInWord.Add(matchingTile);

            int row = startRow;
            int col = startCol;

            if (orientation == TilePlacement.Horizontal)
                col += i;
            else if (orientation == TilePlacement.Vertical)
                row += i;
            else
                return null;

            int bonusRow = row - 1;
            int bonusCol = col - 1;

            int letterPoints = matchingTile.points;

            if (bonusRow >= 0 && bonusRow < aiBonusBoard.GetLength(0) &&
                bonusCol >= 0 && bonusCol < aiBonusBoard.GetLength(1))
            {
                BonusTile bonusTile = aiBonusBoard[bonusCol, bonusRow];

                if (bonusTile != null)
                {
                    switch (bonusTile.bonusType)
                    {
                        case BonusType.DoubleLetter:
                            letterPoints *= 2;
                            break;

                        case BonusType.TripleLetter:
                            letterPoints *= 3;
                            break;

                        case BonusType.DoubleWord:
                            wordMultiplier *= 2;
                            break;

                        case BonusType.TripleWord:
                            wordMultiplier *= 3;
                            break;
                    }
                }
            }

            totalLetterPoints += letterPoints;

            SimPlacedTile placedTile = new SimPlacedTile();
            placedTile.letterInfo = matchingTile;
            placedTile.letterPosition = new LetterPosition(row, col);
            placedTiles.Add(placedTile);
        }

        int finalScore = totalLetterPoints * wordMultiplier;

        if (placedTiles.Count == maxHandSize)
            finalScore += 50;

        RoundMove move = new RoundMove();
        move.isHuman = false;
        move.isValid = true;
        move.word = word;
        move.score = finalScore;
        move.timeUsed = 70f;
        move.placedTiles = null;
        move.simulatedTiles = placedTiles;

        return move;
    }

    private bool IsBetterAIMove(RoundMove candidate, RoundMove currentBest)
    {
        if (candidate == null)
            return false;

        if (currentBest == null)
            return true;

        // 1) Prefer higher total move score
        if (candidate.score > currentBest.score)
            return true;
        if (candidate.score < currentBest.score)
            return false;

        // 2) If scores tie, prefer longer word
        int candidateLength = string.IsNullOrEmpty(candidate.word) ? 0 : candidate.word.Length;
        int bestLength = string.IsNullOrEmpty(currentBest.word) ? 0 : currentBest.word.Length;

        if (candidateLength > bestLength)
            return true;
        if (candidateLength < bestLength)
            return false;

        // 3) If still tied, prefer stronger premium usage
        int candidatePremiumRank = GetMovePremiumRank(candidate);
        int bestPremiumRank = GetMovePremiumRank(currentBest);

        if (candidatePremiumRank > bestPremiumRank)
            return true;
        if (candidatePremiumRank < bestPremiumRank)
            return false;

        return false;
    }

    private int GetMovePremiumRank(RoundMove move)
    {
        if (move == null || move.simulatedTiles == null || boardBonusTiles == null)
            return 0;

        int bestRank = 0;

        foreach (SimPlacedTile tile in move.simulatedTiles)
        {
            int bonusRow = tile.letterPosition.RowX - 1;
            int bonusCol = tile.letterPosition.ColY - 1;

            if (bonusRow < 0 || bonusRow >= boardBonusTiles.GetLength(1) ||
                bonusCol < 0 || bonusCol >= boardBonusTiles.GetLength(0))
                continue;

            BonusTile bonusTile = boardBonusTiles[bonusCol, bonusRow];
            if (bonusTile == null)
                continue;

            switch (bonusTile.bonusType)
            {
                case BonusType.TripleWord: bestRank = Mathf.Max(bestRank, 4); break;
                case BonusType.DoubleWord: bestRank = Mathf.Max(bestRank, 3); break;
                case BonusType.TripleLetter: bestRank = Mathf.Max(bestRank, 2); break;
                case BonusType.DoubleLetter: bestRank = Mathf.Max(bestRank, 1); break;
            }
        }

        return bestRank;
    }

    private void RebuildHandUIFromLogicalHand()
    {
        Debug.Log("===== RebuildHandUIFromLogicalHand START =====");

        if (playerHandTiles == null)
        {
            Debug.LogError("playerHandTiles is null in RebuildHandUIFromLogicalHand.");
            return;
        }

        // Clear all visible tiles from the hand UI
        Singleton.Instance.UIManager.RemoveAllHandTiles();

        // Recreate hand visuals from the logical hand list
        foreach (var tile in playerHandTiles)
        {
            if (tile == null)
            {
                Debug.LogWarning("Encountered null tile in playerHandTiles while rebuilding UI.");
                continue;
            }

            Singleton.Instance.UIManager.AddTileToHand(tile);
            Debug.Log("Re-added tile to UI => " + tile.letter + " (" + tile.points + ")");
        }

        // Update the word list display based on the new hand
        ResetDisplay();

        Debug.Log("Hand UI rebuilt. Logical hand count = " + playerHandTiles.Count);
        Debug.Log("===== RebuildHandUIFromLogicalHand END =====");
    }

    public void ShuffleHand()
    {
        if (playerHandTiles == null || playerHandTiles.Count <= 1)
            return;

        // Return any temporarily placed tiles to hand first so they are included in the shuffle and we don't get duplicates
        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
        {
            Singleton.Instance.UIManager.ReturnTilesToHand();
        }

        // Shuffle playerHandTiles list using Fisher-Yates shuffle algorithm
        for (int i = playerHandTiles.Count - 1; i > 0; i--)
        {
            int r = UnityEngine.Random.Range(0, i + 1);
            LetterInfo temp = playerHandTiles[i];
            playerHandTiles[i] = playerHandTiles[r];
            playerHandTiles[r] = temp;
        }

        // Rebuild UI in the new shuffled order
        RebuildHandUIFromLogicalHand();
        Debug.Log("[GameLogic] Hand shuffled and rebuilt.");
    }

    private void AddRoundWinnerScore(RoundMove winningMove)
    {
        if (winningMove == null || !winningMove.isValid)
            return;

        if (winningMove.isHuman)
            humanTotalScore += winningMove.score;
        else
            aiTotalScore += winningMove.score;

        if (Singleton.Instance.UIManager != null)
            Singleton.Instance.UIManager.UpdateTotalScores(humanTotalScore, aiTotalScore);

        Debug.Log("Total scores => Human: " + humanTotalScore + ", AI: " + aiTotalScore);
    }

    private bool IsGameOver()
    {
        return currentRoundNumber > maxRounds;
    }

    private void EndGame()
    {
        roundFlowActive = false;
        roundRevealStep = 0;
        currentState = TurnState.Busy;

        string finalMessage;

        if (humanTotalScore > aiTotalScore)
        {
            finalMessage = "Game over. Human wins " + humanTotalScore + " to " + aiTotalScore + "!";
        }
        else if (aiTotalScore > humanTotalScore)
        {
            finalMessage = "Game over. AI wins " + aiTotalScore + " to " + humanTotalScore + "!";
        }
        else
        {
            finalMessage = "Game over. It's a tie at " + humanTotalScore + " - " + aiTotalScore + "!";
        }

        Singleton.Instance.UIManager.ShowRoundMessage(finalMessage);

        if (timer != null)
            timer.StopTimer();

        Debug.Log(finalMessage);
    }
    public int GetCurrentRound()
    {
        return currentTurn;
    }

    private List<List<LetterInfo>> CollectAllWordsForAIMove(
TilePlacement mainOrientation,
LetterInfo[,] board,
List<SimPlacedTile> newPlacedTiles)
    {
        List<List<LetterInfo>> wordList = new List<List<LetterInfo>>();

        if (board == null)
        {
            Debug.LogWarning("CollectAllWordsForAIMove: board is null.");
            return wordList;
        }

        if (newPlacedTiles == null || newPlacedTiles.Count == 0)
        {
            Debug.Log("CollectAllWordsForAIMove: no new placed tiles.");
            return wordList;
        }

        Debug.Log("CollectAllWordsForAIMove START. mainOrientation = " + mainOrientation);

        int mainAnchorRow = newPlacedTiles[0].letterPosition.RowX;
        int mainAnchorCol = newPlacedTiles[0].letterPosition.ColY;

        string placedSummary = "";
        foreach (SimPlacedTile simTile in newPlacedTiles)
        {
            if (simTile == null || simTile.letterPosition == null || simTile.letterInfo == null)
                continue;

            placedSummary += "[" +
                             simTile.letterInfo.letter + "@" +
                             simTile.letterPosition.RowX + "," +
                             simTile.letterPosition.ColY + "]";
        }
        Debug.Log("CollectAllWordsForAIMove: newPlacedTiles = " + placedSummary);

        if (mainOrientation == TilePlacement.Horizontal)
        {
            int minCol = newPlacedTiles[0].letterPosition.ColY;
            int row = newPlacedTiles[0].letterPosition.RowX;

            foreach (SimPlacedTile simTile in newPlacedTiles)
            {
                if (simTile == null || simTile.letterPosition == null)
                    continue;

                if (simTile.letterPosition.ColY < minCol)
                {
                    minCol = simTile.letterPosition.ColY;
                }
            }

            mainAnchorRow = row;
            mainAnchorCol = minCol;

            Debug.Log("CollectAllWordsForAIMove: horizontal main anchor = " + mainAnchorRow + "," + mainAnchorCol);

            int firstCol = GetFirstLetterIndex(TilePlacement.Horizontal, board, mainAnchorRow, mainAnchorCol);
            Debug.Log("CollectAllWordsForAIMove: horizontal firstCol = " + firstCol);

            List<LetterInfo> mainWord = GetWordFromBoard(TilePlacement.Horizontal, board, mainAnchorRow, firstCol);

            if (mainWord != null)
            {
                string debugWord = "";
                foreach (LetterInfo tile in mainWord)
                {
                    if (tile != null && !string.IsNullOrEmpty(tile.letter))
                        debugWord += tile.letter;
                }

                Debug.Log("CollectAllWordsForAIMove: horizontal main word raw = " + debugWord +
                          ", count = " + mainWord.Count);
            }

            if (mainWord != null && mainWord.Count > 1)
            {
                AddWordIfNotDuplicate(wordList, mainWord);

                string debugWord = "";
                foreach (LetterInfo tile in mainWord)
                {
                    if (tile != null && !string.IsNullOrEmpty(tile.letter))
                        debugWord += tile.letter;
                }

                Debug.Log("AI main word added = " + debugWord);
            }
            else
            {
                Debug.LogWarning("CollectAllWordsForAIMove: failed to assemble horizontal main word.");
            }
        }
        else
        {
            int minRow = newPlacedTiles[0].letterPosition.RowX;
            int col = newPlacedTiles[0].letterPosition.ColY;

            foreach (SimPlacedTile simTile in newPlacedTiles)
            {
                if (simTile == null || simTile.letterPosition == null)
                    continue;

                if (simTile.letterPosition.RowX < minRow)
                {
                    minRow = simTile.letterPosition.RowX;
                }
            }

            mainAnchorRow = minRow;
            mainAnchorCol = col;

            Debug.Log("CollectAllWordsForAIMove: vertical main anchor = " + mainAnchorRow + "," + mainAnchorCol);

            int firstRow = GetFirstLetterIndex(TilePlacement.Vertical, board, mainAnchorRow, mainAnchorCol);
            Debug.Log("CollectAllWordsForAIMove: vertical firstRow = " + firstRow);

            List<LetterInfo> mainWord = GetWordFromBoard(TilePlacement.Vertical, board, firstRow, mainAnchorCol);

            if (mainWord != null)
            {
                string debugWord = "";
                foreach (LetterInfo tile in mainWord)
                {
                    if (tile != null && !string.IsNullOrEmpty(tile.letter))
                        debugWord += tile.letter;
                }

                Debug.Log("CollectAllWordsForAIMove: vertical main word raw = " + debugWord +
                          ", count = " + mainWord.Count);
            }

            if (mainWord != null && mainWord.Count > 1)
            {
                AddWordIfNotDuplicate(wordList, mainWord);

                string debugWord = "";
                foreach (LetterInfo tile in mainWord)
                {
                    if (tile != null && !string.IsNullOrEmpty(tile.letter))
                        debugWord += tile.letter;
                }

                Debug.Log("AI main word added = " + debugWord);
            }
            else
            {
                Debug.LogWarning("CollectAllWordsForAIMove: failed to assemble vertical main word.");
            }
        }

        foreach (SimPlacedTile simTile in newPlacedTiles)
        {
            if (simTile == null || simTile.letterPosition == null)
                continue;

            int row = simTile.letterPosition.RowX;
            int col = simTile.letterPosition.ColY;

            if (mainOrientation == TilePlacement.Horizontal)
            {
                bool hasCross =
                    (row > 1 && board[row - 1, col] != null) ||
                    (row < boardSizeX && board[row + 1, col] != null);

                if (hasCross)
                {
                    int firstRow = GetFirstLetterIndex(TilePlacement.Vertical, board, row, col);
                    List<LetterInfo> crossWord = GetWordFromBoard(TilePlacement.Vertical, board, firstRow, col);

                    if (crossWord != null)
                    {
                        string debugWord = "";
                        foreach (LetterInfo tile in crossWord)
                        {
                            if (tile != null && !string.IsNullOrEmpty(tile.letter))
                                debugWord += tile.letter;
                        }

                        Debug.Log("CollectAllWordsForAIMove: vertical cross raw = " + debugWord +
                                  " at " + row + "," + col +
                                  ", count = " + crossWord.Count);
                    }

                    if (crossWord != null && crossWord.Count > 1)
                    {
                        AddWordIfNotDuplicate(wordList, crossWord);

                        string debugWord = "";
                        foreach (LetterInfo tile in crossWord)
                        {
                            if (tile != null && !string.IsNullOrEmpty(tile.letter))
                                debugWord += tile.letter;
                        }

                        Debug.Log("AI cross word added = " + debugWord);
                    }
                }
            }
            else
            {
                bool hasCross =
                    (col > 1 && board[row, col - 1] != null) ||
                    (col < boardSizeY && board[row, col + 1] != null);

                if (hasCross)
                {
                    int firstCol = GetFirstLetterIndex(TilePlacement.Horizontal, board, row, col);
                    List<LetterInfo> crossWord = GetWordFromBoard(TilePlacement.Horizontal, board, row, firstCol);

                    if (crossWord != null)
                    {
                        string debugWord = "";
                        foreach (LetterInfo tile in crossWord)
                        {
                            if (tile != null && !string.IsNullOrEmpty(tile.letter))
                                debugWord += tile.letter;
                        }

                        Debug.Log("CollectAllWordsForAIMove: horizontal cross raw = " + debugWord +
                                  " at " + row + "," + col +
                                  ", count = " + crossWord.Count);
                    }

                    if (crossWord != null && crossWord.Count > 1)
                    {
                        AddWordIfNotDuplicate(wordList, crossWord);

                        string debugWord = "";
                        foreach (LetterInfo tile in crossWord)
                        {
                            if (tile != null && !string.IsNullOrEmpty(tile.letter))
                                debugWord += tile.letter;
                        }

                        Debug.Log("AI cross word added = " + debugWord);
                    }
                }
            }
        }

        Debug.Log("CollectAllWordsForAIMove END. word count = " + wordList.Count);
        return wordList;
    }

    private void DebugDumpValidatedBoard()
    {
        Debug.Log("=== VALIDATED BOARD DUMP ===");
        for (int r = 0; r < validatedBoardTiles.GetLength(0); r++)
        {
            string rowStr = "row " + r + " => ";
            for (int c = 0; c < validatedBoardTiles.GetLength(1); c++)
            {
                var tile = validatedBoardTiles[r, c];
                rowStr += tile == null ? "[  ]" : "[" + tile.letter + "]";
            }
            Debug.Log(rowStr);
        }
    }

    private bool CanBuildWordFromBaseAndRack(string word, string baseWord, List<LetterInfo> rackTiles)
    {
        if (string.IsNullOrEmpty(word))
            return false;

        // Build multiset of available letters: baseWord + rack letters
        Dictionary<char, int> available = new Dictionary<char, int>();

        void AddChar(char c)
        {
            char upper = char.ToUpper(c);
            if (!available.ContainsKey(upper))
                available[upper] = 0;
            available[upper]++;
        }

        if (!string.IsNullOrEmpty(baseWord))
        {
            foreach (char c in baseWord)
                AddChar(c);
        }

        if (rackTiles != null)
        {
            foreach (var tile in rackTiles)
            {
                if (tile == null || string.IsNullOrEmpty(tile.letter))
                    continue;
                AddChar(tile.letter[0]);
            }
        }

        // Consume letters required by target word
        foreach (char c in word)
        {
            char upper = char.ToUpper(c);
            if (!available.ContainsKey(upper) || available[upper] <= 0)
                return false;
            available[upper]--;
        }

        return true;
    }

    private List<string> FindExtensionsForBaseWord(string baseWord, List<LetterInfo> rackTiles)
    {
        List<string> extensions = new List<string>();

        if (string.IsNullOrEmpty(baseWord))
            return extensions;

        string baseUpper = baseWord.ToUpper();

        foreach (string dictWord in scrabbleWords)
        {
            if (string.IsNullOrEmpty(dictWord))
                continue;

            string w = dictWord.ToUpper();

            // Must contain the base word and be strictly longer
            if (!w.Contains(baseUpper))
                continue;

            if (w.Length <= baseUpper.Length)
                continue;

            // Check if baseWord + rackTiles can spell dictWord
            if (!CanBuildWordFromBaseAndRack(w, baseUpper, rackTiles))
                continue;

            extensions.Add(w);
        }

        return extensions;
    }

    private List<string> CollectExistingBoardWords()
    {
        List<string> words = new List<string>();

        // Horizontal scan
        for (int row = 1; row <= boardSizeX; row++)
        {
            int col = 1;
            while (col <= boardSizeY)
            {
                if (validatedBoardTiles[row, col] != null &&
                    !string.IsNullOrEmpty(validatedBoardTiles[row, col].letter))
                {
                    string word = "";
                    int startCol = col;

                    while (col <= boardSizeY &&
                           validatedBoardTiles[row, col] != null &&
                           !string.IsNullOrEmpty(validatedBoardTiles[row, col].letter))
                    {
                        word += validatedBoardTiles[row, col].letter.ToUpper();
                        col++;
                    }

                    if (word.Length >= 2)
                        words.Add(word);
                }
                else
                {
                    col++;
                }
            }
        }

        // Vertical scan
        for (int col = 1; col <= boardSizeY; col++)
        {
            int row = 1;
            while (row <= boardSizeX)
            {
                if (validatedBoardTiles[row, col] != null &&
                    !string.IsNullOrEmpty(validatedBoardTiles[row, col].letter))
                {
                    string word = "";
                    int startRow = row;

                    while (row <= boardSizeX &&
                           validatedBoardTiles[row, col] != null &&
                           !string.IsNullOrEmpty(validatedBoardTiles[row, col].letter))
                    {
                        word += validatedBoardTiles[row, col].letter.ToUpper();
                        row++;
                    }

                    if (word.Length >= 2)
                        words.Add(word);
                }
                else
                {
                    row++;
                }
            }
        }

        return words;
    }
    
    private List<RoundMove> FindAllPlacementsForWord(
    string word,
    List<LetterInfo> aiTiles,
    BonusTile[,] aiBonusBoard,
    bool requireConnection)
    {
        List<RoundMove> moves = new List<RoundMove>();

        if (string.IsNullOrEmpty(word))
            return moves;

        for (int boardRow = 1; boardRow <= boardSizeX; boardRow++)
        {
            for (int boardCol = 1; boardCol <= boardSizeY; boardCol++)
            {
                LetterInfo anchorTile = validatedBoardTiles[boardRow, boardCol];
                if (anchorTile == null || string.IsNullOrEmpty(anchorTile.letter))
                    continue;

                char anchorChar = char.ToUpper(anchorTile.letter[0]);

                for (int letterIndex = 0; letterIndex < word.Length; letterIndex++)
                {
                    if (char.ToUpper(word[letterIndex]) != anchorChar)
                        continue;

                    int horizontalStartRow = boardRow;
                    int horizontalStartCol = boardCol - letterIndex;

                    if (horizontalStartCol >= 1 &&
                        horizontalStartCol + word.Length - 1 <= boardSizeY)
                    {
                        RoundMove horizontalCandidate = TryBuildConnectedAIMove(
                            word,
                            horizontalStartRow,
                            horizontalStartCol,
                            TilePlacement.Horizontal,
                            aiTiles,
                            aiBonusBoard
                        );

                        if (horizontalCandidate != null && horizontalCandidate.isValid)
                            AddMoveIfUnique(moves, horizontalCandidate);
                    }

                    int verticalStartRow = boardRow - letterIndex;
                    int verticalStartCol = boardCol;

                    if (verticalStartRow >= 1 &&
                        verticalStartRow + word.Length - 1 <= boardSizeX)
                    {
                        RoundMove verticalCandidate = TryBuildConnectedAIMove(
                            word,
                            verticalStartRow,
                            verticalStartCol,
                            TilePlacement.Vertical,
                            aiTiles,
                            aiBonusBoard
                        );

                        if (verticalCandidate != null && verticalCandidate.isValid)
                            AddMoveIfUnique(moves, verticalCandidate);
                    }
                }
            }
        }

        return moves;
    }
    private void AddUniqueMoves(List<RoundMove> target, List<RoundMove> source)
    {
        if (target == null || source == null)
            return;

        for (int i = 0; i < source.Count; i++)
        {
            RoundMove move = source[i];
            if (move == null || !move.isValid)
                continue;

            target.Add(move);
        }
    }


    private void AddUniqueMoves(List<RoundMove> target, List<RoundMove> source, HashSet<string> seenSignatures)
    {
        if (target == null || source == null || seenSignatures == null)
            return;

        for (int i = 0; i < source.Count; i++)
        {
            RoundMove move = source[i];
            if (move == null || !move.isValid)
                continue;

            string sig = GetAIMoveSignature(move);
            if (string.IsNullOrEmpty(sig))
            {
                Debug.LogWarning(
                    "AddUniqueMoves: skipped valid move with empty signature. word=" +
                    move.word + ", score=" + move.score
                );
                continue;
            }

            if (!seenSignatures.Contains(sig))
            {
                seenSignatures.Add(sig);
                target.Add(move);
            }
        }
    }

    private void AddMoveIfUnique(List<RoundMove> target, RoundMove move)
    {
        if (move == null || !move.isValid)
            return;

        string signature = GetMoveSignature(move);

        for (int i = 0; i < target.Count; i++)
        {
            if (GetMoveSignature(target[i]) == signature)
                return;
        }

        target.Add(move);
    }

    private string GetMoveSignature(RoundMove move)
    {
        if (move == null)
            return "NULL";

        string wordPart = string.IsNullOrEmpty(move.word) ? "" : move.word.ToUpper();
        List<string> coords = new List<string>();

        if (move.simulatedTiles != null)
        {
            foreach (SimPlacedTile simTile in move.simulatedTiles)
            {
                if (simTile == null || simTile.letterPosition == null)
                    continue;

                coords.Add(simTile.letterPosition.RowX + "," + simTile.letterPosition.ColY);
            }
        }

        coords.Sort();
        return wordPart + "|" + string.Join(";", coords.ToArray());
    }

    private int CompareAIMovesBestFirst(RoundMove a, RoundMove b)
    {
        if (a == null && b == null)
            return 0;
        if (a == null)
            return 1;
        if (b == null)
            return -1;

        if (a.score != b.score)
            return b.score.CompareTo(a.score);

        int aLength = string.IsNullOrEmpty(a.word) ? 0 : a.word.Length;
        int bLength = string.IsNullOrEmpty(b.word) ? 0 : b.word.Length;

        if (aLength != bLength)
            return bLength.CompareTo(aLength);

        int aPremium = GetMovePremiumRank(a);
        int bPremium = GetMovePremiumRank(b);

        if (aPremium != bPremium)
            return bPremium.CompareTo(aPremium);

        return 0;
    }

    private int CountWordsFormedByMove(RoundMove move)
    {
        if (move == null || move.simulatedTiles == null || move.simulatedTiles.Count == 0)
            return 0;

        LetterInfo[,] tempBoard = (LetterInfo[,])validatedBoardTiles.Clone();

        foreach (SimPlacedTile simTile in move.simulatedTiles)
        {
            if (simTile == null || simTile.letterInfo == null || simTile.letterPosition == null)
                continue;

            tempBoard[simTile.letterPosition.RowX, simTile.letterPosition.ColY] = simTile.letterInfo;
        }

        TilePlacement orientation = InferMoveOrientation(move);
        List<List<LetterInfo>> allWords = CollectAllWordsForAIMove(
            orientation,
            tempBoard,
            move.simulatedTiles
        );

        return allWords != null ? allWords.Count : 0;
    }
    private TilePlacement InferMoveOrientation(RoundMove move)
    {
        if (move == null || move.simulatedTiles == null || move.simulatedTiles.Count <= 1)
            return TilePlacement.Horizontal;

        int firstRow = move.simulatedTiles[0].letterPosition.RowX;
        int firstCol = move.simulatedTiles[0].letterPosition.ColY;

        bool sameRow = true;

        for (int i = 1; i < move.simulatedTiles.Count; i++)
        {
            if (move.simulatedTiles[i].letterPosition.RowX != firstRow)
                sameRow = false;
        }

        if (sameRow)
            return TilePlacement.Horizontal;

        return TilePlacement.Vertical;
    }

    private int CountAIMoveScore(List<List<LetterInfo>> allWords, List<SimPlacedTile> newPlacedTiles)
    {
        int totalScore = 0;

        if (allWords == null || newPlacedTiles == null)
            return 0;

        List<PlacedTile> placedThisTurn = new List<PlacedTile>();

        foreach (SimPlacedTile simTile in newPlacedTiles)
        {
            if (simTile == null || simTile.letterInfo == null || simTile.letterPosition == null)
                continue;

            PlacedTile placedTile = new PlacedTile();
            placedTile.letterInfo = simTile.letterInfo;
            placedTile.letterPosition = simTile.letterPosition;
            placedThisTurn.Add(placedTile);
        }

        foreach (List<LetterInfo> singleWord in allWords)
        {
            int wordScore = CountWordPoints(singleWord, placedThisTurn);
            totalScore += wordScore;
        }

        return totalScore;
    }

    private void AddWordIfNotDuplicate(List<List<LetterInfo>> wordList, List<LetterInfo> candidateWord)
    {
        if (candidateWord == null || candidateWord.Count == 0)
            return;

        string candidate = "";
        foreach (LetterInfo tile in candidateWord)
        {
            if (tile != null && !string.IsNullOrEmpty(tile.letter))
                candidate += tile.letter.ToUpper();
        }

        foreach (List<LetterInfo> existingWord in wordList)
        {
            string existing = "";
            foreach (LetterInfo tile in existingWord)
            {
                if (tile != null && !string.IsNullOrEmpty(tile.letter))
                    existing += tile.letter.ToUpper();
            }

            if (existing == candidate)
                return;
        }

        wordList.Add(candidateWord);
    }
    private string GetSimTileLetter(SimPlacedTile tile)
    {
        if (tile == null || tile.letterInfo == null || string.IsNullOrEmpty(tile.letterInfo.letter))
            return string.Empty;

        return tile.letterInfo.letter;
    }

    private int GetSimTileRow(SimPlacedTile tile)
    {
        if (tile == null || tile.letterPosition == null)
            return -1;

        return tile.letterPosition.RowX;
    }

    private int GetSimTileCol(SimPlacedTile tile)
    {
        if (tile == null || tile.letterPosition == null)
            return -1;

        return tile.letterPosition.ColY;
    }
    private bool SimTileMatchesBoardCell(SimPlacedTile tile, int row, int col)
    {
        return GetSimTileRow(tile) == row && GetSimTileCol(tile) == col;
    }

    private SimPlacedTile FindSimTileAt(List<SimPlacedTile> tiles, int row, int col)
    {
        if (tiles == null)
            return null;

        for (int i = 0; i < tiles.Count; i++)
        {
            if (SimTileMatchesBoardCell(tiles[i], row, col))
                return tiles[i];
        }

        return null;
    }

    private string GetAIMoveSignature(RoundMove move)
    {
        if (move == null)
            return string.Empty;

        List<string> parts = new List<string>();

        if (move.simulatedTiles != null && move.simulatedTiles.Count > 0)
        {
            for (int i = 0; i < move.simulatedTiles.Count; i++)
            {
                SimPlacedTile tile = move.simulatedTiles[i];
                if (tile == null || tile.letterInfo == null || tile.letterPosition == null)
                    continue;

                string letter = string.IsNullOrEmpty(tile.letterInfo.letter)
                    ? "_"
                    : tile.letterInfo.letter.ToUpper();

                parts.Add(
                    tile.letterPosition.RowX + "_" +
                    tile.letterPosition.ColY + "_" +
                    letter
                );
            }
        }
        else if (move.placedTiles != null && move.placedTiles.Count > 0)
        {
            for (int i = 0; i < move.placedTiles.Count; i++)
            {
                PlacedTile tile = move.placedTiles[i];
                if (tile == null || tile.letterInfo == null || tile.letterPosition == null)
                    continue;

                string letter = string.IsNullOrEmpty(tile.letterInfo.letter)
                    ? "_"
                    : tile.letterInfo.letter.ToUpper();

                parts.Add(
                    tile.letterPosition.RowX + "_" +
                    tile.letterPosition.ColY + "_" +
                    letter
                );
            }
        }

        if (parts.Count == 0)
            return string.Empty;

        parts.Sort();
        return string.Join("|", parts.ToArray());
    }

    private bool MoveCreatesMultipleWords(RoundMove move)
    {
        if (move == null || !move.isValid)
            return false;

        List<string> formedWords = GetAllWordsFromMove(move);
        return formedWords.Count >= 2;
    }

    private string GetMoveWordsDebugString(RoundMove move)
    {
        List<string> formedWords = GetAllWordsFromMove(move);

        if (formedWords == null || formedWords.Count == 0)
            return "<none>";

        return string.Join(",", formedWords.ToArray());
    }

    private List<string> GetAllWordsFromMove(RoundMove move)
    {
        List<string> words = new List<string>();

        if (move == null)
            return words;

        if (move.simulatedTiles == null || move.simulatedTiles.Count == 0)
            return words;

        for (int i = 0; i < move.simulatedTiles.Count; i++)
        {
            SimPlacedTile sim = move.simulatedTiles[i];
            if (sim == null || sim.letterPosition == null)
                continue;

            int x = sim.letterPosition.RowX;
            int y = sim.letterPosition.ColY;

            string horizontalWord = BuildWordFromSimulatedMove(move, x, y, TilePlacement.Horizontal);
            if (!string.IsNullOrEmpty(horizontalWord) && horizontalWord.Length >= 2)
                AddWordIfMissing(words, horizontalWord.ToUpper());

            string verticalWord = BuildWordFromSimulatedMove(move, x, y, TilePlacement.Vertical);
            if (!string.IsNullOrEmpty(verticalWord) && verticalWord.Length >= 2)
                AddWordIfMissing(words, verticalWord.ToUpper());
        }

        return words;
    }
    private void AddWordIfMissing(List<string> list, string word)
    {
        if (list == null || string.IsNullOrEmpty(word))
            return;

        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], word, StringComparison.OrdinalIgnoreCase))
                return;
        }

        list.Add(word);
    }

    private string BuildWordFromSimulatedMove(RoundMove move, int boardRow, int boardCol, TilePlacement direction)
    {
        if (!HasTileAtForMove(move, boardRow, boardCol))
            return string.Empty;

        int startRow = boardRow;
        int startCol = boardCol;

        if (direction == TilePlacement.Horizontal)
        {
            while (HasTileAtForMove(move, startRow, startCol - 1))
                startCol--;
        }
        else if (direction == TilePlacement.Vertical)
        {
            while (HasTileAtForMove(move, startRow - 1, startCol))
                startRow--;
        }
        else
        {
            return string.Empty;
        }

        string word = string.Empty;
        int row = startRow;
        int col = startCol;

        while (HasTileAtForMove(move, row, col))
        {
            string letter = GetLetterAtForMove(move, row, col);
            if (string.IsNullOrEmpty(letter))
                break;

            word += letter.ToUpper();

            if (direction == TilePlacement.Horizontal)
                col++;
            else
                row++;
        }

        return word;
    }

    private bool HasTileAtForMove(RoundMove move, int boardX, int boardY)
    {
        if (GetSimulatedTileAt(move, boardX, boardY) != null)
            return true;

        if (boardX < 1 || boardX > boardSizeX || boardY < 1 || boardY > boardSizeY)
            return false;

        return validatedBoardTiles[boardX, boardY] != null;
    }

    private string GetLetterAtForMove(RoundMove move, int boardX, int boardY)
    {
        SimPlacedTile simTile = GetSimulatedTileAt(move, boardX, boardY);
        if (simTile != null && simTile.letterInfo != null)
            return simTile.letterInfo.letter;

        if (boardX < 1 || boardX > boardSizeX || boardY < 1 || boardY > boardSizeY)
            return string.Empty;

        LetterInfo boardTile = validatedBoardTiles[boardX, boardY];
        if (boardTile == null)
            return string.Empty;

        return boardTile.letter;
    }

    private SimPlacedTile GetSimulatedTileAt(RoundMove move, int boardX, int boardY)
    {
        if (move == null || move.simulatedTiles == null)
            return null;

        for (int i = 0; i < move.simulatedTiles.Count; i++)
        {
            SimPlacedTile sim = move.simulatedTiles[i];
            if (sim == null || sim.letterPosition == null)
                continue;

            if (sim.letterPosition.RowX == boardX &&
                sim.letterPosition.ColY == boardY)
            {
                return sim;
            }
        }

        return null;
    }
    private void EnsureAIGaddagReady()
    {
        if (aiGaddagReady && aiGaddagLexicon != null)
            return;

        aiGaddagLexicon = new GaddagLexicon();

        if (scrabbleWords != null)
        {
            for (int i = 0; i < scrabbleWords.Count; i++)
            {
                string word = scrabbleWords[i];
                if (string.IsNullOrWhiteSpace(word))
                    continue;

                aiGaddagLexicon.AddWord(word.Trim().ToUpper());
            }
        }

        aiGaddagReady = true;
        Debug.Log("EnsureAIGaddagReady: GADDAG built from scrabbleWords. Count = " +
                  (scrabbleWords != null ? scrabbleWords.Count : 0));
    }

    private bool IsWordInDictionary(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        return scrabbleWordSet != null && scrabbleWordSet.Contains(word.ToUpper());
    }

    private HashSet<char> BuildCrossCheckSet(int row, int col, TilePlacement mainPlacement)
    {
        HashSet<char> result = new HashSet<char>();

        if (row < 1 || row > boardSizeX || col < 1 || col > boardSizeY)
            return result;

        if (validatedBoardTiles[row, col] != null)
            return result;

        string before = "";
        string after = "";

        if (mainPlacement == TilePlacement.Horizontal)
        {
            int scanRow = row - 1;
            while (scanRow >= 1 && validatedBoardTiles[scanRow, col] != null)
            {
                before = validatedBoardTiles[scanRow, col].letter.ToUpper() + before;
                scanRow--;
            }

            scanRow = row + 1;
            while (scanRow <= boardSizeX && validatedBoardTiles[scanRow, col] != null)
            {
                after += validatedBoardTiles[scanRow, col].letter.ToUpper();
                scanRow++;
            }
        }
        else if (mainPlacement == TilePlacement.Vertical)
        {
            int scanCol = col - 1;
            while (scanCol >= 1 && validatedBoardTiles[row, scanCol] != null)
            {
                before = validatedBoardTiles[row, scanCol].letter.ToUpper() + before;
                scanCol--;
            }

            scanCol = col + 1;
            while (scanCol <= boardSizeY && validatedBoardTiles[row, scanCol] != null)
            {
                after += validatedBoardTiles[row, scanCol].letter.ToUpper();
                scanCol++;
            }
        }

        if (before.Length == 0 && after.Length == 0)
        {
            for (char ch = 'A'; ch <= 'Z'; ch++)
                result.Add(ch);

            return result;
        }

        for (char ch = 'A'; ch <= 'Z'; ch++)
        {
            string formed = before + ch + after;
            if (IsWordInDictionary(formed))
                result.Add(ch);
        }

        return result;
    }

    private List<AnchorSquare> BuildAnchorSquares()
    {
        List<AnchorSquare> anchors = new List<AnchorSquare>();

        for (int row = 1; row <= boardSizeX; row++)
        {
            for (int col = 1; col <= boardSizeY; col++)
            {
                if (validatedBoardTiles[row, col] != null)
                    continue;

                if (!HasAdjacentValidatedTile(row, col))
                    continue;

                AnchorSquare anchor = new AnchorSquare();
                anchor.row = row;
                anchor.col = col;
                anchor.horizontalCrossChecks = BuildCrossCheckSet(row, col, TilePlacement.Horizontal);
                anchor.verticalCrossChecks = BuildCrossCheckSet(row, col, TilePlacement.Vertical);

                anchors.Add(anchor);
            }
        }

        return anchors;
    }

    private bool WouldWordCoverAnchor(string word, int startRow, int startCol, TilePlacement placement, int anchorRow, int anchorCol)
    {
        if (string.IsNullOrEmpty(word))
            return false;

        for (int i = 0; i < word.Length; i++)
        {
            int row = startRow;
            int col = startCol;

            if (placement == TilePlacement.Horizontal)
                col += i;
            else if (placement == TilePlacement.Vertical)
                row += i;
            else
                return false;

            if (row == anchorRow && col == anchorCol)
                return true;
        }

        return false;
    }

    private bool PassesAnchorCrossChecks(string word, int startRow, int startCol, TilePlacement placement, AnchorSquare anchor)
    {
        if (string.IsNullOrEmpty(word))
            return false;

        for (int i = 0; i < word.Length; i++)
        {
            int row = startRow;
            int col = startCol;

            if (placement == TilePlacement.Horizontal)
                col += i;
            else if (placement == TilePlacement.Vertical)
                row += i;
            else
                return false;

            if (row < 1 || row > boardSizeX || col < 1 || col > boardSizeY)
                return false;

            char needed = char.ToUpper(word[i]);
            LetterInfo existing = validatedBoardTiles[row, col];

            if (existing != null)
            {
                if (string.IsNullOrEmpty(existing.letter))
                    return false;

                if (char.ToUpper(existing.letter[0]) != needed)
                    return false;

                continue;
            }

            HashSet<char> crossChecks = BuildCrossCheckSet(row, col, placement);

            if (crossChecks != null &&
                crossChecks.Count > 0 &&
                !crossChecks.Contains(needed))
            {
                return false;
            }
        }

        return true;
    }

    private bool HasAdjacentValidatedTile(int row, int col)
    {
        if (row < 1 || row > boardSizeX || col < 1 || col > boardSizeY)
            return false;

        if (validatedBoardTiles[row - 1, col] != null) return true;
        if (validatedBoardTiles[row + 1, col] != null) return true;
        if (validatedBoardTiles[row, col - 1] != null) return true;
        if (validatedBoardTiles[row, col + 1] != null) return true;

        return false;
    }

    public bool CanBuildWordFromTiles_TestHook(string word, List<LetterInfo> tiles)
    {
        return CanBuildWordFromTiles(word, tiles);
    }
    /*private List<RoundMove> FindAllConnectedCandidatesGaddag(List<LetterInfo> aiTiles, BonusTile[,] aiBonusBoard)
    {
        Debug.Log("===== FindAllConnectedCandidatesGaddag START =====");

        List<RoundMove> allCandidates = new List<RoundMove>();
        HashSet<string> seenSignatures = new HashSet<string>();

        if (aiTiles == null || aiTiles.Count == 0)
        {
            Debug.LogWarning("FindAllConnectedCandidatesGaddag: aiTiles is null or empty.");
            return allCandidates;
        }

        EnsureAIGaddagReady();

        List<AnchorSquare> anchors = BuildAnchorSquares();
        Debug.Log("FindAllConnectedCandidatesGaddag: anchor count = " + anchors.Count);

        if (scrabbleWords == null || scrabbleWords.Count == 0)
        {
            Debug.LogWarning("FindAllConnectedCandidatesGaddag: scrabbleWords is null or empty.");
            return allCandidates;
        }

        Debug.Log("FindAllConnectedCandidatesGaddag: dictionary word count = " + scrabbleWords.Count);

        int attempted = 0;
        int valid = 0;
        int maxAttempts = 50000;
        bool stoppedEarly = false;

        for (int a = 0; a < anchors.Count; a++)
        {
            AnchorSquare anchor = anchors[a];

            for (int w = 0; w < scrabbleWords.Count; w++)
            {
                string word = scrabbleWords[w];

                if (string.IsNullOrWhiteSpace(word))
                    continue;

                word = word.Trim().ToUpper();

                if (word.Length == 0)
                    continue;

                if (!aiGaddagLexicon.ContainsWord(word))
                    continue;

                for (int letterIndex = 0; letterIndex < word.Length; letterIndex++)
                {
                    int horizontalStartRow = anchor.row;
                    int horizontalStartCol = anchor.col - letterIndex;

                    attempted++;
                    if (attempted > maxAttempts)
                    {
                        stoppedEarly = true;
                        Debug.LogWarning("FindAllConnectedCandidatesGaddag: maxAttempts reached = " + maxAttempts);
                        break;
                    }

                    TryAddGaddagCandidate(
                        allCandidates,
                        seenSignatures,
                        word,
                        horizontalStartRow,
                        horizontalStartCol,
                        TilePlacement.Horizontal,
                        anchor,
                        aiTiles,
                        aiBonusBoard,
                        ref valid
                    );

                    int verticalStartRow = anchor.row - letterIndex;
                    int verticalStartCol = anchor.col;

                    attempted++;
                    if (attempted > maxAttempts)
                    {
                        stoppedEarly = true;
                        Debug.LogWarning("FindAllConnectedCandidatesGaddag: maxAttempts reached = " + maxAttempts);
                        break;
                    }

                    TryAddGaddagCandidate(
                        allCandidates,
                        seenSignatures,
                        word,
                        verticalStartRow,
                        verticalStartCol,
                        TilePlacement.Vertical,
                        anchor,
                        aiTiles,
                        aiBonusBoard,
                        ref valid
                    );
                }

                if (stoppedEarly)
                    break;
            }

            if (stoppedEarly)
                break;
        }

        Debug.Log("FindAllConnectedCandidatesGaddag: attempted = " + attempted);
        Debug.Log("FindAllConnectedCandidatesGaddag: valid = " + valid);
        Debug.Log("FindAllConnectedCandidatesGaddag: unique accepted = " + allCandidates.Count);
        Debug.Log("FindAllConnectedCandidatesGaddag: stoppedEarly = " + stoppedEarly);
        Debug.Log("===== FindAllConnectedCandidatesGaddag END =====");

        return allCandidates;
    }
    */
    private List<RoundMove> FindAllConnectedCandidatesGaddag(List<LetterInfo> aiTiles, BonusTile[,] aiBonusBoard)
    {
        Debug.Log("===== FindAllConnectedCandidatesGaddag START =====");

        List<RoundMove> allCandidates = new List<RoundMove>();
        HashSet<string> seenSignatures = new HashSet<string>();

        if (aiTiles == null || aiTiles.Count == 0)
        {
            Debug.LogWarning("FindAllConnectedCandidatesGaddag: aiTiles is null or empty.");
            return allCandidates;
        }

        EnsureAIGaddagReady();

        List<AnchorSquare> anchors = BuildAnchorSquares();
        Debug.Log("FindAllConnectedCandidatesGaddag: anchor count = " + anchors.Count);

        if (scrabbleWords == null || scrabbleWords.Count == 0)
        {
            Debug.LogWarning("FindAllConnectedCandidatesGaddag: scrabbleWords is null or empty.");
            return allCandidates;
        }

        // Build frequency count of available letters from both AI rack and board
        int[] availableCounts = new int[26];
        foreach (var tile in aiTiles)
        {
            if (tile != null && !string.IsNullOrEmpty(tile.letter))
            {
                char c = char.ToUpper(tile.letter[0]);
                if (c >= 'A' && c <= 'Z')
                    availableCounts[c - 'A']++;
            }
        }
        for (int r = 1; r <= boardSizeX; r++)
        {
            for (int c = 1; c <= boardSizeY; c++)
            {
                var tile = validatedBoardTiles[r, c];
                if (tile != null && !string.IsNullOrEmpty(tile.letter))
                {
                    char ch = char.ToUpper(tile.letter[0]);
                    if (ch >= 'A' && ch <= 'Z')
                        availableCounts[ch - 'A']++;
                }
            }
        }

        // Pre-filter words to only include those constructible with the available letter pool
        List<string> filteredWords = new List<string>();
        int[] wordCounts = new int[26];
        foreach (string rawWord in scrabbleWords)
        {
            if (string.IsNullOrWhiteSpace(rawWord)) continue;
            string w = rawWord.Trim().ToUpper();
            if (w.Length == 0 || w.Length > Mathf.Max(boardSizeX, boardSizeY)) continue;

            System.Array.Clear(wordCounts, 0, 26);
            bool possible = true;
            for (int i = 0; i < w.Length; i++)
            {
                char c = w[i];
                if (c < 'A' || c > 'Z')
                {
                    possible = false;
                    break;
                }
                wordCounts[c - 'A']++;
                if (wordCounts[c - 'A'] > availableCounts[c - 'A'])
                {
                    possible = false;
                    break;
                }
            }
            if (possible)
            {
                filteredWords.Add(w);
            }
        }
        Debug.Log("GADDAG Pre-filter: reduced dictionary from " + scrabbleWords.Count + " to " + filteredWords.Count + " possible words!");

        int attempted = 0;
        int valid = 0;
        int skippedByPrefilter = 0;
        int maxAttempts = 15000;

        for (int a = 0; a < anchors.Count; a++)
        {
            AnchorSquare anchor = anchors[a];

            for (int w = 0; w < filteredWords.Count; w++)
            {
                string word = filteredWords[w];

                if (string.IsNullOrWhiteSpace(word))
                    continue;

                word = word.Trim().ToUpper();

                if (word.Length == 0)
                    continue;

                if (!aiGaddagLexicon.ContainsWord(word))
                    continue;

                for (int letterIndex = 0; letterIndex < word.Length; letterIndex++)
                {
                    int horizontalStartRow = anchor.row;
                    int horizontalStartCol = anchor.col - letterIndex;

                    if (!WordCanBeMadeFromRackOrBoard(
                        word,
                        aiTiles,
                        validatedBoardTiles,
                        horizontalStartRow,
                        horizontalStartCol,
                        TilePlacement.Horizontal))
                    {
                        skippedByPrefilter++;
                    }
                    else
                    {
                        attempted++;

                        if (attempted > maxAttempts)
                            goto EndSearch;

                        TryAddGaddagCandidate(
                            allCandidates,
                            seenSignatures,
                            word,
                            horizontalStartRow,
                            horizontalStartCol,
                            TilePlacement.Horizontal,
                            anchor,
                            aiTiles,
                            aiBonusBoard,
                            ref valid
                        );
                    }

                    int verticalStartRow = anchor.row - letterIndex;
                    int verticalStartCol = anchor.col;

                    if (!WordCanBeMadeFromRackOrBoard(
                        word,
                        aiTiles,
                        validatedBoardTiles,
                        verticalStartRow,
                        verticalStartCol,
                        TilePlacement.Vertical))
                    {
                        skippedByPrefilter++;
                    }
                    else
                    {
                        attempted++;

                        if (attempted > maxAttempts)
                            goto EndSearch;

                        TryAddGaddagCandidate(
                            allCandidates,
                            seenSignatures,
                            word,
                            verticalStartRow,
                            verticalStartCol,
                            TilePlacement.Vertical,
                            anchor,
                            aiTiles,
                            aiBonusBoard,
                            ref valid
                        );
                    }
                }
            }
        }

    EndSearch:
        Debug.Log("FindAllConnectedCandidatesGaddag: attempted = " + attempted);
        Debug.Log("FindAllConnectedCandidatesGaddag: skippedByPrefilter = " + skippedByPrefilter);
        Debug.Log("FindAllConnectedCandidatesGaddag: valid = " + valid);
        Debug.Log("FindAllConnectedCandidatesGaddag: unique accepted = " + allCandidates.Count);
        Debug.Log("===== FindAllConnectedCandidatesGaddag END =====");

        return allCandidates;
    }
    private void TryAddGaddagCandidate(
        List<RoundMove> allCandidates,
        HashSet<string> seenSignatures,
        string word,
        int startRow,
        int startCol,
        TilePlacement placement,
        AnchorSquare anchor,
        List<LetterInfo> aiTiles,
        BonusTile[,] aiBonusBoard,
        ref int validCounter)
    {
        if (string.IsNullOrEmpty(word))
            return;

        if (!WouldWordCoverAnchor(word, startRow, startCol, placement, anchor.row, anchor.col))
            return;

        if (!PassesAnchorCrossChecks(word, startRow, startCol, placement, anchor))
            return;

        RoundMove candidate = TryBuildConnectedAIMove(
            word,
            startRow,
            startCol,
            placement,
            aiTiles,
            aiBonusBoard
        );

        if (candidate == null || !candidate.isValid)
            return;

        string signature = GetAIMoveSignature(candidate);
        if (string.IsNullOrEmpty(signature))
            return;

        if (seenSignatures.Contains(signature))
            return;

        seenSignatures.Add(signature);
        allCandidates.Add(candidate);
        validCounter++;
    }

    private RoundMove TryBuildConnectedAIMove(
        string word,
        int startRow,
        int startCol,
        TilePlacement orientation,
        List<LetterInfo> aiTiles,
        BonusTile[,] aiBonusBoard)
    {
        if (string.IsNullOrEmpty(word))
        {
            Debug.Log("AI candidate rejected: empty word.");
            return null;
        }

        string placementLabel = word + " @ (" + startRow + "," + startCol + ") " + orientation;

        List<LetterInfo> availableTiles = new List<LetterInfo>(aiTiles);
        List<SimPlacedTile> newPlacedTiles = new List<SimPlacedTile>();

        bool touchesExistingBoardTile = false;
        LetterInfo[,] tempBoard = (LetterInfo[,])validatedBoardTiles.Clone();

        Debug.Log("TryBuildConnectedAIMove START => " + placementLabel);

        for (int i = 0; i < word.Length; i++)
        {
            int row = startRow;
            int col = startCol;

            if (orientation == TilePlacement.Horizontal)
                col += i;
            else if (orientation == TilePlacement.Vertical)
                row += i;
            else
            {
                Debug.Log("AI candidate rejected: unsupported orientation for " + placementLabel);
                return null;
            }

            if (row < 1 || row > boardSizeX || col < 1 || col > boardSizeY)
            {
                Debug.Log("AI candidate rejected: out of bounds at [" + row + "," + col + "] for " + placementLabel);
                return null;
            }

            char neededChar = char.ToUpper(word[i]);
            LetterInfo existingBoardTile = tempBoard[row, col];

            if (existingBoardTile != null)
            {
                if (string.IsNullOrEmpty(existingBoardTile.letter))
                {
                    Debug.Log("AI candidate rejected: board tile exists but letter empty at [" + row + "," + col + "] for " + placementLabel);
                    return null;
                }

                char existingChar = char.ToUpper(existingBoardTile.letter[0]);

                if (existingChar != neededChar)
                {
                    Debug.Log(
                        "AI candidate rejected: conflict at [" + row + "," + col + "] for " +
                        placementLabel + ". Existing=" + existingChar + ", needed=" + neededChar
                    );
                    return null;
                }

                touchesExistingBoardTile = true;
                Debug.Log("AI reused existing board tile '" + existingChar + "' at [" + row + "," + col + "] for " + placementLabel);
                continue;
            }

            LetterInfo matchingTile = null;

            for (int t = 0; t < availableTiles.Count; t++)
            {
                if (availableTiles[t] == null || string.IsNullOrEmpty(availableTiles[t].letter))
                    continue;

                if (char.ToUpper(availableTiles[t].letter[0]) == neededChar)
                {
                    matchingTile = availableTiles[t];
                    availableTiles.RemoveAt(t);
                    break;
                }
            }

            if (matchingTile == null)
            {
                Debug.Log("AI candidate rejected: no rack tile for '" + neededChar + "' in " + placementLabel);
                return null;
            }

            tempBoard[row, col] = matchingTile;

            SimPlacedTile simTile = new SimPlacedTile();
            simTile.letterInfo = matchingTile;
            simTile.letterPosition = new LetterPosition(row, col);
            newPlacedTiles.Add(simTile);

            Debug.Log("AI placed new tile '" + matchingTile.letter + "' at [" + row + "," + col + "] for " + placementLabel);

            bool touchesByAdjacency = false;

            if (row > 1 && validatedBoardTiles[row - 1, col] != null)
                touchesByAdjacency = true;
            if (row < boardSizeX && validatedBoardTiles[row + 1, col] != null)
                touchesByAdjacency = true;
            if (col > 1 && validatedBoardTiles[row, col - 1] != null)
                touchesByAdjacency = true;
            if (col < boardSizeY && validatedBoardTiles[row, col + 1] != null)
                touchesByAdjacency = true;

            if (touchesByAdjacency)
            {
                touchesExistingBoardTile = true;
                Debug.Log("AI candidate touches existing board via adjacency at [" + row + "," + col + "] for " + placementLabel);
            }
        }

        if (!touchesExistingBoardTile)
        {
            Debug.Log("AI candidate rejected: did not touch existing board tile => " + placementLabel);
            return null;
        }

        if (newPlacedTiles.Count == 0)
        {
            Debug.Log("AI candidate rejected: placed zero new tiles => " + placementLabel);
            return null;
        }

        List<List<LetterInfo>> allWords = CollectAllWordsForAIMove(
            orientation,
            tempBoard,
            newPlacedTiles
        );

        if (allWords == null || allWords.Count == 0)
        {
            Debug.Log("AI candidate rejected: no words collected => " + placementLabel);
            return null;
        }

        Debug.Log("AI collected " + allWords.Count + " word(s) for " + placementLabel);

        for (int i = 0; i < allWords.Count; i++)
        {
            string builtWord = "";
            foreach (LetterInfo tile in allWords[i])
            {
                if (tile != null && !string.IsNullOrEmpty(tile.letter))
                    builtWord += tile.letter;
            }

            Debug.Log("AI formed word [" + i + "] = " + builtWord + " for " + placementLabel);
        }

        if (!CheckWordValidity(allWords))
        {
            Debug.Log("AI candidate rejected: dictionary check failed => " + placementLabel);
            return null;
        }

        int totalScore = CountAIMoveScore(allWords, newPlacedTiles);

        if (newPlacedTiles.Count == maxHandSize)
            totalScore += 50;

        Debug.Log("AI candidate accepted => " + placementLabel + ", totalScore = " + totalScore);

        RoundMove move = new RoundMove();
        move.isHuman = false;
        move.isValid = true;
        move.word = word;
        move.score = totalScore;
        move.timeUsed = 70f;
        move.placedTiles = null;
        move.simulatedTiles = newPlacedTiles;

        return move;
    }

    private bool WordCanBeMadeFromRackOrBoard(
    string word,
    List<LetterInfo> aiTiles,
    LetterInfo[,] board,
    int startRow,
    int startCol,
    TilePlacement placement)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        Dictionary<char, int> rackCounts = new Dictionary<char, int>();

        if (aiTiles != null)
        {
            for (int i = 0; i < aiTiles.Count; i++)
            {
                if (aiTiles[i] == null || string.IsNullOrEmpty(aiTiles[i].letter))
                    continue;

                char c = char.ToUpper(aiTiles[i].letter[0]);

                if (rackCounts.ContainsKey(c))
                    rackCounts[c]++;
                else
                    rackCounts[c] = 1;
            }
        }

        Dictionary<char, int> neededFromRack = new Dictionary<char, int>();

        for (int i = 0; i < word.Length; i++)
        {
            int row = startRow;
            int col = startCol;

            if (placement == TilePlacement.Horizontal)
                col += i;
            else if (placement == TilePlacement.Vertical)
                row += i;
            else
                return false;

            if (row < 1 || row > boardSizeX || col < 1 || col > boardSizeY)
                return false;

            char needed = char.ToUpper(word[i]);
            LetterInfo existing = board[row, col];

            if (existing != null && !string.IsNullOrEmpty(existing.letter))
            {
                if (char.ToUpper(existing.letter[0]) != needed)
                    return false;

                continue;
            }

            if (neededFromRack.ContainsKey(needed))
                neededFromRack[needed]++;
            else
                neededFromRack[needed] = 1;
        }

        foreach (KeyValuePair<char, int> kv in neededFromRack)
        {
            int have = rackCounts.ContainsKey(kv.Key) ? rackCounts[kv.Key] : 0;

            if (kv.Value > have)
                return false;
        }

        return true;
    }
}