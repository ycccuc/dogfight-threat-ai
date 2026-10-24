# Battle 场景追击演示计划

**目标：** 用户进入 Battle 后，使用 WASD 操纵原玩家飞机；一架开局可见的红色敌机使用现有纯 C# AI 追逐玩家。

**设计：** 复用 PlayerController/FlightModel/PlayerView。新增 ChaseAiInputSource 作为 MonoBehaviour 输入适配器，提供只读状态快照并调用 Core AI。World 不读取用于插值的 Art Transform，不持有可写模拟状态。场景引用由 Unity 编辑器 API 保存，敌机 prefab 不持有场景玩家引用。

- [x] 检查 Battle、玩家 prefab、输入配置、相机及生命周期，确认 Legacy 输入已启用。
- [x] 添加真实 Battle PlayMode 测试，先验证原场景缺少敌机时失败。
- [x] PlayerController 暴露只读 State/IsInitialized；修正首步插值旧状态未初始化的问题。
- [x] 新增 ChaseAiInputSource 薄接线，初始化顺序安全、目标不可用时刹车。
- [x] 用 EditorSceneManager/PrefabUtility 创建红色敌机 prefab 和 Battle 实例，开局位于玩家附近，保存所有引用及 meta。
- [x] 跑原有 EditMode 回归与 PlayMode 实际追击测试，核对 Console、相机与场景资源。
- [x] 保存演示说明及验证结果，打开 Unity Battle 进入 Play，交给用户实际操纵。

本次不新增武器/伤害、多人房间、威胁网格或替换飞行规则。接入过程中的目标丢失表现沿用 Core 刹车；纯追击仍可能因惯性越过玩家。
