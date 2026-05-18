using Cysharp.Threading.Tasks;
using DatabasePlugin;
using NetworkResponeData;
using System;
using System.Collections.Generic;
using UnityEngine;


namespace TRAINEE
{ 
    public partial class NetworkManager
    {
        private Dictionary<string, OnlineUserInfo> cachedOnlineUsers = new Dictionary<string, OnlineUserInfo>();
        
        private bool OnUpdateOnlineUser(ResAdminUserLoginNotification user)
        {
            Debug.Log($"[DrillInstructorClient] 사용자 로그인: {user.account.Id}");

            cachedOnlineUsers[user.account.Id] = new OnlineUserInfo
            {
                AccountId = user.account.Id,
                ClientId = user.clientId,
                AccountType = user.account.AccountType,
                LoginTime = DateTime.Now
            };

            return true;
        }

        private bool OnUpdateOnlineUser(ResAdminUserLogOutNotification user)
        {
            if(cachedOnlineUsers.ContainsKey(user.accountId))
            {
                Debug.Log($"[DrillInstructorClient] 사용자 로그아웃: {user.accountId}");

                cachedOnlineUsers.Remove(user.accountId);
                return true;
            }

            Debug.Log($"[DrillInstructorClient] 사용자 로그아웃 삭제 실패: {user.accountId}");
            return false;
        }

        private bool OnUpdateAllOnlineUser(ResAdminGetOnlineUsers users)
        {
            List<OnlineUserInfo> newUsers = new List<OnlineUserInfo>();

            for(int i = 0; i<users.users.Count; i++)
            {
                newUsers.Add(users.users[i]);
            }

            if (cachedOnlineUsers != null)
            {
                cachedOnlineUsers.Clear();
            }

            for(int i = 0; i<newUsers.Count; i++)
            {
                cachedOnlineUsers[newUsers[i].AccountId] = newUsers[i];
            }

            return true;
        }

        
        public async UniTask SergeantConnectFishNetAsync(string address, int port)
        {
            if (_fishNetManager == null)
            {
                if (!OnFindToFishNet())
                {
                    Debug.LogError("FishNet Manager를 찾을 수 없습니다.");
                    return;
                }
            }

            Debug.Log($"[Sergeant] 새로운 피쉬넷 서버 접속 시도: {address}:{port}");
            OnSetTransportAddress(_fishNetManager.TransportManager.Transport, address, (ushort)port);
            RegisterFishNetBroadcasts();

            _fishNetManager.ClientManager.StartConnection();

            await UniTask.WaitUntil(() => _isConnectedToFishNet == true);
        }

        
    }
}
