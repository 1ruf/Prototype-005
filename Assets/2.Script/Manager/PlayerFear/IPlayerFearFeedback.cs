public interface IPlayerFearFeedback
{
    void Play(PlayerFearThreat threat);
    void PlayChaseStarted(PlayerFearThreat threat);
}
