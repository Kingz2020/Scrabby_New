using System.Collections.Generic;
using System.Threading.Tasks;

public interface IGameRepository
{
    Task<GameSession> CreateAsync(GameSession session);
    Task<GameSession> GetByIdAsync(string gameId);
    Task SaveAsync(GameSession session);
    Task AddRoundResultAsync(string gameId, RoundResult roundResult);
    Task<IReadOnlyList<GameSession>> GetOpenGamesAsync(int count);
    Task<IReadOnlyList<GameSession>> GetGamesForPlayerAsync(string playerId, int count);
}
