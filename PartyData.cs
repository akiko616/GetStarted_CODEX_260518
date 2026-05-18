using DarkRift;
using System;

namespace PartyManagerPlugin
{
    /// <summary>
    /// 파티 데이터
    /// </summary>
    [Serializable]
    public class Party : IDarkRiftSerializable
    {
        public int PartyId { get; set; }
        public string SceneFile { get; set; }
        public string SceneSetFile { get; set; }
        public int MaxPlayer { get; set; }
        public string PartyName { get; set; }
        public string PartyInfo { get; set; }

        // account.id -> role 매핑
        public System.Collections.Generic.Dictionary<string, string> AccountIdAndRole { get; set; }

        public Party()
        {
            PartyId = 0;
            SceneFile = string.Empty;
            SceneSetFile = string.Empty;
            MaxPlayer = 4;
            PartyName = string.Empty;
            PartyInfo = string.Empty;
            AccountIdAndRole = new System.Collections.Generic.Dictionary<string, string>();
        }

        public Party(int partyId, string sceneFile = "", string sceneSetFile = "", int maxPlayer = 4, string partyName = null, string partyInfo = null)
        {
            PartyId = partyId;
            SceneFile = sceneFile ?? string.Empty;
            SceneSetFile = sceneSetFile ?? string.Empty;
            MaxPlayer = maxPlayer;
            PartyName = partyName ?? string.Empty;
            PartyInfo = partyInfo ?? string.Empty;
            AccountIdAndRole = new System.Collections.Generic.Dictionary<string, string>();
        }

        public void Deserialize(DeserializeEvent e)
        {
            PartyId = e.Reader.ReadInt32();
            SceneFile = e.Reader.ReadString();
            SceneSetFile = e.Reader.ReadString();
            MaxPlayer = e.Reader.ReadInt32();
            PartyName = e.Reader.ReadString();
            PartyInfo = e.Reader.ReadString();

            int count = e.Reader.ReadInt32();
            AccountIdAndRole = new System.Collections.Generic.Dictionary<string, string>(count);

            for (int i = 0; i < count; i++)
            {
                string key = e.Reader.ReadString();
                string value = e.Reader.ReadString();
                AccountIdAndRole[key] = value;
            }
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(PartyId);
            e.Writer.Write(SceneFile);
            e.Writer.Write(SceneSetFile);
            e.Writer.Write(MaxPlayer);
            e.Writer.Write(PartyName);
            e.Writer.Write(PartyInfo);

            e.Writer.Write(AccountIdAndRole.Count);
            foreach (var kvp in AccountIdAndRole)
            {
                e.Writer.Write(kvp.Key);
                e.Writer.Write(kvp.Value);
            }
        }

        /// <summary>파티에 멤버 추가</summary>
        public bool AddMember(string accountId, string role = "")
        {
            if (string.IsNullOrWhiteSpace(accountId))
                return false;

            if (AccountIdAndRole.ContainsKey(accountId))
                return false;

            if (AccountIdAndRole.Count >= MaxPlayer)
                return false;

            AccountIdAndRole[accountId] = role ?? string.Empty;
            return true;
        }

        /// <summary>파티에서 멤버 제거</summary>
        public bool RemoveMember(string accountId)
        {
            return AccountIdAndRole.Remove(accountId);
        }

        /// <summary>멤버의 역할 변경</summary>
        public bool ChangeRole(string accountId, string newRole)
        {
            if (!AccountIdAndRole.ContainsKey(accountId))
                return false;

            AccountIdAndRole[accountId] = newRole ?? string.Empty;
            return true;
        }

        /// <summary>멤버 존재 여부</summary>
        public bool HasMember(string accountId)
        {
            return AccountIdAndRole.ContainsKey(accountId);
        }

        /// <summary>파티 멤버 수</summary>
        public int MemberCount => AccountIdAndRole.Count;

        /// <summary>파티가 비어있는지 확인</summary>
        public bool IsEmpty => AccountIdAndRole.Count == 0;

        /// <summary>파티가 가득 찼는지 확인</summary>
        public bool IsFull => AccountIdAndRole.Count >= MaxPlayer;

        /// <summary>파티에 남은 자리 수</summary>
        public int AvailableSlots => Math.Max(0, MaxPlayer - AccountIdAndRole.Count);

        public override string ToString()
        {
            return $"Party[ID:{PartyId}, Scene:{SceneFile}, Members:{MemberCount}/{MaxPlayer}]";
        }
    }
}
