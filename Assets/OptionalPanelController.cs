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

        // First time only. Someone who already knows the rules should not have
        // to dismiss them every launch.
        HowToPlayPanel.ShowIfNotSeen();
    }

    // The way back into the rules, placed under the play button rather than
    // given a position of its own - the panel has been rearranged more than
    // once, and anything with fixed coordinates ends up on top of something.
    private void AddHowToPlayLink()
    {
        if (playButton == null)
            return;

        RectTransform anchor = playButton.transform as RectTransform;

        if (anchor == null || anchor.parent == null)
            return;

        GameObject go = new GameObject("HowToPlayLink",
            typeof(RectTransform), typeof(TextMeshProUGUI), typeof(Button));

        go.transform.SetParent(anchor.parent, false);

        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.text = "How to play";
        label.fontSize = 30f;
        label.color = new Color(0.945f, 0.878f, 0.733f, 0.95f);
        label.fontStyle = FontStyles.Underline;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = true;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchor.anchorMin;
        rect.anchorMax = anchor.anchorMax;
        rect.pivot = anchor.pivot;
        rect.sizeDelta = new Vector2(anchor.sizeDelta.x, 52f);

        // Just below the play button, measured off it rather than guessed.
        rect.anchoredPosition = anchor.anchoredPosition +
            new Vector2(0f, -(anchor.sizeDelta.y * 0.5f + 42f));

        Button button = go.GetComponent<Button>();
        button.targetGraphic = label;
        button.onClick.AddListener(HowToPlayPanel.Show);
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
                playLabel.text = "Play today's puzzle";
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

        if (optionPanel != null) optionPanel.SetActive(false);
        if (pregamePanel != null) pregamePanel.SetActive(false);
        if (gameoverPanel != null) gameoverPanel.SetActive(false);
        if (matchstatusPanel != null) matchstatusPanel.SetActive(false);
        if (difficultyPanel != null) difficultyPanel.SetActive(false);
        if (gameplayPanel != null) gameplayPanel.SetActive(true);

        Singleton.Instance.GameLogic.StartDaily(day);

        Debug.Log("[OptionPanel] Daily started: " + day);
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

        if (manager.IsReady)
        {
            dailyStatusLabel.text = "Puzzle #" + manager.Today.dayNumber +
                                    " - one word, one go.";
        }
        else
        {
            dailyStatusLabel.text = "Building today's puzzle...";
        }
    }

    private void StartSolo(GameLogic.SoloDifficulty chosen)
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

        Singleton.Instance.DebugManager.StartNewGame(chosen);

        Debug.Log("[OptionPanel] Solo started, difficulty=" + chosen);
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

        Debug.Log("[OptionPanel] Opening difficulty panel for Solo.");
    }

    public void OnMultiplayerPressed()
    {
        LeaveDaily();

        Debug.Log("[OptionPanel] Multiplayer selected");

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

        Debug.Log("[OptionPanel] Returned to the main menu.");
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

        Debug.Log("[OptionPanel] Showing option panel");
    }
}
