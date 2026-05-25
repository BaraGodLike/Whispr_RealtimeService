namespace Application.Exceptions;

public sealed class RelayMessageNotFoundException : Exception
{
    public RelayMessageNotFoundException(string message)
        : base(message)
    {
    }
}
