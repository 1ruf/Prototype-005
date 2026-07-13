/// <summary>
/// 레버가 상태 변경을 전달할 수 있는 대상입니다.
/// 레버는 이 인터페이스 외의 대상 구현 세부 사항을 알 필요가 없습니다.
/// </summary>
public interface ILeverControllable
{
    void SetLeverState(bool isOn);
}

public interface ILeverToggleable
{
    void ToggleLeverState();
}
