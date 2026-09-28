# 自产素材（original）

这里放**自己产出或改造**的素材，按类型分目录。

| 目录 | 放什么 | 状态 |
|------|--------|------|
| `sprites/` | 飞机、子弹、图标等位图 | 空 |
| `tiles/` | 地形 / 背景地块 | 空 |
| `ui/` | HUD、血条、雷达、按钮、准星 | 空 |
| `vfx/` | 粒子贴图、爆炸序列帧、尾焰 | 空 |
| `animations/` | 逐帧动画序列（也可直接按 `sprites` 命名放 sprites） | 空 |
| `audio/` | 音效、背景音乐 | 空 |
| `fonts/` | 位图字体 / TMP 字体源文件 | 空 |

## 约定

- 命名：`<类别>_<对象>_<编号>_<尺寸>.<扩展名>`，例：`ship_player_01_32x32.png`
  （完整规则见 [`docs/02-design/美术规范.md`](../../docs/02-design/美术规范.md)）
- 像素密度必须符合美术规范：**PPU 32**，Filter Mode = Point，Compression = None
- **第三方素材不要放这里**，统一进 [`art-source/third-party/<包名>/`](../third-party/)，
  并在 [`docs/02-design/素材台账.md`](../../docs/02-design/素材台账.md) 登记授权信息
