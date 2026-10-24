#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;

namespace UnityPackageImporter.Models;

internal enum AvatarParameterComparison { Equal, NotEqual, Greater, Less, OpenInterval }
internal sealed record AvatarPropertyKey(InitialAppearanceEffect Effect, long TargetId, string Property)
{
    internal static AvatarPropertyKey Of(InitialAppearanceWrite write) => new(write.Effect, write.TargetId, write.Property);
}
internal abstract record AvatarWardrobeCondition;
internal sealed record AvatarParameterCondition(AvatarParameterId Parameter, AvatarParameterComparison Comparison,
    double Value, double Upper = 0) : AvatarWardrobeCondition;
internal sealed record AvatarActiveCondition(AvatarPropertyKey Property, bool Expected = true) : AvatarWardrobeCondition;
internal sealed record AvatarWardrobeRule(string Id, InitialAppearanceWrite Write,
    IReadOnlyList<AvatarWardrobeCondition> Conditions, bool Inverted = false);

internal sealed class AvatarWardrobeGraph
{
    private readonly IReadOnlyList<AvatarPropertyKey> order;
    private readonly IReadOnlyDictionary<AvatarPropertyKey, IReadOnlyList<AvatarWardrobeRule>> rules;
    internal AvatarParameterCatalog Parameters { get; }
    internal IReadOnlyDictionary<string, AvatarParameterId>? IndependentControls { get; private init; }
    internal IReadOnlyDictionary<AvatarPropertyKey, InitialAppearanceWrite> Baselines { get; }

