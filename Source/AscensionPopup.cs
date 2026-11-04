using System.Collections.Generic;
using RimWorld;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// 飞升研究完成时弹出的特殊文本信件。
    ///
    /// 正文直接搬群星（Stellaris）官方完成事件的中文本地化文本：
    ///   设计进化 → bio.20（基因飞升）、血肉苦弱 → cyber.21（赛博飞升）、
    ///   合成进化 → synth.4（合成飞升）、心胜于物 → shroud.2800（灵能飞升）。
    /// 灵能那条清掉了群星专有的动态 token（[出神状态]/[感知]/§H虚境§! 高亮标记/
    /// 按守护者动态追加的段落），其余逐字保留官方译文。
    ///
    /// 标题与正文都走 Localization.T 跟随游戏语言切换（2026-10-25 用户「把四个飞升也加入英文版」
    /// 推翻早前「飞升保留中文」，标题改为双语）。只有四条飞升在这张表里——那些"归零某条链系数"的
    /// 嵌套危险科技完成时不弹（查不到 defName 静默返回）。
    /// </summary>
    public static class AscensionPopup
    {
        private struct PopupText
        {
            public string TitleZh;
            public string TitleEn;
            public string BodyZh;
            public string BodyEn;

            public PopupText(string titleZh, string titleEn, string bodyZh, string bodyEn)
            {
                TitleZh = titleZh;
                TitleEn = titleEn;
                BodyZh = bodyZh;
                BodyEn = bodyEn;
            }
        }

        private static readonly Dictionary<string, PopupText> Texts =
            new Dictionary<string, PopupText>
        {
            {
                "RL_MindOverMatter", new PopupText("心胜于物", "Mind Over Matter",
                    "在被困于出神状态多日后，我们终于短暂地打开了一条通往虚境的大门。" +
                    "现在，我们可以以更清晰的视角感知到这个领域，从那漩涡般的混沌中解析出规律。" +
                    "我们的行动影响灵能流动，无形的实体则从另一边凝视着我们。" +
                    "他们有时好奇，有时友好……有时又带着恶意。" +
                    "在现实世界掌握灵能只是一个开端。" +
                    "既然已经建立了初步的灵能链接，我们就能将认知的能量集中起来并潜入虚境了。",
                    "After days of being locked in hypnotic trance, we have finally managed to open " +
                    "a brief gateway into the Shroud. Now, we can see this realm with greater " +
                    "clarity, resolving patterns from the swirling mass. Our actions influence its " +
                    "shifting flows of psionic energy, and formless entities gaze back on us from " +
                    "the other side. Sometimes with curiosity, sometimes with kindness... and " +
                    "sometimes with malice. Our psionic mastery in the corporeal realm was only " +
                    "the beginning. Now that we have established an initial psionic link, we can " +
                    "combine our cognitive powers and delve into the Shroud.")
            },
            {
                "RL_SyntheticEvolution", new PopupText("合成进化", "Synthetic Evolution",
                    "在多方的通力协作下，我们实现了几乎不可能完成的任务，将意识转变成代码，" +
                    "躯体化身机械。这个过程并非一帆风顺，转变远非易事。" +
                    "但无论付出了什么代价，现在的新躯体已经将无尽的可能性展开在了我们的面前。",
                    "In a coordinated effort, we have achieved what seemed impossible, transforming " +
                    "our consciousness into code, and our bodies into machines. The process was not " +
                    "without difficulty, the transition far from easy. But whatever may have been " +
                    "lost along the way, our new configuration offers endless possibilities.")
            },
            {
                "RL_CyberneticAscension", new PopupText("血肉苦弱", "Cybernetic Ascension",
                    "今天是我们社会的一个转折点。我们终于得以用全内置机械部件来强化改造自己的躯体。" +
                    "这个过程并非一帆风顺，转变远非易事。" +
                    "但无论付出了什么代价，现在的新躯体已经将无尽的可能性展开在了我们的面前。",
                    "Today marks a turning point for our society. We are now able to augment our " +
                    "bodies with fully-integrated machine parts. The process was not without " +
                    "difficulty, the transition far from easy. But whatever may have been lost " +
                    "along the way, our new existence offers endless possibilities.")
            },
            {
                "RL_GeneAscension", new PopupText("设计进化", "Genetic Ascension",
                    "无论我们是为追求最纯粹的自我而奋斗，还是以克隆铺就成功之路，" +
                    "抑或是通过有意的基因突变来增强自我，此刻过后我们都将为自己的选择一往无前。",
                    "Whether we strive for the purest form of ourselves, clone our way to success, " +
                    "or genetically augment ourselves through deliberate mutation, this moment marks " +
                    "a point of no return.")
            },
        };

        /// <summary>
        /// 飞升研究完成时调用。非四条飞升（如嵌套归零科技）静默跳过。
        /// </summary>
        public static void Show(ResearchProjectDef proj)
        {
            if (proj == null) return;

            PopupText text;
            if (!Texts.TryGetValue(proj.defName, out text)) return;

            // 自定义信件 def 缺失时回退原版 PositiveEvent，绝不因为缺 def 崩在这里。
            LetterDef def = DefDatabase<LetterDef>.GetNamed("RL_AscensionComplete", false)
                            ?? LetterDefOf.PositiveEvent;

            // playSound: false —— 飞升 sting 已在同一时刻由 ResearchLoopsSounds 播放，
            // 关掉信件自身的到达音，避免两声叠在一起。标题（2026-10-25 起）与正文都随语言切换。
            Find.LetterStack.ReceiveLetter(
                Localization.T(text.TitleZh, text.TitleEn),
                Localization.T(text.BodyZh, text.BodyEn),
                def,
                playSound: false);
        }
    }
}
