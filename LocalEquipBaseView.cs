using TRAINEE;
using UnityEngine;

public class LocalEquipBaseView : MonoBehaviour, IInteractableView
{
    private string _itemid;
    private string _instanceId;
    private ELobbyItemType _type;
    public string InstanceID { get => _instanceId; set => _instanceId = value; }

    public LocalEquipBaseView(string itemID, string instanceID)
    {
        _itemid = itemID;
        _instanceId = instanceID;
    }

    public GameModelEventBase Param
    {
        get
        {
            LobbyItemEventData gameModelEventBase = new LobbyItemEventData();
            gameModelEventBase._id = _itemid;
            gameModelEventBase._instanceId = _instanceId;
            gameModelEventBase._type = _type;
            return gameModelEventBase;

        }
    }
    public Vector3 Anchor { get => transform.position; }


    public virtual void Init(string itemid, string instanceid,ELobbyItemType type)
    {
        _itemid = itemid;
        _instanceId = instanceid;
        _type = type;

    }

}
