using Mirror;
using UnityEngine;

public class NetworkPracticePlayer : NetworkBehaviour
{
    // 每秒移动的 Unity 世界单位数，可在 Inspector 中调整。
    [SerializeField]
    private float moveSpeed = 3f;

    // 客户端下一次允许发送输入的时间。
    private float nextInputSendTime;

    // 服务器保存这个玩家最近提交的移动方向。
    private Vector3 serverMoveDirection;

    // 服务器最近收到输入的时间，用于处理输入中断。
    private float lastInputReceiveTime;

    // 服务器修改血量，Mirror 将结果同步到客户端。
    [SyncVar]
    [SerializeField]
    private int health = 100;
    // 直接根据血量判断死亡，不另外保存一份状态。
    private bool IsDead => health <= 0;
    // 服务器限制测试扣血的频率。
    private float nextDamageTime;

    // 本机玩家初始化完成后，用绿色标识自己。
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        // 获取当前玩家物体上的图片渲染组件。
        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();

        // 如果预制体漏挂组件，输出明确提示，避免空引用异常。
        if (spriteRenderer == null)
        {
            Debug.LogError("玩家预制体缺少 SpriteRenderer。", this);
            return;
        }

        spriteRenderer.color = Color.green;
    }

    // 服务器修改血量，Mirror 将结果同步到客户端。

    // 客户端发出请求后，这段方法体在服务器端执行。
    [Command]
    private void CmdRequestTest()
    {
        Debug.Log($"服务器收到测试请求，玩家网络编号：{netId}");
    }

    // 客户端调用，方法体由服务器执行。
    [Command]
    private void CmdSetMoveInput(int horizontal, int vertical)
    {
        // 已死亡的玩家不能再改变移动方向。
        if (IsDead)
        {
            serverMoveDirection = Vector3.zero;
            return;
        }

        // 服务器限制输入范围，不直接相信客户端传来的数值。
        horizontal = Mathf.Clamp(horizontal, -1, 1);
        vertical = Mathf.Clamp(vertical, -1, 1);

        Vector3 direction = new Vector3(horizontal, vertical, 0f);

        // 防止斜向移动更快。
        serverMoveDirection = Vector3.ClampMagnitude(direction, 1f);

        lastInputReceiveTime = Time.unscaledTime;
    }

    // 测试用途：只能请求给自己的玩家扣血。
    [Command]
    private void CmdRequestTestDamage()
    {
        // 半秒内不重复处理扣血请求。
        if (Time.time < nextDamageTime)
        {
            return;
        }

        nextDamageTime = Time.time + 0.5f;

        // 每次扣 10 点，但不允许血量低于零。
        health = Mathf.Max(health - 10, 0);
    }

    // 仅显示本机玩家的血量，供当前练习观察结果。
    private void OnGUI()
    {
        if (!isLocalPlayer)
        {
            return;
        }

        GUI.Label(
            new Rect(10, 140, 300, 30),
            $"My HP: {health} / 100"
        );

        // 这里只负责显示，停止移动的规则由服务器执行。
        if (IsDead)
        {
            GUI.Label(
                new Rect(10, 175, 300, 30),
                "You are dead"
            );
        }
    }

    private void Update()
    {
        // 本机玩家按下空格时，向服务器申请测试扣血。
        if (isLocalPlayer && Input.GetKeyDown(KeyCode.Space))
        {
            CmdRequestTestDamage();
        }

        // 客户端：只有自己的玩家读取键盘并发送输入。
        if (isLocalPlayer && Time.unscaledTime >= nextInputSendTime)
        {
            int horizontal = 0;
            int vertical = 0;

            if (Application.isFocused)
            {
                if (Input.GetKey(KeyCode.A))
                {
                    horizontal -= 1;
                }

                if (Input.GetKey(KeyCode.D))
                {
                    horizontal += 1;
                }

                if (Input.GetKey(KeyCode.S))
                {
                    vertical -= 1;
                }

                if (Input.GetKey(KeyCode.W))
                {
                    vertical += 1;
                }
            }

            // 即使方向为零，也要发送，让服务器知道已经松开按键。
            CmdSetMoveInput(horizontal, vertical);

            // 最快每 0.05 秒发送一次，约每秒 20 次。
            nextInputSendTime = Time.unscaledTime + 0.05f;
        }

        // 服务器：根据保存的输入，计算这个玩家的位置。
        if (isServer)
        {
            // 超过半秒没有收到输入，停止移动。
            // 已死亡，或者长时间没有收到输入，都停止移动。
            if (IsDead || Time.unscaledTime - lastInputReceiveTime > 0.5f)
            {
                serverMoveDirection = Vector3.zero;
            }

            transform.position +=
                serverMoveDirection * moveSpeed * Time.deltaTime;
        }
    }
}