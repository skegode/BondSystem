namespace OnwardsSwift.API.MobileLocal.Contracts;

public class ApiEnvelope<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public string? ErrorCode { get; set; }

    public static ApiEnvelope<T> Ok(T? data, string message = "ok")
        => new() { Success = true, Message = message, Data = data };

    public static ApiEnvelope<T> Fail(string message, string errorCode)
        => new() { Success = false, Message = message, ErrorCode = errorCode };
}
