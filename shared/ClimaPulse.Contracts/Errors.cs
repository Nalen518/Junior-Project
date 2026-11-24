namespace ClimaPulse.Contracts;

// Services throw only these types on purpose. Anything else is a bug.
// Messages never contain SQL, stack traces or file paths (contract section 7).

/// <summary>Base type of every error a service is allowed to throw on purpose.</summary>
public abstract class ClimaPulseException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>Input broke a rule. Errors maps a property name to its messages.</summary>
public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : ClimaPulseException("One or more inputs are invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>An id does not exist.</summary>
public sealed class NotFoundException(string entity, int id)
    : ClimaPulseException($"{entity} {id} was not found.")
{
    public string Entity { get; } = entity;
    public int Id { get; } = id;
}

/// <summary>Duplicate, or an operation already running (for example a baseline refresh).</summary>
public sealed class ConflictException(string message) : ClimaPulseException(message);

/// <summary>Open-Meteo failed, timed out, returned nothing usable, or rate-limited us.</summary>
public sealed class UpstreamException(string message, int? cityId = null, Exception? inner = null)
    : ClimaPulseException(message, inner)
{
    public int? CityId { get; } = cityId;
}
