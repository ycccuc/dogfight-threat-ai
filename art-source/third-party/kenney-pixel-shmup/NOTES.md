# Kenney Pixel Shmup

| 项 | 值 |
|---|---|
| 来源 | https://kenney.nl/assets/pixel-shmup |
| 作者 | Kenney（www.kenney.nl） |
| 版本 | 1.1（创建于 2021-11-01） |
| 授权 | **CC0 1.0** —— 可用于个人、教育、商业项目，可修改可再分发 |
| 署名 | 非强制，但建议在结题报告/游戏内保留「Art by Kenney」 |
| 授权原文 | `source/License.txt`（**随包保留，不要删**） |
| 用途 | 第一阶段占位素材：玩家机 / 敌机 / 地形 |
| 登记 | [`docs/02-design/素材台账.md`](../../../docs/02-design/素材台账.md) |

## 内容与规格

| 目录 | 内容 | 规格 |
|---|---|---|
| `source/Ships/` | 24 架飞机（单图） | **32 × 32** |
| `source/Tiles/` | 120 个地形块（单图） | **16 × 16** |
| `source/Tilemap/` | 图集 `ships.png` / `tiles.png`（含 packed 版） | 图集，块间距 1px |
| `source/Tilesheet (*).txt` | 图集切分参数 | — |
| `source/Preview.png`、`source/Sample.png` | 预览图，**不要导入 Unity** | — |

## ⚠️ 这个包**没有**的东西

核对过实际内容：只有飞机和地形。以下全部需要另外补：

- 子弹 / 弹幕贴图
- 爆炸、尾焰等序列帧与粒子
- HUD / 血条 / 雷达 UI
- 字体
- 音效与音乐

→ 待补清单见 [`docs/02-design/素材台账.md`](../../../docs/02-design/素材台账.md) 第三节。

## 导入约定

- 全局 `Pixels Per Unit` 统一设 **32**（见 [`docs/02-design/美术规范.md`](../../../docs/02-design/美术规范.md)）
  这样 32×32 的飞机正好占 1×1 世界单位，16×16 的地形块占 0.5×0.5 世界单位
- `Preview.png`、`Sample.png`、`*.url`、`Tilesheet *.txt` **不要**拷进 Unity 的 `Assets/`
  （`source/` 是原始归档，导入 Unity 时只挑 `Ships/`、`Tiles/`、`Tilemap/`）
- 导入目标是 Unity 工程的 `Assets/ThirdParty/kenney-pixel-shmup/`，
  **不是** `Assets/Plugins/`（`Plugins/` 是 Unity 特殊目录，只放原生插件 `.dll/.so/.aar`）
