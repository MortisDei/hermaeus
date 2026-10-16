using System.ComponentModel;
using System.Diagnostics;

namespace Hermaeus.Agent.Services;

/// <summary>
/// Bounded capture for the two Agent process operations. Tree termination is
/// best effort; root exit is not evidence of complete descendant containment.
/// </summary>
internal static class AgentProcessCapture
{
    internal const int RetainedCharactersPerStream = 128 * 1024;

    internal sealed record Outcome(string StandardOutput, string StandardError,
        int? ExitCode, AgentToolOutcomeSignal Signal, string CleanupDetail);

    internal static async Task<Outcome> RunAsync(ProcessStartInfo startInfo,
        TimeSpan timeout, CancellationToken ct, TimeSpan? cleanupTimeout = null,
        bool retainStandardOutputBeginning = false)
    {
        ct.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("The operating system refused to start the process.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        using var readerCancellation = new CancellationTokenSource();
        var stdout = new OutputTail(retainStandardOutputBeginning);
        var stderr = new OutputTail();
        Exception? readFailure = null;

        async Task ReadAsync(StreamReader reader, OutputTail tail)
        {
            var buffer = new char[4096];
            try
            {
                int count;
                while ((count = await reader.ReadAsync(buffer.AsMemory(), readerCancellation.Token)) != 0)
                    tail.Append(buffer.AsSpan(0, count));
            }
            catch (OperationCanceledException) when (readerCancellation.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or System.Text.DecoderFallbackException)
            {
                Interlocked.CompareExchange(ref readFailure, ex, null);
                // A failed reader must not leave the writer blocked on its
                // pipe. A late read may race an already-expired cleanup owner.
                try { deadline.Cancel(); }
                catch (ObjectDisposedException) when (readerCancellation.IsCancellationRequested) { }
            }
        }

        var stdoutTask = ReadAsync(process.StandardOutput, stdout);
        var stderrTask = ReadAsync(process.StandardError, stderr);
        var exitTask = process.WaitForExitAsync();
        var finished = Task.WhenAll(exitTask, stdoutTask, stderrTask);
        var signal = AgentToolOutcomeSignal.Completed;
        var cleanupDetail = string.Empty;
        try
        {
            // The deadline includes pipe drain: descendants may inherit pipes
            // and keep them open even after the original process exits.
            await finished.WaitAsync(deadline.Token);
            if (readFailure is not null) signal = AgentToolOutcomeSignal.Failed;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            signal = readFailure is not null ? AgentToolOutcomeSignal.Failed
                : ct.IsCancellationRequested ? AgentToolOutcomeSignal.Cancelled
                : AgentToolOutcomeSignal.TimedOut;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception)
        {
            readFailure = ex;
            signal = AgentToolOutcomeSignal.Failed;
        }
        finally
        {
            if (signal != AgentToolOutcomeSignal.Completed)
            {
                var rootAlreadyExited = process.HasExited;
                try
                {
                    if (!rootAlreadyExited) process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
                {
                    // A natural exit can race cleanup. Only an observed live
                    // root with a failed kill is a termination failure.
                    if (ex is AggregateException || !process.HasExited)
                        cleanupDetail = $"Tree termination incomplete: {ex.Message}";
                }

                using var cleanup = new CancellationTokenSource(cleanupTimeout ?? TimeSpan.FromSeconds(5));
                readerCancellation.Cancel();
                try { await finished.WaitAsync(cleanup.Token); }
                catch (OperationCanceledException) when (cleanup.IsCancellationRequested)
                {
                    cleanupDetail += " Cleanup deadline elapsed; root or output drain remains incomplete.";
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception or AggregateException)
                {
                    cleanupDetail += $" Cleanup failed: {ex.Message}";
                }
                finally
                {
                    readerCancellation.Cancel();
                    // Observe a late failure even when the cleanup deadline
                    // cannot establish completion. Do not await it unboundedly.
                    _ = finished.ContinueWith(task => _ = task.Exception,
                        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
                cleanupDetail += rootAlreadyExited
                    ? " Root had already exited; descendant ownership is unverified."
                    : " Tree termination requested; root exit does not prove all descendants exited.";
            }
        }

        // Do not read mutable capture state while an uncooperative reader is
        // still completing after a cleanup deadline.
        var output = stdoutTask.IsCompleted ? stdout.ToString() : "[stdout drain incomplete]";
        var error = stderrTask.IsCompleted ? stderr.ToString() : "[stderr drain incomplete]";
        if (readFailure is not null) cleanupDetail += $" Output read failed: {readFailure.Message}";
        return new Outcome(output, error, process.HasExited ? process.ExitCode : null, signal, cleanupDetail.Trim());
    }

    private sealed class OutputTail(bool retainBeginning = false)
    {
        private readonly char[] _characters = new char[RetainedCharactersPerStream];
        private int _position;
        private int _length;
        private long _total;

        public void Append(ReadOnlySpan<char> text)
        {
            _total += text.Length;
            if (retainBeginning) text = text[..Math.Min(text.Length, _characters.Length - _length)];
            if (text.Length > _characters.Length) text = text[^_characters.Length..];
            var first = Math.Min(text.Length, _characters.Length - _position);
            text[..first].CopyTo(_characters.AsSpan(_position));
            text[first..].CopyTo(_characters);
            _position = (_position + text.Length) % _characters.Length;
            _length = Math.Min(_characters.Length, _length + text.Length);
        }

        public override string ToString()
        {
            var start = (_position - _length + _characters.Length) % _characters.Length;
            var first = Math.Min(_length, _characters.Length - start);
            var text = new string(_characters, start, first) + new string(_characters, 0, _length - first);
            if (_total <= _length) return text;
            return retainBeginning
                ? $"{text}\n[{_total - _length} later character(s) omitted]"
                : $"[{_total - _length} earlier character(s) omitted]\n{text}";
        }
    }
}
