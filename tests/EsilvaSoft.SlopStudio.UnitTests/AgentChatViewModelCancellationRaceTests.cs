using System.Reflection;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// Regression coverage for the race between the "cancel this turn" command and the turn's own CTS disposal in
/// <c>AgentChatViewModel.Turn.cs</c> (AC-11). Before the fix, a cancel arriving in the narrow window after
/// <c>RunTurnAsync</c>'s <c>finally</c> block disposed the turn's <see cref="CancellationTokenSource"/> made
/// <c>CancelAsync()</c> throw an unhandled <see cref="ObjectDisposedException"/>. These tests exercise the private
/// <c>TurnRun</c> helper directly (via reflection, matching the style already used elsewhere in this suite for
/// private members) so the exact interaction is proven deterministically, without relying on timing across the
/// dispatcher, the runtime pump and the provider adapter.
/// </summary>
[TestFixture]
public sealed class AgentChatViewModelCancellationRaceTests
{
    private static readonly Type TurnRunType =
        typeof(AgentChatViewModel).GetNestedType("TurnRun", BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("AgentChatViewModel.TurnRun was not found; the ViewModel's shape changed.");

    private static object NewTurnRun(CancellationTokenSource cancellation)
    {
        var ctor = TurnRunType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SingleOrDefault(candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 2 && parameters[0].ParameterType == typeof(AgentTurnId) &&
                    parameters[1].ParameterType == typeof(CancellationTokenSource);
            })
            ?? throw new InvalidOperationException("TurnRun(AgentTurnId, CancellationTokenSource) constructor not found.");
        return ctor.Invoke([AgentTurnId.New(), cancellation]);
    }

    private static Task RequestCancellationAsync(object run)
    {
        var method = TurnRunType.GetMethod("RequestCancellationAsync", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("TurnRun.RequestCancellationAsync not found.");
        return (Task)method.Invoke(run, null)!;
    }

    private static void DisposeCancellation(object run)
    {
        var method = TurnRunType.GetMethod("DisposeCancellation", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("TurnRun.DisposeCancellation not found.");
        method.Invoke(run, null);
    }

    [Test]
    public void CancellingATurnAfterItsCtsWasAlreadyDisposedIsASilentNoOp()
    {
        // Reproduces the exact original bug: the turn finished first (RunTurnAsync's finally already disposed the
        // CTS), then a cancel request arrives. Before the fix this threw ObjectDisposedException from CancelAsync();
        // the fix must make it a no-op, never an exception, and idempotent when repeated.
        using var cts = new CancellationTokenSource();
        var run = NewTurnRun(cts);

        DisposeCancellation(run);

        Assert.DoesNotThrowAsync(async () => await RequestCancellationAsync(run));
        Assert.DoesNotThrowAsync(async () => await RequestCancellationAsync(run), "Repeating the cancel must stay a no-op.");
    }

    [Test]
    public void CancellingBeforeDisposalStillCancelsTheToken()
    {
        // The fix must not turn cancellation into a no-op in the ordinary case: requested before the turn finishes,
        // the token is actually signalled.
        using var cts = new CancellationTokenSource();
        var run = NewTurnRun(cts);

        Assert.DoesNotThrowAsync(async () => await RequestCancellationAsync(run));
        Assert.That(cts.IsCancellationRequested, Is.True);

        DisposeCancellation(run);
    }

    [Test]
    public async Task ConcurrentCancelRequestsAndDisposalNeverThrowUnderContention()
    {
        // Stress-tests the exact race described by the audit: many pairs of "cancel" and "the turn just finished, so
        // dispose the CTS" racing against each other with a shared start gate, forcing maximum contention instead of
        // relying on incidental timing. Every pairing must complete without an unhandled exception.
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var cts = new CancellationTokenSource();
            var run = NewTurnRun(cts);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var cancelling = Task.Run(async () =>
            {
                await start.Task;
                await RequestCancellationAsync(run);
            });
            var disposing = Task.Run(async () =>
            {
                await start.Task;
                DisposeCancellation(run);
            });

            start.SetResult();

            Exception? failure = null;
            try
            {
                await Task.WhenAll(cancelling, disposing);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Assert.That(failure, Is.Null, $"Iteration {iteration} threw: {failure}");
        }
    }
}
