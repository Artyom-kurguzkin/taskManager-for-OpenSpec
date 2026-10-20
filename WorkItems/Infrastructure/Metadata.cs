using OpenSpecStore;
using YamlDotNet.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization.EventEmitters;
using YamlDotNet.Serialization.NamingConventions;

namespace WorkItems.Infrastructure;

/// <summary>The app's <c>x-rtm:</c> block in a change's .openspec.yaml. Property order is the order written.</summary>
internal sealed class Rtm
{
    public int? Id { get; set; }
    public string? Title { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public List<string>? Contributors { get; set; }
    public List<string>? Features { get; set; }
    public Dictionary<string, string>? Lanes { get; set; }
    public string? Label { get; set; }
    /// <summary>"Deleted" in the app: hidden from the board, folder kept. Null (omitted) when visible.</summary>
    public bool? Hidden { get; set; }
    public List<string>? Commits { get; set; }
    public List<RtmTest>? Tests { get; set; }
}

internal sealed class RtmTest
{
    public string Suite { get; set; } = "";
    public string Test { get; set; } = "";
    public int? Note { get; set; }
}

internal static class Metadata
{
    private static readonly IDeserializer Reader = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance).IgnoreUnmatchedProperties().Build();

    private static readonly ISerializer Writer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
        .WithEventEmitter(next => new FlowSequences(next))
        .Build();

    /// <summary>Lists are written as [a, b] (as people write them by hand), so a change shows as a one-line git diff.
    /// Mappings inside a list ({suite: x, test: y}) go on that line too.</summary>
    private sealed class FlowSequences(IEventEmitter next) : ChainedEventEmitter(next)
    {
        private int depth;   // how many lists we are inside

        public override void Emit(SequenceStartEventInfo eventInfo, IEmitter emitter)
        {
            eventInfo.Style = SequenceStyle.Flow;
            depth++;
            base.Emit(eventInfo, emitter);
        }

        public override void Emit(SequenceEndEventInfo eventInfo, IEmitter emitter)
        {
            depth--;
            base.Emit(eventInfo, emitter);
        }

        public override void Emit(MappingStartEventInfo eventInfo, IEmitter emitter)
        {
            if (depth > 0) eventInfo.Style = MappingStyle.Flow;
            base.Emit(eventInfo, emitter);
        }
    }

    internal static T Read<T>(string yamlPath) where T : new() =>
        OpenSpecStoreApi.ReadRtmBlock(yamlPath) is string block && block.Trim().Length > 0 ? Reader.Deserialize<T>(block) ?? new T() : new T();

    internal static (bool Success, string Message) Write<T>(string yamlPath, T value) =>
        OpenSpecStoreApi.WriteRtmBlock(yamlPath, Writer.Serialize(value!));

    /// <summary>One section of the x-rtm block (config.yaml is shared with other modules, so never rewrite the whole block).</summary>
    internal static T? ReadSection<T>(string yamlPath, string key) where T : class =>
        OpenSpecStoreApi.ReadRtmSection(yamlPath, key) is string s && s.Trim().Length > 0 ? Reader.Deserialize<T>(s) : null;

    internal static (bool Success, string Message) WriteSection<T>(string yamlPath, string key, T? value) where T : class =>
        OpenSpecStoreApi.WriteRtmSection(yamlPath, key, value is null ? null : Writer.Serialize(value));
}
