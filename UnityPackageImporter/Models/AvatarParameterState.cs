#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;

namespace UnityPackageImporter.Models;

internal enum AvatarParameterType { Bool, Int, Float }
internal sealed record AvatarParameterId(string Avatar, string Scope, string Name);
internal sealed record AvatarParameterDeclaration(AvatarParameterId Id, AvatarParameterType Type,
    double? DefaultValue, bool? Saved, bool? Synced, string Source);
internal sealed record AvatarParameterAlias(AvatarParameterId From, AvatarParameterId To);
internal sealed record AvatarParameterDefinition(AvatarParameterId Id, AvatarParameterType Type,
    double DefaultValue, bool Saved, bool Synced, IReadOnlyList<string> Sources)
{
    internal bool Accepts(double value) => double.IsFinite(value) && Type switch
    {
        AvatarParameterType.Bool => value is 0 or 1,
        AvatarParameterType.Int => value >= int.MinValue && value <= int.MaxValue && value == Math.Truncate(value),
        AvatarParameterType.Float => value >= -float.MaxValue && value <= float.MaxValue,
        _ => false
    };
}

internal sealed class AvatarParameterCatalog
{
    private readonly IReadOnlyDictionary<AvatarParameterId, AvatarParameterId> identities;
    internal string Avatar { get; }
    internal IReadOnlyDictionary<AvatarParameterId, AvatarParameterDefinition> Definitions { get; }

    internal AvatarParameterCatalog(string avatar, IEnumerable<AvatarParameterDeclaration> declarations,
        IEnumerable<AvatarParameterAlias>? aliases = null, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(avatar)) throw new InvalidDataException("An exact avatar identity is required.");
        ArgumentNullException.ThrowIfNull(declarations);
        token.ThrowIfCancellationRequested();
        Avatar = avatar;
        void Validate(AvatarParameterId id)
        {
            if (id == null || id.Avatar != avatar || id.Scope == null || string.IsNullOrWhiteSpace(id.Name) ||
                id.Name.Contains('\0') || id.Scope.Contains('\0'))
                throw new InvalidDataException("Parameter identities must belong to one avatar and retain exact scope/name.");
        }
        var links = new Dictionary<AvatarParameterId, AvatarParameterId>();
        foreach (var alias in aliases ?? Array.Empty<AvatarParameterAlias>())
        {
            token.ThrowIfCancellationRequested();
            if (alias == null) throw new InvalidDataException("Parameter aliases cannot be null.");
            Validate(alias.From);
            Validate(alias.To);
            if (!links.TryAdd(alias.From, alias.To))
                throw new InvalidDataException("A scoped parameter has more than one alias declaration.");
        }
        var resolved = new Dictionary<AvatarParameterId, AvatarParameterId>();
        AvatarParameterId Canonical(AvatarParameterId id)
        {
            Validate(id);
            var path = new HashSet<AvatarParameterId>();
            var current = id;
            while (links.TryGetValue(current, out var next))
            {
                token.ThrowIfCancellationRequested();
                if (!path.Add(current)) throw new InvalidDataException("Cyclic parameter aliases cannot be compiled.");
                current = next;
            }
            foreach (var item in path) resolved[item] = current;
            resolved[id] = current;
            return current;
        }
        foreach (var id in links.Keys) Canonical(id);
        var grouped = new Dictionary<AvatarParameterId, List<AvatarParameterDeclaration>>();
        foreach (var sourceDeclaration in declarations)
        {
            token.ThrowIfCancellationRequested();
            var declaration = sourceDeclaration;
            if (declaration == null || !Enum.IsDefined(declaration.Type) || string.IsNullOrWhiteSpace(declaration.Source))
                throw new InvalidDataException("Parameter declarations require a type and source provenance.");
            var id = Canonical(declaration.Id);
            if (declaration.DefaultValue.HasValue && !new AvatarParameterDefinition(declaration.Id, declaration.Type,
                declaration.DefaultValue.Value, false, true, Array.Empty<string>()).Accepts(declaration.DefaultValue.Value))
                throw new InvalidDataException("Invalid typed parameter default: " + declaration.Id.Name);
            if (declaration.Type == AvatarParameterType.Float && declaration.DefaultValue.HasValue)
                declaration = declaration with { DefaultValue = (double)(float)declaration.DefaultValue.Value };
            if (!grouped.TryGetValue(id, out var list)) grouped.Add(id, list = new());
            list.Add(declaration);
        }
        var definitions = new Dictionary<AvatarParameterId, AvatarParameterDefinition>();
        foreach (var pair in grouped)
        {
            token.ThrowIfCancellationRequested();
            var entries = pair.Value;
            if (entries.Select(entry => entry.Type).Distinct().Count() != 1 ||
                entries.Where(entry => entry.DefaultValue.HasValue).Select(entry => entry.DefaultValue!.Value).Distinct().Count() > 1 ||
                entries.Where(entry => entry.Saved.HasValue).Select(entry => entry.Saved!.Value).Distinct().Count() > 1 ||
                entries.Where(entry => entry.Synced.HasValue).Select(entry => entry.Synced!.Value).Distinct().Count() > 1)
                throw new InvalidDataException("Conflicting declarations require an explicit source-specific parameter policy: " + pair.Key.Name);
            var definition = new AvatarParameterDefinition(pair.Key, entries[0].Type,
                entries.Select(entry => entry.DefaultValue).FirstOrDefault(value => value.HasValue) ?? 0,
                entries.Select(entry => entry.Saved).FirstOrDefault(value => value.HasValue) ?? false,
                entries.Select(entry => entry.Synced).FirstOrDefault(value => value.HasValue) ?? true,
                Array.AsReadOnly(entries.Select(entry => entry.Source).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray()));
            if (!definition.Accepts(definition.DefaultValue)) throw new InvalidDataException("Invalid typed parameter default: " + pair.Key.Name);
            definitions.Add(pair.Key, definition);
        }
        foreach (var target in resolved.Values)
            if (!definitions.ContainsKey(target)) throw new InvalidDataException("Parameter alias has no declared destination: " + target.Name);
        identities = new ReadOnlyDictionary<AvatarParameterId, AvatarParameterId>(resolved);
        Definitions = new ReadOnlyDictionary<AvatarParameterId, AvatarParameterDefinition>(definitions);
    }

    internal AvatarParameterId Resolve(AvatarParameterId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (id.Avatar != Avatar) throw new InvalidDataException("A parameter cannot cross avatar ownership.");
        var canonical = identities.TryGetValue(id, out var target) ? target : id;
        if (!Definitions.ContainsKey(canonical)) throw new InvalidDataException("Undeclared parameter: " + id.Name);
        return canonical;
    }

    internal AvatarParameterState CreateState() => new(this, Definitions.ToDictionary(pair => pair.Key, pair => pair.Value.DefaultValue));

    internal static AvatarParameterCatalog FromExpressions(string avatar, IReadOnlyDictionary<string, VrcParameter> parameters, string source)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return new(avatar, parameters.Select(pair =>
        {
            if (pair.Value == null || pair.Key != pair.Value.Name) throw new InvalidDataException("Expression parameter identity does not match its declaration.");
            var parameter = pair.Value;
            var type = parameter.ValueType switch
            {
                0 => AvatarParameterType.Int,
                1 => AvatarParameterType.Float,
                2 => AvatarParameterType.Bool,
                _ => throw new InvalidDataException("Unknown expression parameter type.")
            };
            return new AvatarParameterDeclaration(new(avatar, "", pair.Key), type,
                parameter.HasDefaultValue ? parameter.DefaultValue : null, parameter.Saved, parameter.Synced, source);
        }));
    }
}

