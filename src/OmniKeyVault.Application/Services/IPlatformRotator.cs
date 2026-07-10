namespace OmniKeyVault.Application;

/// <summary>
/// One-click credential rotation per ROADMAP v0.4 S8-T1 / S8-T2 / MANUAL §4.3.2.
/// Each platform (OpenAI, GitHub, etc.) implements this interface to expose
/// its native "rotate secret" flow. The EditorWindow's "Rotate" button calls
/// <see cref="RotateAsync"/>; the returned new value is written to the entry's
/// field and the previous value is archived via <see cref="OmniKeyVault.Application.BackupService"/>.
///
/// Implementations must:
///   - Be platform-API-only (no UI): the EditorWindow owns the UI thread.
///   - Return a <see cref="RotationResult"/> with the new value, the old
///     value's identifier (for archival), and an optional "user-visible note"
///     explaining the rotation (e.g. "Old API key revoked at OpenAI").
///   - Never log or surface the old or new value in <c>ex.Message</c> on failure.
/// </summary>
public interface IPlatformRotator
{
    /// <summary>Stable platform id (e.g. <c>"openai"</c>, <c>"github"</c>).
    /// Must match the <c>platform_id</c> on the entry for the rotator to be
    /// applicable.</summary>
    string PlatformId { get; }

    /// <summary>Human-readable display name for the UI (e.g. "OpenAI API Key").</summary>
    string DisplayName { get; }

    /// <summary>The entry field key this rotator produces (e.g. <c>"api_key"</c>,
    /// <c>"token"</c>). The EditorWindow only shows the "Rotate" button when
    /// the entry has a field with this key.</summary>
    string FieldKey { get; }

    /// <summary>Perform the rotation. <paramref name="currentValue"/> is the
    /// field's current plaintext. The implementation calls the platform's API
    /// and returns the new value (already in plaintext, ready to be written
    /// to the field). The old value is returned in <see cref="RotationResult.OldValue"/>
    /// for archival; the implementation should revoke it on the platform side
    /// if possible.</summary>
    /// <exception cref="NotSupportedException">When this rotator cannot handle
    /// the entry (e.g. missing required configuration).</exception>
    /// <exception cref="PlatformApiException">When the platform API call fails.</exception>
    Task<RotationResult> RotateAsync(string currentValue, CancellationToken ct = default);
}

/// <summary>Outcome of a successful rotation.</summary>
public sealed class RotationResult
{
    /// <summary>The new plaintext value to write to the field.</summary>
    public required string NewValue { get; init; }

    /// <summary>The previous plaintext value (now revoked), archived to the
    /// entry's history snapshot for recovery. May be the same as
    /// <see cref="NewValue"/> for idempotent rotations (e.g. tests).</summary>
    public required string OldValue { get; init; }

    /// <summary>Optional human-readable note to show in the success toast
    /// (e.g. "OpenAI: old key revoked, new key valid").</summary>
    public string? Note { get; init; }

    /// <summary>Whether the old value was actually revoked on the platform
    /// side. <c>false</c> for rotators that can't revoke (e.g. read-only
    /// APIs) or for "rotate-by-replace" workflows where revocation happens
    /// asynchronously.</summary>
    public bool OldValueRevoked { get; init; } = true;
}

/// <summary>Thrown by <see cref="IPlatformRotator.RotateAsync"/> when the
/// platform API call fails. The message is sanitized — it does not contain
/// the old or new value.</summary>
public sealed class PlatformApiException : Exception
{
    public string PlatformId { get; }
    public int? StatusCode { get; init; }

    public PlatformApiException(string platformId, string message) : base(message)
        => PlatformId = platformId;

    public PlatformApiException(string platformId, string message, Exception inner) : base(message, inner)
        => PlatformId = platformId;
}
