namespace Qvd;

/// <summary>Thrown when a file does not conform to the QVD format this library understands.</summary>
public class QvdFormatException : Exception
{
    public QvdFormatException(string message)
        : base(message)
    {
    }

    public QvdFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