internal sealed class AvatarParameterState
{
    internal AvatarParameterCatalog Catalog { get; }
    internal IReadOnlyDictionary<AvatarParameterId, double> Values { get; }
    internal double this[AvatarParameterId id] => Values[Catalog.Resolve(id)];

    internal AvatarParameterState(AvatarParameterCatalog catalog, Dictionary<AvatarParameterId, double> values)
    {
        Catalog = catalog;
        if (values.Count != catalog.Definitions.Count || values.Any(pair => !catalog.Definitions.TryGetValue(pair.Key, out var definition) || !definition.Accepts(pair.Value)))
            throw new InvalidDataException("Parameter state must exactly match its catalog.");
        Values = new ReadOnlyDictionary<AvatarParameterId, double>(new Dictionary<AvatarParameterId, double>(values));
    }

    internal AvatarParameterState With(params (AvatarParameterId Id, double Value)[] changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var proposed = new Dictionary<AvatarParameterId, double>();
        foreach (var change in changes)
        {
            var id = Catalog.Resolve(change.Id);
            if (!Catalog.Definitions[id].Accepts(change.Value)) throw new InvalidDataException("Invalid typed parameter value: " + id.Name);
            double normalized = Catalog.Definitions[id].Type == AvatarParameterType.Float ? (double)(float)change.Value : change.Value;
            if (proposed.TryGetValue(id, out double value) && value != normalized)
                throw new InvalidDataException("An atomic update contains conflicting writes to the same parameter.");
            proposed[id] = normalized;
        }
        var next = Values.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var pair in proposed) next[pair.Key] = pair.Value;
        return new(Catalog, next);
    }

    internal AvatarParameterState ResetUnsaved() => new(Catalog, Catalog.Definitions.ToDictionary(pair => pair.Key,
        pair => pair.Value.Saved ? Values[pair.Key] : pair.Value.DefaultValue));
}
