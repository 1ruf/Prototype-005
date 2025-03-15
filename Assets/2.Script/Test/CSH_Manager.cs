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

    
    List<FacePart> partList = new List<FacePart>();

    private void Awake()
    {
        if(Instance != null)
            Destroy(this);
        else
            Instance = this;
    }
    public void AddFacePart(FacePart part)
    {
        partList.Add(part);
        Clone(_enemy);
        Check();
    }

    private void Clone(GameObject gameObject)
    {
        GameObject enemy = Instantiate(gameObject,null);
        enemy.transform.position = _plr.position + new Vector3(Random.Range(5, 20), enemy.transform.position.y, Random.Range(5, 20)); 
    }
    private void Check()
    {
        int targetAmout = 5;
        partAmoutText.text = $"part:{partList.Count}/{targetAmout}";
        if (partList.Count >= targetAmout)
        {
            partAmoutText.gameObject.SetActive(false);
            inGame.SetActive(false);
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
