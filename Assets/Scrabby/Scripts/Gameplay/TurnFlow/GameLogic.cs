using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
//using System.Diagnostics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
//using static GameLogic;
using Random = UnityEngine.Random;
using Unity.Profiling;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine.Networking;

// Coordinate convention used everywhere:
// x = horizontal board coordinate / column, 1-based for LetterPosition.
// y = vertical board coordinate / row, 1-based for LetterPosition.
// LetterPosition.RowX = x; LetterPosition.ColY = y.
// Board arrays use [x, y].
// Bonus arrays are 0-based: boardBonusTiles[x - 1, y - 1].

public class GameLogic : MonoBehaviour
{
    private static readonly ProfilerMarker EvaluateAIMoveMarker =
        new ProfilerMarker("AI.EvaluateAIMoveIncremental");

    private static readonly ProfilerMarker FirstTurnMarker =
        new ProfilerMarker("AI.FindBestFirstTurnPlacementGaddag");

    private static readonly ProfilerMarker FindWordsMarker =
        new ProfilerMarker("AI.FindPossibleWords");

    private static readonly ProfilerMarker ScoreSortMarker =
        new ProfilerMarker("AI.ScoreAndSortWords");

    private static readonly ProfilerMarker TestPlacementsMarker =
        new ProfilerMarker("AI.TestPlacements");

    private static readonly ProfilerMarker AdvanceRoundRevealMarker =
    new ProfilerMarker("Round.AdvanceRoundReveal");

    private static readonly ProfilerMarker CompareMovesMarker =
        new ProfilerMarker("Round.CompareMoves");

    private static readonly ProfilerMarker ApplyWinningMoveMarker =
        new ProfilerMarker("Round.ApplyWinningMove");

    private static readonly ProfilerMarker GameOverCheckMarker =
        new ProfilerMarker("Round.IsGameOver");

    private static readonly ProfilerMarker StartNextRoundMarker =
        new ProfilerMarker("Round.StartNextRound");

    private int maxHandSize;//=7;
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
    private List<RoundResult> roundHistory = new List<RoundResult>();

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

    private SoloDifficulty currentSoloDifficulty = SoloDifficulty.Medium;

    private int[,] precalculatedCrossChecks;
    private int[,] precalculatedCrossChecksVertical;
    private bool aiGaddagReady = false;
    public bool enableScoreDebug = true;
    private bool aiEvaluationRunning = false;
    private bool aiEvaluationFinished = false;
    private RoundMove aiBestMoveSoFar = null;

    private bool aiGaddagLoading;
    private const string GaddagBinaryFileName = "gaddag.bin";


    private int[,] cachedHorizontalCrossChecks;
    private int[,] cachedVerticalCrossChecks;

    //private bool aiGaddagReady;
    //private bool aiGaddagBuilding;

    private const int GaddagNodeBudgetPerSlice = 256;
    private const double GaddagFrameBudgetMs = 2.0;

    private const int DebugCrossRejectLogLimit = 20;
    private readonly System.Diagnostics.Stopwatch aiStageStopwatch = new System.Diagnostics.Stopwatch();
    private readonly System.Diagnostics.Stopwatch aiTotalStopwatch = new System.Diagnostics.Stopwatch();

    [SerializeField] private bool enableAITimingLogs = true;

    private int buildMoveCalls = 0;
    private double buildMoveMs = 0;

    //private bool aiGaddagBuildLogged = false;
    //private double aiGaddagBuildMs = 0.0;
    private IMoveAgent humanAgent;
    private IMoveAgent aiAgent;
    private bool opponentMoveRequested = false;
    private bool opponentMoveReady = false;

    //private bool isOnlineMatch = false;
    private string localPlayerUid = "";
    private string currentMatchId = "";

    private bool isOnlineMatch = false;
    private bool isLocalPlayerHost = false;

    public event Action<RoundMove> onlineSubmissionReady;

    private GameInitMode currentInitMode = GameInitMode.Solo;

    private const int MaxAIDifficultyCandidates = 300;
    private readonly List<RoundMove> aiDifficultyCandidates =
        new List<RoundMove>(MaxAIDifficultyCandidates);
    private RoundMove bestAICandidate;

    // The word the dictionary rejected on the player's last submission, kept so
    // the reveal step can colour those tiles rather than only naming the failure.
    private List<LetterInfo> rejectedPlayerWord;

    private void Awake()
    {
        //SetSoloDifficulty(SoloDifficulty.Medium);
        /*if (maxHandSize <= 0)
            maxHandSize = 7;
        */
        EnsureAIGaddagReady();
    }


    [ContextMenu("Build GADDAG Binary")]
    private void BuildGaddagBinaryForEditor()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning(
                "[GADDAG] Stop Play Mode before building the packaged binary.");

            return;
        }

        if (scrabbleWordsList == null)
        {
            Debug.LogError(
                "[GADDAG] scrabbleWordsList has not been assigned in the Inspector.");

            return;
        }

        string[] rawWords = scrabbleWordsList.text.Split(
            new[] { '\r', '\n' },
            System.StringSplitOptions.RemoveEmptyEntries);

        string outputFolder = Path.Combine(Application.dataPath, "StreamingAssets");
        string outputPath = Path.Combine(outputFolder, "gaddag.bin");

        Directory.CreateDirectory(outputFolder);

        GaddagNode.ResetCounters();

        Stopwatch timer = Stopwatch.StartNew();

        GaddagLexicon lexicon = new GaddagLexicon();

        int addedWords = 0;

        for (int i = 0; i < rawWords.Length; i++)
        {
            string word = rawWords[i];

            if (string.IsNullOrWhiteSpace(word))
                continue;

            lexicon.AddWord(word);
            addedWords++;
        }

        lexicon.SaveToBinary(outputPath);

        timer.Stop();

#if UNITY_EDITOR
    UnityEditor.AssetDatabase.Refresh();
