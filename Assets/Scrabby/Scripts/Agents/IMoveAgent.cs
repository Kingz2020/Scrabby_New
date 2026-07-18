public interface IMoveAgent
{
    string AgentId { get; }
    bool IsHuman { get; }
    RoundMove GetMove(GameLogic gameLogic);
}