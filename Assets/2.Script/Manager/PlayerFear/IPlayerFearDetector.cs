public interface IPlayerFearDetector
{
    bool TryGetVisibleThreat(out PlayerFearThreat threat);
}
