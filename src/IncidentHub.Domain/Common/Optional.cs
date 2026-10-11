namespace IncidentHub.Domain.Common;

/// <summary>A value that may be "not provided", so a partial update can tell "leave as is" from "set to null".</summary>
public readonly record struct Optional<T>(bool HasValue, T Value)
{
    public static implicit operator Optional<T>(T value) => new(true, value);
}
