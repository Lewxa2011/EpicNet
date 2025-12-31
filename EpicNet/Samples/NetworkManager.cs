using EpicNet;
using UnityEngine;

public class NetworkManager : EpicMonoBehaviourCallbacks
{
    public static NetworkManager Instance;

    public Transform localHead;
    public Transform localLeftHand;
    public Transform localRightHand;

    private string cachedName;
    private GameObject localPlayerInstance;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }

        cachedName = PlayerPrefs.GetString("epic_device_name", "Player" + Random.Range(1000, 9999));
        PlayerPrefs.SetString("epic_device_name", cachedName);
    }

    private void Start()
    {
        EpicNetwork.LoginWithDeviceId(cachedName, (success, message) =>
        {
            if (success)
            {
                Debug.Log($"Login successful: {message}");
                OnLogin();
            }
            else
            {
                Debug.LogError($"Login failed: {message}");
            }
        });
    }

    private void Update()
    {
        EpicNetwork.Update();
    }

    private void OnLogin()
    {
        EpicNetwork.NickName = cachedName;
        EpicNetwork.ConnectUsingSettings();
        EpicNetwork.JoinRandomRoom();
    }

    public override void OnConnectedToMaster()
    {
        base.OnConnectedToMaster();
        Debug.Log("Connected to EOS Master");
    }

    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();
        Debug.Log($"Joined room. IsMasterClient: {EpicNetwork.IsMasterClient}");

        SpawnLocalPlayer();
    }

    public override void OnPlayerEnteredRoom(EpicPlayer newPlayer)
    {
        base.OnPlayerEnteredRoom(newPlayer);
        Debug.Log($"Player entered room: {newPlayer.NickName}");
    }

    public override void OnPlayerLeftRoom(EpicPlayer otherPlayer)
    {
        base.OnPlayerLeftRoom(otherPlayer);
        Debug.Log($"Player left room: {otherPlayer.NickName}");
    }

    public override void OnMasterClientSwitched(EpicPlayer newMaster)
    {
        base.OnMasterClientSwitched(newMaster);
        Debug.Log($"Master client switched! New master: {EpicNetwork.MasterClient?.NickName}");
        Debug.Log($"Am I the master? {EpicNetwork.IsMasterClient}");

        if (EpicNetwork.IsMasterClient)
        {
            Debug.Log("I am now the master client!");

            OnBecameMasterClient();
        }
    }

    private void SpawnLocalPlayer()
    {
        if (localPlayerInstance == null)
        {
            localPlayerInstance = EpicNetwork.Instantiate("Net Player", Vector3.zero, Quaternion.identity);
            Debug.Log("Local player spawned");
        }
    }

    private void OnBecameMasterClient()
    {
        Debug.Log("Taking over master client responsibilities...");
    }

    public override void OnLeftRoom()
    {
        base.OnLeftRoom();
        Debug.Log("Left room");

        if (localPlayerInstance != null)
        {
            Destroy(localPlayerInstance);
            localPlayerInstance = null;
        }
    }
}