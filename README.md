# Dogfight Threat AI

> 基于实时威胁评估与动态决策的 2D 飞机对战游戏
> 湖北经济学院 · 信息工程学院 · 大学生创新训练项目 · 2026.6 – 2027.5

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Unity](https://img.shields.io/badge/Unity-2022.3%20LTS-black?logo=unity&logoColor=white)](https://unity.com/)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)

**当前阶段**：第一阶段 —— 单机空战核心闭环（进度见 [更新日志](#更新日志)）

---

## 这是什么

一个俯视角 2D 空战游戏。与市面上多数 2D 空战游戏的区别在于三点（也是立项书的三个创新点）：

1. **物理化飞行手感** —— `Rigidbody2D` 驱动的惯性滑行、转向阻尼、速度边界与失速惩罚
2. **可测试的 AI** —— 行为树决策 + BFS 区域巡逻/搜索，且 AI 层**不依赖 Unity**，能脱离引擎单测
3. **能批量对战** —— 同一份对局代码可以无渲染快进跑几百局，输出胜率 CSV

第 2、3 点决定了工程结构，读代码前建议先看 [架构说明](docs/02-design/架构说明.md)。

---

## 快速开始

### 环境

- Unity **2022.3 LTS**（本项目用 2022.3.62f3）
- .NET 8 SDK（只用于 `tools/` 下的工具；游戏本体不需要）
- VS Code（推荐扩展见 `.vscode/extensions.json`）

### 打开工程

```bash
git clone https://github.com/ycccuc/dogfight-threat-ai.git
cd dogfight-threat-ai
git lfs install                 # .psd/.aseprite/.unitypackage 等走 LFS
git config --global core.longpaths true   # Windows 上 Unity 路径会超长
```

用 Unity Hub 打开 `src/client/DogfightThreatAI/`，然后：

1. 菜单 **Dogfight → 项目初始化**（建排序层、2D 重力归零、修 Build Settings）
2. 菜单 **Dogfight → 检查项目配置**（确认没有遗漏）
3. 打开 `Assets/_Project/Scenes/Battle.unity`，按 Play

### 常用入口

| 想做什么 | 怎么做 |
|----------|--------|
| 跑单测 | `Window → General → Test Runner → EditMode → Run All` |
| 批量对战（输出胜率 CSV） | 菜单 `Dogfight → 批量对战` |
| 验证手动步进物理可用 | 进播放模式 → 菜单 `Dogfight → 验证 → 手动步进物理` |
| 20 秒验证 AI 层能不能编译 | `dotnet build tools/ai-compile-check/AiCompileCheck.csproj` |

---

## 目录结构

```
├── art-source/                  素材源文件（不直接导入 Unity）
│   ├── original/                自产素材（sprites / tiles / ui / vfx / animations / audio / fonts）
│   └── third-party/<包名>/       第三方素材包（source/ 原样归档 + LICENSE + NOTES.md）
│
├── docs/
│   ├── 00-project/              会议纪要 / 周报
│   ├── 01-requirements/         需求与调研
│   ├── 02-design/               架构 / 美术规范 / 系统设计
│   ├── 03-test/                 测试用例与证据
│   ├── 04-manuals/              操作手册 / 部署手册
│   ├── 05-reports/              阶段性报告与结题材料
│   └── 素材台账.md              第三方素材来源与授权登记（软著要用）
│
├── src/
│   ├── client/DogfightThreatAI/ Unity 工程（见下方"工程内结构"）
│   ├── server/                  网关 / 逻辑服务器（第二阶段）
│   └── shared/Protocol/         跨进程消息协议
│
├── tools/
│   ├── ai-compile-check/        验证 AI 层零 Unity 依赖（约 20 秒）
│   └── ai-benchmark/results/    批量对战输出的 CSV（生成物，不入库）
│
└── tests/                       跨模块测试与脚本
```

### Unity 工程内结构

```
src/client/DogfightThreatAI/Assets/_Project/
├── Scripts/
│   ├── Ai/         零 Unity 依赖：飞行与伤害公式、BFS、行为树、批量对战统计
│   ├── Gameplay/   MonoBehaviour：MatchLoop（唯一时间入口）、PlaneAgent、配置资产
│   ├── Editor/     编辑器工具：像素导入、项目初始化、批量对战窗口、物理探针
│   └── Tests/      EditMode 单测
├── Art/            导入的贴图（由 SpriteBank 用语义键引用，代码里不写文件名）
└── Scenes/         Battle.unity
```

---

## 关键约定

- **AI 层不许引用 UnityEngine**（`Dogfight.Ai.asmdef` 里 `noEngineReferences: true`，编译器强制）
- **游戏逻辑只在 `MatchLoop.Tick(dt)` 里推进**，不要在别处写 `Update()` 逻辑
- **配置数值放 ScriptableObject，运行期烘焙成纯数据结构**；跨进程消息才用 JSON
- **代码里不出现贴图路径或文件名**，一律通过 `SpriteBankSO` 的语义键取图
- **新素材必须登记** `docs/素材台账.md`，并符合 `docs/02-design/美术规范.md`

---

## 文档索引

| 文档 | 内容 |
|------|------|
| [架构说明](docs/02-design/架构说明.md) | 三条边界、程序集划分、一帧怎么走、怎么跑 |
| [美术规范](docs/02-design/美术规范.md) | PPU / 分辨率 / 排序层 / 命名 / 素材准入 6 条 |
| [素材台账](docs/素材台账.md) | 素材来源与授权、待补清单、红线 |
| [暑期调研与实践成果整合](docs/05-reports/暑期调研与实践成果整合.md) | 立项书约束、竞品调研、Demo 复盘、W0–W2 路线 |
| [协作流程](CONTRIBUTING.md) | 分支模型、提交规范、PR 要求 |

---

## 团队

| 成员 | 院系 / 专业 | 方向 |
|------|-------------|------|
| 袁博轩（负责人） | 信息工程学院 / 软件工程 | 架构与客户端 |
| 王晟 | 统计与数学学院 / 统计学 | 待细化 |
| 张梦琦 | 信息工程学院 / 物联网工程 | 待细化 |
| 舒文豪 | 信息工程学院 / 软件工程 | 待细化 |
| 卓晟 | 信息工程学院 / 软件工程 | 待细化 |

指导教师：关培超、王艺

> ⚠️ 立项书原文**未给出人名到模块的对应**，上表的"方向"待团队确认后补齐。

---

## 更新日志

- **2026-10-05** 第一阶段 W0：架构底座落地
  四个程序集（`Ai` / `Gameplay` / `Editor` / `Tests`）、`MatchLoop` 显式时间入口、
  `IPlaneInput` 输入抽象、飞行与伤害公式、BFS 与巡逻航线、行为树引擎、
  批量对战窗口与胜率 CSV、像素导入后处理、项目一键初始化、EditMode 单测
- **2026-10-02** 仓库结构重整（`art-source`、`docs` 编号目录、Unity 工程入库）
- **2026-06-07** 项目启动

---

## 许可证

[MIT](LICENSE) · 第三方素材授权见 [素材台账](docs/素材台账.md)
