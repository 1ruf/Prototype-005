public abstract class GameControllerBase
{
    public abstract GameControllerBase Init();
    public virtual void Update() { }
    public virtual void FixedUpdate() { }
    public virtual void OnStageReset() { }
    public virtual void OnDestroy() { }
}
