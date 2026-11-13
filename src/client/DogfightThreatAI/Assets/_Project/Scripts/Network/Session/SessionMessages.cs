using Mirror;

// 房间控制消息与高频飞行输入分开。发起者由 Mirror 的连接提供，不能由消息指定。
public enum SessionAction : byte
{
    StartVersus,
    StartCoop,
    ReturnToLobby
}

public struct SessionRequestMessage : NetworkMessage
{
    public SessionAction Action;
}

/// <summary>由服务器逐连接发送的界面状态，不参与服务器权限判断。</summary>
public struct SessionStateMessage : NetworkMessage
{
    public bool IsRoomOwner;
    public bool CanStartMatch;
    public int PlayerCount;
    public int ReadyCount;
}
