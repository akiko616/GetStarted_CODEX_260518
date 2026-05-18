using FishNet.Connection;
using FishNet.Object;
using UnityEngine;


namespace TRAINEE
{
    public class EquipMeleeView : EquipBaseView
    {
        public override Transform LeftHand => base.LeftHand;
        public override Transform LeftHandHint => base.LeftHandHint;

        public override void Init(string itemid, string instanceid, string ownerId)
        {
            Debug.Log($"EquipMeleeView Init - id : {itemid} / insid : {instanceid} / ownerid : {ownerId}");
            base.Init(itemid, instanceid, ownerId);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            Debug.Log($"EquipMeleeView  id : {_itemid.Value} / insid : {_instanceId.Value} / ownerid : {_ownerId.Value}");

            base.OnRegisterEvent();

            if (string.IsNullOrEmpty(_ownerId.Value))
            {
#if !GAMEINSTANCE
                ItemManager.Instance.OnRegisterItem(this);
#endif
            }
            else
            {
#if !GAMEINSTANCE
                // 늦게 입장한 교관석도 이 아이템을 자기 장부(Pool)에 넣어둬야 나중에 렌더링할 수 있습니다.
                ItemManager.Instance.RegisterNetworkItemToClient(this.NetworkObject, _itemid.Value, _instanceId.Value);
#endif
            }
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
        }

        [Server]
        public override void ToDropState()
        {
            base.ToDropState();
        }
        [Server]
        public override void ToEquipState()
        {
            base.ToEquipState();
        }


        public override void OnDespawnServer(NetworkConnection connection)
        {
            base.OnDespawnServer(connection);
            base.OnUnRegisterEvent();
        }
    }
}
