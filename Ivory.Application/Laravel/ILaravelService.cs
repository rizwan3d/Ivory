using System.Threading;
using System.Threading.Tasks;

namespace Ivory.Application.Laravel;

public interface ILaravelService
{
    Task<int> RunLaravelAsync(string[] args, string phpVersionSpec, CancellationToken cancellationToken = default);
}
