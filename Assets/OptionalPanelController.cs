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
    [SerializeField] private Button playButton;
    [SerializeField] private TextMeshProUGUI playLabel;
    [SerializeField] private GameObject difficultyRow;
    [SerializeField] private GameObject multiplayerBlurb;

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

    private bool multiplayerChosen;
    private GameLogic.SoloDifficulty difficulty = GameLogic.SoloDifficulty.Medium;

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
    }

    // ---------------------------------------------------------------- tabs --
    public void ShowSoloTab()
    {
        multiplayerChosen = false;
        Refresh();
    }

    public void ShowMultiplayerTab()
    {
        multiplayerChosen = true;
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

        if (difficultyRow != null)
            difficultyRow.SetActive(!multiplayerChosen);

        if (multiplayerBlurb != null)
            multiplayerBlurb.SetActive(multiplayerChosen);

        if (playLabel != null)
            playLabel.text = multiplayerChosen ? "Find an opponent" : "Play";

        Paint(soloTabButton, !multiplayerChosen);
        Paint(multiplayerTabButton, multiplayerChosen);

        Paint(easyChip, !multiplayerChosen && difficulty == GameLogic.SoloDifficulty.Easy);
        Paint(mediumChip, !multiplayerChosen && difficulty == GameLogic.SoloDifficulty.Medium);
        Paint(hardChip, !multiplayerChosen && difficulty == GameLogic.SoloDifficulty.Hard);
        Paint(expertChip, !multiplayerChosen && difficulty == GameLogic.SoloDifficulty.Expert);
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
        if (multiplayerChosen)
        {
            OnMultiplayerPressed();
            return;
        }

        StartSolo(difficulty);
    }

    private void StartSolo(GameLogic.SoloDifficulty chosen)
    {
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
        Debug.Log("[OptionPanel] Multiplayer selected");

        if (preGamePanelController != null)
            preGamePanelController.EnterMultiplayerFlow();
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
