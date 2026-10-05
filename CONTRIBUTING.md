# 协作流程

> 面向本项目成员。开工前读一遍，尤其是「禁止事项」。

---

## 一、每台机器做一次

```bash
git lfs install                          # .psd/.aseprite/.unitypackage 走 LFS
git config --global core.longpaths true  # Windows 上 Unity 路径会超长
git config commit.template .gitmessage   # 提交信息模板（若仓库有 .gitmessage）
```

另外要**配置 Unity 的场景智能合并**，否则两个人改同一个 `.unity` 会变成整文件冲突、
只能二选一丢一个人的工作。命令见 `.gitattributes` 顶部注释。

**克隆到短路径**（如 `D:\dev\dogfight`）：本项目实测 Unity 深层路径已到 230+ 字符。

---

## 二、分支模型

```
main ────●────────────●───────────●────  只接受 PR，保持可演示
          \          /           /
dev  ──────●────●───●───────●───●─────  集成分支，日常都合到这里
            \  /           \  /
任务分支 ────●             ──●           短命分支，合并后即删
```

| 分支 | 用途 | 生命周期 |
|------|------|----------|
| `main` | 稳定可演示版本 | 长期 |
| `dev` | 日常集成 | 长期 |
| `feat/<任务>` | 新功能 | 合并后删除 |
| `fix/<问题>` | 修 Bug | 合并后删除 |
| `chore/<杂项>` | 构建 / 配置 / 文档 | 合并后删除 |

**命名用任务不用人名**：`feat/match-loop` ✅ ／ `feature/王晟` ❌

> 以人名命名的分支会长期存活、越漂越远，最后合并时冲突成灾 —— Unity 的 `.unity` 尤其严重。

### 标准流程

```bash
git checkout dev && git pull
git checkout -b feat/match-loop
# ...写代码...
git add -A && git commit
git push -u origin feat/match-loop
# 在 GitHub 开 PR，目标分支选 dev，指定至少 1 人 Review
# Squash and merge → 删除任务分支
```

---

## 三、提交信息

格式 `<type>: <中文描述>`，type 小写英文，冒号后一个空格，描述不超过 50 字。

`feat` 新功能 ｜ `fix` 修 Bug ｜ `docs` 文档 ｜ `refactor` 重构 ｜ `test` 测试 ｜ `chore` 杂项 ｜ `perf` 性能

- **一个 commit 只做一件事**
- 禁止 `update`、`修改`、`1`、`.` 这类无信息量的描述
- 正文写**为什么**改，不要复述改了什么
- 关联 issue 写 `Closes #12`

---

## 四、PR 要求

- 目标分支是 **`dev`**，不是 `main`
- 按 `.github/PULL_REQUEST_TEMPLATE.md` 填全，**必须附运行截图或录屏**
- 改动 `src/shared/Protocol/` 时，必须在描述里写明「需要哪一端同步改」
- 至少 1 人 Review 通过才 Squash merge

---

## 五、Unity 场景冲突

**不要手工编辑 `.unity` / `.prefab`** —— 它们是 YAML，手改极易损坏。

1. 确认已配好 `UnityYAMLMerge`（见 `.gitattributes`）
2. 冲突时用 `git mergetool` 交给 Unity Smart Merge
3. 仍无法自动合并的：**两个人当面确认保留谁的版本**，不要各自硬解
4. 从源头减少冲突：一个场景尽量只由一人负责；对象拆成 Prefab 各自维护

---

## 六、代码规范（本项目的硬约束）

- 编码 UTF-8，缩进 4 空格（`.editorconfig` 已约束）
- **`Dogfight.Ai` 层禁止引用 UnityEngine**（asmdef 已强制）。
  需要 Unity 类型时，把代码挪到 `Gameplay` 层，只把纯逻辑留在 `Ai`
- **游戏逻辑只在 `MatchLoop.Tick(dt)` 里推进**，不要在别处写 `Update()` 业务逻辑
- **禁止在每帧调用里** `FindObjectsOfType` / `GetComponent` / `Instantiate` / `Destroy`，一律走对象池
- 与物理相关的计算放固定步长里（本项目的 `MatchLoop.StepOnce`）
- 新增脚本放 `Assets/_Project/Scripts/<层>/<模块>/`

---

## 七、禁止事项

- ❌ 提交 `Library/`、`Temp/`、`Logs/`、`UserSettings/`、`Build/`、`*.csproj`、`*.sln`
- ❌ 提交密钥、密码、`.env`、`*.pfx`
- ❌ `git push --force` 到 `main` / `dev`
- ❌ 直接 push 到 `main`
- ❌ 提交来源不明的素材（软著申请会被卡，见 `docs/素材台账.md`）
- ❌ 在 `Dogfight.Ai` 里加 `using UnityEngine`
- ❌ 在 PR 里混入无关改动（顺手格式化整个文件、顺手改别人的模块）
