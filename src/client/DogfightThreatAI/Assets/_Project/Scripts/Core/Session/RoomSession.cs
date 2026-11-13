using System.Collections.Generic;

namespace DogfightThreatAI.Core.Session
{
    /// <summary>一场会话的成员、准备与房主规则，不依赖网络库或 Unity。</summary>
    public sealed class RoomSession
    {
        private readonly List<int> members = new List<int>();
        private readonly HashSet<int> readyMembers = new HashSet<int>();

        // 连接编号只在本次会话内使用，不是账号或重连凭证。
        public int? Owner => members.Count == 0 ? (int?)null : members[0];
        public int Count => members.Count;
        public int ReadyCount => readyMembers.Count;
        public bool Contains(int member)
        {
            return members.Contains(member);
        }

        public bool IsOwner(int member)
        {
            return Owner == member;
        }

        public bool Join(int member)
        {
            if (Contains(member))
            {
                return false;
            }
            members.Add(member);
            return true;
        }

        public void Leave(int member)
        {
            members.Remove(member);
            readyMembers.Remove(member);
        }

        public bool SetReady(int member, bool ready)
        {
            if (!Contains(member))
            {
                return false;
            }
            if (ready)
            {
                readyMembers.Add(member);
            }
            else
            {
                readyMembers.Remove(member);
            }
            return true;
        }

        public bool CanStart(int requester, int minimumPlayers)
        {
            return minimumPlayers > 0 && IsOwner(requester) && Count >= minimumPlayers && ReadyCount == Count;
        }

        public void ResetReady()
        {
            readyMembers.Clear();
        }

        public void Clear()
        {
            members.Clear();
            readyMembers.Clear();
        }
    }
}
