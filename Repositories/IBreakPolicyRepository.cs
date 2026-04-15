using FlowTracker.Domain;

namespace FlowTracker.Repositories;

public interface IBreakPolicyRepository
{
    Task<BreakPolicy?> GetDefaultPolicyAsync(CancellationToken cancellationToken = default);
}
