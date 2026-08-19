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

    private void Awake()
    {
        if (soloButton != null)
            soloButton.onClick.AddListener(OnSoloPressed);

        if (multiplayerButton != null)
            multiplayerButton.onClick.AddListener(OnMultiplayerPressed);
    }

    private void Start()
    {
        ShowOptionPanel();
    }

    public void OnSoloPressed()
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

        Debug.Log("[OptionPanel] Solo selected -> starting new solo game");
    }

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