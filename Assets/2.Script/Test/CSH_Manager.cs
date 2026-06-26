using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class CSH_Manager : MonoBehaviour
{
    public static CSH_Manager Instance;

    [SerializeField] private Transform _plr;
    [SerializeField] private GameObject _enemy;
    [SerializeField] private Animator animator;
    [SerializeField] private GameObject inGame;
    [SerializeField] private TextMeshProUGUI partAmoutText;

    private readonly List<FacePart> partList = new List<FacePart>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    public void AddFacePart(FacePart part)
    {
        partList.Add(part);

        if (NetworkGameManager.Instance != null && NetworkGameManager.Instance.IsServer)
            NetworkGameManager.Instance.SpawnEnemyNear(GetSpawnOrigin());
        else if (NetworkGameManager.Instance == null)
            Clone(_enemy);

        Check();
    }

    private Vector3 GetSpawnOrigin()
    {
        if (_plr != null)
            return _plr.position;

        PlayerMovement localPlayer = FindFirstObjectByType<PlayerMovement>();
        return localPlayer != null ? localPlayer.transform.position : transform.position;
    }

    private void Clone(GameObject gameObject)
    {
        if (gameObject == null)
            return;

        GameObject enemy = Instantiate(gameObject, null);
        Vector3 origin = GetSpawnOrigin();
        enemy.transform.position = origin + new Vector3(Random.Range(5, 20), enemy.transform.position.y, Random.Range(5, 20));
    }

    private void Check()
    {
        int targetAmout = 5;

        if (partAmoutText != null)
            partAmoutText.text = $"part:{partList.Count}/{targetAmout}";

        if (partList.Count >= targetAmout)
        {
            if (partAmoutText != null)
                partAmoutText.gameObject.SetActive(false);

            if (inGame != null)
                inGame.SetActive(false);

            if (animator != null)
                animator.Play("CutScene");
        }
    }
}

public enum FacePart
{
    Eye,
    Nose,
    Ear,
    Mouth
}
