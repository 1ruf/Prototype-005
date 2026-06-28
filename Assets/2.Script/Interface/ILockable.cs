public interface ILockable
{
    bool IsLocked { get; }
    void RequestSetLocked(bool locked);
}
