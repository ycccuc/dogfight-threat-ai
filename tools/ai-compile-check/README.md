# Ai 层独立编译验证

## 它解决什么问题

`Dogfight.Ai` 这一层（行为树、BFS、飞行与伤害公式）被 `asmdef` 的 `noEngineReferences`
强制规定为**不依赖 UnityEngine**。这个目录下的 .NET 工程就是用来**证明**这条规定的：

它能编译通过，就说明 Ai 层确实一行 Unity 代码都没用。于是：

| 收益 | 具体表现 |
|------|----------|
| 单测快 | 不需要进 Play 模式，不创建 GameObject |
| 反馈快 | 改一行逻辑约 20 秒出结果，不用等 Unity 域重载 |
| 可复用 | 第三阶段的逻辑服务器（无头进程）能直接引用这一层，不用重写 |

## 怎么用

```bash
# 在仓库根目录执行
dotnet build tools/ai-compile-check/AiCompileCheck.csproj
```

期望输出：`已成功生成。 0 个警告 0 个错误`

## 什么时候会用到

- 改完 `Assets/_Project/Scripts/Ai/` 里的任何东西，想快速确认没写错
- CI 里当一道便宜的守门（`Gameplay`/`Editor`/`Tests` 依赖 Unity，只能在 Unity 里编译）
- 有人试图往 Ai 层里加 `using UnityEngine` 时，这里会立刻报错

> 如果报错说找不到 `UnityEngine`，说明有人往 Ai 层加了 Unity 依赖 ——
> **不要**在本工程里补 UnityEngine 引用，那会破坏这条边界。正确做法是把 Unity 相关代码
> 挪到 `Gameplay` 层，只把纯逻辑留在 `Ai`。
