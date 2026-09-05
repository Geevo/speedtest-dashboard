using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Infrastructure.Persistence;

public interface ISpeedTestPersistenceWriter
{
    void PersistCreated(SpeedTestJob job);
    void PersistTransition(SpeedTestJob job);
    void DeleteQueued(Guid jobId);
}
