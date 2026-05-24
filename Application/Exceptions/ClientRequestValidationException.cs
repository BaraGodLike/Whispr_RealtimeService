namespace Application.Exceptions;

public sealed class ClientRequestValidationException : Exception
{
    public ClientRequestValidationException(string message)
        : base(message)
    {
    }
}
