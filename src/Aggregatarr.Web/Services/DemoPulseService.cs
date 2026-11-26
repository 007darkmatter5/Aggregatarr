using Aggregatarr.Web.Hosting;

namespace Aggregatarr.Web.Services;

public sealed class DemoPulseService(DemoWorld world, DashboardStore store, IClock clock) : BackgroundService, IDashboardRefresh
{
    public void Kick() => store.Publish(world.Build(clock.UtcNow, advance: false));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            store.Publish(world.Build(clock.UtcNow, advance: true));
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
