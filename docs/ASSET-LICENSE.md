# 素材授权与来源

本仓库的授权不是一次到位的整体授权，分三层，用哪一层之前先看清对应来源。

## 1. 代码（`Source/`、编译产物 `Assemblies/`）

MIT 授权，见根目录 `LICENSE`。

## 2. Def 与文案（`Defs/`、`Data/`、`Patches/`、`Languages/`、`About/`）

本模组原创，同样按 `LICENSE` 的 MIT 条款授权。

## 3. 音效（`Sounds/*.ogg`）

**不是本模组的原创素材，不在 MIT 授权范围内。** 这 7 条 ogg 是从《群星》(Stellaris, Paradox Interactive)
的音频文件转制而来，与 `Defs/SoundDefs/ResearchLoopsSounds.xml` 中记录的对应关系如下：

| 本模组 defName | 文件 | 《群星》源文件 |
| --- | --- | --- |
| `RL_ResearchStart` | `RL_ResearchStart.ogg` | `ui/start_research_01.wav` |
| `RL_ResearchComplete` | `RL_ResearchComplete.ogg` | `placeholders/car_start_01.wav` |
| `RL_ChainComplete` | `RL_ChainComplete.ogg` | `event/event_activating_unknown_technology.wav` |
| `RL_AscensionMindOverMatter` | `RL_AscensionMindOverMatter.ogg` | `utopia/event_psionic_01.wav` |
| `RL_AscensionSynthetic` | `RL_AscensionSynthetic.ogg` | `event/event_ai_started_01.wav` |
| `RL_AscensionCybernetic` | `RL_AscensionCybernetic.ogg` | `federations/sfx_event_announcement.wav` |
| `RL_AscensionGenetic` | `RL_AscensionGenetic.ogg` | `event/event_alien_nature.wav` |

这些文件只用于音效（不改任何数值，属于反馈层）。它们随仓库分发是否可行，取决于
Paradox Interactive 对其游戏素材二次分发的政策，本仓库不代为声明授权；如有权利方要求移除，
会直接从仓库与安装包中删除，玩家换成自己的音效文件即可——`Sounds/` 下的文件名与
`SoundDef` 里的 `<clipPath>`（无扩展名）一一对应，替换文件不需要改代码或 def。

## 4. `About/Preview.png`

本模组自制的宣传图，游戏内截图加文字排版。
