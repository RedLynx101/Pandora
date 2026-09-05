namespace Pandora.Core;

/// <summary>The destination no longer matches the snapshot being saved; no replacement was performed.</summary>
public sealed class WorkspaceConflictException(string message) : IOException(message);
