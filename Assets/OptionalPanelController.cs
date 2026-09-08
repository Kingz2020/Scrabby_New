using UnityEngine;
using UnityEngine.UI;

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

    private void Awake()
    {
        // Wire up buttons.
        if (soloButton != null)
            soloButton.onClick.AddListener(OnSoloPressed);

        if (multiplayerButton != null)
            multiplayerButton.onClick.AddListener(OnMultiplayerPressed);

        // Ensure correct initial panel state.
        if (optionPanel != null) optionPanel.SetActive(true);
        if (pregamePanel != null) pregamePanel.SetActive(false);
        if (gameplayPanel != null) gameplayPanel.SetActive(false);
        if (gameoverPanel != null) gameoverPanel.SetActive(false);
        if (matchstatusPanel != null) matchstatusPanel.SetActive(false);
        if (difficultyPanel != null) difficultyPanel.SetActive(false);
    }

    private void Start()
    {
        ShowOptionPanel();
    }
    public void OnSoloPressed()
    {
        // Hide all panels except difficulty.
        if (optionPanel != null) optionPanel.SetActive(false);
        if (pregamePanel != null) pregamePanel.SetActive(false);
        if (gameplayPanel != null) gameplayPanel.SetActive(false);
        if (gameoverPanel != null) gameoverPanel.SetActive(false);
        if (matchstatusPanel != null) matchstatusPanel.SetActive(false);

        if (difficultyPanel == null)
        {
            Debug.LogWarning(
                "[OptionPanel] difficultyPanel is not assigned. Cannot open Solo difficulty panel.");
            return;
        }

        difficultyPanel.SetActive(true);

        Debug.Log("[OptionPanel] Opening difficulty panel for Solo.");
    }
    /* public void OnSoloPressed()
     {
         if (optionPanel != null) optionPanel.SetActive(false);
         if (pregamePanel != null) pregamePanel.SetActive(false);
         if (gameplayPanel != null) gameplayPanel.SetActive(true);
         if (gameoverPanel != null) gameoverPanel.SetActive(false);
         if (matchstatusPanel != null) matchstatusPanel.SetActive(false);

         if (Singleton.Instance != null &&
             Singleton.Instance.DebugManager != null)
         {
             Singleton.Instance.DebugManager.LoadFromJson();
             Singleton.Instance.DebugManager.StartNewGame();
         }

         Debug.Log("[OptionPanel] Opening difficulty panel for Solo.");
     }
    */
    public void OnMultiplayerPressed()
    {
        Debug.Log("[OptionPanel] Multiplayer selected");

        if (preGamePanelController != null)
        {
            preGamePanelController.EnterMultiplayerFlow();
        }
    }
 
    public void ShowOptionPanel()
    {
        if (optionPanel != null) optionPanel.SetActive(true);
        if (pregamePanel != null) pregamePanel.SetActive(false);
        if (gameplayPanel != null) gameplayPanel.SetActive(false);
        if (gameoverPanel != null) gameoverPanel.SetActive(false);
        if (matchstatusPanel != null) matchstatusPanel.SetActive(false);

        Debug.Log("[OptionPanel] Showing option panel");
    }
}