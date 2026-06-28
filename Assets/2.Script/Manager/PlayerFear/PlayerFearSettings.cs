using UnityEngine;

[CreateAssetMenu(fileName = "PlayerFearSettings", menuName = "SO/Player/Fear Settings")]
public sealed class PlayerFearSettings : ScriptableObject
{
    [SerializeField] private GameObject soundPrefab;
    [SerializeField] private AudioClip contactClip;
    [SerializeField] private AudioClip runClip;
    [SerializeField] private AudioClip chasingClip;
    [SerializeField] private AudioClip heartbeatAClip;
    [SerializeField] private AudioClip heartbeatBClip;

    public GameObject SoundPrefab => soundPrefab;
    public AudioClip ContactClip => contactClip;
    public AudioClip RunClip => runClip;
    public AudioClip ChasingClip => chasingClip;
    public AudioClip HeartbeatAClip => heartbeatAClip;
    public AudioClip HeartbeatBClip => heartbeatBClip;
}
