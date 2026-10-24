namespace NetMan.Core.Tests;

/// <summary>Adapters whose reads come from a per-adapter script. The last scripted config repeats.</summary>
internal sealed class FakeAdapters : IAdapterReader
{
    private readonly Dictionary<Guid, Queue<LiveConfig>> _scripts = [];
    private readonly List<AdapterInfo> _adapters = [];

    public List<Guid> Reads { get; } = [];

    public FakeAdapters Add(AdapterInfo adapter, params LiveConfig[] script)
    {
        _adapters.Add(adapter);
        _scripts[adapter.Id] = new Queue<LiveConfig>(script);
        return this;
    }

    public void Remove(Guid id) => _adapters.RemoveAll(a => a.Id == id);

    public IReadOnlyList<AdapterInfo> List() => _adapters.ToList();

    public LiveConfig? Read(Guid adapterId)
    {
        Reads.Add(adapterId);
        if (_adapters.All(a => a.Id != adapterId))
        {
            return null;
        }

        var queue = _scripts[adapterId];
        return queue.Count > 1 ? queue.Dequeue() : queue.Peek();
    }
}

internal sealed class FakeRunner : ICommandRunner
{
    private readonly Queue<CommandResult> _results = new();

    public List<IReadOnlyList<string>> Calls { get; } = [];

    public string Executable => "netsh";

    public FakeRunner Then(CommandResult result)
    {
        _results.Enqueue(result);
        return this;
    }

    public Task<CommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        Calls.Add(arguments);
        return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : new CommandResult(0, "Ok.", "", false));
    }
}

internal sealed class FakePinger(PingResult? result = null) : IPinger
{
    public List<string> Targets { get; } = [];

    public Task<PingResult> PingAsync(string target, int count, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Targets.Add(target);
        return Task.FromResult(result ?? new PingResult(count, count, 2));
    }
}
