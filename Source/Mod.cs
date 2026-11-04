using System.Globalization;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Mod entry point. Applies every [HarmonyPatch] in this assembly and owns the settings UI.
    ///
    /// The settings surface is deliberately small (Rico's 「循环科技 - Repeatable Research」 reduced
    /// to what this mod needs): two global multipliers — effect and cost — a preset manager, and one
    /// load-time gate (require a Hi-Tech Research Bench to research loops). Per-loop tuning is NOT
    /// offered: the per-level rates are authored data pinned by the verifier, and a second place
    /// where they can drift is a defect waiting to happen. Skill chains DO follow the effect
    /// multiplier (ceiling-rounded, so a low multiplier keeps at least +1) — both the gene card and
    /// the live skill level read one shared scaled value.
    ///
    /// Layout follows Vanilla Psycasts Expanded (VanillaPsycastsExpanded.PsycastsMod.
    /// DoSettingsWindowContents): the live value is part of the label line — "效果倍率: x1.50" —
    /// and the slider underneath carries no label of its own. That is what makes the multiplier
    /// read as a multiplier instead of a bare number.
    ///
    /// Every change takes effect immediately (ResearchLoopsConfig.NotifyChanged re-projects the
    /// defs and buffs); no restart is required — the RSU:R mod's "restart required" behaviour is
    /// a consequence of its def-editing design, which we do not share.
    /// </summary>
    public class Mod_ResearchLoops : Mod
    {
        public const string HarmonyId = ResearchLoopsIds.HarmonyId;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// Live settings instance. Static because the runtime bridge (ResearchLoopsConfig) and
        /// the preset window both need it without walking the mod list.
        /// </summary>
        public static ResearchLoopsSettings Settings;

        public Mod_ResearchLoops(ModContentPack content) : base(content)
        {
            Settings = GetSettings<ResearchLoopsSettings>();
            Settings.Sanitize();

            Harmony harmony = new Harmony(HarmonyId);
            harmony.PatchAll(System.Reflection.Assembly.GetExecutingAssembly());
            Log.Message("[ResearchLoops] loaded " + HarmonyId + " (effect x" +
                        ResearchLoopsConfig.BonusScale.ToString("0.##", Inv) + ", cost x" +
                        ResearchLoopsConfig.CostScale.ToString("0.##", Inv) + ")");
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            ResearchLoopsSettings settings = Settings;
            if (settings == null) return;

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label(Localization.T(
                "全局倍率：作用于全部 buff / patch / 技能 循环链。技能链随倍率向上取整（低倍率至少 +1）。",
                "Global multiplier: applies to every buff / patch / skill loop. Skill loops round up (at least +1 even at a low multiplier)."));
            listing.Label(Localization.T(
                "改动即时生效；「越低越好」的系数（受击、饥饿等）最低到 ×0.05，不会到 0。",
                "Changes apply instantly. Lower-is-better coefficients (damage taken, hunger, ...) bottom out at x0.05, never 0."));
            listing.Gap(6f);

            float beforeBonus = settings.bonusScale;
            float beforeCost = settings.costScale;

            listing.Label(Localization.T("效果倍率（×", "Effect multiplier (x") +
                          ResearchLoopsSettings.BonusScaleMin.ToString("0.##", Inv) +
                          Localization.T(" – ×", " - x") + ResearchLoopsSettings.BonusScaleMax.ToString("0.##", Inv) +
                          Localization.T("）: ×", "): x") +
                          settings.bonusScale.ToString("0.00", Inv));
            settings.bonusScale = listing.Slider(settings.bonusScale,
                ResearchLoopsSettings.BonusScaleMin, ResearchLoopsSettings.BonusScaleMax);
            listing.Gap(6f);

            listing.Label(Localization.T("成本倍率（×", "Cost multiplier (x") +
                          ResearchLoopsSettings.CostScaleMin.ToString("0.##", Inv) +
                          Localization.T(" – ×", " - x") + ResearchLoopsSettings.CostScaleMax.ToString("0.##", Inv) +
                          Localization.T("）: ×", "): x") +
                          settings.costScale.ToString("0.00", Inv));
            settings.costScale = listing.Slider(settings.costScale,
                ResearchLoopsSettings.CostScaleMin, ResearchLoopsSettings.CostScaleMax);
            listing.Gap(10f);

            listing.Label(Localization.T(
                "音效音量：研究开始 / 单级完成 / 满链完成 / 四条飞升的提示音。",
                "Sound volume: the research start / level-complete / chain-complete / four-ascension stings."));
            listing.Label(Localization.T("音效音量（×", "Sound volume (x") +
                          ResearchLoopsSettings.SoundVolumeMin.ToString("0.##", Inv) +
                          Localization.T(" – ×", " - x") + ResearchLoopsSettings.SoundVolumeMax.ToString("0.##", Inv) +
                          Localization.T("）: ×", "): x") +
                          settings.soundVolume.ToString("0.00", Inv));
            settings.soundVolume = listing.Slider(settings.soundVolume,
                ResearchLoopsSettings.SoundVolumeMin, ResearchLoopsSettings.SoundVolumeMax);
            listing.Gap(10f);

            // Load-time gate: gating a research project on a building is a def property, baked at
            // startup, so this cannot be instant like the multipliers — it applies on the next launch.
            bool beforeHiTech = settings.requireHiTechBench;
            listing.CheckboxLabeled(Localization.T(
                    "研究循环需要已建造「高级研究台」才能研究",
                    "Loops require a built hi-tech research bench"),
                ref settings.requireHiTechBench);
            listing.Label(Localization.T(
                "（开启后需重启游戏生效：所有循环研究节点会要求基地里已建好高级研究台。）",
                "(Applies after a restart: every loop node then requires a built hi-tech research bench.)"));
            listing.Gap(10f);

            // Only push when a control actually moved: NotifyChanged re-projects every def and buff,
            // and this method runs once per frame while the window is open. The hi-tech gate is a
            // def property (load-time), so NotifyChanged here only persists the setting — the gate
            // itself applies on the next launch.
            if (!Mathf.Approximately(beforeBonus, settings.bonusScale) ||
                !Mathf.Approximately(beforeCost, settings.costScale) ||
                beforeHiTech != settings.requireHiTechBench)
            {
                settings.SetScales(settings.bonusScale, settings.costScale);
                ResearchLoopsConfig.NotifyChanged();
            }

            if (listing.ButtonText(Localization.T("管理预设…", "Manage presets..."), null, 1f))
            {
                Find.WindowStack.Add(new Dialog_ManagePresets());
            }

            if (listing.ButtonText(Localization.T("恢复默认倍率（×1 / ×1）", "Reset multipliers (x1 / x1)"), null, 1f))
            {
                settings.ResetToDefaults();
                ResearchLoopsConfig.NotifyChanged();
            }

            listing.End();
        }

        public override string SettingsCategory()
        {
            return "ResearchLoops";
        }
    }
}
