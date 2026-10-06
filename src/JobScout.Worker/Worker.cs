using JobScout.Core.Llm;

namespace JobScout.Worker;

public class Worker(ILlmClient llm, IHostApplicationLifetime lifetime, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var r = await llm.CompleteAsync(
                "Jsi stručný HR poradce. Odpovídej česky.",
                "Shrň ve 3 bodech, co hledá senior .NET architekt, když mění práci.",
                stoppingToken);

            logger.LogInformation("Model {Model}, stop: {StopReason}\n{Text}", r.Model, r.StopReason, r.Text);
            logger.LogInformation("Tokeny: in={In}, out={Out}", r.InputTokens, r.OutputTokens);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Volání Claude selhalo");
            Environment.ExitCode = 1;
        }
        finally
        {
            lifetime.StopApplication();
        }
    }
}