namespace SpeedtestDashboard.Core.History;

public sealed class SpeedTestPersistenceException : Exception
{
    public SpeedTestPersistenceException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
