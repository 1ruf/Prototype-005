using System.Collections.Generic;
using UnityEngine;

public sealed class CSHGameController : GameControllerBase
{
    private const int TargetPartAmount = 5;

    private readonly CSH_Manager host;
    private readonly List<FacePart> partList = new List<FacePart>();

    public CSHGameController(CSH_Manager host)
    {
        this.host = host;
    }

    public override GameControllerBase Init()
    {
        return this;
    }

    public void AddFacePart(FacePart part)
    {
        partList.Add(part);

        if (NetworkGameManager.Instance != null && NetworkGameManager.Instance.IsServer)
            NetworkGameManager.Instance.SpawnEnemyNear(GetSpawnOrigin());
        else if (NetworkGameManager.Instance == null)
            CloneEnemy();

        CheckCompletion();
    }

    private Vector3 GetSpawnOrigin()
    {
        if (host == null)
            return Vector3.zero;

        if (host.Player != null)
            return host.Player.position;

        PlayerMovement localPlayer = Object.FindFirstObjectByType<PlayerMovement>();
        return localPlayer != null ? localPlayer.transform.position : host.transform.position;
    }

    private void CloneEnemy()
    {
        if (host == null || host.EnemyPrefab == null)
            return;

        Vector3 origin = GetSpawnOrigin();
        Vector3 position = origin + new Vector3(Random.Range(5, 20), host.EnemyPrefab.transform.position.y, Random.Range(5, 20));
        GameObject enemy = GameObjectPoolManager.Spawn(host.EnemyPrefab, position, host.EnemyPrefab.transform.rotation);
        if (enemy == null)
            return;

        enemy.transform.position = position;
    }

    private void CheckCompletion()
    {
        if (host == null)
            return;

        if (host.PartAmountText != null)
            host.PartAmountText.text = $"part:{partList.Count}/{TargetPartAmount}";

        if (partList.Count < TargetPartAmount)
            return;

        if (host.PartAmountText != null)
            host.PartAmountText.gameObject.SetActive(false);

        if (host.InGame != null)
            host.InGame.SetActive(false);

        if (host.Animator != null)
            host.Animator.Play("CutScene");
    }
}
