using SpeedSaga.API.Infrastructure;

namespace SpeedSaga.API.Services;

/// <summary>Pre-builds common puzzle templates so first game start stays fast.</summary>
public sealed class PuzzleWarmupService : IHostedService
{
    static readonly string[] Tiers = ["Easy", "Medium", "Hard", "SuperHard"];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() =>
        {
            // Warm the most common level slots first so early game starts stay instant.
            for (int levelId = 1; levelId <= 64; levelId++)
            {
                if (cancellationToken.IsCancellationRequested) return;
                foreach (var tier in Tiers)
                    ComplexPuzzleGenerator.ToJson(tier, PuzzleTemplateProvider.PuzzleSeed(levelId, tier));
            }
            for (int levelId = 65; levelId <= 200; levelId++)
            {
                if (cancellationToken.IsCancellationRequested) return;
                foreach (var tier in Tiers)
                    ComplexPuzzleGenerator.ToJson(tier, PuzzleTemplateProvider.PuzzleSeed(levelId, tier));
            }
        }, cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
