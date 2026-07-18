public class AIMoveAgent : IMoveAgent
{
    public string AgentId => "ai";
    public bool IsHuman => false;

    public RoundMove GetMove(GameLogic gameLogic)
    {
        if (gameLogic == null)
            return null;

        return gameLogic.GetLatestAIMove();
    }
}