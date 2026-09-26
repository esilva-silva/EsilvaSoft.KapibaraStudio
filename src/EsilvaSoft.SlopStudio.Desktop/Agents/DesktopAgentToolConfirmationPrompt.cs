using EsilvaSoft.SlopStudio.Application.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.Agents;

/// <summary>
/// Desktop implementation of <see cref="IAgentToolConfirmationPrompt"/> (ADR-056): routes each confirmation to the
/// inline card of the conversation that owns the channel, through the handler attached by the chat
/// (<see cref="Attach"/>). The registry applies the deadline and cancels the token; an expired, cancelled or failed
/// prompt — and a prompt without an attached chat — is a rejection. Nothing here approves anything by itself and there
/// is no "always" answer.
/// </summary>
public sealed class DesktopAgentToolConfirmationPrompt : IAgentToolConfirmationPrompt
{
    private volatile Func<AgentToolConfirmationRequest, CancellationToken, Task<AgentToolConfirmationDecision>>? _handler;

    /// <summary>Attaches the chat handler (runs on the UI thread). Returns a detach token.</summary>
    public IDisposable Attach(Func<AgentToolConfirmationRequest, CancellationToken, Task<AgentToolConfirmationDecision>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
        return new Detacher(this, handler);
    }

    public async Task<AgentToolConfirmationDecision> ConfirmAsync(AgentToolConfirmationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_handler is not { } handler || cancellationToken.IsCancellationRequested)
        {
            return AgentToolConfirmationDecision.Rejected;
        }

        try
        {
            var decision = await AgentUiDispatch.RunOnUiAsync(() => handler(request, cancellationToken)).ConfigureAwait(false);
            // A decision that lands after the deadline never counts as an approval.
            return cancellationToken.IsCancellationRequested ? AgentToolConfirmationDecision.Rejected : decision;
        }
        catch (Exception)
        {
            return AgentToolConfirmationDecision.Rejected;
        }
    }

    private sealed class Detacher(
        DesktopAgentToolConfirmationPrompt owner,
        Func<AgentToolConfirmationRequest, CancellationToken, Task<AgentToolConfirmationDecision>> handler) : IDisposable
    {
        public void Dispose()
        {
            if (ReferenceEquals(owner._handler, handler))
            {
                owner._handler = null;
            }
        }
    }
}
