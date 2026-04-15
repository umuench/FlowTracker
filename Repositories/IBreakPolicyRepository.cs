using FlowTracker.Domain;

namespace FlowTracker.Repositories;

/// <summary>
/// Definiert den Lesezugriff auf Pausenrichtlinien.
/// </summary>
public interface IBreakPolicyRepository
{
    /// <summary>
    /// Liefert die Standard-Pausenrichtlinie oder <c>null</c>, falls keine vorhanden ist.
    /// </summary>
    Task<BreakPolicy?> GetDefaultPolicyAsync(CancellationToken cancellationToken = default);
}
