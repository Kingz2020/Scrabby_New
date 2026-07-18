public class HumanMoveAgent : IMoveAgent
{
    public string AgentId => "human";
    public bool IsHuman => true;

    public RoundMove GetMove(GameLogic gameLogic)
    {
        if (gameLogic == null)
            return null;

        return gameLogic.EvaluatePlayerSubmissionFromAgent();
    }
}