    internal AvatarWardrobeGraph(AvatarParameterCatalog parameters, IEnumerable<InitialAppearanceWrite> baselines,
        IEnumerable<AvatarWardrobeRule> sourceRules, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(baselines);
        ArgumentNullException.ThrowIfNull(sourceRules);
        token.ThrowIfCancellationRequested();
        Parameters = parameters;
        var defaults = new Dictionary<AvatarPropertyKey, InitialAppearanceWrite>();
        static void ValidateWrite(InitialAppearanceWrite write)
        {
            if (write == null || write.TargetId == 0 || string.IsNullOrWhiteSpace(write.Property) || !Enum.IsDefined(write.Effect) ||
                !float.IsFinite(write.Weight) || write.Effect == InitialAppearanceEffect.Active && write.Property != "m_IsActive" ||
                write.Effect == InitialAppearanceEffect.Material && (write.Material == null || write.Material.FileId == 0 ||
                    write.Material.Guid == null || write.Material.Guid.Length != 32 || write.Material.Guid.Any(character => !Uri.IsHexDigit(character))))
                throw new InvalidDataException("Wardrobe property writes require an exact target and a finite supported value.");
        }
        foreach (var baseline in baselines)
        {
            token.ThrowIfCancellationRequested();
            ValidateWrite(baseline);
            if (!defaults.TryAdd(AvatarPropertyKey.Of(baseline), baseline))
                throw new InvalidDataException("Each wardrobe property must have exactly one authored baseline.");
        }
        var grouped = defaults.Keys.ToDictionary(key => key, _ => new List<AvatarWardrobeRule>());
        var dependencies = defaults.Keys.ToDictionary(key => key, _ => new HashSet<AvatarPropertyKey>());
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in sourceRules)
        {
            token.ThrowIfCancellationRequested();
            if (rule == null || string.IsNullOrWhiteSpace(rule.Id) || !identities.Add(rule.Id) || rule.Conditions == null)
                throw new InvalidDataException("Wardrobe rules require unique source identities and explicit conditions.");
            ValidateWrite(rule.Write);
            var key = AvatarPropertyKey.Of(rule.Write);
            if (!defaults.ContainsKey(key)) throw new InvalidDataException("A wardrobe rule has no authored property baseline.");
            var conditions = rule.Conditions.ToArray();
            foreach (var condition in conditions)
            {
                token.ThrowIfCancellationRequested();
                if (condition is AvatarParameterCondition parameter)
                {
                    parameters.Resolve(parameter.Parameter);
                    if (!Enum.IsDefined(parameter.Comparison) || double.IsNaN(parameter.Value) || double.IsNaN(parameter.Upper) ||
                        (parameter.Comparison == AvatarParameterComparison.OpenInterval
                            ? parameter.Value >= parameter.Upper
                            : !double.IsFinite(parameter.Value)))
                        throw new InvalidDataException("Wardrobe parameter conditions require a valid comparison and ordered interval.");
                }
                else if (condition is AvatarActiveCondition active)
                {
                    if (active.Property == null || active.Property.Effect != InitialAppearanceEffect.Active || !defaults.ContainsKey(active.Property))
                        throw new InvalidDataException("An ancestor condition requires a known active-state property.");
                    dependencies[key].Add(active.Property);
                }
                else throw new InvalidDataException("Wardrobe condition has no verified evaluator.");
            }
            grouped[key].Add(rule with { Conditions = Array.AsReadOnly(conditions) });
        }
        var remaining = dependencies.ToDictionary(pair => pair.Key, pair => pair.Value.Count);
        var consumers = defaults.Keys.ToDictionary(key => key, _ => new List<AvatarPropertyKey>());
        foreach (var pair in dependencies)
            foreach (var dependency in pair.Value) consumers[dependency].Add(pair.Key);
        var ready = new Queue<AvatarPropertyKey>(remaining.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var sorted = new List<AvatarPropertyKey>();
        while (ready.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var key = ready.Dequeue();
            sorted.Add(key);
            foreach (var consumer in consumers[key])
                if (--remaining[consumer] == 0) ready.Enqueue(consumer);
        }
        if (sorted.Count != defaults.Count) throw new InvalidDataException("Cyclic reactive property dependencies require a separate runtime adapter.");
        order = sorted.AsReadOnly();
        Baselines = new ReadOnlyDictionary<AvatarPropertyKey, InitialAppearanceWrite>(defaults);
        rules = new ReadOnlyDictionary<AvatarPropertyKey, IReadOnlyList<AvatarWardrobeRule>>(
            grouped.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<AvatarWardrobeRule>)pair.Value.AsReadOnly()));
    }

    internal IReadOnlyDictionary<AvatarPropertyKey, InitialAppearanceWrite> Evaluate(AvatarParameterState state, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(state.Catalog, Parameters)) throw new InvalidDataException("Wardrobe evaluation requires its own avatar parameter catalog.");
        var output = new Dictionary<AvatarPropertyKey, InitialAppearanceWrite>();
        foreach (var key in order)
        {
            token.ThrowIfCancellationRequested();
            var selected = Baselines[key];
            foreach (var rule in rules[key])
            {
                token.ThrowIfCancellationRequested();
                bool active = rule.Conditions.All(condition => condition switch
                {
                    AvatarActiveCondition property => output[property.Property].Active == property.Expected,
                    AvatarParameterCondition parameter => Compare(state[parameter.Parameter], parameter,
                        Parameters.Definitions[Parameters.Resolve(parameter.Parameter)].Type),
                    _ => throw new InvalidDataException("Unknown wardrobe condition.")
                });
                if (active != rule.Inverted) selected = rule.Write;
            }
            output.Add(key, selected);
        }
        return new ReadOnlyDictionary<AvatarPropertyKey, InitialAppearanceWrite>(output);
    }

    private static bool Compare(double value, AvatarParameterCondition condition, AvatarParameterType type)
    {
        double lower = type == AvatarParameterType.Float ? (double)(float)condition.Value : condition.Value;
        double upper = type == AvatarParameterType.Float ? (double)(float)condition.Upper : condition.Upper;
        return condition.Comparison switch
        {
            AvatarParameterComparison.Equal => value == lower,
            AvatarParameterComparison.NotEqual => value != lower,
            AvatarParameterComparison.Greater => value > lower,
            AvatarParameterComparison.Less => value < lower,
            AvatarParameterComparison.OpenInterval => value > lower && value < upper,
            _ => throw new InvalidDataException("Unknown parameter comparison.")
        };
    }

    internal static AvatarParameterId IndependentParameter(string avatar, string control) => new(avatar, "vrcfury-toggle", control);

    internal static AvatarWardrobeGraph FromIndependentControls(string avatar, IReadOnlyList<AvatarWardrobeControl> controls, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(controls);
        token.ThrowIfCancellationRequested();
        var declarations = new List<AvatarParameterDeclaration>();
        var defaults = new List<InitialAppearanceWrite>();
        var actions = new List<AvatarWardrobeRule>();
        var identities = new Dictionary<string, AvatarParameterId>(StringComparer.Ordinal);
        foreach (var control in controls)
        {
            token.ThrowIfCancellationRequested();
            if (control == null || string.IsNullOrWhiteSpace(control.Id) || control.Bindings == null || control.Bindings.Count == 0)
                throw new InvalidDataException("Independent controls require an exact source identity and nonempty bindings.");
            var id = IndependentParameter(avatar, control.Id);
            if (!identities.TryAdd(control.Id, id)) throw new InvalidDataException("Independent controls require unique source identities.");
            declarations.Add(new(id, control.ParameterType, control.DefaultOn ? 1 : 0, control.Saved, control.Synced, control.Id));
            int index = 0;
            foreach (var binding in control.Bindings)
            {
                token.ThrowIfCancellationRequested();
                if (binding == null || binding.Off == null || binding.On == null)
                    throw new InvalidDataException("Independent controls require explicit on/off property writes.");
                if (AvatarPropertyKey.Of(binding.Off) != AvatarPropertyKey.Of(binding.On))
                    throw new InvalidDataException("An independent binding must restore the same property it controls.");
                defaults.Add(binding.Off);
                actions.Add(new(control.Id + "/" + index++, binding.On,
                    new AvatarWardrobeCondition[] { new AvatarParameterCondition(id, AvatarParameterComparison.Equal, 1) }));
            }
        }
        return new(new AvatarParameterCatalog(avatar, declarations, token: token), defaults, actions, token)
        {
            IndependentControls = new ReadOnlyDictionary<string, AvatarParameterId>(identities)
        };
    }
}
