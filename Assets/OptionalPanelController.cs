using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Solo, multiplayer and - for solo - the difficulty, all decided on one card.
//
// Difficulty used to be a screen of its own: four buttons and a Back, shown
// after Solo was pressed, to answer one question that only matters for solo.
// Folding it in removes a tap, a screen and the Back button, and lets a player
// see what they are about to start before they start it.
//
// Everything new is optional. With the tabs and chips unassigned this behaves
// exactly as it used to, so the old difficulty panel keeps working until the
// new card replaces it.
public class OptionPanelController : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button soloButton;
    [SerializeField] private Button multiplayerButton;

    [Header("Panels")]
    [SerializeField] private GameObject optionPanel;
    [SerializeField] private GameObject pregamePanel;
    [SerializeField] private GameObject gameplayPanel;
    [SerializeField] private GameObject gameoverPanel;
    [SerializeField] private PreGamePanel preGamePanelController;
    [SerializeField] private GameObject matchstatusPanel;
    [SerializeField] private GameObject difficultyPanel;

    [Header("One-card flow")]
    [SerializeField] private Button soloTabButton;
    [SerializeField] private Button multiplayerTabButton;
    [SerializeField] private Button dailyTabButton;
    [SerializeField] private Button playButton;
    [SerializeField] private TextMeshProUGUI playLabel;
    [SerializeField] private GameObject difficultyRow;
    [SerializeField] private GameObject multiplayerBlurb;
    [SerializeField] private GameObject dailyBlurb;
    [SerializeField] private TextMeshProUGUI dailyStatusLabel;

    [Header("Difficulty chips")]
    [SerializeField] private Button easyChip;
    [SerializeField] private Button mediumChip;
    [SerializeField] private Button hardChip;
    [SerializeField] private Button expertChip;

    [Header("Chip and tab look")]
    [SerializeField] private Sprite selectedSprite;
    [SerializeField] private Sprite idleSprite;
    [SerializeField] private Color selectedColour = new Color(0.945f, 0.878f, 0.733f, 1f);
    [SerializeField] private Color idleColour = new Color(0.039f, 0.149f, 0.267f, 0.30f);
    [SerializeField] private Color selectedTextColour = new Color(0.227f, 0.173f, 0.094f, 1f);
    [SerializeField] private Color idleTextColour = new Color(1f, 1f, 1f, 0.85f);

    private const string DifficultyPrefsKey = "Scrabby.SoloDifficulty";

    // The tabs' navy, opaque enough to read against the sky.
    private static readonly Color SettingsNavy = new Color(0.039f, 0.149f, 0.267f, 0.88f);

    // Three choices now, so what used to be "is multiplayer chosen" cannot
    // answer the question any more.
    private enum Mode { Solo, Multiplayer, Daily }

    private Mode mode = Mode.Solo;
    private GameLogic.SoloDifficulty difficulty = GameLogic.SoloDifficulty.Medium;

    private bool multiplayerChosen { get { return mode == Mode.Multiplayer; } }
    private bool dailyChosen { get { return mode == Mode.Daily; } }

    // True once the card has the controls it needs to run the folded-in flow.
    private bool OneCard
    {
        get { return soloTabButton != null || multiplayerTabButton != null; }
    }

    private void Awake()
    {
        if (OneCard)
        {
            Wire(soloTabButton, ShowSoloTab);
            Wire(multiplayerTabButton, ShowMultiplayerTab);
            Wire(dailyTabButton, ShowDailyTab);
            Wire(playButton, OnPlayPressed);

            Wire(easyChip, delegate { ChooseDifficulty(GameLogic.SoloDifficulty.Easy); });
            Wire(mediumChip, delegate { ChooseDifficulty(GameLogic.SoloDifficulty.Medium); });
            Wire(hardChip, delegate { ChooseDifficulty(GameLogic.SoloDifficulty.Hard); });
            Wire(expertChip, delegate { ChooseDifficulty(GameLogic.SoloDifficulty.Expert); });

            difficulty = (GameLogic.SoloDifficulty)PlayerPrefs.GetInt(
                DifficultyPrefsKey, (int)GameLogic.SoloDifficulty.Medium);
        }
        else
        {
            // The old two-screen flow.
            Wire(soloButton, OnSoloPressed);
            Wire(multiplayerButton, OnMultiplayerPressed);
        }

        if (optionPanel != null) optionPanel.SetActive(true);
        if (pregamePanel != null) pregamePanel.SetActive(false);
        if (gameplayPanel != null) gameplayPanel.SetActive(false);
        if (gameoverPanel != null) gameoverPanel.SetActive(false);
        if (matchstatusPanel != null) matchstatusPanel.SetActive(false);
        if (difficultyPanel != null) difficultyPanel.SetActive(false);
    }

    private static void Wire(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
            button.onClick.AddListener(action);
    }

    private void Start()
    {
        ShowOptionPanel();
        Refresh();

        AddHowToPlayLink();
        AddStatsLink();
        AddSettingsButton();
        AddGameplayMainMenuButton();
        DressPlayScreenButtons();

        // First time only, and led rather than read: a card of rules is only
        // any use to the people who read it, and watching people play said
        // that is nobody. The card is still behind "How to play".
        StartCoroutine(OpenTheTutorialOnceTheMenuIsUp());
    }

    // A frame or two, so the menu has laid itself out before the hand starts
    // pointing at where its buttons are.
    private IEnumerator OpenTheTutorialOnceTheMenuIsUp()
    {
        yield return null;
        yield return null;

        // Dressed here too, not in Start: the tabs and chips are sized by
        // their layout, and before it has run they have no height for the
        // art to be scaled to.
        Canvas.ForceUpdateCanvases();
        DressMenuButtons();

        TutorialFlow.RunIfNew();
    }

    // The way out of a game. A solo game had none: the only buttons on the
    // board screen played the round, and the one in the corner started a new
    // game, which is not what someone wants who has finished playing for now.
    //
    // An online match is safe to walk away from - it lives on the server and
    // is waiting in the matches list - and a solo game is not saved either
    // way, so this asks nothing before leaving.
    private void AddGameplayMainMenuButton()
    {
        if (gameplayPanel == null)
            return;

        if (gameplayPanel.transform.Find("MainMenuButton_Gameplay") != null)
            return;

        GameObject go = new GameObject("MainMenuButton_Gameplay",
            typeof(RectTransform), typeof(Image), typeof(Button));

        go.transform.SetParent(gameplayPanel.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(300f, 84f);

        // Beside the back-to-matches button rather than under it: in an online
        // match both are on screen at once.
        rect.anchoredPosition = new Vector2(-330f, -1020f);

        Image face = go.GetComponent<Image>();
        face.color = new Color(0.941f, 0.698f, 0.235f, 1f);

        GameObject labelGo = new GameObject("Label",
            typeof(RectTransform), typeof(TextMeshProUGUI));

        labelGo.transform.SetParent(go.transform, false);

        TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = "Main Menu";
        label.fontSize = 32f;
        label.color = new Color(0.227f, 0.173f, 0.094f, 1f);
        label.alignment = TextAlignmentOptions.Center;

        RectTransform labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = face;
        button.onClick.AddListener(LeaveGameForMainMenu);
    }

    // The play screen's buttons, in the thick style: white for the things that
    // play the game, blue for the ways out. Found by name rather than wired in
    // the scene, which can only be edited with Unity closed - and dressed in
    // place, so each still does exactly what it did before.
    //
    // The ▶ becomes the word PLAY: a triangle on a button is a media control,
    // and this one submits a word.
    private void DressPlayScreenButtons()
    {
        if (gameplayPanel == null)
            return;

        Sprite back = Resources.Load<Sprite>("Buttons/Icon_return");
        Sprite shuffle = Resources.Load<Sprite>("Buttons/Icon_shuffle");

        foreach (Button b in gameplayPanel.GetComponentsInChildren<Button>(true))
        {
            switch (b.name)
            {
                case "Check Word":
                    ChunkyButton.Dress(b, ChunkyButton.Face.White, "Play", null);
                    break;

                case "Return Letter Button":
                    ChunkyButton.Dress(b, ChunkyButton.Face.White, null, back);
                    break;

                case "ShuffleButton":
                    ChunkyButton.Dress(b, ChunkyButton.Face.White, null, shuffle);
                    break;

                case "BacktoMatchButton":
                    ChunkyButton.Dress(b, ChunkyButton.Face.Blue, "Back to match", null);
                    break;

                case "MainMenuButton_Gameplay":
                    ChunkyButton.Dress(b, ChunkyButton.Face.Blue, "Main menu", null);
                    break;
            }
        }
    }

    // The menu keeps its own look - the tile for what is chosen, glass for
    // the rest - and gets the board's thickness, light and press on top of
    // it. The white and blue keys are the board's; on the menu's art they
    // looked pasted on.
    private void DressMenuButtons()
    {
        Button[] buttons =
        {
            playButton,
            soloTabButton, multiplayerTabButton, dailyTabButton,
            easyChip, mediumChip, hardChip, expertChip
        };

        foreach (Button b in buttons)
            ChunkyButton.Deepen(b);

        if (optionPanel != null)
        {
            Transform settings = optionPanel.transform.Find("SettingsButton");

            if (settings != null)
                ChunkyButton.Deepen(settings.GetComponent<Button>());
        }
    }

    // Every way into a game passes through here first, so none of them can
    // inherit the last one's coroutines, board or result panel.
    private void AbandonWhateverWasPlaying()
    {
        if (Singleton.Instance != null && Singleton.Instance.GameLogic != null)
            Singleton.Instance.GameLogic.AbandonGameInProgress();
    }

    private void LeaveGameForMainMenu()
    {
        // Stop listening to a match we are walking away from, so its updates
        // do not pull us back into the board.
        if (Singleton.Instance != null && Singleton.Instance.OnlineMatchController != null)
        {
            Singleton.Instance.OnlineMatchController.StopWatchingCurrentMatch();
            Singleton.Instance.OnlineMatchController.LeftMatchViews();
        }

        // And stop the game itself, so a reveal or replay still in flight
        // cannot show its result over whatever comes next.
        if (Singleton.Instance != null && Singleton.Instance.GameLogic != null)
            Singleton.Instance.GameLogic.AbandonGameInProgress();

        ReturnToMainMenu();
    }

    // The way back into the rules, placed under the play button rather than
    // given a position of its own - the panel has been rearranged more than
    // once, and anything with fixed coordinates ends up on top of something.
    private void AddHowToPlayLink()
    {
        AddLinkUnderPlay("HowToPlayLink", "How to play", 0, HowToPlayPanel.Show);
    }

    // The other thing a player comes to the menu for that is not a game: how
    // they have been doing. Under the rules, in the same hand, because the two
    // belong together - one is what to do, the other is how it has gone.
    private void AddStatsLink()
    {
        AddLinkUnderPlay("StatsLink", "Your progress", 1, StatsPanel.Show);
    }

    // What the tutorial points at.
    //
    // Rectangles rather than buttons: the hand only needs somewhere to go,
    // and the tutorial calls ShowSoloTab, ChooseDifficulty and OnPlayPressed
    // itself, so a menu step cannot be half-done if the tap misses.
    public RectTransform SoloTabRect { get { return RectOf(soloTabButton); } }
    public RectTransform PlayRect { get { return RectOf(playButton); } }
    public RectTransform EasyChipRect { get { return RectOf(easyChip); } }
    public RectTransform MediumChipRect { get { return RectOf(mediumChip); } }
    public RectTransform HardChipRect { get { return RectOf(hardChip); } }
    public RectTransform ExpertChipRect { get { return RectOf(expertChip); } }

    public RectTransform DifficultyRowRect
    {
        get
        {
            return difficultyRow != null
                ? difficultyRow.transform as RectTransform
                : null;
        }
    }

    private static RectTransform RectOf(Button button)
    {
        return button != null ? button.transform as RectTransform : null;
    }

    // Somewhere to turn the sound off. A game played on a bus needs this, and
    // a player who cannot find it turns the whole phone down instead.
    //
    // Top right of the screen rather than a third link under Play: the card
    // ends a little below the two links already there, so a third would hang
    // off the bottom of it - and this is where a settings button lives on
    // every other app they own.
    private void AddSettingsButton()
    {
        if (optionPanel == null)
            return;

        if (optionPanel.transform.Find("SettingsButton") != null)
            return;

        GameObject go = new GameObject("SettingsButton",
            typeof(RectTransform), typeof(Image), typeof(Button));

        go.transform.SetParent(optionPanel.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(172f, 68f);
        rect.anchoredPosition = new Vector2(-38f, -46f);

        // The menu's own dark navy, as on the tabs, but solid: it sits on
        // open sky rather than on the card, and see-through glass there made
        // it all but invisible. The tabs' rounded sprite, where there is one.
        Image face = go.GetComponent<Image>();
        face.color = SettingsNavy;

        if (idleSprite != null)
        {
            face.sprite = idleSprite;
            face.type = Image.Type.Sliced;
        }

        GameObject labelGo = new GameObject("Label",
            typeof(RectTransform), typeof(TextMeshProUGUI));

        labelGo.transform.SetParent(go.transform, false);

        TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = "Settings";
        label.fontSize = 28f;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;

        RectTransform labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = face;
        button.onClick.AddListener(SettingsPanel.Show);
    }

    private void AddLinkUnderPlay(
        string name, string text, int slot, UnityEngine.Events.UnityAction onPressed)
    {
        if (playButton == null)
            return;

        RectTransform anchor = playButton.transform as RectTransform;

        if (anchor == null || anchor.parent == null)
            return;

        if (anchor.parent.Find(name) != null)
            return;

        GameObject go = new GameObject(name,
            typeof(RectTransform), typeof(TextMeshProUGUI), typeof(Button));

        go.transform.SetParent(anchor.parent, false);

        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 32f;

        // White with a thin navy edge. Cream on the card's glass, over a
        // pale sky, was barely there; the edge keeps it sharp over a cloud.
        label.color = Color.white;
        label.outlineColor = SettingsNavy;
        label.outlineWidth = 0.18f;
        label.fontStyle = FontStyles.Underline | FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = true;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchor.anchorMin;
        rect.anchorMax = anchor.anchorMax;
        rect.pivot = anchor.pivot;
        rect.sizeDelta = new Vector2(anchor.sizeDelta.x, 52f);

        // Just below the play button, measured off it rather than guessed,
        // and each further link a line below the one before.
        rect.anchoredPosition = anchor.anchoredPosition +
            new Vector2(0f, -(anchor.sizeDelta.y * 0.5f + 42f + slot * 54f));

        Button button = go.GetComponent<Button>();
        button.targetGraphic = label;
        button.onClick.AddListener(onPressed);
    }

    // ---------------------------------------------------------------- tabs --
    public void ShowSoloTab()
    {
        mode = Mode.Solo;
        Refresh();
    }

    public void ShowMultiplayerTab()
    {
        mode = Mode.Multiplayer;
        Refresh();
    }

    public void ShowDailyTab()
    {
        mode = Mode.Daily;

        // Asking for it here rather than on Play: if it is not ready yet, the
        // few seconds it needs are spent while the player reads the tab
        // instead of after they have committed to starting.
        if (DailyManager.Instance != null)
            DailyManager.Instance.Prepare();

        Refresh();
    }

    public void ChooseDifficulty(GameLogic.SoloDifficulty chosen)
    {
        difficulty = chosen;
        PlayerPrefs.SetInt(DifficultyPrefsKey, (int)chosen);
        Refresh();
    }

    // Difficulty only means something for solo, so it is only on screen there.
    private void Refresh()
    {
        if (!OneCard)
            return;

        bool solo = mode == Mode.Solo;

        // Difficulty is a solo idea: the daily sets its own level from how well
        // you do, and multiplayer has an opponent instead.
        if (difficultyRow != null)
            difficultyRow.SetActive(solo);

        if (multiplayerBlurb != null)
            multiplayerBlurb.SetActive(multiplayerChosen);

        if (dailyBlurb != null)
            dailyBlurb.SetActive(dailyChosen);

        if (playLabel != null)
        {
            if (dailyChosen)
                playLabel.text = PlayedToday() ? "See today's result"
                                               : "Play today's puzzle";
            else if (multiplayerChosen)
                playLabel.text = "Find an opponent";
            else
                playLabel.text = "Play";
        }

        RefreshDailyStatus();

        Paint(soloTabButton, solo);
        Paint(multiplayerTabButton, multiplayerChosen);
        Paint(dailyTabButton, dailyChosen);

        Paint(easyChip, solo && difficulty == GameLogic.SoloDifficulty.Easy);
        Paint(mediumChip, solo && difficulty == GameLogic.SoloDifficulty.Medium);
        Paint(hardChip, solo && difficulty == GameLogic.SoloDifficulty.Hard);
        Paint(expertChip, solo && difficulty == GameLogic.SoloDifficulty.Expert);
    }

    // Selected is a letter tile, unselected is glass - the same pairing the
    // pregame card's tabs use.
    private void Paint(Button button, bool selected)
    {
        if (button == null)
            return;

        if (button.image != null)
        {
            if (selectedSprite != null && idleSprite != null)
                button.image.sprite = selected ? selectedSprite : idleSprite;

            // The shape stays the same and the colour carries the state. A tab
            // is stretched far wider than a letter tile ever is, and a tile
            // pulled to that width stops reading as a tile - it just reads as
            // a stretched picture of one.
            //
            // Sliced, so the rounded corners keep their size at any width
            // instead of being pulled out with the rest of the image.
            button.image.type = Image.Type.Sliced;

            button.image.color = selected ? selectedColour : idleColour;
        }

        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);

        if (label != null)
            label.color = selected ? selectedTextColour : idleTextColour;
    }

    // --------------------------------------------------------------- start --
    public void OnPlayPressed()
    {
        if (dailyChosen)
        {
            OnDailyPressed();
            return;
        }

        if (multiplayerChosen)
        {
            OnMultiplayerPressed();
            return;
        }

        StartSolo(difficulty);
    }

    // ---------------------------------------------------------------- daily --
    public void OnDailyPressed()
    {
        if (DailyManager.Instance == null)
        {
            Debug.LogError("[OptionPanel] No DailyManager in the scene.");
            return;
        }

        StartCoroutine(StartDailyWhenReady());
    }

    private IEnumerator StartDailyWhenReady()
    {
        AbandonWhateverWasPlaying();

        DailyManager manager = DailyManager.Instance;

        manager.Prepare();

        // Usually already done - it starts at launch and the tab asks again -
        // but a player quick enough to get here first waits rather than being
        // handed nothing.
        while (!manager.IsReady && manager.IsGenerating)
        {
            RefreshDailyStatus();
            yield return null;
        }

        DailyBoard day = manager.Today;

        if (day == null)
        {
            Debug.LogError("[OptionPanel] Today's puzzle could not be built.");

            if (dailyStatusLabel != null)
                dailyStatusLabel.text = "Today's puzzle could not be built.";

            yield break;
        }

        // Already answered: the result goes over the menu rather than the
        // board being laid out for a puzzle that cannot be played again.
        DailyProgress.DailyRecord answered;

        if (DailyProgress.AnsweredToday(out answered))
        {
            DailyResultPanel.Show(day, answered.score, answered.word);
            yield break;
        }

        if (optionPanel != null) optionPanel.SetActive(false);
        if (pregamePanel != null) pregamePanel.SetActive(false);
        if (gameoverPanel != null) gameoverPanel.SetActive(false);
        if (matchstatusPanel != null) matchstatusPanel.SetActive(false);
        if (difficultyPanel != null) difficultyPanel.SetActive(false);
        if (gameplayPanel != null) gameplayPanel.SetActive(true);

        Singleton.Instance.GameLogic.StartDaily(day);

        ScrabbyLog.Trace("[OptionPanel] Daily started: " + day);
    }

    // The board has to be on screen for the best word to be shown on it, even
    // though the day itself is over.
    public void ShowGameplayForDailyReveal()
    {
        if (optionPanel != null) optionPanel.SetActive(false);
        if (pregamePanel != null) pregamePanel.SetActive(false);
        if (gameoverPanel != null) gameoverPanel.SetActive(false);
        if (matchstatusPanel != null) matchstatusPanel.SetActive(false);
        if (difficultyPanel != null) difficultyPanel.SetActive(false);
        if (gameplayPanel != null) gameplayPanel.SetActive(true);
    }

    private static bool PlayedToday()
    {
        DailyProgress.DailyRecord record;
        return DailyProgress.AnsweredToday(out record);
    }

    private void RefreshDailyStatus()
    {
        if (dailyStatusLabel == null)
            return;

        if (!dailyChosen)
            return;

        DailyManager manager = DailyManager.Instance;

        if (manager == null)
        {
            dailyStatusLabel.text = "Daily puzzle unavailable.";
            return;
        }

        if (PlayedToday())
        {
            DailyProgress.DailyRecord record = DailyProgress.Load();

            dailyStatusLabel.text = "Played today: " + record.word.ToUpper() +
                                    " for " + record.score + ".";
        }
        else if (manager.IsReady)
        {
            dailyStatusLabel.text = "Puzzle #" + manager.Today.dayNumber +
                                    " - one word, one go.";
        }
        else
        {
            dailyStatusLabel.text = "Building today's puzzle...";
        }
    }

    // Public, because the daily's result card also offers a solo game, and
    // starting one from there used to skip all of this: the panels stayed as
    // the daily left them, nothing stopped what was still running, and the
    // game began underneath whatever was on screen.
    public void StartSolo(GameLogic.SoloDifficulty chosen)
    {
        LeaveDaily();

        if (optionPanel != null) optionPanel.SetActive(false);
        if (pregamePanel != null) pregamePanel.SetActive(false);
        if (gameoverPanel != null) gameoverPanel.SetActive(false);
        if (matchstatusPanel != null) matchstatusPanel.SetActive(false);
        if (difficultyPanel != null) difficultyPanel.SetActive(false);

        if (Singleton.Instance == null || Singleton.Instance.DebugManager == null)
        {
            Debug.LogError("[OptionPanel] DebugManager unavailable; cannot start solo.");
            return;
        }

        // The letter bag has to be loaded before a new game is dealt.
        Singleton.Instance.DebugManager.LoadFromJson();

        if (gameplayPanel != null) gameplayPanel.SetActive(true);

        // A clean slate before anything is dealt: whatever the last game left
        // behind goes now - its board, and anything it was still doing, such
        // as a replay that would otherwise put the game-over panel back up
        // over this game.
        AbandonWhateverWasPlaying();

        Singleton.Instance.DebugManager.StartNewGame(chosen);

        ScrabbyLog.Trace("[OptionPanel] Solo started, difficulty=" + chosen);
    }

    // Background generation borrows the real board and bag, so anything that
    // starts a game on them has to call this first.
    private void LeaveDaily()
    {
        if (DailyManager.Instance != null)
            DailyManager.Instance.Abort();

        if (Singleton.Instance != null && Singleton.Instance.GameLogic != null)
            Singleton.Instance.GameLogic.LeaveDailyMode();
    }

    // ----------------------------------------------- the old two-screen flow --
    public void OnSoloPressed()
    {
        if (optionPanel != null) optionPanel.SetActive(false);
        if (pregamePanel != null) pregamePanel.SetActive(false);
        if (gameplayPanel != null) gameplayPanel.SetActive(false);
        if (gameoverPanel != null) gameoverPanel.SetActive(false);
        if (matchstatusPanel != null) matchstatusPanel.SetActive(false);

        if (Singleton.Instance == null || Singleton.Instance.DebugManager == null)
            return;

        Singleton.Instance.DebugManager.LoadFromJson();

        if (difficultyPanel == null)
        {
            Debug.LogWarning(
                "[OptionPanel] difficultyPanel is not assigned. Cannot open Solo difficulty panel.");
            return;
        }

        difficultyPanel.SetActive(true);

        ScrabbyLog.Trace("[OptionPanel] Opening difficulty panel for Solo.");
    }

    public void OnMultiplayerPressed()
    {
        LeaveDaily();
        AbandonWhateverWasPlaying();

        ScrabbyLog.Trace("[OptionPanel] Multiplayer selected");

        if (preGamePanelController != null)
            preGamePanelController.EnterMultiplayerFlow();
    }

    // The game over panel's Main Menu button has been wired to this name in the
    // scene all along, but the method was never written - so pressing it did
    // nothing at all, silently, because an unresolved persistent call is not an
    // error Unity reports.
    //
    // Leaving a finished game means leaving whatever it was: a daily has to be
    // stood down too, or its board and its locked buttons follow you out.
    public void ReturnToMainMenu()
    {
        if (Singleton.Instance != null && Singleton.Instance.GameLogic != null)
            Singleton.Instance.GameLogic.LeaveDailyMode();

        ShowOptionPanel();

        ScrabbyLog.Trace("[OptionPanel] Returned to the main menu.");
    }

    public void ShowOptionPanel()
    {
        if (optionPanel != null) optionPanel.SetActive(true);
        if (pregamePanel != null) pregamePanel.SetActive(false);
        if (gameplayPanel != null) gameplayPanel.SetActive(false);
        if (gameoverPanel != null) gameoverPanel.SetActive(false);
        if (matchstatusPanel != null) matchstatusPanel.SetActive(false);
        if (difficultyPanel != null) difficultyPanel.SetActive(false);

        Refresh();

        ScrabbyLog.Trace("[OptionPanel] Showing option panel");
    }
}
