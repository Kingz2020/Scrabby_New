using UnityEngine;

public class NewGameButton : MonoBehaviour
{
    [SerializeField] private GameLogic gameLogic;

    public void OnClickNewGame()
    {
        if (gameLogic == null)
        {
            gameLogic = FindAnyObjectByType<GameLogic>();
        }

        if (gameLogic == null)
        {
            Debug.LogError("NewGameButton: GameLogic reference is missing.");
            return;
        }

        gameLogic.BeginGameFromButton();
    }
}