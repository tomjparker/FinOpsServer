namespace FinOpsServer.Infra;

public sealed class SagaOrchestrator
{
    // each step returns: (ok, compensator)
    public async Task<bool> ExecuteAsync(params Func<Task<(bool ok, Func<Task> undo)>>[] steps)
    {
        var undo = new Stack<Func<Task>>();
        try
        {
            foreach (var step in steps)
            {
                var (ok, compensator) = await step();
                if (!ok) throw new Exception("step failed");
                undo.Push(compensator);
            }
            return true;
        }
        catch
        {
            while (undo.Count > 0)
            {
                try { await undo.Pop()(); } catch { /* log & continue */ }
            }
            return false;
        }
    }
}
