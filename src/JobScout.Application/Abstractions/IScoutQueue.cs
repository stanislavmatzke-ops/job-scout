namespace JobScout.Application.Abstractions;

/// <summary>
/// Searches running in the background of the API (6h – the first search right after the first CV).
/// One user is at most once in the queue or running – a double click or two tabs don't pay twice.
/// </summary>
public interface IScoutQueue
{
    /// <returns>false = this user is already waiting or running (or the queue is switched off)</returns>
    bool TryEnqueue(Guid userId);

    /// <summary>Waiting or running right now.</summary>
    bool IsBusy(Guid userId);
}
