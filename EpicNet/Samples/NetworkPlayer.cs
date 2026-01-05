using EpicNet;
using UnityEngine;

public class NetworkPlayer : MonoBehaviour
{
    [SerializeField] private Transform head;
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;

    private EpicView view;

    private void Start()
    {
        view = GetComponent<EpicView>();
    }

    void Update()
    {
        if (!view.IsMine) return;

        SyncTransform(NetworkManager.Instance.localHead, head);
        SyncTransform(NetworkManager.Instance.localLeftHand, leftHand);
        SyncTransform(NetworkManager.Instance.localRightHand, rightHand);
    }

    void SyncTransform(Transform from, Transform to)
    {
        to.position = from.position;
        to.rotation = from.rotation;
    }
}