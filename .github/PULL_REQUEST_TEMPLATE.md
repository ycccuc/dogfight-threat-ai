<!--
PR 模板：开 Pull Request 时 GitHub 会自动填入下面内容。
请**全部勾完再请人 review**。没勾完的 PR 会被直接打回。
-->

## 这个 PR 做了什么

<!-- 一两句话说明。不要写「更新了一些东西」这种。 -->

Closes #

## 改动类型

<!-- 勾选一项或多项 -->

- [ ] `feat` 新功能
- [ ] `fix` 修复 bug
- [ ] `docs` 文档
- [ ] `art` 素材
- [ ] `refactor` 重构
- [ ] `test` 测试
- [ ] `chore` / `build` / `ci` 杂项

## 影响范围

- [ ] 客户端（Unity）
- [ ] 网关服 GatewayServer
- [ ] 逻辑服 LogicServer
- [ ] **共享协议 `src/shared/Protocol/`**
- [ ] 文档
- [ ] 素材
- [ ] 仓库配置（`.gitignore` / `.gitattributes` / `.editorconfig` / `.github/`）

---

## 自查清单

### 必须全部勾选

- [ ] 我已**关闭 Unity** 后再切换 / 合并分支
- [ ] `main` 分支拉下来能编译、能运行（我本地验证过）
- [ ] 没有提交 `Library/` `Temp/` `Logs/` `UserSettings/` `obj/` `bin/` `Build/`
- [ ] 没有提交密钥、token、账号、本机绝对路径
- [ ] 没有夹带与本次改动无关的文件

### 涉及 Unity 时

- [ ] 新增/删除的资源，`.meta` 文件**在同一次提交里**
- [ ] 没有和其他人**同时**修改同一个 `.unity` 场景或 `.prefab`
- [ ] 改动了 `ProjectSettings/`（Layers / Tags / 物理 / 画质 / Packages）→ **已在群里同步**

### 涉及协议时

- [ ] 我修改了 `src/shared/Protocol/` 下的文件
      → **已请客户端与服务端双方都确认过**（这是硬要求，不可跳过）
- [ ] 如果是破坏性改动（删字段 / 改语义），已在下方写明「哪一端需要同步改」
- [ ] `*.schema.json` 已通过 JSON Schema 语法校验
- [ ] 已同步更新 `docs/02-design/网络协议设计.md`

### 涉及文档时

- [ ] 文档里的**相对路径链接**我点过，没有断链
- [ ] 文档末尾的变更记录表已更新
- [ ] 图表同时提交了源文件（`.drawio`）和导出图（`.png`）

### 涉及素材时

- [ ] 已在 `docs/02-design/素材台账.md` 登记来源与授权
- [ ] 命名符合 `docs/02-design/美术规范.md`
- [ ] 第三方素材保留了 `License.txt`，并已更新 `THIRD-PARTY-NOTICES.md`
- [ ] 单文件未超过 50 MB（超过则已在群里说明）

---

## 破坏性改动说明

<!--
如果改动了协议、改了 ProjectSettings、或改了别人依赖的接口，在这里写清：
「哪一端需要同步改什么」。没有就写「无」。
-->

无

## 截图 / 验证证据

<!--
UI 改动、表现改动、联机效果，尽量贴图或贴日志。
答辩材料可以在这里积累。
-->

## 给 reviewer 的说明

<!-- 想让 reviewer 重点看哪里？哪里有你不确定的地方？老实写出来比藏着好。 -->
