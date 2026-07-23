namespace Template.Api.Contracts;

internal sealed record ApiResponse<T>(T Data) where T : notnull;
