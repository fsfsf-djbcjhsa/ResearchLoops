using System;
using UnityEngine;
using RimWorld;
using Verse;
using Verse.Sound;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// UI surface for the standalone 自动循环 (auto-loop) feature.
    ///
    /// Three entry points, all called from AutoLoopPatches.cs:
    ///   - DrawAutoLoopButton : the toggle button injected into the research panel, just ABOVE the
    ///     vanilla 开始研究 / 停止研究 button (in the gap between the project description and the
    ///     start button, so it never collides with the vanilla button or the "Active Project" header).
    ///   - ShowLevelToast     : a small, non-blocking white corner toast when one level of an
    ///     auto-looping chain completes. Replaces the vanilla per-level completion dialog.
    ///   - ShowChainComplete  : a fullscreen blocking prompt, raised only when the chain reaches its
    ///     final level.
    ///
    /// Merged into Source/ and compiled into ResearchLoops.dll. The code is written against the
    /// verified RimWorld 1.6 API (decompiled Assembly-CSharp). Mod.cs's PatchAll() pulls in the
    /// [HarmonyPatch] classes in AutoLoopPatches.cs automatically; this file holds the UI surface they
    /// call. See AutoLoop/README.md for design notes.
    /// </summary>
    public static class AutoLoopUI
    {
        // User asked for "左上角或右上角" — either is acceptable; defaulting to top-right.
        private enum Corner { TopLeft, TopRight }
        private static readonly Corner ToastCorner = Corner.TopRight;

        private const float ToastWidth = 360f;
        private const float ToastHeight = 40f;
        private const float ToastMargin = 24f;
        private const float ToastYOffset = 96f;   // clear of the top status bar
        private const float ToastSeconds = 2.5f;   // auto-dismiss duration

        /// <summary>
        /// Draws the 自动循环 toggle button in the research panel. Placed in the gap between the
        /// selected project's description and the vanilla start/stop button, so it never collides
        /// with the vanilla button or the "Active Project" progress header below it. Shown only for
        /// loop defs whose chain is not finished (a finished chain has nothing left to loop).
        /// </summary>
        public static void DrawAutoLoopButton(Rect startButRect, ResearchProjectDef selectedProject)
        {
            if (selectedProject == null) return;
            ResearchLoopTemplate template = ResearchLoopProgress.TemplateOf(selectedProject);
            if (template == null) return;                              // not one of our loops

            int completed = GameComponent_ResearchLoops.GetLevel(template.defName);
            if (ResearchLoopPlan.ChainComplete(template, completed)) return;  // chain done: nothing to loop

            bool on = AutoLoopState.IsEnabled(template.defName);

            // Sit above the start button (40px up, 34px tall) — inside the description/button gap.
            Rect butRect = new Rect(startButRect.x, startButRect.y - 40f, startButRect.width, 34f);
            string label = on
                ? Localization.T("自动循环：开", "Auto-loop: on")
                : Localization.T("自动循环：关", "Auto-loop: off");
            Color textColor = on ? new Color(0.55f, 1f, 0.55f) : Color.gray;

            if (Widgets.ButtonText(butRect, label, true, true, textColor, true, TextAnchor.MiddleCenter))
            {
                AutoLoopState.SetEnabled(template.defName, !on);
                SoundDefOf.Tick_Tiny.PlayOneShot(SoundInfo.OnCamera());
            }
        }

        /// <summary>Opens the level-up corner toast. Any prior toast is replaced so they never stack.</summary>
        public static void ShowLevelToast(ResearchLoopTemplate template, int level)
        {
            if (template == null) return;
            CloseExistingToasts();
            Find.WindowStack.Add(new AutoLoopToastWindow(template, level));
        }

        /// <summary>Opens the fullscreen "chain complete" prompt.</summary>
        public static void ShowChainComplete(ResearchLoopTemplate template, int level)
        {
            if (template == null) return;
            Find.WindowStack.Add(new AutoLoopCompleteWindow(template, level));
        }

        private static void CloseExistingToasts()
        {
            WindowStack stack = Find.WindowStack;
            if (stack == null) return;
            for (int i = stack.Windows.Count - 1; i >= 0; i--)
            {
                if (stack.Windows[i] is AutoLoopToastWindow)
                {
                    stack.TryRemove(stack.Windows[i], false);
                }
            }
        }

        /// <summary>
        /// Small, non-blocking corner toast for a single level completion. No background, no shadow,
        /// never steals focus or input — research keeps auto-progressing underneath. Self-dismisses
        /// after ToastSeconds via WindowUpdate.
        /// </summary>
        private class AutoLoopToastWindow : Window
        {
            private readonly ResearchLoopTemplate template;
            private readonly int level;
            private readonly float expireAt;

            public AutoLoopToastWindow(ResearchLoopTemplate template, int level)
            {
                this.template = template;
                this.level = level;
                this.expireAt = UnityEngine.Time.realtimeSinceStartup + ToastSeconds;

                layer = WindowLayer.GameUI;
                doWindowBackground = false;
                drawShadow = false;
                preventCameraMotion = false;
                focusWhenOpened = false;
                closeOnAccept = false;
                closeOnCancel = false;
                closeOnClickedOutside = false;
                absorbInputAroundWindow = false;
                soundAppear = null;
                soundClose = null;
                onlyOneOfTypeAllowed = true;  // CloseExistingToasts already enforces this; belt-and-suspenders
            }

            public override Vector2 InitialSize => new Vector2(ToastWidth, ToastHeight);

            // Small margin so the contracted inner rect still has room for the label.
            protected override float Margin => 4f;

            protected override void SetInitialSizeAndPosition()
            {
                float x = ToastCorner == Corner.TopRight
                    ? UI.screenWidth - ToastWidth - ToastMargin
                    : ToastMargin;
                windowRect = new Rect(x, ToastYOffset, ToastWidth, ToastHeight).Rounded();
            }

            public override void WindowUpdate()
            {
                if (UnityEngine.Time.realtimeSinceStartup > expireAt)
                {
                    Close(false);
                }
            }

            public override void DoWindowContents(Rect inRect)
            {
                Color prevColor = GUI.color;
                GameFont prevFont = Text.Font;
                TextAnchor prevAnchor = Text.Anchor;

                Text.Font = GameFont.Small;
                Text.Anchor = ToastCorner == Corner.TopRight ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
                GUI.color = Color.white;

                string text = Localization.T(
                    ResearchLoopPlan.Pick(template.name, template.nameEn) + " 第 " + level.ToString() + " 级 完成",
                    ResearchLoopPlan.Pick(template.name, template.nameEn) + " level " + level.ToString() + " complete");
                Widgets.Label(inRect, text);

                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
                GUI.color = prevColor;
            }
        }

        /// <summary>
        /// Fullscreen blocking prompt shown only when an auto-looped chain reaches its final level.
        /// WindowLayer.Super puts it above the research panel; the modal blocks input until the
        /// player dismisses it (button or Esc).
        /// </summary>
        private class AutoLoopCompleteWindow : Window
        {
            private readonly ResearchLoopTemplate template;
            private readonly int level;

            public AutoLoopCompleteWindow(ResearchLoopTemplate template, int level)
            {
                this.template = template;
                this.level = level;

                layer = WindowLayer.Super;
                doWindowBackground = false;
                drawShadow = false;
                closeOnClickedOutside = false;
                closeOnCancel = true;          // Esc dismisses
                forcePause = false;            // game keeps running underneath; the modal just blocks input
                preventCameraMotion = true;
                focusWhenOpened = true;
                absorbInputAroundWindow = true; // block input outside the window (whole screen anyway)
                soundAppear = null;
                onlyOneOfTypeAllowed = true;
            }

            public override Vector2 InitialSize => new Vector2(UI.screenWidth, UI.screenHeight);

            protected override float Margin => 0f;  // let the dim backdrop cover the full screen

            protected override void SetInitialSizeAndPosition()
            {
                windowRect = new Rect(0f, 0f, UI.screenWidth, UI.screenHeight).Rounded();
            }

            public override void DoWindowContents(Rect inRect)
            {
                Color prevColor = GUI.color;
                GameFont prevFont = Text.Font;
                TextAnchor prevAnchor = Text.Anchor;

                // Dim the whole screen behind the card.
                GUI.color = new Color(0.04f, 0.04f, 0.04f, 0.72f);
                GUI.DrawTexture(inRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true);
                GUI.color = Color.white;

                Text.Anchor = TextAnchor.MiddleCenter;

                string title = Localization.T("循环科技 全部完成", "Tech Cycle complete");
                string body = Localization.T(
                    ResearchLoopPlan.Pick(template.name, template.nameEn) + " 已全部研究完毕（共 " +
                    template.maxLevel.ToString() + " 级），自动循环已关闭。",
                    ResearchLoopPlan.Pick(template.name, template.nameEn) + " is fully researched (" +
                    template.maxLevel.ToString() + " levels). Auto-loop turned off.");

                float cardW = Mathf.Min(560f, inRect.width - 80f);
                float cardY = inRect.height * 0.32f;
                Rect cardRect = new Rect((inRect.width - cardW) / 2f, cardY, cardW, 140f);

                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(cardRect.x, cardRect.y, cardRect.width, 40f), title);

                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(cardRect.x, cardRect.y + 50f, cardRect.width, 84f), body);

                float butW = 200f, butH = 44f;
                Rect butRect = new Rect((inRect.width - butW) / 2f, cardRect.y + cardRect.height + 24f, butW, butH);
                if (Widgets.ButtonText(butRect, Localization.T("知道了", "OK"), true, true, true, TextAnchor.MiddleCenter))
                {
                    Close();
                }

                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
                GUI.color = prevColor;
            }
        }
    }
}
