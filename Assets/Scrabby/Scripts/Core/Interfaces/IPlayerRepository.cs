using System.Collections.Generic;
using System.Threading.Tasks;

public interface IPlayerRepository
{
    Task<PlayerProfile> GetByIdAsync(string playerId);
    Task SaveAsync(PlayerProfile playerProfile);
    Task<bool> ExistsAsync(string playerId);
    Task<IReadOnlyList<PlayerProfile>> GetTopRatedAsync(int count);
}
