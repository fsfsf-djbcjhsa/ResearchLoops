using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Player-facing global multipliers for every loop chain.
    ///
    /// Borrowed (in reduced form) from the Rico 「循环科技 - Repeatable Research」 settings card:
    /// two sliders and a preset manager, no per-loop tuning. The per-loop sliders Rico ships
    /// (29 entries x 4 fields) exist because his loops are one-off mod extensions; ours are
    /// authored data with a verifier pinned on the authored numbers, so a global multiplier
    /// covers the same player need ("make it faster / cheaper") without inventing a second
    /// place where the per-loop rates can drift.
    ///
    /// Skill loops are deliberately NOT scaled: one completed level is exactly +1 skill level
    /// (user directive), and a fractional multiplier would either break that contract or need
    /// rounding rules nobody asked for.
    ///
    /// Both scales are persisted by vanilla ModSettings plumbing and applied instantly —
    /// <see cref="ResearchLoopsConfig.NotifyChanged"/> re-projects every loop def and buff,
    /// so no restart is needed (the RSU:R mod's "all changes require a game restart" is the
    /// opposite of what we do here).
    /// </summary>
    public class ResearchLoopsSettings : ModSettings
    {
        /// <summary>
        /// Effect multiplier bounds. Linear on the EFFECT (0.5 = half, 2 = double), not on the
        /// per-level rate: on a compounding curve the rate form is exponential, and 5x turned the
        /// 50-level x0.077 incoming-damage factor into x0.0000006 — in-game report: 「拉满之后
        /// 承伤系数能到 0」. Bounds live in LoopMath so the verifier can compile and assert them.
        /// </summary>
        public const float BonusScaleMin = LoopMath.EffectScaleMin;
        public const float BonusScaleMax = LoopMath.EffectScaleMax;

        public const float CostScaleMin = 0.1f;
        public const float CostScaleMax = 10f;

        /// <summary>Sound-volume multiplier bounds. 0 = muted, 1 = authored ogg volume, up to 2x boost.</summary>
        public const float SoundVolumeMin = 0f;
        public const float SoundVolumeMax = 2f;

        /// <summary>Multiplier on every loop's effect, applied linearly (1 = authored values).</summary>
        public float bonusScale = 1f;

        /// <summary>Multiplier on every loop level's research cost. 1 = authored values.</summary>
        public float costScale = 1f;

        /// <summary>
        /// When true, every generated loop research requires a Hi-Tech Research Bench to be built
        /// before it can be researched (ResearchProjectDef.requiredResearchBuilding = HiTechResearchBench).
        /// A load-time gate: the defs are generated once when the game starts, so toggling this option
        /// takes effect after a restart — like any other def edit. Default false keeps the current
        /// behaviour (loops gate only on their research prerequisite, e.g. MultiAnalyzer).
        /// </summary>
        public bool requireHiTechBench = false;

        /// <summary>Multiplier on every ResearchLoops sound sting. 1 = authored volume, 0 = muted.</summary>
        public float soundVolume = 1f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref bonusScale, "bonusScale", 1f);
            Scribe_Values.Look(ref costScale, "costScale", 1f);
            Scribe_Values.Look(ref requireHiTechBench, "requireHiTechBench", false);
            Scribe_Values.Look(ref soundVolume, "soundVolume", 1f);
            // Runs in every Scribe mode on purpose: sanitizing is idempotent, and a config file
            // hand-edited to NaN/Infinity (or a corrupted read) must not poison every
            // Factor()/LevelCost() call afterwards.
            Sanitize();
        }

        /// <summary>Clamps both scales into their authored ranges; NaN/Infinity falls back to 1.</summary>
        public void Sanitize()
        {
            bonusScale = ClampScale(bonusScale, BonusScaleMin, BonusScaleMax);
            costScale = ClampScale(costScale, CostScaleMin, CostScaleMax);
            soundVolume = ClampScale(soundVolume, SoundVolumeMin, SoundVolumeMax);
        }

        public void ResetToDefaults()
        {
            bonusScale = 1f;
            costScale = 1f;
            requireHiTechBench = false;
            soundVolume = 1f;
        }

        /// <summary>Sets both scales at once (preset apply) with the same sanitizing.</summary>
        public void SetScales(float bonus, float cost)
        {
            bonusScale = bonus;
            costScale = cost;
            Sanitize();
        }

        private static float ClampScale(float value, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 1f;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }

    /// <summary>
    /// Named snapshots of the two scales, stored as hand-rolled flat JSON under the game's
    /// Config folder.
    ///
    /// Why hand-rolled (Rico's lesson, documented in his own source): System.Text.Json and
    /// JsonUtility are both unusable here — JsonUtility recursed into a UnityStubs crash on the
    /// 1.7 preview, and System.Text.Json is not referenced by the game. A flat one-object-per-
    /// line format needs no dependency at all, greps fine, and survives a truncated file (any
    /// unreadable line is skipped, the rest loads).
    /// </summary>
    public static class ResearchLoopsPresets
    {
        public const string FileName = "ResearchLoops_Presets.json";
        public const int MaxPresets = 32;
        public const int MaxNameLength = 48;

        public struct Preset
        {
            public string name;
            public float bonusScale;
            public float costScale;
            public string savedAt;
        }

        public static string FilePath
        {
            get { return Path.Combine(GenFilePaths.ConfigFolderPath, FileName); }
        }

        public static List<Preset> Load()
        {
            var result = new List<Preset>();
            string path = FilePath;
            if (!File.Exists(path)) return result;

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Log.Error("[ResearchLoops] cannot read preset file " + path + ": " + ex.Message);
                return result;
            }

            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf('{') < 0) continue;
                Preset preset;
                if (TryParseLine(lines[i], out preset) && !string.IsNullOrEmpty(preset.name))
                {
                    result.Add(preset);
                }
            }
            return result;
        }

        /// <summary>Inserts or replaces the preset with this name. False when the file could not be written.</summary>
        public static bool Save(string name, float bonusScale, float costScale)
        {
            string clean = SanitizeName(name);
            List<Preset> presets = Load();

            var entry = new Preset
            {
                name = clean,
                bonusScale = Clamp(bonusScale, ResearchLoopsSettings.BonusScaleMin, ResearchLoopsSettings.BonusScaleMax),
                costScale = Clamp(costScale, ResearchLoopsSettings.CostScaleMin, ResearchLoopsSettings.CostScaleMax),
                savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            };

            int existing = presets.FindIndex(p => string.Equals(p.name, clean, StringComparison.Ordinal));
            if (existing >= 0)
            {
                presets[existing] = entry;
            }
            else
            {
                if (presets.Count >= MaxPresets) presets.RemoveAt(0);
                presets.Add(entry);
            }
            return WriteAll(presets);
        }

        public static bool Delete(string name)
        {
            List<Preset> presets = Load();
            int removed = presets.RemoveAll(p => string.Equals(p.name, name, StringComparison.Ordinal));
            if (removed == 0) return false;
            return WriteAll(presets);
        }

        /// <summary>Strips characters that would break the flat JSON, caps the length, never returns empty.</summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return Localization.T("未命名", "Unnamed");
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '"' || c == '\\' || char.IsControl(c)) continue;
                sb.Append(c);
            }
            string clean = sb.ToString().Trim();
            if (clean.Length == 0) return Localization.T("未命名", "Unnamed");
            if (clean.Length > MaxNameLength) clean = clean.Substring(0, MaxNameLength);
            return clean;
        }

        private static bool WriteAll(List<Preset> presets)
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.Append("[\n");
                for (int i = 0; i < presets.Count; i++)
                {
                    Preset p = presets[i];
                    sb.Append("  {\"name\":\"").Append(Escape(p.name)).Append("\",\"bonus\":")
                      .Append(p.bonusScale.ToString("0.###", CultureInfo.InvariantCulture))
                      .Append(",\"cost\":")
                      .Append(p.costScale.ToString("0.###", CultureInfo.InvariantCulture))
                      .Append(",\"savedAt\":\"").Append(Escape(p.savedAt)).Append("\"}");
                    if (i < presets.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append("]\n");
                File.WriteAllText(FilePath, sb.ToString());
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[ResearchLoops] cannot write preset file " + FilePath + ": " + ex.Message);
                return false;
            }
        }

        private static bool TryParseLine(string line, out Preset preset)
        {
            preset = new Preset();
            string name = JsonString(line, "name");
            float? bonus = JsonNumber(line, "bonus");
            float? cost = JsonNumber(line, "cost");
            if (name == null || !bonus.HasValue || !cost.HasValue) return false;

            preset.name = SanitizeName(name);
            preset.bonusScale = Clamp(bonus.Value, ResearchLoopsSettings.BonusScaleMin, ResearchLoopsSettings.BonusScaleMax);
            preset.costScale = Clamp(cost.Value, ResearchLoopsSettings.CostScaleMin, ResearchLoopsSettings.CostScaleMax);
            preset.savedAt = JsonString(line, "savedAt") ?? string.Empty;
            return true;
        }

        private static string JsonString(string line, string key)
        {
            int k = line.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0) return null;
            int colon = line.IndexOf(':', k + 1);
            if (colon < 0) return null;
            int open = line.IndexOf('"', colon + 1);
            if (open < 0) return null;
            int close = open + 1;
            while (close < line.Length)
            {
                if (line[close] == '\\') { close += 2; continue; }
                if (line[close] == '"') break;
                close++;
            }
            if (close >= line.Length) return null;
            return Unescape(line.Substring(open + 1, close - open - 1));
        }

        private static float? JsonNumber(string line, string key)
        {
            int k = line.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0) return null;
            int colon = line.IndexOf(':', k + 1);
            if (colon < 0) return null;
            int start = colon + 1;
            while (start < line.Length && line[start] == ' ') start++;
            int end = start;
            while (end < line.Length && IsNumberChar(line[end])) end++;
            if (end == start) return null;

            float value;
            if (float.TryParse(line.Substring(start, end - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value) &&
                !float.IsNaN(value) && !float.IsInfinity(value))
                return value;
            return null;
        }

        private static bool IsNumberChar(char c)
        {
            return (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '+' || c == 'e' || c == 'E';
        }

        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else if (char.IsControl(c)) continue;
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static string Unescape(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static float Clamp(float value, float min, float max)
        {
            return LoopMath.Clamp(LoopMath.Safe(value, 1f), min, max);
        }
    }

    /// <summary>
    /// The single bridge between the settings file and the running game. Every runtime consumer
    /// (the plan's costs, the buff stage, the two patch chains) reads the scales from here, so
    /// there is exactly one place where "what are the current multipliers" is answered.
    ///
    /// Null-safe: before the mod's settings exist (or after they are torn down) the scales read
    /// as 1, i.e. the authored values. The getters avoid lambdas and allocations because
    /// Patch_FoodFall runs inside pawn AI ticks.
    /// </summary>
    public static class ResearchLoopsConfig
    {
        /// <summary>Global effect multiplier; 1 when settings are unavailable or corrupted.</summary>
        public static float BonusScale
        {
            get
            {
                ResearchLoopsSettings settings = Mod_ResearchLoops.Settings;
                return settings == null ? 1f : LoopMath.Safe(settings.bonusScale, 1f);
            }
        }

        /// <summary>Global cost multiplier; 1 when settings are unavailable or corrupted.</summary>
        public static float CostScale
        {
            get
            {
                ResearchLoopsSettings settings = Mod_ResearchLoops.Settings;
                return settings == null ? 1f : LoopMath.Safe(settings.costScale, 1f);
            }
        }

        /// <summary>
        /// True when loop research should require a Hi-Tech Research Bench (requiredResearchBuilding).
        /// Read by the def generator at load time; null-safe (false when settings are unavailable).
        /// </summary>
        public static bool RequireHiTechBench
        {
            get
            {
                ResearchLoopsSettings settings = Mod_ResearchLoops.Settings;
                return settings != null && settings.requireHiTechBench;
            }
        }

        /// <summary>
        /// Multiplier applied to every ResearchLoops sound sting (SoundInfo.volumeFactor).
        /// Read live at play time, so a slider change is instant; null-safe (1 when settings are
        /// unavailable). 0 mutes, 1 = authored ogg volume.
        /// </summary>
        public static float SoundVolume
        {
            get
            {
                ResearchLoopsSettings settings = Mod_ResearchLoops.Settings;
                return settings == null ? 1f : LoopMath.Safe(settings.soundVolume, 1f);
            }
        }

        /// <summary>
        /// REMOVED 2026-10-23 (user directive 「默认名称改为直接效果，删除群星风格的那些命名」):
        /// the useConciseLabels toggle and the conceptName it switched away from are both gone.
        /// ResearchLoopPlan.Label now always renders the loop's authored effect name.
        /// </summary>

        /// <summary>
        /// Persists the settings file and re-projects every loop def and buff so a slider or a
        /// preset takes effect immediately, mid-game, without a restart.
        /// </summary>
        public static void NotifyChanged()
        {
            ResearchLoopsSettings settings = Mod_ResearchLoops.Settings;
            if (settings != null) settings.Write();

            GameComponent_ResearchLoops component = GameComponent_ResearchLoops.Current;
            if (component != null) component.NotifySettingsChanged();
        }
    }

    /// <summary>
    /// Preset manager: a name field, a save button, and one apply/delete pair per preset.
    ///
    /// Self-written instead of showing a text-entry dialog because this RimWorld build has no
    /// Dialog_TextEntry (verified against 1.6.4871 with Mono.Cecil): the name field is a plain
    /// Widgets.TextField, which is also what Rico's mod does for the same reason.
    ///
    /// Buttons delete directly (delete asks for confirmation inline) — no other modal dialog is
    /// stacked, so a click can never be swallowed by this window's input absorption.
    /// </summary>
    public class Dialog_ManagePresets : Window
    {
        private const float RowHeight = 28f;
        private const float RowGap = 4f;
        private const float NameWidth = 200f;
        private const float ValueWidth = 86f;
        private const float ButtonWidth = 64f;
        private const int MaxShownName = 14;

        private string nameDraft = "";
        private Vector2 scrollPos;
        private List<ResearchLoopsPresets.Preset> presets;
        private string status = "";

        public Dialog_ManagePresets()
        {
            optionalTitle = Localization.T("循环科技 — 预设管理", "Tech Cycle - Presets");
            doCloseButton = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            draggable = true;
            Refresh();
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(600f, 540f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = 0f;

            ResearchLoopsSettings settings = Mod_ResearchLoops.Settings;
            if (settings != null)
            {
                Widgets.Label(new Rect(0f, y, inRect.width, 24f),
                    Localization.T("当前：效果 ×", "Current: effect x") +
                    settings.bonusScale.ToString("0.##", CultureInfo.InvariantCulture) +
                    Localization.T("   成本 ×", "   cost x") +
                    settings.costScale.ToString("0.##", CultureInfo.InvariantCulture));
            }
            y += 26f;

            Widgets.Label(new Rect(0f, y, inRect.width, 44f),
                Localization.T("以当前倍率另存为预设。预设保存在游戏 Config 目录（ResearchLoops_Presets.json），与存档无关，换档继续可用。", "Save the current multipliers as a preset. Presets live in the game Config folder (ResearchLoops_Presets.json), independent of any save."));
            y += 40f;

            float saveWidth = 96f;
            nameDraft = Widgets.TextField(new Rect(0f, y, inRect.width - saveWidth - 10f, RowHeight),
                nameDraft ?? "");
            if (Widgets.ButtonText(new Rect(inRect.width - saveWidth, y, saveWidth, RowHeight), Localization.T("保存预设", "Save preset")))
            {
                SaveCurrent();
            }
            y += RowHeight + 12f;

            bool hasStatus = !string.IsNullOrEmpty(status);
            float listHeight = inRect.height - y - (hasStatus ? 24f : 0f);
            if (listHeight < 60f) listHeight = 60f;

            if (presets.Count == 0)
            {
                Widgets.Label(new Rect(0f, y + 4f, inRect.width, 30f), Localization.T("（还没有预设）", "(No presets yet)"));
            }
            else
            {
                float contentHeight = presets.Count * (RowHeight + RowGap) + RowGap;
                Rect outRect = new Rect(0f, y, inRect.width, listHeight);
                Rect viewRect = new Rect(0f, 0f, outRect.width - 20f, contentHeight);
                Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);
                float rowY = RowGap;
                for (int i = 0; i < presets.Count; i++)
                {
                    DrawPresetRow(presets[i], rowY, viewRect.width);
                    rowY += RowHeight + RowGap;
                }
                Widgets.EndScrollView();
            }

            if (hasStatus)
            {
                Widgets.Label(new Rect(0f, inRect.height - 22f, inRect.width, 22f), status);
            }
        }

        private void DrawPresetRow(ResearchLoopsPresets.Preset preset, float y, float width)
        {
            ResearchLoopsSettings settings = Mod_ResearchLoops.Settings;
            bool isCurrent = settings != null &&
                Mathf.Approximately(settings.bonusScale, preset.bonusScale) &&
                Mathf.Approximately(settings.costScale, preset.costScale);

            string name = preset.name.Length > MaxShownName
                ? preset.name.Substring(0, MaxShownName) + "…"
                : preset.name;
            if (isCurrent) name += Localization.T("（当前）", " (current)");

            Widgets.Label(new Rect(0f, y, NameWidth, RowHeight), name);
            Widgets.Label(new Rect(NameWidth, y, ValueWidth, RowHeight),
                Localization.T("效果 ×", "Effect x") + preset.bonusScale.ToString("0.##", CultureInfo.InvariantCulture));
            Widgets.Label(new Rect(NameWidth + ValueWidth, y, ValueWidth, RowHeight),
                Localization.T("成本 ×", "Cost x") + preset.costScale.ToString("0.##", CultureInfo.InvariantCulture));

            float x = NameWidth + ValueWidth * 2f + 8f;
            if (Widgets.ButtonText(new Rect(x, y, ButtonWidth, RowHeight), Localization.T("套用", "Apply")))
            {
                ApplyPreset(preset);
            }
            x += ButtonWidth + 8f;
            if (x + ButtonWidth <= width && Widgets.ButtonText(new Rect(x, y, ButtonWidth, RowHeight), Localization.T("删除", "Delete")))
            {
                ConfirmDelete(preset);
            }
        }

        private void SaveCurrent()
        {
            ResearchLoopsSettings settings = Mod_ResearchLoops.Settings;
            if (settings == null) return;

            if (ResearchLoopsPresets.Save(nameDraft, settings.bonusScale, settings.costScale))
            {
                status = Localization.T("已保存「", "Saved \"") + ResearchLoopsPresets.SanitizeName(nameDraft) + Localization.T("」", "\"");
                nameDraft = "";
                Refresh();
            }
            else
            {
                status = Localization.T("保存失败，详见 Player.log", "Save failed - see Player.log");
            }
        }

        private void ApplyPreset(ResearchLoopsPresets.Preset preset)
        {
            ResearchLoopsSettings settings = Mod_ResearchLoops.Settings;
            if (settings == null) return;

            settings.SetScales(preset.bonusScale, preset.costScale);
            ResearchLoopsConfig.NotifyChanged();
            status = Localization.T("已套用「", "Applied \"") + preset.name + Localization.T("」，即时生效", "\" - effective now");
        }

        private void ConfirmDelete(ResearchLoopsPresets.Preset preset)
        {
            string name = preset.name;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                Localization.T("删除预设「", "Delete preset \"") + name + Localization.T("」？此操作不可撤销。", "\"? This cannot be undone."),
                delegate
                {
                    if (ResearchLoopsPresets.Delete(name))
                    {
                        status = Localization.T("已删除「", "Deleted \"") + name + Localization.T("」", "\"");
                        Refresh();
                    }
                },
                true,
                Localization.T("删除预设", "Delete preset")));
        }

        private void Refresh()
        {
            presets = ResearchLoopsPresets.Load();
        }
    }
}
