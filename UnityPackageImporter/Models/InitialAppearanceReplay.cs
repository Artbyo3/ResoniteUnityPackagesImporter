#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace UnityPackageImporter.Models;

internal sealed record AppearanceClipInput(string Guid, long FileId, string? SourcePath, string? Yaml, string? Sha256);
internal sealed record AppearanceReplayOutcome(bool CanApply, InitialAppearanceDiagnostic[] Diagnostics, InitialAppearanceWrite[] Writes);

internal sealed class InitialAppearanceReplay
{
    internal const int MaximumBytes = 32 * 1024 * 1024;
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 64
    };

    public required int SchemaVersion { get; init; }
    public required string LoadedBuildHash { get; init; }
    public required Guid SessionId { get; init; }
    public required string SourcePath { get; init; }
    public required DateTimeOffset CapturedUtc { get; init; }
    public required string Yaml { get; init; }
    public required string YamlSha256 { get; init; }
    public required long AvatarRootId { get; init; }
    public required AuthoredAppearanceObject[]? Objects { get; init; }
    public required Dictionary<long, long>? TransformOwners { get; init; }
    public required Dictionary<long, InitialAppearanceRenderer> Renderers { get; init; }
    public required bool ExternalContextVerifiedAbsent { get; init; }
    public required bool AllowMixedFrameworks { get; init; }
    public required AppearanceClipInput[] Clips { get; init; }
    public required AppearanceReplayOutcome Recorded { get; init; }

    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static InitialAppearanceReplay Capture(string yaml, InitialAppearanceContext context,
        IEnumerable<AppearanceClipInput> clips, InitialAppearanceResult result, string buildHash, Guid sessionId, string sourcePath) => new()
    {
        SchemaVersion = 1,
        LoadedBuildHash = buildHash,
        SessionId = sessionId,
        SourcePath = sourcePath,
        CapturedUtc = DateTimeOffset.UtcNow,
        Yaml = yaml,
        YamlSha256 = Hash(yaml),
        AvatarRootId = context.AvatarRootId,
        Objects = context.AuthoredHierarchy?.Objects.Values.ToArray(),
        TransformOwners = context.AuthoredHierarchy?.TransformOwners.ToDictionary(pair => pair.Key, pair => pair.Value),
        Renderers = context.Renderers.ToDictionary(pair => pair.Key, pair => new InitialAppearanceRenderer(pair.Value.GameObjectId,
            pair.Value.Shapes.ToDictionary(shape => shape.Key, shape => shape.Value), pair.Value.Materials.ToArray())),
        ExternalContextVerifiedAbsent = context.ExternalAnimationAndParameterContextVerifiedAbsent,
        AllowMixedFrameworks = context.AllowMixedFrameworks,
        Clips = clips.ToArray(),
        Recorded = Outcome(result)
    };

    internal static AppearanceReplayOutcome Outcome(InitialAppearanceResult result) => new(result.CanApply,
        result.Diagnostics.ToArray(), result.Writes.OrderBy(write => write.Effect).ThenBy(write => write.TargetId)
            .ThenBy(write => write.Property, StringComparer.Ordinal).ToArray());

    internal string Serialize()
    {
        ValidateInputBudget();
        using var memory = new MemoryStream();
        using var bounded = new AppearanceReplayByteStream(memory, MaximumBytes);
        JsonSerializer.Serialize(bounded, this, JsonOptions);
        return Encoding.UTF8.GetString(memory.GetBuffer(), 0, checked((int)memory.Length));
    }

    internal void ValidateInputBudget()
    {
        long bytes = Encoding.UTF8.GetByteCount(Yaml);
        foreach (var clip in Clips) bytes += clip.Yaml == null ? 0 : Encoding.UTF8.GetByteCount(clip.Yaml);
        if (bytes > MaximumBytes) throw new InvalidDataException("Appearance replay sources exceed the local evidence byte budget.");
    }

    internal static InitialAppearanceReplay Deserialize(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("Appearance replay exceeds the local evidence byte budget.");
        return JsonSerializer.Deserialize<InitialAppearanceReplay>(json, JsonOptions) ?? throw new InvalidDataException("Missing appearance replay document.");
    }

    internal InitialAppearanceResult Evaluate(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (SchemaVersion != 1 || Yaml == null || YamlSha256 != Hash(Yaml) || Clips == null || Renderers == null || Recorded == null ||
            Recorded.Diagnostics == null || Recorded.Writes == null || (Objects == null) != (TransformOwners == null) ||
            Renderers.Values.Any(renderer => renderer == null || renderer.Shapes == null || renderer.Materials == null || renderer.Materials.Any(material => material == null)))
            throw new InvalidDataException("Appearance replay schema, source hash or context is incomplete.");
        var dependencies = new Dictionary<(string Guid, long Id), AppearanceClipInput>();
        foreach (var clip in Clips)
        {
            if (clip == null || string.IsNullOrEmpty(clip.Guid) || !dependencies.TryAdd((clip.Guid, clip.FileId), clip) ||
                (clip.Yaml == null ? clip.Sha256 != null : clip.Sha256 != Hash(clip.Yaml)))
                throw new InvalidDataException("Appearance replay has duplicate, incomplete or changed clip evidence.");
        }
        UnityStaticClipResult? Resolve(string guid, long identity)
        {
            token.ThrowIfCancellationRequested();
            if (!dependencies.TryGetValue((guid, identity), out var input))
                throw new InvalidDataException("Replay dependency was not captured: " + guid + ":" + identity + ". A missing resolver cannot stand in for production evaluation.");
            return input.Yaml == null ? null : UnityStaticAnimationClip.Parse(input.Yaml, identity, token);
        }
        var context = new InitialAppearanceContext
        {
            AvatarRootId = AvatarRootId,
            AuthoredHierarchy = Objects == null ? null : new AuthoredAppearanceHierarchy(Objects, TransformOwners!),
            Renderers = Renderers,
            ExternalAnimationAndParameterContextVerifiedAbsent = ExternalContextVerifiedAbsent,
            AllowMixedFrameworks = AllowMixedFrameworks
        };
        return VrcFuryInitialAppearance.Evaluate(Yaml, context, Resolve, token);
    }

    internal bool MatchesRecorded(InitialAppearanceResult result) =>
        JsonSerializer.Serialize(Recorded, JsonOptions) == JsonSerializer.Serialize(Outcome(result), JsonOptions);
}
