using Lanting.Core.Island;
using Lanting.Core.Modules;

namespace Lanting.Core.Tests;

public class ModuleHostTests
{
    [Fact]
    public async Task Faulty_module_does_not_stop_the_others()
    {
        var log = new List<string>();
        var good = new FakeModule("good", log);
        var bad = new FakeModule("bad", log, failOnStart: true);
        var faults = new List<string>();
        var host = new ModuleHost([bad, good], new NullHost());
        host.ModuleFaulted += (_, e) => faults.Add(e.ModuleId);

        await host.StartAsync(CancellationToken.None);

        Assert.Equal(["bad"], faults);
        Assert.Equal([good], host.Started);
    }

    [Fact]
    public async Task Dispose_stops_modules_in_reverse_order()
    {
        var log = new List<string>();
        var host = new ModuleHost([new FakeModule("a", log), new FakeModule("b", log)], new NullHost());
        await host.StartAsync(CancellationToken.None);

        await host.DisposeAsync();

        Assert.Equal(["start a", "start b", "stop b", "stop a"], log);
    }

    private sealed class FakeModule(string id, List<string> log, bool failOnStart = false) : IIslandModule
    {
        public string Id => id;

        public Task StartAsync(IIslandHost host, CancellationToken cancellationToken)
        {
            if (failOnStart)
            {
                throw new InvalidOperationException("boom");
            }

            log.Add($"start {id}");
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            log.Add($"stop {id}");
            return Task.CompletedTask;
        }
    }

    private sealed class NullHost : IIslandHost
    {
        public void Show(IslandContent content)
        {
        }

        public void Withdraw(string moduleId, string key)
        {
        }
    }
}
