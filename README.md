# 循环科技 · Research Loops

![预览](About/Preview.png)

把《群星》的循环科技搬进环世界。**29 项关键科技，每一项都是一条可以反复研究的科技链**：研究完一级，节点不会消失，而是原地变成下一级，成本和收益一起往上走，效果永久叠加。后期研究台不再闲置。

- 支持版本：RimWorld 1.6
- 前置依赖：[Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)
- 创意工坊：https://steamcommunity.com/sharedfiles/filedetails/?id=3830009762
- 完整功能与数值说明：[INTRO.md](INTRO.md)
- 变更日志：[CHANGELOG.md](CHANGELOG.md)
- 素材授权：[docs/ASSET-LICENSE.md](docs/ASSET-LICENSE.md)

## 下载与安装

只想玩的玩家不需要编译，仓库里已经带着编译好的 `Assemblies/ResearchLoops.dll`。

1. 下载 [Releases](../../releases) 里的 `ResearchLoops_v1.1.zip`，或直接在创意工坊订阅
2. 解压得到 `ResearchLoops` 文件夹，放进 `<游戏目录>\Mods\`
3. 在启动器里启用，排在 **Harmony** 之后
4. 游戏内研究出「多元分析仪」，研究面板会多出「循环科技」这一页

已有存档可以直接加，等级从 0 开始，不会凭空给加成；中途卸载后加成消失，存档本身不会坏。

## 内容一览

| 分组 | 条数 | 例子 |
| --- | --- | --- |
| 工作与技能 | 1 | 八个工作技能每级各 +1（加在原版 20 级钳制之后，依然线性生效） |
| 生产与生活 | 6 | 通用劳作、休息效率、冥想速率、娱乐/疲劳/饥饿衰减 |
| 战斗 | 11 | 远程与近战伤害、承伤、护甲、闪避、命中、射速、机动 |
| 机械与灵能 | 5 | 机械带宽、机械能耗、灵能敏感度、心灵熵上限与恢复 |
| 机体韧化 | 4 | 部位血量、远程精准、疼痛、凝血 |
| 特殊 | 2 | 物品品质（7 级）、技能兴趣（4 级，识别 VSE 档位） |

另有 4 条一次性飞升节点（心胜于物、合成进化、血肉苦弱、设计进化），各 1 万科研点，前置为对应链各练到 10 级，效果无法消除。

- **加成只落在自己的殖民者与奴隶身上**（合成进化后加上自己的机械体），敌人、访客、商队、动物、囚犯、炮塔一律不吃。
- **所有「越低越好」的系数有 0.05 地板**，护甲钳在 2.0，闪避与命中以 100% 为天花板，堆不出无敌。
- 设置页可整体调 **效果倍率（×0.1–×5）** 与 **成本倍率（×0.1–×10）**，改动即时生效，可存成命名预设。
- 研究面板带「自动循环」开关：每完成一级自动接下一级，用角落轻提示替代弹窗。

## 兼容性

- 只新增自己的研究页、buff、基因与补丁，**不修改原版任何 def**
- 实测过 VE 全家桶、1000+ mod 列表，以及鼠猫米螈龙娘沃芬与雪豹等种族 mod
- 皇权（灵能三条链）与生物科技（机械四条链）都是可选的，没有对应 DLC 时它们只是暂时用不上
- 语言：中文 + 机翻英文

## 从源码编译

```powershell
powershell -ExecutionPolicy Bypass -File Source\build.ps1 -GameRoot <你的 RimWorld 安装目录>
```

脚本直接用 .NET SDK 自带的 Roslyn 编译器，需要本机游戏 `RimWorldWin64_Data\Managed` 下的 DLL 与
Harmony 的 `0Harmony.dll`（默认从创意工坊物品 2009463077 目录探测，可用 `-HarmonyDll` 覆盖）。
产物写入 `Assemblies\ResearchLoops.dll`。`Source\deploy.ps1` 把运行时文件集镜像到 `<游戏目录>\Mods\ResearchLoops`。

`Assemblies\ResearchLoops.dll` 一并提交进仓库，方便只想玩的玩家；仓库不打包 `0Harmony.dll`，前置请各自订阅。

## 许可

代码与 Def 按 [MIT](LICENSE) 授权；音效文件是从《群星》音频转制的第三方素材，**不在 MIT 授权范围内**，
来源与对应关系见 [docs/ASSET-LICENSE.md](docs/ASSET-LICENSE.md)。

打包版可自由下载用于个人游戏。禁止用于收费服务、付费整合包与 AI 训练数据集；转载须保留本声明与原仓库链接。
