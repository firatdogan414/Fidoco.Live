using Microsoft.AspNetCore.Http;

namespace Fidoco.SharedKernel.Http;

/// <summary>
/// Error responses are ProblemDetails with a machine-readable "code" extension. The frontend translates
/// codes (errors.&lt;code&gt;), so the API never returns user-facing text.
/// </summary>
public static class ApiResults
{
    public static IResult Problem(int statusCode, string code, string? detail = null) =>
        TypedResults.Problem(
            statusCode: statusCode,
            title: code,
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static IResult NotFound(string code = "not_found") => Problem(StatusCodes.Status404NotFound, code);

    public static IResult Conflict(string code) => Problem(StatusCodes.Status409Conflict, code);

    public static IResult Forbidden(string code) => Problem(StatusCodes.Status403Forbidden, code);

    public static IResult BadRequest(string code) => Problem(StatusCodes.Status400BadRequest, code);
}

/// <summary>Small explicit validation helper: collect field errors, return one 400 with code "validation_failed".</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = [];

    public bool HasErrors => _errors.Count > 0;

    public ValidationErrors Add(string field, string code)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            _errors[field] = list = [];
        }

        list.Add(code);
        return this;
    }

    public ValidationErrors Required(string field, string? value, int maxLength = 0)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Add(field, "required");
        }

        return maxLength > 0 && value.Length > maxLength ? Add(field, "too_long") : this;
    }

    public ValidationErrors MaxLength(string field, string? value, int maxLength) =>
        value is not null && value.Length > maxLength ? Add(field, "too_long") : this;

    public ValidationErrors Email(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Add(field, "required");
        }

        value = value.Trim();
        var at = value.IndexOf('@', StringComparison.Ordinal);
        var valid = value.Length <= 254 && at > 0 && at < value.Length - 3 && value.IndexOf('.', at) > at + 1 && !value.Contains(' ', StringComparison.Ordinal);
        return valid ? this : Add(field, "invalid_email");
    }

    public IResult ToResult() =>
        TypedResults.ValidationProblem(
            _errors.ToDictionary(e => e.Key, e => e.Value.ToArray()),
            title: "validation_failed",
            extensions: new Dictionary<string, object?> { ["code"] = "validation_failed" });
}

public static class Emails
{
    /// <summary>Canonical form used for lookups and uniqueness.</summary>
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
