using System.Globalization;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Domain.Users;

namespace IncidentHub.Domain.Incidents;

/// <summary>
/// The core aggregate. <see cref="Status"/> changes only through <see cref="TransitionTo"/>; every change appends
/// <see cref="IncidentHistory"/> rows and raises a domain event. Times are passed in (the domain has no clock).
/// </summary>
public sealed class Incident : AggregateRoot
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 20_000;
    public const int StepsMaxLength = 5_000;
    public const int NoteMaxLength = IncidentHistory.NoteMaxLength;

    /// <summary>History <c>Field</c> values of a comment; internal ones are hidden from non-responders.</summary>
    public const string CommentHistoryField = "Comment";
    public const string InternalCommentHistoryField = "InternalComment";

    private readonly List<IncidentHistory> _history = [];

    private Incident()
    {
    }

    /// <summary>Assigned by the database sequence on insert; <c>0</c> until then.</summary>
    public int Number { get; private set; }

    public Guid ApplicationId { get; private set; }

    public AppEnvironment Environment { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public string? StepsToReproduce { get; private set; }

    public Severity Severity { get; private set; }

    public Priority? Priority { get; private set; }

    public IncidentStatus Status { get; private set; }

    public IncidentSource Source { get; private set; }

    public Guid ReporterId { get; private set; }

    public Guid? AssigneeId { get; private set; }

    public Guid? AssignedTeamId { get; private set; }

    public string? Fingerprint { get; private set; }

    public string? ExternalId { get; private set; }

    public DateTimeOffset? AckDueAt { get; private set; }

    public DateTimeOffset? ResolveDueAt { get; private set; }

    public bool IsSlaBreached { get; private set; }

    public DateTimeOffset? AcknowledgedAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public CloseReason? CloseReason { get; private set; }

    public Guid? DuplicateOfId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<IncidentHistory> History => _history;

    public static Incident Report(
        Guid applicationId,
        AppEnvironment environment,
        Severity severity,
        string title,
        string description,
        string? stepsToReproduce,
        IncidentSource source,
        Actor reporter,
        DateTimeOffset at,
        string? externalId = null)
    {
        ArgumentNullException.ThrowIfNull(reporter);
        ValidateApplicationId(applicationId);
        ValidateTitle(title);
        ValidateDescription(description);
        ValidateSteps(stepsToReproduce);

        var incident = new Incident
        {
            ApplicationId = applicationId,
            Environment = environment,
            Severity = severity,
            Title = title,
            Description = description,
            StepsToReproduce = stepsToReproduce,
            Status = IncidentStatus.New,
            Source = source,
            ReporterId = reporter.UserId,
            ExternalId = externalId,
            Fingerprint = IncidentFingerprint.Compute(applicationId, title),
            CreatedAt = at,
            UpdatedAt = at,
        };

        incident.AddHistory(nameof(Status), null, IncidentStatus.New, null, ActorIdOf(reporter), at);
        incident.Raise(new IncidentReported(incident.Id, applicationId, environment, severity, source, reporter.UserId, at));
        return incident;
    }

    /// <summary>Moves the incident along the workflow. The only way to change <see cref="Status"/>.</summary>
    /// <exception cref="InvalidTransitionException">The move is not in the transition table.</exception>
    /// <exception cref="TransitionNotPermittedException">The actor may not make this move.</exception>
    /// <exception cref="TransitionRequirementException">A required note, close reason or duplicate target is missing or inconsistent.</exception>
    public void TransitionTo(
        IncidentStatus to,
        Actor actor,
        string? note,
        DateTimeOffset at,
        CloseReason? closeReason = null,
        Guid? duplicateOfId = null)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var from = Status;
        if (!IncidentWorkflow.IsDefined(from, to))
        {
            throw new InvalidTransitionException(from, to);
        }

        if (!IncidentWorkflow.IsAllowed(from, to, actor, this))
        {
            throw new TransitionNotPermittedException(from, to);
        }

        ValidateTransitionInputs(from, to, note, closeReason, duplicateOfId);

        var actorId = ActorIdOf(actor);
        Status = to;
        UpdatedAt = at;
        AddHistory(nameof(Status), from, to, note, actorId, at);

        if (from == IncidentStatus.New)
        {
            AcknowledgedAt ??= at;
        }

        if (to == IncidentStatus.Resolved)
        {
            ResolvedAt = at;
        }
        else if (to == IncidentStatus.Closed)
        {
            ClosedAt = at;
            CloseReason = closeReason;
            DuplicateOfId = duplicateOfId;
            AddHistory(nameof(CloseReason), null, closeReason, null, actorId, at);
            if (duplicateOfId is not null)
            {
                AddHistory(nameof(DuplicateOfId), null, duplicateOfId, null, actorId, at);
            }
        }

        Raise(new IncidentTransitioned(Id, from, to, actorId, note, at));
    }

    /// <summary>
    /// Adds a comment and records it in the history. Allowed on a closed incident; deliberately leaves
    /// <see cref="UpdatedAt"/> alone so a comment never makes another editor's <c>If-Match</c> stale.
    /// </summary>
    /// <exception cref="CommentNotPermittedException">An internal comment by an actor that is not a responder.</exception>
    public Comment AddComment(string body, bool isInternal, Actor actor, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(body.Length, Comment.BodyMaxLength);
        if (actor.IsSystem)
        {
            throw new InvalidOperationException("Comments are written by users, not by the system.");
        }

        if (isInternal && !IncidentWorkflow.IsResponder(actor))
        {
            throw new CommentNotPermittedException();
        }

        var comment = Comment.Create(Id, actor.UserId, body, isInternal, at);
        AddHistory(isInternal ? InternalCommentHistoryField : CommentHistoryField, null, comment.Id, null, actor.UserId, at);
        Raise(new CommentAdded(Id, comment.Id, isInternal, actor.UserId, at));
        return comment;
    }

    /// <summary>Replaces assignee and team. Does nothing (no history, no event) when neither changes.</summary>
    public void Assign(Guid? assigneeId, Guid? teamId, Actor actor, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureNotClosed();
        if (assigneeId == Guid.Empty)
        {
            throw new ArgumentException("Assignee id cannot be empty.", nameof(assigneeId));
        }

        if (teamId == Guid.Empty)
        {
            throw new ArgumentException("Team id cannot be empty.", nameof(teamId));
        }

        if (assigneeId == AssigneeId && teamId == AssignedTeamId)
        {
            return;
        }

        var actorId = ActorIdOf(actor);
        var previousAssigneeId = AssigneeId;
        var previousTeamId = AssignedTeamId;

        if (assigneeId != previousAssigneeId)
        {
            AddHistory(nameof(AssigneeId), previousAssigneeId, assigneeId, null, actorId, at);
            AssigneeId = assigneeId;
        }

        if (teamId != previousTeamId)
        {
            AddHistory(nameof(AssignedTeamId), previousTeamId, teamId, null, actorId, at);
            AssignedTeamId = teamId;
        }

        UpdatedAt = at;
        Raise(new IncidentAssigned(Id, assigneeId, teamId, previousAssigneeId, previousTeamId, actorId, at));
    }

    /// <summary>Applies a partial edit. Severity has its own method because it needs a reason and new SLA due times.</summary>
    public void Update(IncidentChanges changes, Actor actor, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(actor);
        EnsureNotClosed();

        if (changes.Title.HasValue)
        {
            ValidateTitle(changes.Title.Value);
        }

        if (changes.Description.HasValue)
        {
            ValidateDescription(changes.Description.Value);
        }

        if (changes.StepsToReproduce.HasValue)
        {
            ValidateSteps(changes.StepsToReproduce.Value);
        }

        if (changes.ApplicationId.HasValue)
        {
            ValidateApplicationId(changes.ApplicationId.Value);
        }

        var actorId = ActorIdOf(actor);
        var changed = new List<string>();

        void Apply<T>(Optional<T> change, T current, Action<T> assign, string field)
        {
            if (!change.HasValue || EqualityComparer<T>.Default.Equals(change.Value, current))
            {
                return;
            }

            AddHistory(field, current, change.Value, null, actorId, at);
            assign(change.Value);
            changed.Add(field);
        }

        Apply(changes.Title, Title, v => Title = v, nameof(Title));
        Apply(changes.Description, Description, v => Description = v, nameof(Description));
        Apply(changes.StepsToReproduce, StepsToReproduce, v => StepsToReproduce = v, nameof(StepsToReproduce));
        Apply(changes.Priority, Priority, v => Priority = v, nameof(Priority));
        Apply(changes.ApplicationId, ApplicationId, v => ApplicationId = v, nameof(ApplicationId));
        Apply(changes.Environment, Environment, v => Environment = v, nameof(Environment));

        if (changed.Count == 0)
        {
            return;
        }

        if (changed.Contains(nameof(Title)) || changed.Contains(nameof(ApplicationId)))
        {
            Fingerprint = IncidentFingerprint.Compute(ApplicationId, Title);
        }

        UpdatedAt = at;
        Raise(new IncidentUpdated(Id, changed, actorId, at));
    }

    /// <summary>
    /// Changes the severity and the SLA due times that go with it. The caller computes the due times
    /// (<c>SlaClock</c>, never in a controller) and passes them in.
    /// </summary>
    public void ChangeSeverity(
        Severity severity,
        string reason,
        DateTimeOffset ackDueAt,
        DateTimeOffset resolveDueAt,
        Actor actor,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(reason.Length, NoteMaxLength);
        EnsureNotClosed();

        if (severity == Severity)
        {
            return;
        }

        var actorId = ActorIdOf(actor);
        AddHistory(nameof(Severity), Severity, severity, reason, actorId, at);
        Severity = severity;
        AckDueAt = ackDueAt;
        ResolveDueAt = resolveDueAt;
        UpdatedAt = at;
        Raise(new IncidentUpdated(Id, [nameof(Severity)], actorId, at));
    }

    /// <summary>Sets the SLA due times when the incident is created. Not an auditable change, so no history or event.</summary>
    public void SetSlaDueTimes(DateTimeOffset ackDueAt, DateTimeOffset resolveDueAt)
    {
        AckDueAt = ackDueAt;
        ResolveDueAt = resolveDueAt;
    }

    /// <summary>Flags the resolve SLA as breached. Idempotent; recorded as a system action.</summary>
    public void MarkSlaBreached(DateTimeOffset at)
    {
        if (IsSlaBreached)
        {
            return;
        }

        IsSlaBreached = true;
        UpdatedAt = at;
        AddHistory(nameof(IsSlaBreached), false, true, null, ActorIdOf(Actor.System), at);
        Raise(new IncidentUpdated(Id, [nameof(IsSlaBreached)], null, at));
    }

    private static Guid? ActorIdOf(Actor actor) => actor.IsSystem ? null : actor.UserId;

    private static void ValidateApplicationId(Guid applicationId)
    {
        if (applicationId == Guid.Empty)
        {
            throw new ArgumentException("Application is required.", nameof(applicationId));
        }
    }

    private static void ValidateTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(title.Length, TitleMaxLength);
    }

    private static void ValidateDescription(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(description.Length, DescriptionMaxLength);
    }

    private static void ValidateSteps(string? steps)
    {
        if (steps is not null)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(steps.Length, StepsMaxLength);
        }
    }

    private static string? Format(object? value) => value switch
    {
        null => null,
        Guid guid => guid.ToString("D"),
        DateTimeOffset time => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private void EnsureNotClosed()
    {
        if (Status == IncidentStatus.Closed)
        {
            throw new IncidentClosedException();
        }
    }

    private void ValidateTransitionInputs(
        IncidentStatus from,
        IncidentStatus to,
        string? note,
        CloseReason? closeReason,
        Guid? duplicateOfId)
    {
        if (note is { Length: > NoteMaxLength })
        {
            throw new TransitionRequirementException("note", $"Note must be at most {NoteMaxLength} characters.");
        }

        if (to is IncidentStatus.Resolved or IncidentStatus.Reopened && string.IsNullOrWhiteSpace(note))
        {
            throw new TransitionRequirementException("note", $"A note is required when moving to {to}.");
        }

        if (to == IncidentStatus.Closed)
        {
            if (closeReason is null)
            {
                throw new TransitionRequirementException("closeReason", "A close reason is required when closing an incident.");
            }

            if (from == IncidentStatus.New && closeReason is not (Incidents.CloseReason.Duplicate or Incidents.CloseReason.Invalid))
            {
                throw new TransitionRequirementException("closeReason", "A new incident can only be closed as Duplicate or Invalid.");
            }

            if (from == IncidentStatus.Resolved && closeReason == Incidents.CloseReason.Duplicate)
            {
                throw new TransitionRequirementException("closeReason", "A resolved incident cannot be closed as Duplicate.");
            }
        }
        else if (closeReason is not null)
        {
            throw new TransitionRequirementException("closeReason", "A close reason is only valid when closing an incident.");
        }

        if (closeReason == Incidents.CloseReason.Duplicate)
        {
            if (duplicateOfId is null || duplicateOfId == Guid.Empty)
            {
                throw new TransitionRequirementException("duplicateOfId", "The original incident is required when closing as Duplicate.");
            }

            if (duplicateOfId == Id)
            {
                throw new TransitionRequirementException("duplicateOfId", "An incident cannot be a duplicate of itself.");
            }
        }
        else if (duplicateOfId is not null)
        {
            throw new TransitionRequirementException("duplicateOfId", "The original incident is only valid when closing as Duplicate.");
        }
    }

    private void AddHistory(string field, object? oldValue, object? newValue, string? note, Guid? actorId, DateTimeOffset at) =>
        _history.Add(IncidentHistory.Create(Id, field, Format(oldValue), Format(newValue), note, actorId, at));
}
