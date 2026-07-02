namespace HousingPolicy.Api.Services;

/// <summary>Non-recoverable upstream error (unexpected status, malformed body). Maps to HTTP 502.</summary>
public class CongressApiException : Exception
{
    public CongressApiException(string message) : base(message) { }
    public CongressApiException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The bill does not exist upstream (HTTP 404). Maps to HTTP 404.</summary>
public sealed class BillNotFoundException : CongressApiException
{
    public BillNotFoundException(string message) : base(message) { }
}

/// <summary>Rate limit not cleared after retries (HTTP 429). Maps to HTTP 429.</summary>
public sealed class RateLimitedException : CongressApiException
{
    public RateLimitedException(string message) : base(message) { }
}
