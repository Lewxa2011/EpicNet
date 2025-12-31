using EpicNet;
using UnityEngine;

public class EpicStats : MonoBehaviour
{
    [SerializeField] private string log;

    public void Start()
    {
        EpicView ev = GetComponent<EpicView>();
        log = $"ViewID: {ev.ViewID}, Owner: {ev.Owner?.NickName}, IsMine: {ev.IsMine}";
    }
}