using Blumchen.Serialization;
using Blumchen.Subscriptions.Management;
using Blumchen.Subscriptions.Replication;
using JetBrains.Annotations;
using Npgsql;
using System.Text.Json.Serialization;

namespace Blumchen.Subscriptions;

public sealed class SubscriptionOptionsBuilder
{
    private NpgsqlConnectionStringBuilder? _connectionStringBuilder;
    private NpgsqlDataSource? _dataSource;
    private PublicationManagement.PublicationSetupOptions _publicationSetupOptions = new();
    private ReplicationSlotManagement.ReplicationSlotSetupOptions? _replicationSlotSetupOptions;
    private IReplicationDataMapper? _dataMapper;
    private readonly Dictionary<Type, IHandler> _registry = [];
    private IErrorProcessor? _errorProcessor;
    private INamingPolicy? _namingPolicy;
    private JsonSerializerContext? _jsonSerializerContext;
    private readonly TableDescriptorBuilder _tableDescriptorBuilder = new();
    private TableDescriptorBuilder.MessageTable? _messageTable;
    
    [UsedImplicitly]
    public SubscriptionOptionsBuilder WithTable(
        Func<TableDescriptorBuilder, TableDescriptorBuilder> builder)
    {
        _messageTable = builder(_tableDescriptorBuilder).Build();
        return this;
    }

    [UsedImplicitly]
    public SubscriptionOptionsBuilder ConnectionString(string connectionString)
    {
        _connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        return this;
    }

    [UsedImplicitly]
    public SubscriptionOptionsBuilder DataSource(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
        return this;
    }

    [UsedImplicitly]
    public SubscriptionOptionsBuilder NamingPolicy(INamingPolicy namingPolicy)
    {
        _namingPolicy = namingPolicy;
        return this;
    }

    [UsedImplicitly]
    public SubscriptionOptionsBuilder JsonContext(JsonSerializerContext jsonSerializerContext)
    {
        _jsonSerializerContext = jsonSerializerContext;
        return this;
    }

    [UsedImplicitly]
    public SubscriptionOptionsBuilder WithPublicationOptions(PublicationManagement.PublicationSetupOptions publicationOptions)
    {
        _publicationSetupOptions =
            publicationOptions with { TypeResolver = _publicationSetupOptions.TypeResolver};
        return this;
    }

    [UsedImplicitly]
    public SubscriptionOptionsBuilder WithReplicationOptions(ReplicationSlotManagement.ReplicationSlotSetupOptions replicationSlotOptions)
    {
        _replicationSlotSetupOptions = replicationSlotOptions;
        return this;
    }

    [UsedImplicitly]
    public SubscriptionOptionsBuilder Consumes<T>(IHandler<T> handler) where T : class
    {
        _registry.TryAdd(typeof(T), handler);
        return this;
    }

    [UsedImplicitly]
    public SubscriptionOptionsBuilder WithErrorProcessor(IErrorProcessor? errorProcessor)
    {
        _errorProcessor = errorProcessor;
        return this;
    }
    
    internal ISubscriptionOptions Build()
    {
        _messageTable ??= _tableDescriptorBuilder.Build();
        ArgumentNullException.ThrowIfNull(_connectionStringBuilder);
        ArgumentNullException.ThrowIfNull(_dataSource);
        ArgumentNullException.ThrowIfNull(_jsonSerializerContext);

        var typeResolver = new JsonTypeResolver(_jsonSerializerContext, _namingPolicy);
        foreach (var type in _registry.Keys) typeResolver.WhiteList(type);
        _dataMapper = new ReplicationDataMapper(typeResolver);
        _publicationSetupOptions = _publicationSetupOptions with { TypeResolver = typeResolver, TableDescriptor = _messageTable};

        Ensure(() =>_registry.Keys.Except(_publicationSetupOptions.TypeResolver.Values()), "Unregistered types:{0}");
        Ensure(() => _publicationSetupOptions.TypeResolver.Values().Except(_registry.Keys), "Unregistered consumer for type:{0}");
        if (_registry.Count == 0)_registry.Add(typeof(object), new ObjectTracingConsumer());
        
        return new SubscriptionOptions(
            _dataSource,
            _connectionStringBuilder,
            _publicationSetupOptions,
            _replicationSlotSetupOptions ?? new ReplicationSlotManagement.ReplicationSlotSetupOptions(),
            _errorProcessor ?? new ConsoleOutErrorProcessor(),
            _dataMapper,
            _registry);

        static void Ensure(Func<IEnumerable<Type>> evalFn, string formattedMsg)
        {
            var misses = evalFn().ToArray();
            if (misses.Length > 0) throw new Exception(string.Format(formattedMsg, string.Join(", ", misses.Select(t => $"'{t.Name}'"))));
        }
    }
}
