using System.Collections.Generic;
using UnityEngine;

public class MenuAnimationController : MonoBehaviour
{
    [SerializeField] private List<AudioSource> audios;

    public void PlayAudio(int index) => audios[index].Play();
}
