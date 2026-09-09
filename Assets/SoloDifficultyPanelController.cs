using UnityEngine;
using UnityEngine.UI;

public class SoloDifficultyPanelController : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject difficultyPanel;
    [SerializeField] private GameObject gameplayPanel;
    [SerializeField] private GameObject optionPanel;

    [Header("Difficulty Buttons")]
    [SerializeField] private Button easyButton;
    [SerializeField] private Button mediumButton;
    [SerializeField] private Button hardButton;
    [SerializeField] private Button expertButton;
    [SerializeField] private Button backButton;

    [Header("Existing Game References")]
    [SerializeField] private GameLogic gameLogic;

    private const string DifficultyPrefsKey = "Scrabby.SoloDifficulty";

    private void Awake()
    {
        easyButton.onClick.AddListener(OnEasyPressed);
        mediumButton.onClick.AddListener(OnMediumPressed);
        hardButton.onClick.AddListener(OnHardPressed);
        expertButton.onClick.AddListener(OnExpertPressed);
        backButton.onClick.AddListener(OnBackPressed);
    }

    private void OnDestroy()
    {
        easyButton.onClick.RemoveListener(OnEasyPressed);
        mediumButton.onClick.RemoveListener(OnMediumPressed);
        hardButton.onClick.RemoveListener(OnHardPressed);
        expertButton.onClick.RemoveListener(OnExpertPressed);
        backButton.onClick.RemoveListener(OnBackPressed);
    }

    public void Open()
    {
        //SetDifficultyButtonsInteractable(true);

        if (difficultyPanel != null)
            difficultyPanel.SetActive(true);
    }

    public void OnEasyPressed()
    {
        StartSoloGame(GameLogic.SoloDifficulty.Easy);
    }

    public void OnMediumPressed()
    {
        StartSoloGame(GameLogic.SoloDifficulty.Medium);
    }

    public void OnHardPressed()
    {
        StartSoloGame(GameLogic.SoloDifficulty.Hard);
    }

    public void OnExpertPressed()
    {
        StartSoloGame(GameLogic.SoloDifficulty.Expert);
    }

    public void OnBackPressed()
    {
 

        if (difficultyPanel != null)
            difficultyPanel.SetActive(false);

        if (optionPanel != null)
            optionPanel.SetActive(true);
    }

    private void StartSoloGame(GameLogic.SoloDifficulty difficulty)
    {
        Debug.Log($"[UI] Starting solo game, difficulty={difficulty}");

        if (gameLogic == null)
        {
            Debug.LogError("[UI] gameLogic is not assigned.");
            return;
        }

        if (gameplayPanel == null)
        {
            Debug.LogError("[UI] gameplayPanel is not assigned.");
            return;
        }

        if (difficultyPanel != null)
            difficultyPanel.SetActive(false);

        gameplayPanel.SetActive(true);
        Debug.Log("[UI] gameplayPanel activated");

        gameLogic.SetSoloDifficulty(difficulty);

        // Initialize the game (like old StartNewGame), but do NOT start the round yet.
        gameLogic.InitSoloGameForDifficulty(7, 15, 15);

        Debug.Log($"[UI] Difficulty set to {difficulty}. Waiting for Play button.");
    }


    private string GetPercentileDescription(GameLogic.SoloDifficulty difficulty)
    {
        switch (difficulty)
        {
            case GameLogic.SoloDifficulty.Easy:
                return "50%-75%";

            case GameLogic.SoloDifficulty.Medium:
                return "20%-45%";

            case GameLogic.SoloDifficulty.Hard:
                return "5%-20%";

            case GameLogic.SoloDifficulty.Expert:
                return "0%-5%";

            default:
                return "unknown";
        }
    }
}