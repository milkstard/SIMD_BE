using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Common.Exceptions;

/// <summary>
/// A unique index rejected the write. Raised by the DbContext (subclass of <see cref="DbUpdateException"/> so existing
/// catch blocks keep working); handlers translate it into a <see cref="ConflictException"/> with a useful message.
/// </summary>
public sealed class DuplicateKeyException(string message, Exception inner) : DbUpdateException(message, inner);