#endif

        long fileBytes = new FileInfo(outputPath).Length;

        Debug.Log(
            $"[GADDAG] Binary created | " +
            $"sourceWords={rawWords.Length:N0} | " +
            $"addedWords={addedWords:N0} | " +
            $"nodes={GaddagNode.CreatedCount:N0} | " +
            $"fileBytes={fileBytes:N0} | " +
            $"dt={timer.Elapsed.TotalMilliseconds:F2}ms | " +
            $"path={outputPath}");
    }

    public enum SoloDifficulty
    {
        Easy = 0,
        Medium = 1,
        Hard = 2,
        Expert = 3
    }

    public enum GameMode
    {
        HumanVsAI,
        HumanVsHumanLocal,
        HumanVsHumanOnline
    }

    [SerializeField] private GameMode gameMode = GameMode.HumanVsAI;

    public void SetBoardSize(int rows, int cols)
    {
        boardSizeX = rows;
        boardSizeY = cols;
    }

    public enum GameInitMode
    {
        Solo,
        Online
    }

    public enum TurnState
    {
        PlayerTurn,
        AITurn,
        Busy
    }

    public RoundMove EvaluatePlayerSubmissionFromAgent()
    {
        return EvaluatePlayerSubmission();
    }

    public RoundMove GetLatestAIMove()
    {
        return aiBestMoveSoFar;
    }

    public bool IsOnlineMatch
    {
        get { return currentInitMode == GameInitMode.Online; }
    }

    private class SearchState
    {
        public List<SimPlacedTile> placedTiles = new();
        public int anchorRow;
        public int anchorCol;
        public int leftMostCol;
    }
    
    private sealed class ScoredRackWord
    {
        public string Word;
        public int EstimatedScore;
        public int Length;
    }




    public class GaddagLexicon
    {
        public const char Separator = '>';

        // Change this if you deliberately change the binary format later.
        private const int BinaryFormatVersion = 1;
        private const string BinaryMagic = "SCRABBY_GADDAG";

        private readonly GaddagNode root = new GaddagNode();
        private readonly HashSet<string> words = new HashSet<string>();

        public GaddagNode Root
        {
            get { return root; }
        }

        public long NodeCount { get; private set; }

        public bool ContainsWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return false;

            return words.Contains(word.Trim().ToUpperInvariant());
        }

        public void AddWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return;

            word = word.Trim().ToUpperInvariant();

            if (!words.Add(word))
                return;

            for (int split = 1; split <= word.Length; split++)
            {
                GaddagNode current = root;

                for (int i = split - 1; i >= 0; i--)
                {
                    current = current.GetOrAdd(word[i]);
                }

                current = current.GetOrAdd(Separator);

                for (int i = split; i < word.Length; i++)
                {
                    current = current.GetOrAdd(word[i]);
                }

                current.isTerminal = true;
            }
        }


        
        
        // Writes a compact tree format:
        // Header: magic string + binary version
        // Node: terminal bool + child count (ushort)
        // Edge: edge character (char) + child node recursively
        //
        // A GADDAG is a tree in your implementation, not a shared-node graph,
        // so recursive serialization is valid.
        public void SaveToBinary(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Binary path is null or empty.", nameof(path));

            string directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (FileStream stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(BinaryMagic);
                writer.Write(BinaryFormatVersion);

                long writtenNodeCount = 0;
                WriteNode(writer, root, ref writtenNodeCount);

                writer.Flush();

                NodeCount = writtenNodeCount;
            }
        }

        public static GaddagLexicon LoadFromBinary(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Binary path is null or empty.", nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException("GADDAG binary was not found.", path);

            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                return LoadFromBinary(reader);
            }
        }

        

        // This overload lets Unity load the binary from StreamingAssets on Android
        // as byte[] via UnityWebRequest, then deserialize directly from memory.
        public static GaddagLexicon LoadFromBinary(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                throw new ArgumentException("GADDAG binary data is empty.", nameof(bytes));

            using (MemoryStream stream = new MemoryStream(bytes, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                return LoadFromBinary(reader);
            }
        }

        private static GaddagLexicon LoadFromBinary(BinaryReader reader)
        {
            string magic = reader.ReadString();

            if (magic != BinaryMagic)
            {
                throw new InvalidDataException(
                    "This file is not a valid Scrabby GADDAG binary.");
            }

            int version = reader.ReadInt32();

            if (version != BinaryFormatVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported GADDAG binary version {version}. " +
                    $"Expected {BinaryFormatVersion}.");
            }

            GaddagLexicon lexicon = new GaddagLexicon();

            // Important:
            // lexicon.root already exists and is readonly.
            // We clear and fill that existing object; we never replace it.
            lexicon.root.edges.Clear();
            lexicon.root.isTerminal = false;

            long loadedNodeCount = 0;
            ReadNodeInto(reader, lexicon.root, ref loadedNodeCount);

            lexicon.NodeCount = loadedNodeCount;

            return lexicon;
        }

        private static void WriteNode(
            BinaryWriter writer,
            GaddagNode node,
            ref long writtenNodeCount)
        {
            writtenNodeCount++;

            writer.Write(node.isTerminal);

            int edgeCount = node.edges.Count;

            if (edgeCount > ushort.MaxValue)
            {
                throw new InvalidDataException(
                    $"Node has too many edges: {edgeCount}.");
            }

            writer.Write((ushort)edgeCount);

            // Dictionary iteration order is not guaranteed. Sorting produces
            // deterministic binary output, useful for Git diffs and debugging.
            List<char> edgeLetters = new List<char>(node.edges.Keys);
            edgeLetters.Sort();

            for (int i = 0; i < edgeLetters.Count; i++)
            {
                char edgeLetter = edgeLetters[i];
                GaddagNode child = node.edges[edgeLetter];

                writer.Write(edgeLetter);
                WriteNode(writer, child, ref writtenNodeCount);
            }
        }

        

        private static void ReadNodeInto(
            BinaryReader reader,
            GaddagNode node,
            ref long loadedNodeCount)
        {
            loadedNodeCount++;

            node.isTerminal = reader.ReadBoolean();

            ushort edgeCount = reader.ReadUInt16();

            node.edges.Clear();

            for (int i = 0; i < edgeCount; i++)
            {
                char edgeLetter = reader.ReadChar();

                GaddagNode child = new GaddagNode();
                node.edges.Add(edgeLetter, child);

                ReadNodeInto(reader, child, ref loadedNodeCount);
            }
        }

        // Retained from your original class, although currently unused.
        private void AddPath(string form)
        {
            GaddagNode current = root;

            for (int i = 0; i < form.Length; i++)
            {
                current = current.GetOrAdd(form[i]);
            }

            current.isTerminal = true;
        }

        // Retained from your original class, although currently unused.
        private string Reverse(string input)
        {
            char[] chars = input.ToCharArray();
            Array.Reverse(chars);
            return new string(chars);
        }
    }
    /*
    public class GaddagLexicon
    {
        public const char Separator = '>';
        private readonly GaddagNode root = new GaddagNode();
        private readonly HashSet<string> words = new HashSet<string>();

        public GaddagNode Root
        {
            get { return root; }
        }

        public long NodeCount { get; private set; }


        public bool ContainsWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return false;

            return words.Contains(word.ToUpperInvariant());
        }

        public void AddWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return;

            word = word.Trim().ToUpperInvariant();

            if (!words.Add(word))
                return;

            for (int split = 1; split <= word.Length; split++)
            {
                GaddagNode current = root;

                // Reverse prefix directly.
                for (int i = split - 1; i >= 0; i--)
                {
                    current = current.GetOrAdd(word[i]);
                }

                // Separator.
                current = current.GetOrAdd(Separator);

                // Suffix directly.
                for (int i = split; i < word.Length; i++)
                {
                    current = current.GetOrAdd(word[i]);
                }

                current.isTerminal = true;
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
    */
    public class AnchorSquare
    {
        public int row;
        public int col;
        public int horizontalCrossChecks;
        public int verticalCrossChecks;
    }

    public class GaddagNode
    {
        public static long CreatedCount=0;
        //public static long GetOrAddCalls;
        //public static long EdgeCreationCount;

        public Dictionary<char, GaddagNode> edges = new Dictionary<char, GaddagNode>();
        public bool isTerminal = false;

        public GaddagNode()
        {
            CreatedCount++;
        }

        public GaddagNode GetOrAdd(char c)
        {
            //GetOrAddCalls++;

            GaddagNode next;

            if (!edges.TryGetValue(c, out next))
            {
                next = new GaddagNode();
                edges.Add(c, next);
                //EdgeCreationCount++;
            }

            return next;
        }

        public static void ResetCounters()
        {
            CreatedCount = 0;
            //GetOrAddCalls = 0;
            //EdgeCreationCount = 0;
        }
    }

    public void SetMaxRounds(int rounds)
    {
        if (rounds > 0)
            maxRounds = rounds;
    }

    public SoloDifficulty CurrentSoloDifficulty
    {
        get { return currentSoloDifficulty; }
    }

    public void SetSoloDifficulty(SoloDifficulty difficulty)
    {
        currentSoloDifficulty = difficulty;

        Debug.Log(
            $"[AI] Solo difficulty set to {currentSoloDifficulty}");
    }

    private void InitOnlineStateShell()
    {
        currentState = TurnState.PlayerTurn;

        pendingPlayerMove = null;
        pendingAIMove = null;
        pendingWinningMove = null;

        roundFlowActive = false;
        roundRevealStep = 0;
        roundStarted = true;

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
        {
            Singleton.Instance.UIManager.ClearRoundMessage();
        }
    }
    public void InitGame(
    int maxHandSize,
    int boardSizeX,
    int boardSizeY,
    GameInitMode mode)
    {
        Debug.Log($"[INIT-DEBUG] InitGame CALLED maxHandSizeParam={maxHandSize} " +
              $"this.maxHandSize(before)={this.maxHandSize}");

        currentInitMode = mode;

        var boardGen = UnityEngine.Object.FindAnyObjectByType<BoardGen>();
        if (boardGen != null)
        {
            boardSizeX = boardGen.RowX;
            boardSizeY = boardGen.RowY;

            Debug.Log("[INIT] Auto-detected board size " +
                      boardSizeX + " x " + boardSizeY +
                      " (width x height)");
        }

        this.maxHandSize = maxHandSize;
        this.boardSizeX = boardSizeX;
        this.boardSizeY = boardSizeY;

        InitSharedState();
        LoadDictionaryIfNeeded();

        if (mode == GameInitMode.Solo)
        {
            InitSoloState();

            // Only local Solo AI needs this 279,496-word structure.
            EnsureAIGaddagReady();

            Debug.Log("[INIT] Solo InitGame complete.");
        }
        else
        {
            InitOnlineStateShell();

            Debug.Log("[INIT] Online InitGame complete. Waiting for match snapshot.");
        }
    }

    private void InitSharedState()
    {
        currentTurn = 0;

        validatedBoardTiles = new LetterInfo[boardSizeX + 2, boardSizeY + 2];

        if (playerHandTiles == null)
            playerHandTiles = new List<LetterInfo>();
        else
            playerHandTiles.Clear();

        boardBonusTiles = new BonusTile[boardSizeX, boardSizeY];

        pendingPlayerMove = null;
        pendingAIMove = null;
        pendingWinningMove = null;

        roundFlowActive = false;
        roundRevealStep = 0;
        roundStarted = false;

        aiEvaluationRunning = false;
        aiEvaluationFinished = false;
        aiBestMoveSoFar = null;

        currentState = TurnState.PlayerTurn;

        if (timer != null)
            timer.ResetTimer();

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
        {
            Singleton.Instance.UIManager.ClearRoundMessage();
            Singleton.Instance.UIManager.RemoveAllHandTiles();
            Singleton.Instance.UIManager.ClearCommittedBoardTiles();
        }

        if (Singleton.Instance != null && Singleton.Instance.DropManager != null)
            Singleton.Instance.DropManager.ResetLocations();
    }

    private void LoadDictionaryIfNeeded()
    {
        scrabbleWords = new List<string>();
        scrabbleWordSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (scrabbleWordsList != null)
        {
            scrabbleWords = new List<string>(
                scrabbleWordsList.text.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries));

            for (int i = 0; i < scrabbleWords.Count; i++)
            {
                string w = scrabbleWords[i];
                if (!string.IsNullOrWhiteSpace(w))
                    scrabbleWordSet.Add(w.Trim().ToUpperInvariant());
            }
        }

        Debug.Log("[INIT] Dictionary size " + scrabbleWordSet.Count);
    }

    public void BeginGameFromButton()
    {
        Debug.Log("[TRACE] BeginGameFromButton CALLED");

        // The same Game Over panel is used by solo and online games.
        // Route online results to the online rematch flow.
        if (Singleton.Instance != null &&
            Singleton.Instance.OnlineMatchController != null &&
            Singleton.Instance.OnlineMatchController.IsViewingOnlineMatchResult())
        {
            Singleton.Instance.OnlineMatchController.PlayAgainFromResults();
            return;
        }

        // Solo restart flow.
        if (Singleton.Instance != null &&
            Singleton.Instance.OnlineMatchController != null)
        {
            Singleton.Instance.OnlineMatchController.ClearViewingOnlineMatchResult();
        }

        if (Singleton.Instance != null &&
            Singleton.Instance.UIManager != null &&
            Singleton.Instance.UIManager.gameOverPanel != null)
        {
            Singleton.Instance.UIManager.gameOverPanel.SetActive(false);
        }

        StopAllCoroutines();
        ClearBoardForNewGame();
        InitGame(maxHandSize, boardSizeX, boardSizeY, GameInitMode.Solo);
        StartCoroutine(StartRound());
    }

    private void InitSoloState()
    {
        if (bonusTileBag != null && bonusBag != null)
            bonusTileBag.ResetBonusBag(bonusBag);

        // Every route into a solo game lands here, which is the only place the
        // refill is guaranteed to happen. Doing it in the callers meant the New
        // Game button drew from whatever the previous games had left behind.
        if (_tileBag != null && letterBag != null)
            _tileBag.ResetLetterBag(letterBag);

        humanTotalScore = 0;
        aiTotalScore = 0;
        currentRoundNumber = 1;
        roundHistory = new List<RoundResult>();

        currentState = TurnState.PlayerTurn;

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
        {
            Singleton.Instance.UIManager.UpdateRoundText(currentRoundNumber, maxRounds);
            Singleton.Instance.UIManager.UpdateTotalScores(humanTotalScore, aiTotalScore);
            Singleton.Instance.UIManager.ClearRoundMessage();
        }
    }

    

    private void ClearBoardForNewGame()
    {
        Debug.Log("ClearBoardForNewGame START");

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
        {
            Singleton.Instance.UIManager.ReturnTilesToHand();
            Singleton.Instance.UIManager.RemoveAllHandTiles();
            Singleton.Instance.UIManager.ClearCommittedBoardTiles();
            Singleton.Instance.UIManager.ClearRoundMessage();
        }

        if (Singleton.Instance != null && Singleton.Instance.DropManager != null)
        {
            Singleton.Instance.DropManager.ResetLocations();
        }

        if (validatedBoardTiles != null)
            System.Array.Clear(validatedBoardTiles, 0, validatedBoardTiles.Length);

        if (boardBonusTiles != null)
            System.Array.Clear(boardBonusTiles, 0, boardBonusTiles.Length);

        Debug.Log("ClearBoardForNewGame END");
    }

    private IEnumerator StartRound()
    {
        Debug.Log("[ONLINE-CHECK] StartRound START. isOnlineMatch=" + isOnlineMatch);

        // Last round's word keeps its box through the reveal; it goes when the
        // next round actually begins.
        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
            Singleton.Instance.UIManager.HidePlayedWordHighlight();

        // SOLO-only safeguard: ensure bag is populated before first round
        /*if (currentInitMode == GameInitMode.Solo)
        {
            var bag = GetTileBag();
            var letters = bag.GetLetters();

            if (letters == null || letters.Count == 0)
            {
                UnityEngine.Debug.LogWarning(
                    "[SOLO-INIT] StartRound detected empty bag in Solo mode. " +
                    "Calling DebugManager.StartNewGame(Easy) as fallback."
                );

                if (Singleton.Instance != null && Singleton.Instance.DebugManager != null)
                {
                    // Fallback to Easy if difficulty not yet set; you can adjust this.
                    Singleton.Instance.DebugManager.StartNewGame(GameLogic.SoloDifficulty.Easy);
                }
            }
        }*/

        roundStarted = true;
        roundFlowActive = false;
        roundRevealStep = 0;
        pendingPlayerMove = null;
        pendingAIMove = null;
        pendingWinningMove = null;

        opponentMoveRequested = false;
        opponentMoveReady = false;

        currentState = TurnState.PlayerTurn;
        aiEvaluationRunning = false;
        aiEvaluationFinished = false;
        aiBestMoveSoFar = null;

        if (playerHandTiles == null)
            playerHandTiles = new List<LetterInfo>();

        playerHandTiles.Clear();

        boardBonusTiles = new BonusTile[boardSizeX, boardSizeY];
        if (bonusTileBag != null && bonusBag != null)
            bonusTileBag.ResetBonusBag(bonusBag);

        PlaceBonusTilesOnBoard();

        float revealDelay = 0.3f;

        if (bonusBoardView != null)
            bonusBoardView.StartRevealBonusTiles(revealDelay);

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
            Singleton.Instance.UIManager.RemoveAllHandTiles();

        var bag = GetTileBag();
        var letters = bag.GetLetters();

        UnityEngine.Debug.Log(
            $"[BAG-DEBUG] Before RefillPlayerHandAnimated in StartRound | " +
            $"bag null? {bag == null} | " +
            $"letters count={letters?.Count ?? -1}"
        );

        yield return StartCoroutine(RefillPlayerHandAnimated(2f));
        ResetDisplay();

        yield return null; // lets the frame render fully before expensive rack validation

        yield return StartCoroutine(EnsurePlayableInitialRack(0f));
        ResetDisplay();

        SaveCurrentRoundSnapshot();

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
            Singleton.Instance.UIManager.ClearRoundMessage();

        yield return new WaitForSeconds(1.5f);

        if (timer != null)
        {
            timer.ResetTimer();
            timer.StartTimer();
        }

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

    // How strong a move each difficulty plays, as a fraction of the best move
    // available this turn. Ranking by position in the candidate list does not
    // work: move scores are heavily top-skewed, so everything past roughly the
    // top fifth sits in a flat tail where rank 25% and rank 60% score almost the
    // same. Banding on score is what the player actually feels.
    private void GetDifficultyScoreBand(
                                            SoloDifficulty difficulty,
                                            out float minimumFraction,
                                            out float maximumFraction)
    {
        switch (difficulty)
        {
            case SoloDifficulty.Easy:
                minimumFraction = 0.20f;
                maximumFraction = 0.40f;
                break;

            case SoloDifficulty.Medium:
                minimumFraction = 0.45f;
                maximumFraction = 0.65f;
                break;

            case SoloDifficulty.Hard:
                minimumFraction = 0.75f;
                maximumFraction = 0.95f;
                break;

            case SoloDifficulty.Expert:
            default:
                minimumFraction = 1.00f;
                maximumFraction = 1.00f;
                break;
        }
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
        return CheckWordValidity(inputWords, out _);
    }

    // Reports the first word the dictionary rejected so the UI can point at it.
    // One is enough: a move usually fails on a single word, and colouring every
    // failure at once would just be noise.
    public bool CheckWordValidity(
        List<List<LetterInfo>> inputWords,
        out List<LetterInfo> firstRejectedWord)
    {
        firstRejectedWord = null;

        foreach (var wordTiles in inputWords)
        {
            string word = string.Empty;
            foreach (var wordTile in wordTiles)
            {
                word += wordTile.letter;
            }

            if (scrabbleWordSet == null || !scrabbleWordSet.Contains(word.ToUpper()))
            {
                firstRejectedWord = wordTiles;
                return false;
            }
        }

        return true;
    }
    public int CountWordPoints(List<LetterInfo> word, List<PlacedTile> placedThisTurn)
    {
        int totalLetterPoints = 0;
        int wordMultiplier = 1;

        foreach (var tile in word)
        {
            int x = -1;
            int y = -1;
            bool isNewlyPlaced = false;

            foreach (var placedTile in placedThisTurn)
            {
                if (placedTile.letterInfo == tile)
                {
                    x = placedTile.letterPosition.RowX;
                    y = placedTile.letterPosition.ColY;
                    isNewlyPlaced = true;
                    break;
                }
            }

            int letterPoints = tile.points;

            if (isNewlyPlaced && x > 0 && y > 0)
            {
                int bonusX = x - 1;
                int bonusY = y - 1;

                bool inRange =
                    bonusX >= 0 && bonusX < boardBonusTiles.GetLength(0) &&
                    bonusY >= 0 && bonusY < boardBonusTiles.GetLength(1);

                if (inRange)
                {
                    BonusTile bonusTile = boardBonusTiles[bonusX, bonusY];

                    if (bonusTile != null && !tile.bonusUsed)
                    {
                        Debug.Log(
                            "[SCORE] Tile " + tile.letter +
                            " at x=" + x + " y=" + y +
                            " -> bonusX=" + bonusX + " bonusY=" + bonusY +
                            " bonusType=" + bonusTile.bonusType
                        );

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
    }

    public int CountWordPoints(List<LetterInfo> word)
    {
        return CountWordPoints(word, new List<PlacedTile>());
    }

    public void EndTurn()
    {
        if (!roundStarted)
            return;

        if (gameMode == GameMode.HumanVsHumanLocal &&
            roundFlowActive &&
            opponentMoveRequested &&
            !opponentMoveReady)
        {
            SubmitLocalOpponentMove();
            return;
        }

        if (!roundFlowActive)
        {
            pendingPlayerMove = EvaluatePlayerSubmission();
            roundFlowActive = true;
            roundRevealStep = 0;
            AdvanceRoundReveal();
            return;
        }

        AdvanceRoundReveal();
    }

    public void RefillPlayerHand()
    {
        Debug.Log("[ONLINE-CHECK] RefillPlayerHand START. isOnlineMatch=" + isOnlineMatch);

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
            Debug.Log("[ONLINE-CHECK] RefillPlayerHand LOCAL DRAW -> " + tile.letter + tile.points);
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
                    validatedBoardTiles[x + 1, y + 1] != null;

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
                    validatedBoardTiles[x + 1, y + 1] != null;

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

    private void AdvanceRoundReveal()
    {
        using (AdvanceRoundRevealMarker.Auto())
        {
            if (!roundFlowActive)
                return;

            switch (roundRevealStep)
            {
                case 0:
                    if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
                    {
                        if (pendingPlayerMove != null && pendingPlayerMove.isValid)
                            Singleton.Instance.UIManager.ShowRoundMessage(
                                "You played " + pendingPlayerMove.word + " for " + pendingPlayerMove.score + " points. Press EndTurn."
                            );
                        else
                        {
                            Singleton.Instance.UIManager.ShowRoundMessage(
                                "Your move was invalid. Press EndTurn."
                            );

                            Singleton.Instance.UIManager.HighlightRejectedWord(
                                rejectedPlayerWord
                            );
                        }
                    }

                    roundRevealStep = 1;
                    break;

                case 1:
                    if (!opponentMoveRequested)
                    {
                        Debug.Log("FLOW Opponent move not requested yet. Requesting now.");

                        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
                            Singleton.Instance.UIManager.ReturnTilesToHand();

                        RequestOpponentMove();
                        return;
                    }

                    if (!opponentMoveReady)
                    {
                        Debug.Log("FLOW Waiting for opponent move...");

                        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
                        {
                            switch (gameMode)
                            {
                                case GameMode.HumanVsAI:
                                    Singleton.Instance.UIManager.ShowRoundMessage("AI is thinking...");
                                    break;

                                case GameMode.HumanVsHumanLocal:
                                    Singleton.Instance.UIManager.ShowRoundMessage("Waiting for Player 2 move...");
                                    break;

                                case GameMode.HumanVsHumanOnline:
                                    Singleton.Instance.UIManager.ShowRoundMessage("Waiting for online opponent move...");
                                    break;
                            }
                        }

                        return;
                    }

                    Debug.Log("FLOW Opponent move ready. Revealing opponent move.");

                    if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
                    {
                        if (pendingAIMove != null && pendingAIMove.isValid)
                            Singleton.Instance.UIManager.ShowRoundMessage(
                                "Opponent played " + pendingAIMove.word + " for " + pendingAIMove.score + " points. Press EndTurn."
                            );
                        else
                            Singleton.Instance.UIManager.ShowRoundMessage(
                                "Opponent could not make a valid move. Press EndTurn."
                            );
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
                        string.Equals(
                            pendingPlayerMove.word,
                            pendingAIMove.word,
                            StringComparison.OrdinalIgnoreCase
                        );

                    if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
                    {
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
                    }

                    Debug.Log("Displayed final winner message.");
                    roundRevealStep = 3;
                    break;

                case 3:
                    ApplyWinningMove(pendingWinningMove);
                    RecordRoundResult();

                    if (IsGameOver())
                        EndGame();
                    else
                        StartCoroutine(StartNextRound());

                    Debug.Log("Applied winning move. Checked for game over.");
                    break;
            }
        }
    }



    private bool HasTimedOut()
    {
        if (timer == null)
            return false;

        return timer.GetRemainingTime() <= 0f;
    }

    private bool HasPlayerTimedOut()
    {
        if (timer == null)
            return false;

        return timer.GetRemainingTime() <= 0f;
    }

    
    public RoundMove TestCompareMoves(RoundMove playerMove, RoundMove aiMove)
    {
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
                  ", word: " + playerMove.word +
                  ", isHuman: " + playerMove.isHuman);

        Debug.Log("AI => valid: " + aiMove.isValid +
                  ", score: " + aiMove.score +
                  ", time: " + aiMove.timeUsed +
                  ", word: " + aiMove.word +
                  ", isHuman: " + aiMove.isHuman);

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

        Debug.Log("Scores are tied.");

        bool playerIsHuman = playerMove.isHuman;
        bool aiIsHuman = aiMove.isHuman;

        if (playerIsHuman && !aiIsHuman)
        {
            Debug.Log("Scores tied in human vs AI. Human wins tie.");
            return playerMove;
        }

        if (!playerIsHuman && aiIsHuman)
        {
            Debug.Log("Scores tied in AI vs human. Human wins tie.");
            return aiMove;
        }

        if (playerMove.timeUsed < aiMove.timeUsed)
        {
            Debug.Log("Scores tied. Player wins on faster time.");
            return playerMove;
        }

        if (aiMove.timeUsed < playerMove.timeUsed)
        {
            Debug.Log("Scores tied. AI wins on faster time.");
            return aiMove;
        }

        Debug.Log("Scores and time are tied exactly. Falling back to first argument.");
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

        LetterPosition popupAnchor = GetPopupAnchorPosition(winningMove);
        if (popupAnchor != null && Singleton.Instance != null && Singleton.Instance.UIManager != null)
        {
            Debug.Log(
                "Showing validated word score popup at RowX=" + popupAnchor.RowX +
                ", ColY=" + popupAnchor.ColY +
                ", score=" + winningMove.score
            );

            Singleton.Instance.UIManager.HighlightPlayedWord(
                GetMainWordCells(winningMove), winningMove.score);
        }
        else
        {
            Debug.LogWarning("Could not show validated word score popup: popupAnchor or UIManager was null.");
        }

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

            /*Debug.Log(
                "AI removing logical hand tile => " +
                simTile.letterInfo.letter + " (" + simTile.letterInfo.points + "), removed = " + removed
            );*/

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

    private IEnumerator StartNextRound()
    {
        Debug.Log("[ONLINE-CHECK] StartnexttRound START. isOnlineMatch=" + isOnlineMatch);


        yield return new WaitForSeconds(1.5f);

        roundFlowActive = false;
        roundRevealStep = 0;
        pendingPlayerMove = null;
        pendingAIMove = null;
        pendingWinningMove = null;

        opponentMoveRequested = false;
        opponentMoveReady = false;

        aiEvaluationRunning = false;
        aiEvaluationFinished = false;
        aiBestMoveSoFar = null;
        currentState = TurnState.PlayerTurn;

        currentRoundNumber++;

        boardBonusTiles = new BonusTile[boardSizeX, boardSizeY];
        if (bonusTileBag != null && bonusBag != null)
            bonusTileBag.ResetBonusBag(bonusBag);

        PlaceBonusTilesOnBoard();

        float revealDelay = 0.3f;
        if (bonusBoardView != null)
            bonusBoardView.StartRevealBonusTiles(revealDelay);


        if (playerHandTiles == null)
            playerHandTiles = new List<LetterInfo>();

        yield return StartCoroutine(RefillPlayerHandAnimated(2f));
        RebuildHandUIFromLogicalHand();
        ResetDisplay();
        SaveCurrentRoundSnapshot();

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
        {
            Singleton.Instance.UIManager.UpdateRoundText(currentRoundNumber, maxRounds);
            Singleton.Instance.UIManager.ClearRoundMessage();
        }

        yield return new WaitForSeconds(1.5f);

        if (timer != null)
        {
            Debug.Log("Resetting timer in StartNextRound");
            timer.ResetTimer();
            Debug.Log("Starting timer in StartNextRound");
            timer.StartTimer();
        }
        else
        {
            Debug.LogWarning("StartNextRound: timer is null.");
        }

        Debug.Log("StartNextRound END");
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

    private RoundMove FindBestFirstTurnPlacementGaddag(
    List<LetterInfo> rack,
    BonusTile[,] bonusBoard)
    {
        using (FirstTurnMarker.Auto())
        {
            if (rack == null || rack.Count == 0)
                return CreateInvalidMove(false, GetCurrentTimeUsed());

            List<string> words;

            using (FindWordsMarker.Auto())
            {
                words = FindPossibleAIWordsFromRackFast(rack);
            }

            using (ScoreSortMarker.Auto())
            {
                words.Sort((a, b) =>
                {
                    int scoreA = EstimateWordBaseScoreFromRack(a, rack);
                    int scoreB = EstimateWordBaseScoreFromRack(b, rack);

                    if (scoreA != scoreB)
                        return scoreB.CompareTo(scoreA);

                    return b.Length.CompareTo(a.Length);
                });
            }

            int maxWordsToTest = Mathf.Min(words.Count, 30);

            aiDifficultyCandidates.Clear();
            bestAICandidate = null;

            using (TestPlacementsMarker.Auto())
            {
                for (int i = 0; i < maxWordsToTest; i++)
                {
                    string word = words[i];
                    if (string.IsNullOrEmpty(word))
                        continue;

                    //RoundMove candidate = FindBestFirstTurnPlacement(word, rack, bonusBoard);
                    RoundMove candidate = ScoreAIFirstTurnPlacement(
                                            word,
                                            boardSizeX / 2,
                                            boardSizeY / 2,
                                            TilePlacement.Horizontal,
                                            rack,
                                            bonusBoard);

                    if (candidate != null && candidate.isValid)
                    {
                        aiDifficultyCandidates.Add(candidate);

                        if (aiDifficultyCandidates.Count >= MaxAIDifficultyCandidates)
                            break;
                    }
                }
            }

            RoundMove chosen = SelectMoveForDifficulty(aiDifficultyCandidates);

            return chosen ?? CreateInvalidMove(false, GetCurrentTimeUsed());
        }
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

            int bonusX = row - 1; // RowX is x
            int bonusY = col - 1; // ColY is y

            int letterPoints = matchingTile.points;

            if (bonusX >= 0 && bonusX < aiBonusBoard.GetLength(0) &&
                bonusY >= 0 && bonusY < aiBonusBoard.GetLength(1))
            {
                BonusTile bonusTile = aiBonusBoard[bonusX, bonusY];

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
        move.timeUsed = GetCurrentTimeUsed();
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
            int bonusX = tile.letterPosition.RowX - 1;
            int bonusY = tile.letterPosition.ColY - 1;

            if (bonusX < 0 || bonusX >= boardBonusTiles.GetLength(0) ||
                bonusY < 0 || bonusY >= boardBonusTiles.GetLength(1))
            {
                continue;
            }

            BonusTile bonusTile = boardBonusTiles[bonusX, bonusY];


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
        Debug.Log("[HANDUI] playerHandTiles null? " + (playerHandTiles == null));
        Debug.Log("[HANDUI] Singleton.Instance null? " + (Singleton.Instance == null));

        UIManager ui = (Singleton.Instance != null) ? Singleton.Instance.UIManager : null;
        Debug.Log("[HANDUI] UIManager null? " + (ui == null));

        Debug.Log("===== RebuildHandUIFromLogicalHand START =====");

        if (playerHandTiles == null)
        {
            Debug.LogError("[HANDUI] playerHandTiles is null in RebuildHandUIFromLogicalHand.");
            return;
        }

        if (Singleton.Instance == null)
        {
            Debug.LogError("[HANDUI] Singleton.Instance is null in RebuildHandUIFromLogicalHand.");
            return;
        }

        if (ui == null)
        {
            Debug.LogError("[HANDUI] Singleton.Instance.UIManager is null in RebuildHandUIFromLogicalHand.");
            return;
        }

        ui.RemoveAllHandTiles();

        foreach (var tile in playerHandTiles)
        {
            if (tile == null)
                continue;

            ui.AddTileToHand(tile);
        }

        ResetDisplay();
        Debug.Log("[HANDUI] Rebuilt hand with count = " + playerHandTiles.Count);
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
        //Debug.Log("[GameLogic] Hand shuffled and rebuilt.");
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
    }

    private List<BonusCellSnapshot> SnapshotBonusBoard()
    {
        List<BonusCellSnapshot> snapshot = new List<BonusCellSnapshot>();

        if (boardBonusTiles == null)
            return snapshot;

        for (int x = 0; x < boardBonusTiles.GetLength(0); x++)
        {
            for (int y = 0; y < boardBonusTiles.GetLength(1); y++)
            {
                BonusTile bonus = boardBonusTiles[x, y];

                if (bonus == null)
                    continue;

                snapshot.Add(new BonusCellSnapshot
                {
                    x = x,
                    y = y,
                    bonusType = bonus.bonusType
                });
            }
        }

        return snapshot;
    }

    private void RestoreBonusBoard(List<BonusCellSnapshot> snapshot)
    {
        if (boardBonusTiles == null)
            return;

        System.Array.Clear(boardBonusTiles, 0, boardBonusTiles.Length);

        if (snapshot != null)
        {
            foreach (BonusCellSnapshot cell in snapshot)
            {
                if (cell == null ||
                    cell.x < 0 || cell.x >= boardBonusTiles.GetLength(0) ||
                    cell.y < 0 || cell.y >= boardBonusTiles.GetLength(1))
                    continue;

                boardBonusTiles[cell.x, cell.y] = new BonusTile(cell.bonusType);
            }
        }

        if (bonusBoardView != null)
            bonusBoardView.DrawBonusTilesImmediately();
    }

    private static List<SimPlacedTileData> ToReplayTiles(RoundMove move)
    {
        List<SimPlacedTileData> tiles = new List<SimPlacedTileData>();

        if (move == null || !move.isValid || move.simulatedTiles == null)
            return tiles;

        foreach (SimPlacedTile sim in move.simulatedTiles)
        {
            if (sim == null || sim.letterInfo == null || sim.letterPosition == null)
                continue;

            tiles.Add(new SimPlacedTileData
            {
                letter = sim.letterInfo.letter,
                points = sim.letterInfo.points,
                row = sim.letterPosition.RowX,
                col = sim.letterPosition.ColY
            });
        }

        return tiles;
    }

    private void RecordRoundResult()
    {
        RoundResult result = new RoundResult
        {
            roundNumber = currentRoundNumber,
            humanScore = (pendingPlayerMove != null && pendingPlayerMove.isValid) ? pendingPlayerMove.score : 0,
            aiScore = (pendingAIMove != null && pendingAIMove.isValid) ? pendingAIMove.score : 0,
            humanWord = (pendingPlayerMove != null && pendingPlayerMove.isValid) ? pendingPlayerMove.word : "",
            aiWord = (pendingAIMove != null && pendingAIMove.isValid) ? pendingAIMove.word : "",
            humanValid = pendingPlayerMove != null && pendingPlayerMove.isValid,
            aiValid = pendingAIMove != null && pendingAIMove.isValid,
            humanWasWinner = pendingWinningMove != null && pendingWinningMove.isHuman,

            humanTiles = ToReplayTiles(pendingPlayerMove),
            aiTiles = ToReplayTiles(pendingAIMove),
            winnerTiles = ToReplayTiles(pendingWinningMove),
            bonusBoard = SnapshotBonusBoard()
        };

        roundHistory.Add(result);

        Debug.Log($"[ROUND RESULT] Round {result.roundNumber}: Human '{result.humanWord}' ({result.humanScore}) vs AI '{result.aiWord}' ({result.aiScore}), winner={(result.humanWasWinner ? "Human" : "AI")}");
    }


    private bool IsGameOver()
    {
        return currentRoundNumber >= maxRounds;
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

        string roundSummary =
                    $"Final score: {humanTotalScore} - AI {aiTotalScore} " +
                    $"(played {roundHistory.Count} rounds)";

        // The per-round breakdown lives on the replay rows below, so repeating it
        // in the summary text just prints every score twice.
        foreach (var r in roundHistory)
        {
            Debug.Log(
                $"Round {r.roundNumber}: " +
                $"{r.humanWord}({r.humanScore}) vs " +
                $"{r.aiWord}({r.aiScore})"
            );
        }

        Singleton.Instance.UIManager.ShowGameOverPanel(finalMessage, roundSummary);

        Singleton.Instance.UIManager.ShowSoloRoundReplayRows(
            roundHistory,
            round => StartCoroutine(PlaySoloReplayFromGameOver(round)));
    }

    // The panel covers the board, so it steps aside for the replay and comes
    // back afterwards with the summary intact.
    private IEnumerator PlaySoloReplayFromGameOver(RoundResult round)
    {
        UIManager ui = Singleton.Instance != null ? Singleton.Instance.UIManager : null;

        if (ui == null)
            yield break;

        if (ui.gameOverPanel != null)
            ui.gameOverPanel.SetActive(false);

        yield return StartCoroutine(ReplaySoloRound(round));

        if (ui.gameOverPanel != null)
            ui.gameOverPanel.SetActive(true);
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
            Debug.LogWarning("CollectAllWordsForAIMove board is null.");
            return wordList;
        }

        if (newPlacedTiles == null || newPlacedTiles.Count == 0)
            return wordList;

        int mainAnchorRow = newPlacedTiles[0].letterPosition.RowX;
        int mainAnchorCol = newPlacedTiles[0].letterPosition.ColY;

        if (mainOrientation == TilePlacement.Horizontal)
        {
            int minCol = newPlacedTiles[0].letterPosition.ColY;
            int row = newPlacedTiles[0].letterPosition.RowX;

            foreach (SimPlacedTile simTile in newPlacedTiles)
            {
                if (simTile == null || simTile.letterPosition == null) continue;
                if (simTile.letterPosition.ColY < minCol)
                    minCol = simTile.letterPosition.ColY;
            }

            mainAnchorRow = row;
            mainAnchorCol = minCol;

            int firstCol = GetFirstLetterIndex(TilePlacement.Horizontal, board, mainAnchorRow, mainAnchorCol);
            List<LetterInfo> mainWord = GetWordFromBoard(TilePlacement.Horizontal, board, mainAnchorRow, firstCol);

            if (mainWord != null && mainWord.Count > 1)
                AddWordIfNotDuplicate(wordList, mainWord);
        }
        else
        {
            int minRow = newPlacedTiles[0].letterPosition.RowX;
            int col = newPlacedTiles[0].letterPosition.ColY;

            foreach (SimPlacedTile simTile in newPlacedTiles)
            {
                if (simTile == null || simTile.letterPosition == null) continue;
                if (simTile.letterPosition.RowX < minRow)
                    minRow = simTile.letterPosition.RowX;
            }

            mainAnchorRow = minRow;
            mainAnchorCol = col;

            int firstRow = GetFirstLetterIndex(TilePlacement.Vertical, board, mainAnchorRow, mainAnchorCol);
            List<LetterInfo> mainWord = GetWordFromBoard(TilePlacement.Vertical, board, firstRow, mainAnchorCol);

            if (mainWord != null && mainWord.Count > 1)
                AddWordIfNotDuplicate(wordList, mainWord);
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

                    if (crossWord != null && crossWord.Count > 1)
                        AddWordIfNotDuplicate(wordList, crossWord);
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

                    if (crossWord != null && crossWord.Count > 1)
                        AddWordIfNotDuplicate(wordList, crossWord);
                }
            }
        }

        return wordList;
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

            placedThisTurn.Add(new PlacedTile
            {
                letterInfo = simTile.letterInfo,
                letterPosition = simTile.letterPosition
            });
        }

        foreach (List<LetterInfo> singleWord in allWords)
        {
            totalScore += CountWordPoints(singleWord, placedThisTurn);
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


    private bool IsWordInDictionary(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        return scrabbleWordSet != null && scrabbleWordSet.Contains(word.ToUpper());
    }

    private int BuildCrossCheckSet(int row, int col, TilePlacement mainPlacement)
    {
        if (row < 1 || row > boardSizeX || col < 1 || col > boardSizeY)
            return 0;

        if (validatedBoardTiles[row, col] != null)
            return 0;

        char[] beforeBuffer = new char[boardSizeX + boardSizeY];
        char[] afterBuffer = new char[boardSizeX + boardSizeY];
        int beforeLen = 0;
        int afterLen = 0;

        if (mainPlacement == TilePlacement.Horizontal)
        {
            int scanRow = row - 1;
            while (scanRow >= 1 && validatedBoardTiles[scanRow, col] != null)
            {
                beforeBuffer[beforeLen++] = char.ToUpperInvariant(validatedBoardTiles[scanRow, col].letter[0]);
                scanRow--;
            }

            Array.Reverse(beforeBuffer, 0, beforeLen);

            scanRow = row + 1;
            while (scanRow <= boardSizeX && validatedBoardTiles[scanRow, col] != null)
            {
                afterBuffer[afterLen++] = char.ToUpperInvariant(validatedBoardTiles[scanRow, col].letter[0]);
                scanRow++;
            }
        }
        else
        {
            int scanCol = col - 1;
            while (scanCol >= 1 && validatedBoardTiles[row, scanCol] != null)
            {
                beforeBuffer[beforeLen++] = char.ToUpperInvariant(validatedBoardTiles[row, scanCol].letter[0]);
                scanCol--;
            }

            Array.Reverse(beforeBuffer, 0, beforeLen);

            scanCol = col + 1;
            while (scanCol <= boardSizeY && validatedBoardTiles[row, scanCol] != null)
            {
                afterBuffer[afterLen++] = char.ToUpperInvariant(validatedBoardTiles[row, scanCol].letter[0]);
                scanCol++;
            }
        }

        if (beforeLen == 0 && afterLen == 0)
            return CreateAllLettersAllowed();

        string before = new string(beforeBuffer, 0, beforeLen);
        string after = new string(afterBuffer, 0, afterLen);

        int result = 0;

        for (char ch = 'A'; ch <= 'Z'; ch++)
        {
            string formed = before + ch + after;
            if (IsWordInDictionary(formed))
                result = AllowLetter(result, ch);
        }

        return result;
    }

    private List<AnchorSquare> BuildAnchors()
    {
        List<AnchorSquare> anchors = new();

        bool boardHasTiles = HasAnyValidatedTilesOnBoard();

        // First move: every square is an anchor
        if (!boardHasTiles)
        {
            for (int r = 1; r <= boardSizeX; r++)
            {
                for (int c = 1; c <= boardSizeY; c++)
                {
                    anchors.Add(new AnchorSquare
                    {
                        row = r,
                        col = c
                    });
                }
            }

            return anchors;
        }

        // Later moves: only squares adjacent to existing tiles
        for (int r = 1; r <= boardSizeX; r++)
        {
            for (int c = 1; c <= boardSizeY; c++)
            {
                if (validatedBoardTiles[r, c] != null)
                    continue;

                bool adjacent =
                    validatedBoardTiles[r - 1, c] != null ||
                    validatedBoardTiles[r + 1, c] != null ||
                    validatedBoardTiles[r, c - 1] != null ||
                    validatedBoardTiles[r, c + 1] != null;

                if (adjacent)
                {
                    anchors.Add(new AnchorSquare
                    {
                        row = r,
                        col = c
                    });
                }
            }
        }

        return anchors;
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

                AnchorSquare anchor = new AnchorSquare
                {
                    row = row,
                    col = col,
                    horizontalCrossChecks = BuildCrossCheckSet(row, col, TilePlacement.Horizontal),
                    verticalCrossChecks = BuildCrossCheckSet(row, col, TilePlacement.Vertical)
                };

                anchors.Add(anchor);
            }
        }

        return anchors;
    }

    private bool PassCrossCheck(int row, int col, char c, TilePlacement mainPlacement)
    {
        int[,] table = (mainPlacement == TilePlacement.Horizontal)
            ? precalculatedCrossChecks
            : precalculatedCrossChecksVertical;

        if (table != null && IsInBounds(row, col))
        {
            int mask = table[row, col];
            return CrossCheckAllows(mask, c);
        }

        int fallbackMask = BuildCrossCheckSet(row, col, mainPlacement);
        return CrossCheckAllows(fallbackMask, c);
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

    private IEnumerator EvaluateAIMoveIncremental()
    {
        aiEvaluationRunning = true;
        aiEvaluationFinished = false;
        aiBestMoveSoFar = null;

        Debug.Log("[AI-TRACE] EvaluateAIMoveIncremental START");

        try
        {
            using (EvaluateAIMoveMarker.Auto())
            {
                aiDifficultyCandidates.Clear();
                bestAICandidate = null;

                if (currentRoundSnapshot == null)
                {
                    Debug.LogError("[AI-TRACE] currentRoundSnapshot is NULL");
                    aiBestMoveSoFar = CreateInvalidMove(false, GetCurrentTimeUsed());
                    yield break;
                }

                if (currentRoundSnapshot.initialTiles == null)
                {
                    Debug.LogError("[AI-TRACE] currentRoundSnapshot.initialTiles is NULL");
                    aiBestMoveSoFar = CreateInvalidMove(false, GetCurrentTimeUsed());
                    yield break;
                }

                Debug.Log(
                    $"[AI-TRACE] snapshot initialTilesCount={currentRoundSnapshot.initialTiles.Count} " +
                    $"initialBonusTilesNull={(currentRoundSnapshot.initialBonusTiles == null)} " +
                    $"boardHasValidatedTiles={HasAnyValidatedTilesOnBoard()} " +
                    $"liveBoardTileCount={CountValidatedBoardTiles()} " +
                    $"snapshotRack={RackToString(currentRoundSnapshot.initialTiles)}"
                );

                StartStageTimer("Clone snapshot");
                List<LetterInfo> aiTiles = CloneTilesForAI(currentRoundSnapshot.initialTiles);
                BonusTile[,] aiBonusBoard = CloneBonusTilesForAI(currentRoundSnapshot.initialBonusTiles);
                EndStageTimer("Clone snapshot");

                Debug.Log(
                    $"[AI-TRACE] clone result rackCount={(aiTiles == null ? -1 : aiTiles.Count)} " +
                    $"rack={RackToString(aiTiles)} " +
                    $"bonusBoardNull={(aiBonusBoard == null)}"
                );

                bool boardHasTiles = HasAnyValidatedTilesOnBoard();
                Debug.Log($"[AI-TRACE] branch boardHasTiles={boardHasTiles}");

                if (!boardHasTiles)
                {
                    Debug.Log("[AI-TRACE] entering first-turn search");

                    StartStageTimer("First-turn search");
                    aiBestMoveSoFar = FindBestFirstTurnPlacementGaddag(aiTiles, aiBonusBoard);
                    EndStageTimer("First-turn search");

                    Debug.Log(
                        "[AI-TRACE] first-turn search returned " +
                        DescribeMove(aiBestMoveSoFar)
                    );
                }
                else
                {
                    Debug.Log("[AI-TRACE] entering connected GADDAG search");

                    StartStageTimer("Connected GADDAG search");
                    yield return StartCoroutine(
                        FindBestGaddagMoveCoroutine(
                            aiTiles,
                            aiBonusBoard,
                            move =>
                            {
                                Debug.Log("[AI-TRACE] connected search callback move=" + DescribeMove(move));
                                aiBestMoveSoFar = move;
                            }));
                    EndStageTimer("Connected GADDAG search");

                    Debug.Log(
                        "[AI-TRACE] connected search finished aiBestMoveSoFar=" +
                        DescribeMove(aiBestMoveSoFar)
                    );
                }

                StartStageTimer("Finalize AI move");

                if (aiBestMoveSoFar == null)
                {
                    Debug.LogWarning("[AI-TRACE] aiBestMoveSoFar was NULL after search, creating invalid move");
                    aiBestMoveSoFar = CreateInvalidMove(false, GetCurrentTimeUsed());
                }

                aiBestMoveSoFar.isHuman = false;
                aiBestMoveSoFar.timeUsed = GetCurrentTimeUsed();

                Debug.Log(
                    "[AI-TRACE] finalize result " +
                    DescribeMove(aiBestMoveSoFar)
                );

                EndStageTimer("Finalize AI move");
            }
        }
        finally
        {
            aiEvaluationRunning = false;
            aiEvaluationFinished = true;

            Debug.Log(
                "[AI-TRACE] EvaluateAIMoveIncremental END " +
                $"running={aiEvaluationRunning} finished={aiEvaluationFinished} " +
                $"finalMove={DescribeMove(aiBestMoveSoFar)}"
            );
        }
    }

    private string DescribeMove(RoundMove move)
    {
        if (move == null)
            return "NULL";

        int simCount = move.simulatedTiles == null ? -1 : move.simulatedTiles.Count;
        int placedCount = move.placedTiles == null ? -1 : move.placedTiles.Count;

        return
            $"valid={move.isValid}, word='{move.word}', score={move.score}, " +
            $"isHuman={move.isHuman}, timeUsed={move.timeUsed}, " +
            $"simTiles={simCount}, placedTiles={placedCount}";
    }

    // Every cell of the word the move laid down, walked out along the placement
    // direction through whatever was already on the board, so the outline boxes
    // the whole word rather than only the tiles the player contributed.
    private List<LetterPosition> GetMainWordCells(RoundMove move)
    {
        List<LetterPosition> cells = new List<LetterPosition>();

        if (move == null || move.simulatedTiles == null || move.simulatedTiles.Count == 0)
            return cells;

        // Works before the move is applied as well as after: overlaying the move's
        // own tiles onto the board is a no-op once they are already there.
        LetterInfo[,] board = (LetterInfo[,])validatedBoardTiles.Clone();

        foreach (SimPlacedTile sim in move.simulatedTiles)
        {
            if (sim != null && sim.letterPosition != null)
                board[sim.letterPosition.RowX, sim.letterPosition.ColY] = sim.letterInfo;
        }

        TilePlacement orientation = InferMoveOrientationFromSimTiles(move.simulatedTiles);

        int row = move.simulatedTiles[0].letterPosition.RowX;
        int col = move.simulatedTiles[0].letterPosition.ColY;

        if (orientation == TilePlacement.Vertical)
        {
            int first = GetFirstLetterIndex(TilePlacement.Vertical, board, row, col);

            for (int r = first; r <= boardSizeX && board[r, col] != null; r++)
                cells.Add(new LetterPosition(r, col));
        }
        else
        {
            int first = GetFirstLetterIndex(TilePlacement.Horizontal, board, row, col);

            for (int c = first; c <= boardSizeY && board[row, c] != null; c++)
                cells.Add(new LetterPosition(row, c));
        }

        // A single tile that formed no run still deserves its own box.
        if (cells.Count == 0)
            cells.Add(new LetterPosition(row, col));

        return cells;
    }

    private LetterPosition GetPopupAnchorPosition(RoundMove move)
    {
        if (move == null || move.simulatedTiles == null || move.simulatedTiles.Count == 0)
        {
            Debug.LogWarning("GetPopupAnchorPosition: move or simulatedTiles is null/empty.");
            return null;
        }

        TilePlacement orientation = InferMoveOrientationFromSimTiles(move.simulatedTiles);

        // Since the move has already been applied, the word's letters are in validatedBoardTiles.
        // We find the bottom-rightmost tile of the entire word on the board.
        int anyRow = move.simulatedTiles[0].letterPosition.RowX;
        int anyCol = move.simulatedTiles[0].letterPosition.ColY;

        if (orientation == TilePlacement.Horizontal)
        {
            int firstCol = GetFirstLetterIndex(TilePlacement.Horizontal, validatedBoardTiles, anyRow, anyCol);
            int maxCol = firstCol;
            while (maxCol < boardSizeY && validatedBoardTiles[anyRow, maxCol + 1] != null)
            {
                maxCol++;
            }
            return new LetterPosition(anyRow, maxCol);
        }
        else if (orientation == TilePlacement.Vertical)
        {
            int firstRow = GetFirstLetterIndex(TilePlacement.Vertical, validatedBoardTiles, anyRow, anyCol);
            int maxRow = firstRow;
            while (maxRow < boardSizeX && validatedBoardTiles[maxRow + 1, anyCol] != null)
            {
                maxRow++;
            }
            return new LetterPosition(maxRow, anyCol);
        }
        else if (orientation == TilePlacement.SingleTile)
        {
            bool hasVertical =
                (anyRow > 1 && validatedBoardTiles[anyRow - 1, anyCol] != null) ||
                (anyRow < boardSizeX && validatedBoardTiles[anyRow + 1, anyCol] != null);

            if (hasVertical)
            {
                int firstRow = GetFirstLetterIndex(TilePlacement.Vertical, validatedBoardTiles, anyRow, anyCol);
                int maxRow = firstRow;
                while (maxRow < boardSizeX && validatedBoardTiles[maxRow + 1, anyCol] != null)
                {
                    maxRow++;
                }
                return new LetterPosition(maxRow, anyCol);
            }

            bool hasHorizontal =
                (anyCol > 1 && validatedBoardTiles[anyRow, anyCol - 1] != null) ||
                (anyCol < boardSizeY && validatedBoardTiles[anyRow, anyCol + 1] != null);

            if (hasHorizontal)
            {
                int firstCol = GetFirstLetterIndex(TilePlacement.Horizontal, validatedBoardTiles, anyRow, anyCol);
                int maxCol = firstCol;
                while (maxCol < boardSizeY && validatedBoardTiles[anyRow, maxCol + 1] != null)
                {
                    maxCol++;
                }
                return new LetterPosition(anyRow, maxCol);
            }

            return move.simulatedTiles[0].letterPosition;
        }

        // Fallback
        SimPlacedTile bestTile = move.simulatedTiles[0];
        foreach (SimPlacedTile simTile in move.simulatedTiles)
        {
            if (simTile == null || simTile.letterPosition == null)
                continue;

            if (simTile.letterPosition.RowX > bestTile.letterPosition.RowX)
                bestTile = simTile;
            else if (simTile.letterPosition.RowX == bestTile.letterPosition.RowX &&
                     simTile.letterPosition.ColY > bestTile.letterPosition.ColY)
                bestTile = simTile;
        }
        return bestTile.letterPosition;
    }


    private int CountEmptySquaresLeft(
    int row,
    int col)
    {
        int count = 0;

        col--;

        while (col >= 1 &&
              validatedBoardTiles[row, col] == null)
        {
            count++;
            col--;
        }

        return count;
    }


    private LetterInfo RemoveRackTile(
    List<LetterInfo> rack,
    char c)
    {
        if (rack == null)
            return null;

        for (int i = 0; i < rack.Count; i++)
        {
            if (rack[i] == null)
                continue;

            if (string.IsNullOrEmpty(rack[i].letter))
                continue;

            if (char.ToUpper(
                rack[i].letter[0]) == c)
            {
                LetterInfo t = rack[i];
                rack.RemoveAt(i);
                return t;
            }
        }

        return null;
    }


    private RoundMove BuildMove(SearchState state, TilePlacement searchOrientation)
    {
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            buildMoveCalls++;

            try
            {
                if (state == null || state.placedTiles == null || state.placedTiles.Count == 0)
                    return null;

                //TilePlacement orientation = InferMoveOrientationFromSimTiles(state.placedTiles);
                TilePlacement orientation = searchOrientation;

                if (orientation == TilePlacement.NoTilePlaced || orientation == TilePlacement.WrongTilePlacement)
                    return null;

                foreach (var sim in state.placedTiles)
                {
                    if (sim == null || sim.letterInfo == null || sim.letterPosition == null)
                        return null;

                    if (validatedBoardTiles[sim.letterPosition.RowX, sim.letterPosition.ColY] != null)
                        return null;
                }

                foreach (var sim in state.placedTiles)
                    validatedBoardTiles[sim.letterPosition.RowX, sim.letterPosition.ColY] = sim.letterInfo;

                List<List<LetterInfo>> allWords = null;
                string mainWord = "";
                int totalScore = 0;
                bool valid = false;

                try
                {
                    allWords = CollectAllWordsForAIMove(orientation, validatedBoardTiles, state.placedTiles);
                    if (allWords != null && allWords.Count > 0 && CheckWordValidity(allWords))
                    {
                        mainWord = GetMainWordFromPlacedTiles(state.placedTiles, validatedBoardTiles, orientation);
                        totalScore = CountAIMoveScore(allWords, state.placedTiles);
                        if (state.placedTiles.Count == maxHandSize)
                            totalScore += 50;
                        valid = true;
                    }
                }
                finally
                {
                    foreach (var sim in state.placedTiles)
                        validatedBoardTiles[sim.letterPosition.RowX, sim.letterPosition.ColY] = null;
                }

                if (!valid)
                    return null;

                return new RoundMove
                {
                    isValid = true,
                    isHuman = false,
                    simulatedTiles = CloneSimPlacedTiles(state.placedTiles),
                    word = mainWord,
                    score = totalScore,
                    timeUsed = 70f
                };
            }
            finally
            {
                sw.Stop();
                buildMoveMs += sw.Elapsed.TotalMilliseconds;
            }
        }
    }

    private bool IsInBounds(int row, int col)
    {
        return row >= 1 &&
               row <= boardSizeX &&
               col >= 1 &&
               col <= boardSizeY;
    }

    private RoundMove CreateInvalidMove(bool isHuman, float timeUsed = 0f)
    {
        return new RoundMove
        {
            isValid = false,
            isHuman = isHuman,
            score = 0,
            word = "",
            timeUsed = timeUsed,
            placedTiles = new List<PlacedTile>(),
            simulatedTiles = new List<SimPlacedTile>()
        };
    }


    private bool CanBuildWordFromTiles(string word, List<LetterInfo> tiles)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        Dictionary<char, int> available = new Dictionary<char, int>();

        if (tiles != null)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                LetterInfo tile = tiles[i];
                if (tile == null || string.IsNullOrEmpty(tile.letter))
                    continue;

                char c = char.ToUpper(tile.letter[0]);

                if (!available.ContainsKey(c))
                    available[c] = 0;

                available[c]++;
            }
        }

        for (int row = 1; row <= boardSizeX; row++)
        {
            for (int col = 1; col <= boardSizeY; col++)
            {
                LetterInfo boardTile = validatedBoardTiles[row, col];
                if (boardTile == null || string.IsNullOrEmpty(boardTile.letter))
                    continue;

                char c = char.ToUpper(boardTile.letter[0]);

                if (!available.ContainsKey(c))
                    available[c] = 0;

                available[c]++;
            }
        }

        Dictionary<char, int> needed = new Dictionary<char, int>();

        word = word.Trim().ToUpperInvariant();

        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];

            if (!needed.ContainsKey(c))
                needed[c] = 0;

            needed[c]++;

            int have = available.ContainsKey(c) ? available[c] : 0;
            if (needed[c] > have)
                return false;
        }

        return true;
    }

    /*private RoundMove FindBestFirstTurnPlacementGaddag(
    List<LetterInfo> rack,
    BonusTile[,] bonusBoard)
    {
        using (FirstTurnMarker.Auto())
        {
            if (rack == null || rack.Count == 0)
                return CreateInvalidMove(false, GetCurrentTimeUsed());

            List<string> words;

            using (FindWordsMarker.Auto())
            {
                words = FindPossibleAIWordsFromRackFast(rack);
            }

            using (ScoreSortMarker.Auto())
            {
                words.Sort((a, b) =>
                {
                    int scoreA = EstimateWordBaseScoreFromRack(a, rack);
                    int scoreB = EstimateWordBaseScoreFromRack(b, rack);

                    if (scoreA != scoreB)
                        return scoreB.CompareTo(scoreA);

                    return b.Length.CompareTo(a.Length);
                });
            }

            int maxWordsToTest = Mathf.Min(words.Count, 30);

            RoundMove best = null;

            using (TestPlacementsMarker.Auto())
            {
                for (int i = 0; i < maxWordsToTest; i++)
                {
                    string word = words[i];
                    if (string.IsNullOrEmpty(word))
                        continue;

                    RoundMove candidate = FindBestFirstTurnPlacement(word, rack, bonusBoard);

                    if (candidate != null && candidate.isValid)
                    {
                        if (best == null || IsBetterAIMove(candidate, best))
                            best = candidate;
                    }
                }
            }

            return best ?? CreateInvalidMove(false, GetCurrentTimeUsed());
        }
    }
    */
    private List<string> FindPossibleAIWordsFromRackFast(List<LetterInfo> rack)
    {
        List<string> results = new List<string>();

        if (rack == null || rack.Count == 0 || scrabbleWords == null)
            return results;

        Dictionary<char, int> rackCounts = new Dictionary<char, int>();

        for (int i = 0; i < rack.Count; i++)
        {
            LetterInfo tile = rack[i];
            if (tile == null || string.IsNullOrEmpty(tile.letter))
                continue;

            char c = char.ToUpper(tile.letter[0]);

            if (!rackCounts.ContainsKey(c))
                rackCounts[c] = 0;

            rackCounts[c]++;
        }

        for (int i = 0; i < scrabbleWords.Count; i++)
        {
            string word = scrabbleWords[i];
            if (string.IsNullOrWhiteSpace(word))
                continue;

            word = word.Trim().ToUpperInvariant();

            if (word.Length == 0 || word.Length > rack.Count)
                continue;

            if (CanBuildWordFromRackCounts(word, rackCounts))
                results.Add(word);
        }

        return results;
    }

    private TilePlacement InferMoveOrientationFromSimTiles(List<SimPlacedTile> tiles)
    {
        if (tiles == null || tiles.Count == 0)
            return TilePlacement.NoTilePlaced;

        if (tiles.Count == 1)
            return TilePlacement.SingleTile;

        int firstRow = tiles[0].letterPosition.RowX;
        int firstCol = tiles[0].letterPosition.ColY;

        bool sameRow = true;
        bool sameCol = true;

        for (int i = 1; i < tiles.Count; i++)
        {
            if (tiles[i] == null || tiles[i].letterPosition == null)
                return TilePlacement.WrongTilePlacement;

            if (tiles[i].letterPosition.RowX != firstRow)
                sameRow = false;

            if (tiles[i].letterPosition.ColY != firstCol)
                sameCol = false;
        }

        if (sameRow)
            return TilePlacement.Horizontal;

        if (sameCol)
            return TilePlacement.Vertical;

        return TilePlacement.WrongTilePlacement;
    }

    private string GetMainWordFromPlacedTiles(
        List<SimPlacedTile> newPlacedTiles,
        LetterInfo[,] board,
        TilePlacement orientation)
    {
        if (newPlacedTiles == null || newPlacedTiles.Count == 0 || board == null)
            return string.Empty;

        if (orientation == TilePlacement.SingleTile)
        {
            int row = newPlacedTiles[0].letterPosition.RowX;
            int col = newPlacedTiles[0].letterPosition.ColY;

            bool hasVertical =
                (row > 1 && board[row - 1, col] != null) ||
                (row < boardSizeX && board[row + 1, col] != null);

            if (hasVertical)
            {
                int firstRow = GetFirstLetterIndex(TilePlacement.Vertical, board, row, col);
                List<LetterInfo> word = GetWordFromBoard(TilePlacement.Vertical, board, firstRow, col);
                return BuildWordString(word);
            }

            bool hasHorizontal =
                (col > 1 && board[row, col - 1] != null) ||
                (col < boardSizeY && board[row, col + 1] != null);

            if (hasHorizontal)
            {
                int firstCol = GetFirstLetterIndex(TilePlacement.Horizontal, board, row, col);
                List<LetterInfo> word = GetWordFromBoard(TilePlacement.Horizontal, board, row, firstCol);
                return BuildWordString(word);
            }

            return newPlacedTiles[0].letterInfo != null ? newPlacedTiles[0].letterInfo.letter : string.Empty;
        }

        if (orientation == TilePlacement.Horizontal)
        {
            int row = newPlacedTiles[0].letterPosition.RowX;
            int minCol = newPlacedTiles[0].letterPosition.ColY;

            for (int i = 1; i < newPlacedTiles.Count; i++)
            {
                if (newPlacedTiles[i].letterPosition.ColY < minCol)
                    minCol = newPlacedTiles[i].letterPosition.ColY;
            }

            int firstCol = GetFirstLetterIndex(TilePlacement.Horizontal, board, row, minCol);
            return BuildWordString(GetWordFromBoard(TilePlacement.Horizontal, board, row, firstCol));
        }

        if (orientation == TilePlacement.Vertical)
        {
            int col = newPlacedTiles[0].letterPosition.ColY;
            int minRow = newPlacedTiles[0].letterPosition.RowX;

            for (int i = 1; i < newPlacedTiles.Count; i++)
            {
                if (newPlacedTiles[i].letterPosition.RowX < minRow)
                    minRow = newPlacedTiles[i].letterPosition.RowX;
            }

            int firstRow = GetFirstLetterIndex(TilePlacement.Vertical, board, minRow, col);
            return BuildWordString(GetWordFromBoard(TilePlacement.Vertical, board, firstRow, col));
        }

        return string.Empty;
    }

    private List<SimPlacedTile> CloneSimPlacedTiles(List<SimPlacedTile> source)
    {
        List<SimPlacedTile> clone = new List<SimPlacedTile>();

        if (source == null)
            return clone;

        for (int i = 0; i < source.Count; i++)
        {
            SimPlacedTile sim = source[i];
            if (sim == null || sim.letterInfo == null || sim.letterPosition == null)
                continue;

            clone.Add(new SimPlacedTile
            {
                letterInfo = new LetterInfo(sim.letterInfo),
                letterPosition = new LetterPosition(
                    sim.letterPosition.RowX,
                    sim.letterPosition.ColY
                )
            }
            );
        }

        return clone;
    }

    private string BuildWordString(List<LetterInfo> wordTiles)
    {
        if (wordTiles == null || wordTiles.Count == 0)
            return string.Empty;

        string word = string.Empty;

        for (int i = 0; i < wordTiles.Count; i++)
        {
            if (wordTiles[i] != null && !string.IsNullOrEmpty(wordTiles[i].letter))
                word += wordTiles[i].letter;
        }

        return word;
    }
    private bool CanBuildWordFromRackCounts(string word, Dictionary<char, int> rackCounts)
    {
        Dictionary<char, int> needed = new Dictionary<char, int>();

        for (int i = 0; i < word.Length; i++)
        {
            char c = char.ToUpper(word[i]);

            if (!needed.ContainsKey(c))
                needed[c] = 0;

            needed[c]++;

            int have = rackCounts.ContainsKey(c) ? rackCounts[c] : 0;
            if (needed[c] > have)
                return false;
        }

        return true;
    }
    private int EstimateWordBaseScoreFromRack(string word, List<LetterInfo> rack)
    {
        if (string.IsNullOrEmpty(word) || rack == null)
            return 0;

        List<LetterInfo> temp = new List<LetterInfo>(rack);
        int total = 0;

        for (int i = 0; i < word.Length; i++)
        {
            char needed = char.ToUpper(word[i]);

            for (int j = 0; j < temp.Count; j++)
            {
                LetterInfo tile = temp[j];
                if (tile == null || string.IsNullOrEmpty(tile.letter))
                    continue;

                if (char.ToUpper(tile.letter[0]) == needed)
                {
                    total += tile.points;
                    temp.RemoveAt(j);
                    break;
                }
            }
        }

        if (word.Length == maxHandSize)
            total += 50;

        return total;
    }

    private RoundMove EvaluatePlayerSubmission()
    {
        // Only a dictionary rejection sets this; the placement failures below
        // return early, so clear it here or a previous turn's word gets coloured.
        rejectedPlayerWord = null;

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

        if (orientation == TilePlacement.NoTilePlaced ||
            orientation == TilePlacement.WrongTilePlacement)
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

        if (!CheckWordValidity(words, out List<LetterInfo> rejectedWord))
        {
            rejectedPlayerWord = rejectedWord;
            move.isValid = false;
            move.score = 0;
            move.word = "";
            return move;
        }

        rejectedPlayerWord = null;
        move.isValid = true;
        move.score = 0;
        move.word = "";

        foreach (var singleList in words)
        {
            string finalWord = "";
            foreach (var letter in singleList)
                finalWord += letter.letter;

            if (move.word == "")
                move.word = finalWord;

            move.score += CountWordPoints(singleList, move.placedTiles);
        }

        if (move.placedTiles.Count == maxHandSize)
            move.score += 50;

        return move;
    }

    public void EndTurnSingleGuess()
    {
        Debug.Log("[TRACE] EndTurnSingleGuess CALLED. roundStarted=" + roundStarted + ", mode=" + currentInitMode);

        if (IsOnlineMatch)
        {
            if (currentState != TurnState.PlayerTurn)
                return;

            currentState = TurnState.Busy;

            if (timer != null)
                timer.StopTimer();

            RoundMove move = EvaluatePlayerSubmission();

            if (move != null && move.isValid)
            {
                if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
                {
                    Singleton.Instance.UIManager.HighlightPlayedWord(
                        GetMainWordCells(move), move.score);
                }

                if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
                    Singleton.Instance.UIManager.ShowRoundMessage("Submitting move...");
            }
            else
            {
                if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
                    Singleton.Instance.UIManager.ShowRoundMessage("Invalid move. Try again.");

                currentState = TurnState.PlayerTurn;

                // The move was not submitted, so the player is still in the same round.
                // Resume the existing timer rather than resetting it.
                if (timer != null)
                    timer.ResumeTimer();

                return;
            }

            onlineSubmissionReady?.Invoke(move);
            return;
        }

        if (!roundStarted)
        {
            Debug.Log("[TRACE] EndTurnSingleGuess starting StartRound directly for SOLO mode");
            StartCoroutine(StartRound());
            return;
        }

        if (roundFlowActive)
            return;

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

        if (pendingPlayerMove != null && pendingPlayerMove.isValid)
        {
            // Confirms the word registered and what it is worth, at the moment the
            // player commits it - not only if it goes on to win the round.
            if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
            {
                Singleton.Instance.UIManager.HighlightPlayedWord(
                    GetMainWordCells(pendingPlayerMove), pendingPlayerMove.score);
            }
        }

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
            Singleton.Instance.UIManager.ShowRoundMessage("Checking your word...");

        StartCoroutine(AutoAdvanceRoundFlow());
    }

    private IEnumerator AutoAdvanceRoundFlow()
    {
        yield return new WaitForSeconds(0.8f);

        while (roundFlowActive)
        {
            AdvanceRoundReveal();

            if (!roundFlowActive)
                yield break;

            if (roundRevealStep >= 3)
            {
                yield return new WaitForSeconds(0.8f);
                AdvanceRoundReveal();
                yield break;
            }

            yield return new WaitForSeconds(0.8f);
        }
    }
    private IEnumerator RefillPlayerHandAnimated(float totalDuration = 2f)
    {
        Debug.Log($"[HAND-DEBUG] RefillPlayerHandAnimated ENTER " +
              $"playerHandTiles={(playerHandTiles == null ? "null" : "not null")} " +
              $"playerHandTiles.Count={(playerHandTiles == null ? -1 : playerHandTiles.Count)} " +
              $"maxHandSize={maxHandSize}");



        //Debug.Log("RefillPlayerHandAnimated START");

        if (playerHandTiles == null)
        {
            Debug.LogError("playerHandTiles is null in RefillPlayerHandAnimated.");
            yield break;
        }

        if (_tileBag == null)
        {
            Debug.LogError("_tileBag is null in RefillPlayerHandAnimated.");
            yield break;
        }

        if (playerHandTiles.Count >= maxHandSize)
        {
            Debug.Log("Hand already full or overfull. No animated refill performed.");
            yield break;
        }

        int availableInBag = _tileBag.GetLetters().Count;
        int tilesMissing = maxHandSize - playerHandTiles.Count;
        int tilesToDraw = Mathf.Min(tilesMissing, availableInBag);

        if (tilesToDraw <= 0)
            yield break;

        float delayBetweenTiles = totalDuration / tilesToDraw;

        for (int i = 0; i < tilesToDraw; i++)
        {
            if (playerHandTiles.Count >= maxHandSize)
                yield break;

            LetterInfo tile = _tileBag.DrawLetterTileFromBag();
            if (tile == null)
            {
                Debug.LogWarning("DrawLetterTileFromBag returned null during animated refill.");
                yield break;
            }
            Debug.Log("[ONLINE-CHECK] RefillPlayerHandAnimated LOCAL DRAW -> " + tile.letter + tile.points);
            playerHandTiles.Add(tile);

            if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
                Singleton.Instance.UIManager.AddTileToHand(tile);

            ResetDisplay();

            if (i < tilesToDraw - 1)
                yield return new WaitForSeconds(delayBetweenTiles);
        }

        Debug.Log("[ONLINE-CHECK] RefillPlayerHandAnimated END. isOnlineMatch=" + isOnlineMatch);
    }
    private bool InitialRackHasPlayableWord()
    {
        if (playerHandTiles == null || playerHandTiles.Count == 0 || scrabbleWords == null || scrabbleWords.Count == 0)
            return false;

        Dictionary<char, int> rackCounts = new Dictionary<char, int>();

        for (int i = 0; i < playerHandTiles.Count; i++)
        {
            LetterInfo tile = playerHandTiles[i];
            if (tile == null || string.IsNullOrEmpty(tile.letter))
                continue;

            char c = char.ToUpper(tile.letter[0]);

            if (!rackCounts.ContainsKey(c))
                rackCounts[c] = 0;

            rackCounts[c]++;
        }

        for (int i = 0; i < scrabbleWords.Count; i++)
        {
            string word = scrabbleWords[i];
            if (string.IsNullOrWhiteSpace(word))
                continue;

            word = word.Trim().ToUpperInvariant();

            if (word.Length < 2 || word.Length > playerHandTiles.Count)
                continue;

            if (CanBuildWordFromRackCounts(word, rackCounts))
                return true;
        }

        return false;
    }

    private IEnumerator EnsurePlayableInitialRack(float refillDuration = 2f, int maxAttempts = 10)
    {

        Debug.Log("[ONLINE-CHECK] EnsurePlayableInitialRack START. isOnlineMatch=" + isOnlineMatch);

        int attempt = 0;

        while (attempt < maxAttempts)
        {
            attempt++;

            if (InitialRackHasPlayableWord())
            {
                Debug.Log("Initial rack is playable on attempt " + attempt);
                yield break;
            }

            Debug.LogWarning("[ONLINE-CHECK] Initial rack not playable. Redrawing. attempt=" + attempt + " isOnlineMatch=" + isOnlineMatch);

            if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
                Singleton.Instance.UIManager.RemoveAllHandTiles();

            if (playerHandTiles != null)
            {
                foreach (LetterInfo tile in playerHandTiles)
                {
                    if (tile != null)
                        AddLetterToBag(tile);
                }

                playerHandTiles.Clear();
            }

            yield return StartCoroutine(RefillPlayerHandAnimated(refillDuration));
            ResetDisplay();
        }

        Debug.LogWarning("EnsurePlayableInitialRack reached maxAttempts. Keeping last rack.");
    }
  
    public void AddLetterToBag(LetterInfo tile)
    {
        if (tile == null || _tileBag == null)
            return;

        List<LetterInfo> bagLetters = _tileBag.GetLetters();
        if (bagLetters == null)
            return;

        bagLetters.Add(tile);
    }

    private static int CompareScoredWords(ScoredRackWord a, ScoredRackWord b)
    {
        int scoreCompare = b.EstimatedScore.CompareTo(a.EstimatedScore);
        if (scoreCompare != 0)
            return scoreCompare;

        return b.Length.CompareTo(a.Length);
    }


    private const int AllLettersMask = (1 << 26) - 1;

    private static int CreateAllLettersAllowed()
    {
        return AllLettersMask;
    }

    private static int AllowLetter(int mask, char c)
    {
        char upper = char.ToUpperInvariant(c);
        if (upper < 'A' || upper > 'Z')
            return mask;

        return mask | (1 << (upper - 'A'));
    }

    private static bool CrossCheckAllows(int mask, char c)
    {
        char upper = char.ToUpperInvariant(c);
        if (upper < 'A' || upper > 'Z')
            return false;

        int bit = 1 << (upper - 'A');
        return (mask & bit) != 0;
    }

    private void StartStageTimer(string stageName)
    {
        if (!enableAITimingLogs) return;

        aiStageStopwatch.Restart();
        Debug.Log("[AI-TIME] START " + stageName + " | t=" + Time.realtimeSinceStartup.ToString("F3"));
    }

    private void EndStageTimer(string stageName)
    {
        if (!enableAITimingLogs) return;

        aiStageStopwatch.Stop();
        Debug.Log("[AI-TIME] END " + stageName + " | dt=" + aiStageStopwatch.Elapsed.TotalMilliseconds.ToString("F2") + "ms | t=" + Time.realtimeSinceStartup.ToString("F3"));
    }

    private void EndTotalAITimer()
    {
        if (!enableAITimingLogs) return;

        aiTotalStopwatch.Stop();
        Debug.Log("[AI-TIME] END EvaluateAIMoveIncremental TOTAL | dt=" + aiTotalStopwatch.Elapsed.TotalMilliseconds.ToString("F2") + "ms | t=" + Time.realtimeSinceStartup.ToString("F3"));
    }


    private sealed class GaddagSearchContext
    {
        public RoundMove bestMove;
        public int candidateCount;
        public System.Diagnostics.Stopwatch sliceTimer;

        public int debugLeftCrossRejects;
        public int debugRightCrossRejects;
        public int debugLeftCrossRejectLogs;
        public int debugRightCrossRejectLogs;

        public int nodeExpansions;

        // NEW
        public int rackTileRemovals;
        public int terminalHits;
        public int buildMoveNulls;
    }

    private IEnumerator FindBestGaddagMoveCoroutine(
        List<LetterInfo> rack,
        BonusTile[,] bonusBoard,
        System.Action<RoundMove> onComplete)
    {

        var totalTimer = System.Diagnostics.Stopwatch.StartNew();

        EnsureAIGaddagReady();

        List<AnchorSquare> anchors = BuildAnchors();

        precalculatedCrossChecks = new int[boardSizeX + 2, boardSizeY + 2];
        precalculatedCrossChecksVertical = new int[boardSizeX + 2, boardSizeY + 2];

        const double frameBudgetMs = 2.0;
        var sliceTimer = System.Diagnostics.Stopwatch.StartNew();

        for (int r = 1; r <= boardSizeX; r++)
        {
            for (int c = 1; c <= boardSizeY; c++)
            {
                precalculatedCrossChecks[r, c] = BuildCrossCheckSet(r, c, TilePlacement.Horizontal);
                precalculatedCrossChecksVertical[r, c] = BuildCrossCheckSet(r, c, TilePlacement.Vertical);

                if (sliceTimer.Elapsed.TotalMilliseconds >= frameBudgetMs)
                {
                    sliceTimer.Restart();
                    yield return null;
                }
            }
        }

        int slowAnchorCount = 0;

        GaddagSearchContext ctx = new GaddagSearchContext
        {
            debugLeftCrossRejects = 0,
            debugRightCrossRejects = 0,
            debugLeftCrossRejectLogs = 0,
            debugRightCrossRejectLogs = 0,
            bestMove = null,
            candidateCount = 0,
            sliceTimer = sliceTimer,
            nodeExpansions = 0
        };

        for (int i = 0; i < anchors.Count; i++)
        {
            AnchorSquare anchor = anchors[i];
            if (anchor == null)
                continue;

            double beforeMs = totalTimer.Elapsed.TotalMilliseconds;

            // Horizontal words through this anchor
            int leftLimit = CountEmptySquaresLeft(anchor.row, anchor.col);

            SearchState horizontalState = new SearchState
            {
                anchorRow = anchor.row,
                anchorCol = anchor.col,
                leftMostCol = anchor.col
            };

                GenerateLeftPart(
                    anchor,
                    anchor.col,
                    aiGaddagLexicon.Root,
                    rack,
                    leftLimit,
                    horizontalState,
                    ctx);

            // Vertical words through this anchor
            int aboveLimit = CountEmptySquaresAbove(anchor.row, anchor.col);

            SearchState verticalState = new SearchState
            {
                anchorRow = anchor.row,
                anchorCol = anchor.col,
                leftMostCol = anchor.col
            };

                GenerateTopPart(
                    anchor,
                    anchor.row,
                    aiGaddagLexicon.Root,
                    rack,
                    aboveLimit,
                    verticalState,
                    ctx);

            double anchorMs = totalTimer.Elapsed.TotalMilliseconds - beforeMs;
            if (anchorMs > 2.0)
                slowAnchorCount++;

            if (sliceTimer.Elapsed.TotalMilliseconds >= frameBudgetMs)
            {
                sliceTimer.Restart();
                yield return null;
            }
        }

        Debug.Log(
        $"GADDAG total={totalTimer.Elapsed.TotalMilliseconds:F1}ms " +
        $"anchors={anchors.Count} slow={slowAnchorCount} " +
        $"candidates={ctx.candidateCount} best={(ctx.bestMove != null ? ctx.bestMove.word : "NONE")} " +
        $"xLeft={ctx.debugLeftCrossRejects} xRight={ctx.debugRightCrossRejects} " +
        $"rackRemovals={ctx.rackTileRemovals} terminalHits={ctx.terminalHits} buildMoveNulls={ctx.buildMoveNulls}"
        );

        precalculatedCrossChecks = null;
        precalculatedCrossChecksVertical = null;

        // At this point, aiDifficultyCandidates has been filled inside
        // GenerateLeftPart/GenerateTopPart via ConsiderAIDifficultyCandidate.
        RoundMove chosen = SelectMoveForDifficulty(aiDifficultyCandidates);

        onComplete?.Invoke(chosen ?? ctx.bestMove);
    }

    private bool ShouldYieldSearch(GaddagSearchContext ctx)
    {
        ctx.nodeExpansions++;

        if (ctx.nodeExpansions >= GaddagNodeBudgetPerSlice)
        {
            ctx.nodeExpansions = 0;
            return true;
        }

        if (ctx.sliceTimer.Elapsed.TotalMilliseconds >= GaddagFrameBudgetMs)
        {
            ctx.nodeExpansions = 0;
            return true;
        }

        return false;
    }

    private void GenerateLeftPart(
        AnchorSquare anchor,
        int col,
        GaddagNode node,
        List<LetterInfo> rack,
        int limit,
        SearchState state,
        GaddagSearchContext ctx)
    {
        if (node == null || node.edges == null || state == null || rack == null || ctx == null)
            return;


        if (col < 1)
        {
            if (node.edges.TryGetValue(GaddagLexicon.Separator, out var sepNode))
            {
                    GenerateRightPart(
                        state.anchorRow,
                        anchor.col + 1,
                        sepNode,
                        rack,
                        state,
                        ctx);
            }
            return;
        }

        if (validatedBoardTiles[state.anchorRow, col] == null)
        {
            if (node.edges.TryGetValue(GaddagLexicon.Separator, out var separatorNode))
            {
                    GenerateRightPart(
                        state.anchorRow,
                        anchor.col + 1,
                        separatorNode,
                        rack,
                        state,
                        ctx);
            }

            bool canGoLeft = (col == anchor.col) || (limit > 0);
            if (!canGoLeft)
                return;

            int nextLimit = (col == anchor.col) ? limit : (limit - 1);

            foreach (var edge in node.edges)
            {
                char c = edge.Key;
                if (c == GaddagLexicon.Separator)
                    continue;

                LetterInfo tile = RemoveRackTile(rack, c);
                if (tile == null)
                    continue;
                ctx.rackTileRemovals++;

                if (!PassCrossCheck(state.anchorRow, col, c, TilePlacement.Horizontal))
                {
                    ctx.debugLeftCrossRejects++;

                    if (ctx.debugLeftCrossRejectLogs < DebugCrossRejectLogLimit)
                    {
                        ctx.debugLeftCrossRejectLogs++;
                        Debug.Log($"XCHECK LEFT reject at ({state.anchorRow},{col}) for {c}");
                    }

                    rack.Add(tile);
                    continue;
                }

                state.placedTiles.Add(new SimPlacedTile
                {
                    letterInfo = tile,
                    letterPosition = new LetterPosition(state.anchorRow, col)
                });

                if (col < state.leftMostCol)
                    state.leftMostCol = col;

                    GenerateLeftPart(
                        anchor,
                        col - 1,
                        edge.Value,
                        rack,
                        nextLimit,
                        state,
                        ctx);

                state.placedTiles.RemoveAt(state.placedTiles.Count - 1);
                rack.Add(tile);

                state.leftMostCol = anchor.col;
                for (int i = 0; i < state.placedTiles.Count; i++)
                {
                    int placedCol = state.placedTiles[i].letterPosition.ColY;
                    if (placedCol < state.leftMostCol)
                        state.leftMostCol = placedCol;
                }

            }
        }
        else
        {
            var boardTile = validatedBoardTiles[state.anchorRow, col];
            if (boardTile != null && !string.IsNullOrEmpty(boardTile.letter))
            {
                char boardChar = char.ToUpperInvariant(boardTile.letter[0]);

                if (node.edges.TryGetValue(boardChar, out var nextNode))
                {
                        GenerateLeftPart(
                            anchor,
                            col - 1,
                            nextNode,
                            rack,
                            limit,
                            state,
                            ctx);
                }
            }
        }
    }

    private void GenerateRightPart(
        int row,
        int col,
        GaddagNode node,
        List<LetterInfo> rack,
        SearchState state,
        GaddagSearchContext ctx)
    {
        if (node == null || node.edges == null || state == null || rack == null || ctx == null)
            return;


        if (col > boardSizeY)
        {
            if (node.isTerminal)
            {
                ctx.terminalHits++;
                RoundMove move = BuildMove(state, TilePlacement.Horizontal);
                if (move != null)
                {
                    ConsiderAIDifficultyCandidate(move);

                    ctx.candidateCount++;
                    if (ctx.bestMove == null || IsBetterAIMove(move, ctx.bestMove))
                        ctx.bestMove = move;
                }
            }
            return;
        }

        if (validatedBoardTiles[row, col] == null)
        {
            if (node.isTerminal)
            {
                ctx.terminalHits++;
                RoundMove move = BuildMove(state, TilePlacement.Horizontal);
                if (move != null)
                {
                    ConsiderAIDifficultyCandidate(move);

                    ctx.candidateCount++;
                    if (ctx.bestMove == null || IsBetterAIMove(move, ctx.bestMove))
                        ctx.bestMove = move;
                }
            }

            foreach (var edge in node.edges)
            {
                char c = edge.Key;
                if (c == GaddagLexicon.Separator)
                    continue;

                LetterInfo tile = RemoveRackTile(rack, c);
                if (tile == null)
                    continue;
                ctx.rackTileRemovals++;

                if (!PassCrossCheck(row, col, c, TilePlacement.Horizontal))
                {
                    ctx.debugRightCrossRejects++;

                    if (ctx.debugRightCrossRejectLogs < DebugCrossRejectLogLimit)
                    {
                        ctx.debugRightCrossRejectLogs++;
                        Debug.Log($"XCHECK RIGHT reject at ({row},{col}) for {c}");
                    }

                    rack.Add(tile);
                    continue;
                }

                state.placedTiles.Add(new SimPlacedTile
                {
                    letterInfo = tile,
                    letterPosition = new LetterPosition(row, col)
                });

                    GenerateRightPart(
                        row,
                        col + 1,
                        edge.Value,
                        rack,
                        state,
                        ctx);

                state.placedTiles.RemoveAt(state.placedTiles.Count - 1);
                rack.Add(tile);
            }
        }
        else
        {
            var boardTile = validatedBoardTiles[row, col];
            if (boardTile != null && !string.IsNullOrEmpty(boardTile.letter))
            {
                char boardChar = char.ToUpperInvariant(boardTile.letter[0]);

                if (node.edges.TryGetValue(boardChar, out var nextNode))
                {
                        GenerateRightPart(
                            row,
                            col + 1,
                            nextNode,
                            rack,
                            state,
                            ctx);
                }
            }
        }
    }
    private void EnsureAIGaddagReady()
    {
        if (aiGaddagReady &&
            aiGaddagLexicon != null &&
            aiGaddagLexicon.Root != null)
        {
            return;
        }

        if (aiGaddagLoading)
            return;

        StartCoroutine(LoadAIGaddagFromBinary());
    }


    private IEnumerator LoadAIGaddagFromBinary()
    {
        aiGaddagLoading = true;

        Stopwatch timer = Stopwatch.StartNew();

        string binaryPath = Path.Combine(
            Application.streamingAssetsPath,
            GaddagBinaryFileName);

        using (UnityWebRequest request = UnityWebRequest.Get(binaryPath))
        {
            yield return request.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
        bool requestFailed =
            request.result != UnityWebRequest.Result.Success;
#else
            bool requestFailed =
                request.isNetworkError || request.isHttpError;
#endif

            if (requestFailed)
            {
                aiGaddagLoading = false;

                Debug.LogError(
                    $"[GADDAG] Failed to load gaddag.bin | " +
                    $"path={binaryPath} | error={request.error}");

                yield break;
            }

            byte[] bytes = request.downloadHandler.data;

            try
            {
                GaddagNode.ResetCounters();

                aiGaddagLexicon = GaddagLexicon.LoadFromBinary(bytes);

                aiGaddagReady = true;

                timer.Stop();

                Debug.Log(
                    $"[AI-TIME] GADDAG binary loaded | " +
                    $"bytes={bytes.Length:N0} | " +
                    $"nodes={aiGaddagLexicon.NodeCount:N0} | " +
                    $"createdNodes={GaddagNode.CreatedCount:N0} | " +
                    $"dt={timer.Elapsed.TotalMilliseconds:F2}ms");
            }
            catch (Exception exception)
            {
                aiGaddagReady = false;

                Debug.LogError(
                    $"[GADDAG] Failed to deserialize gaddag.bin | " +
                    $"error={exception}");
            }
            finally
            {
                aiGaddagLoading = false;
            }
        }
    }
    /*
    private void EnsureAIGaddagReady()
    {
        if (aiGaddagReady && aiGaddagLexicon != null && aiGaddagLexicon.Root != null)
            return;

        var buildTimer = System.Diagnostics.Stopwatch.StartNew();
        GaddagNode.CreatedCount = 0;
        aiGaddagLexicon = new GaddagLexicon();

        int addedWords = 0;

        if (scrabbleWords != null)
        {
            for (int i = 0; i < scrabbleWords.Count; i++)
            {
                string word = scrabbleWords[i];

                if (string.IsNullOrWhiteSpace(word))
                    continue;

                aiGaddagLexicon.AddWord(word);
                addedWords++;
            }
        }

        aiGaddagReady = true;

        buildTimer.Stop();
        aiGaddagBuildMs = buildTimer.Elapsed.TotalMilliseconds;

        if (!aiGaddagBuildLogged)
        {
            aiGaddagBuildLogged = true;

            Debug.Log(
                        $"[AI-TIME] GADDAG build complete | " +
                        $"sourceWords={scrabbleWords?.Count ?? 0} | " +
                        $"addedWords={addedWords} | " +
                        $"nodes={GaddagNode.CreatedCount:N0} | " +
                        $"dt={aiGaddagBuildMs:F2}ms"
                    );
        }
    }*/
    private string RackToString(List<LetterInfo> tiles)
    {
        if (tiles == null) return "NULL";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < tiles.Count; i++)
        {
            if (tiles[i] == null || string.IsNullOrEmpty(tiles[i].letter)) continue;
            sb.Append(tiles[i].letter.ToUpperInvariant());
        }
        return sb.ToString();
    }

    private int CountValidatedBoardTiles()
    {
        int count = 0;
        for (int r = 1; r <= boardSizeX; r++)
            for (int c = 1; c <= boardSizeY; c++)
                if (validatedBoardTiles[r, c] != null)
                    count++;
        return count;
    }

    private int CountEmptySquaresAbove(int row, int col)
    {
        int count = 0;

        row--;

        while (row >= 1 &&
              validatedBoardTiles[row, col] == null)
        {
            count++;
            row--;
        }

        return count;
    }

    private void GenerateTopPart(
    AnchorSquare anchor,
    int row,
    GaddagNode node,
    List<LetterInfo> rack,
    int limit,
    SearchState state,
    GaddagSearchContext ctx)
    {
        if (node == null || node.edges == null || state == null || rack == null || ctx == null)
            return;


        if (row < 1)
        {
            if (node.edges.TryGetValue(GaddagLexicon.Separator, out var sepNode))
            {
                    GenerateBottomPart(
                        anchor.row + 1,
                        state.anchorCol,
                        sepNode,
                        rack,
                        state,
                        ctx);
            }
            return;
        }

        if (validatedBoardTiles[row, state.anchorCol] == null)
        {
            if (node.edges.TryGetValue(GaddagLexicon.Separator, out var separatorNode))
            {
                    GenerateBottomPart(
                        anchor.row + 1,
                        state.anchorCol,
                        separatorNode,
                        rack,
                        state,
                        ctx);
            }

            bool canGoUp = (row == anchor.row) || (limit > 0);
            if (!canGoUp)
                return;

            int nextLimit = (row == anchor.row) ? limit : (limit - 1);

            foreach (var edge in node.edges)
            {
                char c = edge.Key;
                if (c == GaddagLexicon.Separator)
                    continue;

                LetterInfo tile = RemoveRackTile(rack, c);
                if (tile == null)
                    continue;
                ctx.rackTileRemovals++;

                if (!PassCrossCheck(row, state.anchorCol, c, TilePlacement.Vertical))
                {
                    rack.Add(tile);
                    continue;
                }

                state.placedTiles.Add(new SimPlacedTile
                {
                    letterInfo = tile,
                    letterPosition = new LetterPosition(row, state.anchorCol)
                });

                    GenerateTopPart(
                        anchor,
                        row - 1,
                        edge.Value,
                        rack,
                        nextLimit,
                        state,
                        ctx);

                state.placedTiles.RemoveAt(state.placedTiles.Count - 1);
                rack.Add(tile);

            }
        }
        else
        {
            var boardTile = validatedBoardTiles[row, state.anchorCol];
            if (boardTile != null && !string.IsNullOrEmpty(boardTile.letter))
            {
                char boardChar = char.ToUpperInvariant(boardTile.letter[0]);

                if (node.edges.TryGetValue(boardChar, out var nextNode))
                {
                        GenerateTopPart(
                            anchor,
                            row - 1,
                            nextNode,
                            rack,
                            limit,
                            state,
                            ctx);
                }
            }
        }
    }

    private void GenerateBottomPart(
        int row,
        int col,
        GaddagNode node,
        List<LetterInfo> rack,
        SearchState state,
        GaddagSearchContext ctx)
    {
        if (node == null || node.edges == null || state == null || rack == null || ctx == null)
            return;


        if (row > boardSizeX)
        {
            if (node.isTerminal)
            {
                ctx.terminalHits++;
                RoundMove move = BuildMove(state, TilePlacement.Vertical);
                if (move != null)
                {
                    ConsiderAIDifficultyCandidate(move);

                    ctx.candidateCount++;
                    if (ctx.bestMove == null || IsBetterAIMove(move, ctx.bestMove))
                        ctx.bestMove = move;
                }
            }
            return;
        }

        if (validatedBoardTiles[row, col] == null)
        {
            if (node.isTerminal)
            {
                ctx.terminalHits++;
                RoundMove move = BuildMove(state, TilePlacement.Vertical);
                if (move != null)
                {
                    ConsiderAIDifficultyCandidate(move);
                    ctx.candidateCount++;
                    if (ctx.bestMove == null || IsBetterAIMove(move, ctx.bestMove))
                        ctx.bestMove = move;
                }
            }

            foreach (var edge in node.edges)
            {
                char c = edge.Key;
                if (c == GaddagLexicon.Separator)
                    continue;

                LetterInfo tile = RemoveRackTile(rack, c);
                if (tile == null)
                    continue;
                ctx.rackTileRemovals++;

                if (!PassCrossCheck(row, col, c, TilePlacement.Vertical))
                {
                    rack.Add(tile);
                    continue;
                }

                state.placedTiles.Add(new SimPlacedTile
                {
                    letterInfo = tile,
                    letterPosition = new LetterPosition(row, col)
                });

                    GenerateBottomPart(
                        row + 1,
                        col,
                        edge.Value,
                        rack,
                        state,
                        ctx);

                state.placedTiles.RemoveAt(state.placedTiles.Count - 1);
                rack.Add(tile);

            }
        }
        else
        {
            var boardTile = validatedBoardTiles[row, col];
            if (boardTile != null && !string.IsNullOrEmpty(boardTile.letter))
            {
                char boardChar = char.ToUpperInvariant(boardTile.letter[0]);

                if (node.edges.TryGetValue(boardChar, out var nextNode))
                {
                        GenerateBottomPart(
                            row + 1,
                            col,
                            nextNode,
                            rack,
                            state,
                            ctx);
                }
            }
        }
    }

    private void RequestOpponentMove()
    {
        if (opponentMoveRequested)
            return;

        opponentMoveRequested = true;
        opponentMoveReady = false;

        switch (gameMode)
        {
            case GameMode.HumanVsAI:
                StartCoroutine(RequestAIOpponentMove());
                break;

            case GameMode.HumanVsHumanLocal:
                RequestLocalHumanOpponentMove();
                break;

            case GameMode.HumanVsHumanOnline:
                RequestOnlineOpponentMove();
                break;
        }
    }

    private IEnumerator RequestAIOpponentMove()
    {
        if (!aiEvaluationFinished && !aiEvaluationRunning)
            StartCoroutine(EvaluateAIMoveIncremental());

        while (aiEvaluationRunning)
            yield return null;

        OnOpponentMoveReady(aiBestMoveSoFar);
    }

    private void RequestLocalHumanOpponentMove()
    {
        currentState = TurnState.PlayerTurn;
        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
            Singleton.Instance.UIManager.ShowRoundMessage("Player 2: build your word, then press EndTurn.");
    }

    private void RequestOnlineOpponentMove()
    {
        currentState = TurnState.Busy;
        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
            Singleton.Instance.UIManager.ShowRoundMessage("Waiting for opponent move...");
    }

    private void OnOpponentMoveReady(RoundMove move)
    {
        pendingAIMove = move;
        opponentMoveReady = true;
    }

    private void SubmitLocalOpponentMove()
    {
        if (gameMode != GameMode.HumanVsHumanLocal)
            return;

        if (!opponentMoveRequested || opponentMoveReady)
            return;

        RoundMove move = EvaluatePlayerSubmission();
        move.isHuman = false;

        OnOpponentMoveReady(move);

        currentState = TurnState.Busy;

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
        {
            if (move != null && move.isValid)
                Singleton.Instance.UIManager.ShowRoundMessage(
                    "Player 2 submitted " + move.word + " for " + move.score + " points. Press EndTurn."
                );
            else
                Singleton.Instance.UIManager.ShowRoundMessage(
                    "Player 2 submitted an invalid move. Press EndTurn."
                );
        }
    }
    public void StartOnlineMatch(MatchData match, string localUid)
    {
        isOnlineMatch = true;
        localPlayerUid = localUid;
        currentMatchId = match.matchId;
        isLocalPlayerHost = (localUid == match.player1Uid);

        InitFromMatchData(match);
    }

    public void InitFromMatchData(MatchData match)
    {
        if (match == null)
        {
            Debug.LogError("[GameLogic] InitFromMatchData: match is null.");
            return;
        }

        BoardStateData board = null;

        if (!string.IsNullOrEmpty(match.boardStateJson))
            board = JsonUtility.FromJson<BoardStateData>(match.boardStateJson);

        LoadBoardStateIntoValidatedTiles(board);

        validatedBoardTiles = new LetterInfo[boardSizeX + 2, boardSizeY + 2];

        if (board != null && board.cells != null)
        {
            foreach (var cell in board.cells)
            {
                if (cell == null || !cell.occupied || cell.tile == null)
                    continue;

                int row = cell.y + 1;
                int col = cell.x + 1;
                validatedBoardTiles[row, col] = TileDataToLetterInfo(cell.tile);
            }
        }

        RackStateData rack = null;

        if (!string.IsNullOrEmpty(match.sharedrackjson))
            rack = JsonUtility.FromJson<RackStateData>(match.sharedrackjson);

        playerHandTiles = new List<LetterInfo>();

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
            Singleton.Instance.UIManager.RemoveAllHandTiles();

        if (rack != null && rack.tiles != null)
        {
            foreach (var tile in rack.tiles)
            {
                if (tile == null)
                    continue;

                LetterInfo info = TileDataToLetterInfo(tile);
                playerHandTiles.Add(info);

                if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
                    Singleton.Instance.UIManager.AddTileToHand(info);
            }
        }

        ResetDisplay();
        ApplyBonusBoardFromMatch(match.bonusBoardJson);
    }

    public RoundMove EvaluateLocalSubmissionForOnline()
    {
        return EvaluatePlayerSubmission(); // reuses your existing validation/scoring exactly as-is
    }

    private LetterInfo TileDataToLetterInfo(TileData tile)
    {
        return new LetterInfo(tile.letter, tile.value);
    }

    // Subsequent round updates — same population, called each time MatchController sees the round advance
    public void ApplyMatchUpdate(MatchData match)
    {
        InitFromMatchData(match);
        ApplyOnlineRoundResult(match.lastRoundResultJson, localPlayerUid);
    }
    private IEnumerator ApplyBonusBoardDelayed(string bonusBoardJson)
    {
        yield return null; // wait one frame so BoardGen.Start() has built the GhostTile grid
        ApplyBonusBoardFromMatch(bonusBoardJson);
    }

    public void BeginOnlineMatchFromRack(
        int maxHandSize,
        int boardSizeX,
        int boardSizeY,
        List<LetterInfo> localRack,
        int localScore,
        int opponentScore,
        int turnNumber,
        string bonusBoardJson,
        string boardStateJson,
        string lastRoundResultJson,
        int totalRounds)
    {
        Debug.Log("[ONLINE] BeginOnlineMatchFromRack CALLED");

        SetMaxRounds(totalRounds);

        StopAllCoroutines();
        ClearBoardForNewGame();
        InitGame(maxHandSize, boardSizeX, boardSizeY, GameInitMode.Online);

        // NEW: apply persisted board state from match
        if (!string.IsNullOrEmpty(boardStateJson))
        {
            BoardStateData savedBoard = JsonUtility.FromJson<BoardStateData>(boardStateJson);
            if (savedBoard != null && savedBoard.cells != null)
            {
                ApplyBoardStateToScene(savedBoard);
            }
            else
            {
                Debug.LogWarning("[ONLINE] boardStateJson could not be parsed, starting from empty board.");
            }
        }
        else
        {
            Debug.Log("[ONLINE] boardStateJson empty, starting from empty board.");
        }

        // Existing: bonus board JSON (multipliers etc.)
        //StartCoroutine(ApplyBonusBoardDelayed(bonusBoardJson));

        StartCoroutine(BeginOnlineRoundIntro(bonusBoardJson,lastRoundResultJson));

        if (localRack == null)
            localRack = new List<LetterInfo>();

        playerHandTiles = new List<LetterInfo>();
        foreach (var tile in localRack)
        {
            if (tile != null)
                playerHandTiles.Add(new LetterInfo(tile));
        }

        humanTotalScore = localScore;
        aiTotalScore = opponentScore;
        currentRoundNumber = Mathf.Max(1, turnNumber);
        roundStarted = true;
        currentState = TurnState.PlayerTurn;

        RebuildHandUIFromLogicalHand();
        SaveCurrentRoundSnapshot();

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
        {
            Singleton.Instance.UIManager.UpdateRoundText(currentRoundNumber, maxRounds);
            Singleton.Instance.UIManager.UpdateTotalScores(humanTotalScore, aiTotalScore);
            Singleton.Instance.UIManager.ClearRoundMessage();
        }

        Debug.Log("[ONLINE] Local hydrated rack count = " + playerHandTiles.Count);
    }

    private IEnumerator BeginOnlineRoundIntro(
    string bonusBoardJson,
    string lastRoundResultJson)
    {
        SetInputLocked(true);

        if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
            Singleton.Instance.UIManager.ClearRoundMessage();

        RoundResultData previousResult = null;

        if (!string.IsNullOrEmpty(lastRoundResultJson))
            previousResult = JsonUtility.FromJson<RoundResultData>(lastRoundResultJson);

        bool hasReplay =
            previousResult != null &&
            previousResult.anyValidMove &&
            !string.IsNullOrEmpty(previousResult.winningTilesJson);

        if (hasReplay)
        {
            SimTileListWrapper winningTiles =
                JsonUtility.FromJson<SimTileListWrapper>(
                    previousResult.winningTilesJson
                );

            if (winningTiles != null &&
                winningTiles.tiles != null &&
                winningTiles.tiles.Count > 0 &&
                Singleton.Instance != null &&
                Singleton.Instance.UIManager != null)
            {
                yield return StartCoroutine(
                    Singleton.Instance.UIManager.PlayWinningWordReplay(
                        winningTiles.tiles,
                        2f
                    )
                );
            }
        }

        yield return StartCoroutine(ApplyBonusBoardDelayed(bonusBoardJson));

        SaveCurrentRoundSnapshot();

        if (timer != null)
        {
            timer.ResetTimer();
            timer.StartTimer();
        }

        SetInputLocked(false);
    }


    private void ApplyBoardStateToScene(BoardStateData board)
    {
        if (board == null || board.cells == null)
            return;

        validatedBoardTiles = new LetterInfo[boardSizeX + 2, boardSizeY + 2];

        if (Singleton.Instance == null || Singleton.Instance.UIManager == null)
        {
            Debug.LogWarning("[ONLINE] UIManager not available; board state loaded logically only.");
            return;
        }

        Singleton.Instance.UIManager.ClearCommittedBoardTiles();

        foreach (BoardCellData cellData in board.cells)
        {
            if (!cellData.occupied || cellData.tile == null)
                continue;

            int row = cellData.y + 1;
            int col = cellData.x + 1;

            if (row < 0 || row >= validatedBoardTiles.GetLength(0) ||
                col < 0 || col >= validatedBoardTiles.GetLength(1))
                continue;

            LetterInfo info = new LetterInfo(cellData.tile.letter, cellData.tile.value);
            validatedBoardTiles[row, col] = info;

            Singleton.Instance.UIManager.PlaceAITileOnBoard(
                info,
                new LetterPosition(row, col)
            );
        }
    }

    public void ApplyOnlineRoundResult(string lastRoundResultJson, string localUid)
    {
        if (string.IsNullOrEmpty(lastRoundResultJson))
            return;

        RoundResultData result = JsonUtility.FromJson<RoundResultData>(lastRoundResultJson);
        if (result == null)
            return;

        if (Singleton.Instance == null || Singleton.Instance.UIManager == null)
            return;

        if (!result.anyValidMove)
        {
            Singleton.Instance.UIManager.ShowRoundMessage("No valid move this round.");
            return;
        }

        bool localWon = result.winnerUid == localUid;

        Singleton.Instance.UIManager.ShowRoundMessage(
            (localWon ? "You won" : (result.winnerDisplayName + " won")) +
            " round " + result.roundNumber + " with " + result.winnerWord +
            " for " + result.winnerScore + " points."
        );
    }

    public void EnsureBoardInitializedForOnline()
    {
        if (boardSizeX <= 0 || boardSizeY <= 0)
        {
            var boardGen = UnityEngine.Object.FindAnyObjectByType<BoardGen>();
            if (boardGen != null)
            {
                boardSizeX = boardGen.RowY;
                boardSizeY = boardGen.RowX;
            }
        }

        if (validatedBoardTiles == null)
            validatedBoardTiles = new LetterInfo[boardSizeX + 2, boardSizeY + 2];

        if (boardBonusTiles == null)
            boardBonusTiles = new BonusTile[boardSizeX, boardSizeY];
    }

    public string GenerateBonusBoardJsonForOnlineMatch()
    {
        Debug.Log("[BONUS] GenerateBonusBoardJsonForOnlineMatch ENTER");

        if (boardSizeX <= 0 || boardSizeY <= 0)
        {
            Debug.LogError("[BONUS] Invalid board size before generation: " + boardSizeX + "x" + boardSizeY);
            return JsonUtility.ToJson(new BonusBoardData());
        }

        if (validatedBoardTiles == null)
            validatedBoardTiles = new LetterInfo[boardSizeX + 2, boardSizeY + 2];

        boardBonusTiles = new BonusTile[boardSizeX, boardSizeY];

        Debug.Log(
            "[BONUS] boardBonusTiles created. Size=" +
            boardBonusTiles.GetLength(0) + "x" +
            boardBonusTiles.GetLength(1)
        );

        if (bonusTileBag != null && bonusBag != null)
        {
            Debug.Log("[BONUS] Resetting bonus bag");
            bonusTileBag.ResetBonusBag(bonusBag);
        }
        else
        {
            Debug.LogWarning("[BONUS] Cannot reset bonus bag");
            return JsonUtility.ToJson(new BonusBoardData());
        }

        Debug.Log("[BONUS] Calling PlaceBonusTilesOnBoard()");
        PlaceBonusTilesOnBoard();
        Debug.Log("[BONUS] Returned from PlaceBonusTilesOnBoard()");

        BonusBoardData data = new BonusBoardData();
        int bonusCount = 0;

        for (int x = 0; x < boardBonusTiles.GetLength(0); x++)
        {
            for (int y = 0; y < boardBonusTiles.GetLength(1); y++)
            {
                BonusTile tile = boardBonusTiles[x, y];
                if (tile == null) continue;

                bonusCount++;
                data.cells.Add(new BonusCellData
                {
                    x = x,
                    y = y,
                    bonusType = tile.bonusType.ToString()
                });
            }
        }

        Debug.Log("[BONUS] Bonus cells collected=" + bonusCount);

        string json = JsonUtility.ToJson(data);

        Debug.Log("[BONUS] JSON length=" + (string.IsNullOrEmpty(json) ? 0 : json.Length));
        Debug.Log("[BONUS] GenerateBonusBoardJsonForOnlineMatch EXIT");

        return json;
    }

    public void ApplyBonusBoardFromMatch(
    string bonusBoardJson,
    bool animateReveal = true)
    {
        EnsureBoardInitializedForOnline();

        boardBonusTiles = new BonusTile[boardSizeX, boardSizeY];

        if (string.IsNullOrEmpty(bonusBoardJson))
            return;

        BonusBoardData data = JsonUtility.FromJson<BonusBoardData>(bonusBoardJson);
        if (data == null || data.cells == null) return;

        foreach (var cell in data.cells)
        {
            if (Enum.TryParse<BonusType>(cell.bonusType, out BonusType parsedType))
            {
                boardBonusTiles[cell.x, cell.y] = new BonusTile(parsedType);
            }
        }

        if (bonusBoardView != null)
        {
            if (animateReveal)
                bonusBoardView.StartRevealBonusTiles(0.3f);
            else
                bonusBoardView.DrawBonusTilesImmediately();
        }
    }

    public void SetInputLocked(bool locked)
    {
        currentState = locked ? TurnState.Busy : TurnState.PlayerTurn;
    }

    public float GetRemainingRoundTime()
    {
        if (timer == null) return 0f;
        return timer.GetRemainingTime();
    }

    public void LoadBoardStateIntoValidatedTiles(BoardStateData board)
    {
        // Ensure the array exists
        validatedBoardTiles = new LetterInfo[boardSizeX + 2, boardSizeY + 2];

        if (board == null || board.cells == null)
            return;

        foreach (var cell in board.cells)
        {
            if (cell == null || !cell.occupied || cell.tile == null)
                continue;

            int row = cell.y + 1;
            int col = cell.x + 1;

            if (row < 0 || row >= validatedBoardTiles.GetLength(0) ||
                col < 0 || col >= validatedBoardTiles.GetLength(1))
                continue;

            validatedBoardTiles[row, col] = TileDataToLetterInfo(cell.tile);
        }
    }

    // Both replays animate through UIManager.PlayMovePreview with these colours,
    // so solo and online look the same by construction rather than by agreement.
    private static readonly Color ReplayFirstPlayerColour = new Color(0.25f, 0.65f, 1f, 1f);
    private static readonly Color ReplaySecondPlayerColour = new Color(1f, 0.70f, 0.20f, 1f);
    private static readonly Color ReplayWinnerColour = new Color(0.20f, 1f, 0.34f, 1f);

    public IEnumerator ReplaySoloRound(RoundResult round)
    {
        if (round == null ||
            Singleton.Instance == null ||
            Singleton.Instance.UIManager == null)
        {
            Debug.LogWarning("[REPLAY] Cannot replay solo round.");
            yield break;
        }

        SetInputLocked(true);

        UIManager ui = Singleton.Instance.UIManager;

        ui.ClearCommittedBoardTiles();

        if (validatedBoardTiles != null)
            System.Array.Clear(validatedBoardTiles, 0, validatedBoardTiles.Length);

        // No stored board snapshot is needed: replaying the earlier rounds'
        // winners in order reproduces exactly the board this round was played on.
        foreach (RoundResult earlier in roundHistory)
        {
            if (earlier == null || earlier.roundNumber >= round.roundNumber)
                continue;

            PlaceReplayTiles(earlier.winnerTiles);
        }

        // The squares as they were scattered for this round, not as they stand now.
        RestoreBonusBoard(round.bonusBoard);

        yield return new WaitForSecondsRealtime(0.5f);

        if (round.humanValid && round.humanTiles.Count > 0)
        {
            ui.ShowRoundMessage(
                $"You played {round.humanWord} ({round.humanScore} points)");

            yield return StartCoroutine(
                ui.PlayMovePreview(round.humanTiles, ReplayFirstPlayerColour, 1.5f));
        }

        if (round.aiValid && round.aiTiles.Count > 0)
        {
            ui.ShowRoundMessage(
                $"Opponent played {round.aiWord} ({round.aiScore} points)");

            yield return StartCoroutine(
                ui.PlayMovePreview(round.aiTiles, ReplaySecondPlayerColour, 1.5f));
        }

        if (round.winnerTiles.Count > 0)
        {
            string winnerWord = round.humanWasWinner ? round.humanWord : round.aiWord;
            int winnerScore = round.humanWasWinner ? round.humanScore : round.aiScore;

            ui.ShowRoundMessage(
                $"{winnerWord} wins the round for {winnerScore} points!");

            yield return StartCoroutine(
                ui.PlayMovePreview(round.winnerTiles, ReplayWinnerColour, 2f));

            PlaceReplayTiles(round.winnerTiles);
        }
        else
        {
            ui.ShowRoundMessage("No valid move this round.");
        }

        SetInputLocked(false);
    }

    private void PlaceReplayTiles(List<SimPlacedTileData> tiles)
    {
        if (tiles == null)
            return;

        foreach (SimPlacedTileData tile in tiles)
        {
            if (tile == null ||
                tile.row < 1 || tile.row > boardSizeX ||
                tile.col < 1 || tile.col > boardSizeY)
                continue;

            LetterInfo letterInfo = new LetterInfo(tile.letter, tile.points);
            letterInfo.bonusUsed = true;

            validatedBoardTiles[tile.row, tile.col] = letterInfo;

            if (Singleton.Instance != null && Singleton.Instance.UIManager != null)
            {
                Singleton.Instance.UIManager.PlaceAITileOnBoard(
                    letterInfo, new LetterPosition(tile.row, tile.col));
            }
        }
    }

    public IEnumerator ReplayOnlineRound(
    OnlineRoundHistoryEntry round)
    {
        if (round == null)
        {
            Debug.LogWarning("[REPLAY] Cannot replay null round.");
            yield break;
        }

        if (Singleton.Instance == null ||
            Singleton.Instance.UIManager == null)
        {
            Debug.LogWarning("[REPLAY] UIManager unavailable.");
            yield break;
        }

        SetInputLocked(true);

        UIManager ui = Singleton.Instance.UIManager;

        ui.ClearCommittedBoardTiles();

        BoardStateData preRoundBoard =
            JsonUtility.FromJson<BoardStateData>(
                round.preRoundBoardStateJson
            );

        ApplyBoardStateToScene(preRoundBoard);

        ApplyBonusBoardFromMatch(round.roundBonusBoardJson);

        yield return new WaitForSecondsRealtime(0.5f);

        SimTileListWrapper player1Move =
            JsonUtility.FromJson<SimTileListWrapper>(
                round.player1SimulatedTilesJson
            );

        SimTileListWrapper player2Move =
            JsonUtility.FromJson<SimTileListWrapper>(
                round.player2SimulatedTilesJson
            );

        Color player1Color = ReplayFirstPlayerColour;
        Color player2Color = ReplaySecondPlayerColour;
        Color winnerColor = ReplayWinnerColour;

        if (round.player1Valid &&
            player1Move != null &&
            player1Move.tiles != null &&
            player1Move.tiles.Count > 0)
        {
            yield return StartCoroutine(
                ui.PlayMovePreview(
                    player1Move.tiles,
                    player1Color,
                    1.5f
                )
            );
        }

        if (round.player2Valid &&
            player2Move != null &&
            player2Move.tiles != null &&
            player2Move.tiles.Count > 0)
        {
            yield return StartCoroutine(
                ui.PlayMovePreview(
                    player2Move.tiles,
                    player2Color,
                    1.5f
                )
            );
        }

        if (round.anyValidMove &&
            !string.IsNullOrEmpty(round.winnerUid))
        {
            string winnerTilesJson =
            round.winnerIsPlayer1
                ? round.player1SimulatedTilesJson
                : round.player2SimulatedTilesJson;

            SimTileListWrapper winnerMove =
                JsonUtility.FromJson<SimTileListWrapper>(
                    winnerTilesJson
                );

            if (winnerMove != null &&
                winnerMove.tiles != null &&
                winnerMove.tiles.Count > 0)
            {
                yield return StartCoroutine(
                    ui.PlayMovePreview(
                        winnerMove.tiles,
                        winnerColor,
                        2f
                    )
                );
            }
        }

        SetInputLocked(false);
    }
    public void LoadOnlineRoundReplay(
    int boardSizeX,
    int boardSizeY,
    int roundNumber,
    int totalRounds,
    int localScore,
    int opponentScore,
    string boardStateJson,
    string bonusBoardJson)
    {
        Debug.Log(
            "[REPLAY] Loading completed online round " +
            roundNumber
        );

        isOnlineMatch = false;
        localPlayerUid = null;
        currentMatchId = null;

        StopAllCoroutines();
        ClearBoardForNewGame();

        InitGame(
            0,
            boardSizeX,
            boardSizeY,
            GameInitMode.Online
        );

        if (!string.IsNullOrEmpty(boardStateJson))
        {
            BoardStateData savedBoard =
                JsonUtility.FromJson<BoardStateData>(
                    boardStateJson
                );

            if (savedBoard != null &&
                savedBoard.cells != null)
            {
                ApplyBoardStateToScene(savedBoard);
                LoadBoardStateIntoValidatedTiles(savedBoard);
            }
            else
            {
                Debug.LogWarning(
                    "[REPLAY] Could not parse replay board state."
                );
            }
        }

        //ApplyBonusBoardFromMatch(bonusBoardJson);
        ApplyBonusBoardFromMatch(bonusBoardJson,animateReveal: false);

        playerHandTiles = new List<LetterInfo>();

        humanTotalScore = localScore;
        aiTotalScore = opponentScore;

        currentRoundNumber = roundNumber;

        SetMaxRounds(totalRounds);

        roundStarted = false;
        currentState = TurnState.Busy;

        SetInputLocked(true);

        if (Singleton.Instance != null &&
            Singleton.Instance.UIManager != null)
        {
            Singleton.Instance.UIManager.RemoveAllHandTiles();

            Singleton.Instance.UIManager.UpdateRoundText(
                roundNumber,
                totalRounds
            );

            Singleton.Instance.UIManager.UpdateTotalScores(
                localScore,
                opponentScore
            );

            Singleton.Instance.UIManager.ShowRoundMessage(
                "Replay: Round " + roundNumber
            );
        }
    }
  
    public void ApplyReplayWinningTiles(
    string simulatedTilesJson)
    {
        if (string.IsNullOrEmpty(simulatedTilesJson))
        {
            Debug.LogWarning(
                "[REPLAY] Winning simulated tiles JSON is empty."
            );
            return;
        }

        SimTileListWrapper wrapper =
            JsonUtility.FromJson<SimTileListWrapper>(
                simulatedTilesJson
            );

        if (wrapper == null ||
            wrapper.tiles == null ||
            wrapper.tiles.Count == 0)
        {
            Debug.LogWarning(
                "[REPLAY] Winning simulated tiles JSON contained no tiles."
            );
            return;
        }

        Debug.Log(
            "[REPLAY] Applying " +
            wrapper.tiles.Count +
            " winning replay tiles."
        );

        foreach (SimPlacedTileData tile in wrapper.tiles)
        {
            if (tile == null)
            {
                Debug.LogWarning(
                    "[REPLAY] Skipping null replay tile."
                );
                continue;
            }

            int row = tile.row;
            int col = tile.col;

            if (row < 1 ||
                row > boardSizeX ||
                col < 1 ||
                col > boardSizeY)
            {
                Debug.LogWarning(
                    "[REPLAY] Tile out of board range: " +
                    tile.letter +
                    " at row=" + row +
                    ", col=" + col
                );
                continue;
            }

            LetterInfo letterInfo = new LetterInfo(
                tile.letter,
                tile.points
            );

            letterInfo.bonusUsed = true;

            LetterPosition position =
                new LetterPosition(row, col);

            validatedBoardTiles[row, col] = letterInfo;

            if (Singleton.Instance != null &&
                Singleton.Instance.UIManager != null)
            {
                Singleton.Instance.UIManager.PlaceAITileOnBoard(
                    letterInfo,
                    position
                );
            }
        }
    }

    public void RevealReplayBonusTiles()
    {
        if (bonusBoardView == null)
        {
            Debug.LogWarning(
                "[REPLAY] Cannot draw bonuses: BonusBoardView is null."
            );
            return;
        }

        bonusBoardView.DrawBonusTilesImmediately();
    }

    private RoundMove SelectMoveForDifficulty(List<RoundMove> candidates)
    {
        if (candidates == null || candidates.Count == 0)
            return null;

        // Put the strongest move back if the sample cap dropped it, so the band
        // is measured against the real best available and not the best sampled.
        if (bestAICandidate != null && !candidates.Contains(bestAICandidate))
            candidates.Add(bestAICandidate);

        // Sort best first using your existing comparison.
        candidates.Sort((a, b) =>
            IsBetterAIMove(a, b) ? -1 : (IsBetterAIMove(b, a) ? 1 : 0));

        int bestScore = candidates[0].score;

        GetDifficultyScoreBand(
            currentSoloDifficulty,
            out float minimumFraction,
            out float maximumFraction);

        float lowestWanted = bestScore * minimumFraction;
        float highestWanted = bestScore * maximumFraction;

        // Sorted best first, so the band is a contiguous run.
        int firstIndex = -1;
        int lastIndexInclusive = -1;

        for (int i = 0; i < candidates.Count; i++)
        {
            float score = candidates[i].score;

            if (score > highestWanted || score < lowestWanted)
                continue;

            if (firstIndex < 0)
                firstIndex = i;

            lastIndexInclusive = i;
        }

        // Nothing landed in the band - a short candidate list can skip straight
        // over it - so fall back to whichever move sits closest to its middle.
        if (firstIndex < 0)
        {
            float wanted = (lowestWanted + highestWanted) * 0.5f;
            float closestGap = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                float gap = Mathf.Abs(candidates[i].score - wanted);

                if (gap < closestGap)
                {
                    closestGap = gap;
                    firstIndex = i;
                    lastIndexInclusive = i;
                }
            }
        }

        int chosenIndex = UnityEngine.Random.Range(
            firstIndex,
            lastIndexInclusive + 1);

        RoundMove chosen = candidates[chosenIndex];

        Debug.Log(
            $"[AI] Difficulty={currentSoloDifficulty} | " +
            $"candidates={candidates.Count:N0} | " +
            $"bestWord={candidates[0]?.word ?? "NONE"} ({bestScore}) | " +
            $"band={minimumFraction:P0}-{maximumFraction:P0} " +
            $"({lowestWanted:F0}-{highestWanted:F0}) | " +
            $"chosenRank={chosenIndex + 1}/{candidates.Count} | " +
            $"chosenWord={chosen?.word ?? "NONE"} ({chosen?.score}) | " +
            $"strength={(bestScore > 0 ? (float)chosen.score / bestScore : 1f):P0}");

        return chosen;
    }
    private void ConsiderAIDifficultyCandidate(RoundMove candidate)
    {
        if (candidate == null || !candidate.isValid)
            return;

        // The capped list is a rough sample of everything playable, which is what
        // the lower difficulties need to find a weak-but-sane move in. The single
        // strongest move has to survive the cap separately, or on a busy board
        // Expert ends up picking the best of whatever the search happened to
        // reach first.
        if (bestAICandidate == null || IsBetterAIMove(candidate, bestAICandidate))
            bestAICandidate = candidate;

        if (aiDifficultyCandidates.Count >= MaxAIDifficultyCandidates)
            return;

        aiDifficultyCandidates.Add(candidate);
    }

    public void InitSoloGameForDifficulty(int handSize, int boardX, int boardY)
    {
        Debug.Log($"[INIT] InitSoloGameForDifficulty: hand={handSize} board={boardX}x{boardY}");

        InitGame(handSize, boardX, boardY, GameInitMode.Solo);
    }
}