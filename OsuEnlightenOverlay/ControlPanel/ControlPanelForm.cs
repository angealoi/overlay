using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OsuEnlightenOverlay.Overlay;

namespace OsuEnlightenOverlay.ControlPanel
{
    /// <summary>
    /// 다크 테마 팔레트 — osu! 계열 플럼 배경 + 시그니처 핑크 액센트.
    /// </summary>
    internal static class Theme
    {
        public static readonly Color Bg          = Color.FromArgb(24, 20, 27);    // 폼 배경 (다크 플럼)
        public static readonly Color Card        = Color.FromArgb(37, 32, 43);    // 섹션 카드
        public static readonly Color Input       = Color.FromArgb(52, 45, 60);    // 입력 컨트롤 배경
        public static readonly Color InputBorder = Color.FromArgb(74, 65, 84);    // 버튼/입력 테두리
        public static readonly Color Text        = Color.FromArgb(233, 227, 239); // 본문 텍스트
        public static readonly Color Muted       = Color.FromArgb(148, 138, 160); // 보조 텍스트
        public static readonly Color Accent      = Color.FromArgb(255, 102, 170); // osu! 핑크
        public static readonly Color AccentHover = Color.FromArgb(255, 133, 190);
        public static readonly Color Green       = Color.FromArgb(120, 224, 143); // 연결됨 상태
        public static readonly Color Hint        = Color.FromArgb(222, 184, 96);  // 단축키 힌트 (구 180,140,0의 다크 대응)
        public static readonly Color BtnHover    = Color.FromArgb(68, 59, 78);
        public static readonly Color BtnDown     = Color.FromArgb(44, 38, 51);
    }

    /// <summary>
    /// 섹션 카드 — GroupBox 대체. 둥근 모서리(Region 클립) + 핑크 타이틀.
    /// BackColor를 통째로 칠하므로 자식 컨트롤 투명 배경 문제가 없다.
    /// </summary>
    internal class Card : Panel
    {
        static readonly Font TitleFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        readonly string title;

        public Card(string title, int x, int y, int width, int height)
        {
            this.title = title;
            Location = new Point(x, y);
            Size = new Size(width, height);
            BackColor = Theme.Card;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateRegion();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateRegion();
        }

        void UpdateRegion()
        {
            // 둥근 모서리 — 카드/배경이 둘 다 어두워 Region 클립의 계단 현상은 안 보인다.
            const int r = 10;
            using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, Width, Height), r))
                Region = new Region(path);
        }

        static GraphicsPath RoundedRect(Rectangle b, int r)
        {
            int d = r * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(b.X, b.Y, d, d, 180, 90);
            p.AddArc(b.Right - d, b.Y, d, d, 270, 90);
            p.AddArc(b.Right - d, b.Bottom - d, d, d, 0, 90);
            p.AddArc(b.X, b.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            TextRenderer.DrawText(e.Graphics, title, TitleFont, new Point(14, 10), Theme.Accent);
        }
    }

    /// <summary>
    /// 컨트롤 패널 — NEWNEWOVERLAY overlay_tab.cpp WinForms 포팅.
    /// Overlay / Difficulty / Cursor / HUD / Skin 섹션. 다크 테마 카드 레이아웃.
    /// </summary>
    public class ControlPanelForm : Form
    {
        OverlaySettings settings;

        // Overlay 섹션
        CheckBox chkEnabled, chkCaptureBlocked, chkHiddenOverride;
        NumericUpDown nudFpsCap;
        OverlayForm overlayRef;

        // Difficulty 섹션
        TrackBar tbAR, tbCS, tbDtAR, tbHtAR;
        NumericUpDown nudAR, nudCS, nudDtAR, nudHtAR;
        Button btnARAuto, btnCSAuto, btnDtARAuto, btnHtARAuto;

        // Cursor 섹션
        CheckBox chkCursorAutoSize;
        NumericUpDown nudCursorSize;
        ComboBox cmbCursorPack;
        Button btnRefreshCursorPacks;

        // HUD 섹션
        CheckBox chkHudFps, chkHudAcc, chkHudCombo, chkHudHitError;
        Button btnEditLayout;
        Label lblEditHint1, lblEditHint2, lblEditHint3;

        // Skin 섹션
        ComboBox cmbSkin;
        Button btnRefreshSkin;

        // ── 다크 타이틀바 (Win10 1809+) ──
        // DWMWA_USE_IMMERSIVE_DARK_MODE — 20 (2004+) / 19 (1809~1909). 실패해도 무해(밝은 타이틀바 유지).
        [DllImport("dwmapi.dll", PreserveSig = true)]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref on, 4);
            }
            catch { /* dwmapi 없음(구버전 OS) — 테마 없이 진행 */ }
        }

        // ── 컨트롤 스타일 헬퍼 ──

        static void StyleButton(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Theme.InputBorder;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = Theme.BtnHover;
            b.FlatAppearance.MouseDownBackColor = Theme.BtnDown;
            b.BackColor = Theme.Input;
            b.ForeColor = Theme.Text;
            b.Cursor = Cursors.Hand;
        }

        static void StyleAccentButton(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Theme.AccentHover;
            b.FlatAppearance.MouseDownBackColor = Theme.Accent;
            b.BackColor = Theme.Accent;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
        }

        static void StyleNud(NumericUpDown n)
        {
            n.BackColor = Theme.Input;
            n.ForeColor = Theme.Text;
            n.BorderStyle = BorderStyle.FixedSingle;
        }

        static void StyleCombo(ComboBox c)
        {
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = Theme.Input;
            c.ForeColor = Theme.Text;
        }

        static CheckBox MakeCheck(string text, int x, int y, int width)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.AutoSize = false;
            c.Width = width;
            c.Location = new Point(x, y);
            c.ForeColor = Theme.Text;
            c.Cursor = Cursors.Hand;
            return c;
        }

        static Label MakeLabel(string text, int x, int y, int width, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.Width = width;
            l.ForeColor = color;
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        public ControlPanelForm(OverlaySettings settings, OverlayForm overlay)
        {
            this.settings = settings;
            this.overlayRef = overlay;
            this.Text = "osu! Enlighten Overlay — Control Panel";
            this.Width = 382;
            this.Height = 680;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Theme.Bg;
            this.ForeColor = Theme.Text;
            this.Font = new Font("Segoe UI", 9f);

            // 스크롤 가능한 컨텐츠 패널 — 모든 섹션을 담아 아래 잘림 방지.
            Panel contentPanel = new Panel();
            contentPanel.Dock = DockStyle.Fill;
            contentPanel.AutoScroll = true;
            contentPanel.BorderStyle = BorderStyle.None;
            contentPanel.BackColor = Theme.Bg;
            this.Controls.Add(contentPanel);

            const int cardX = 10;
            const int cardW = 338;
            int y = 14;

            // ── 상태 표시 (맨 위) ──
            Label lblStatus = new Label();
            lblStatus.Text = "● Ready";
            lblStatus.Location = new Point(cardX, y);
            lblStatus.Width = cardW;
            lblStatus.Height = 22;
            lblStatus.TextAlign = ContentAlignment.MiddleCenter;
            lblStatus.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            lblStatus.ForeColor = Theme.Green;
            contentPanel.Controls.Add(lblStatus);
            y += lblStatus.Height + 2;

            Label lblBeatmap = new Label();
            lblBeatmap.Text = "";
            lblBeatmap.Location = new Point(cardX, y);
            lblBeatmap.Width = cardW;
            lblBeatmap.Height = 17;
            lblBeatmap.TextAlign = ContentAlignment.MiddleCenter;
            lblBeatmap.ForeColor = Theme.Muted;
            lblBeatmap.Font = new Font("Segoe UI", 8.25f);
            contentPanel.Controls.Add(lblBeatmap);
            y += lblBeatmap.Height + 12;

            // ── Overlay ──
            Card grpOverlay = new Card("OVERLAY", cardX, y, cardW, 126);

            chkEnabled = MakeCheck("Enable Overlay", 16, 36, 150);
            chkEnabled.Checked = settings.Enabled;
            chkEnabled.CheckedChanged += (s, e) =>
            {
                settings.Enabled = chkEnabled.Checked;
                if (overlayRef != null) overlayRef.ApplyFpsCap();
                Save();
            };
            grpOverlay.Controls.Add(chkEnabled);

            chkCaptureBlocked = MakeCheck("Capture Blocked", 172, 36, 150);
            chkCaptureBlocked.Checked = settings.CaptureBlocked;
            chkCaptureBlocked.CheckedChanged += (s, e) => { settings.CaptureBlocked = chkCaptureBlocked.Checked; Save(); };
            grpOverlay.Controls.Add(chkCaptureBlocked);

            chkHiddenOverride = MakeCheck("Hidden Override", 16, 60, 200);
            chkHiddenOverride.Checked = settings.HiddenOverride;
            chkHiddenOverride.CheckedChanged += (s, e) => { settings.HiddenOverride = chkHiddenOverride.Checked; Save(); };
            grpOverlay.Controls.Add(chkHiddenOverride);

            grpOverlay.Controls.Add(MakeLabel("FPS Cap:", 16, 90, 60, Theme.Text));

            nudFpsCap = new NumericUpDown();
            nudFpsCap.Location = new Point(80, 87);
            nudFpsCap.Width = 64;
            StyleNud(nudFpsCap);
            nudFpsCap.Minimum = OverlaySettings.FpsCapMin;
            nudFpsCap.Maximum = OverlaySettings.FpsCapMax;
            // 방어: Normalize()가 이미 범위 안으로 맞추지만, 범위 밖 값이 새어들어와도 예외 없이.
            nudFpsCap.Value = Math.Min(nudFpsCap.Maximum, Math.Max(nudFpsCap.Minimum, (decimal)settings.FpsCap));
            nudFpsCap.ValueChanged += (s, e) =>
            {
                settings.FpsCap = (int)nudFpsCap.Value;
                if (overlayRef != null) overlayRef.ApplyFpsCap();
                Save();
            };
            grpOverlay.Controls.Add(nudFpsCap);

            grpOverlay.Controls.Add(MakeLabel("(0 = unlimited)", 152, 90, 130, Theme.Muted));

            contentPanel.Controls.Add(grpOverlay);
            y += grpOverlay.Height + 10;

            // ── Difficulty Changer ──
            Card grpDiff = new Card("DIFFICULTY CHANGER", cardX, y, cardW, 206);

            // NOMOD AR 상한 10 — osu! 실제 NOMOD AR이 10에서 막히고, 그 이상은 부자연스럽다.
            // min/max는 OverlaySettings 상수 — Normalize() 클램프와 동일 범위를 공유한다.
            AddValueRow(grpDiff, 36, "AR",
                settings.ArValue, OverlaySettings.ArMin, OverlaySettings.ArMax, 2,
                (v) => { settings.ArValue = v; Save(); if (overlayRef != null) overlayRef.RefreshDifficulty(); },
                () => overlayRef != null ? overlayRef.GetMapAR() : 9.0f,
                out tbAR, out nudAR, out btnARAuto);
            AddValueRow(grpDiff, 76, "CS",
                settings.CsValue, OverlaySettings.CsMin, OverlaySettings.CsMax, 2,
                (v) => { settings.CsValue = v; Save(); if (overlayRef != null) overlayRef.RefreshDifficulty(); },
                // 채움은 mod 적용 CS — nomod를 채우면 EZ에서 mod가 무시됨
                () => overlayRef != null ? overlayRef.GetAutoCS() : 4.0f,
                out tbCS, out nudCS, out btnCSAuto);
            AddValueRow(grpDiff, 116, "DT",
                settings.ArDtValue, OverlaySettings.DtArMin, OverlaySettings.DtArMax, 2,
                (v) => { settings.ArDtValue = v; Save(); if (overlayRef != null) overlayRef.RefreshDifficulty(); },
                () => overlayRef != null ? overlayRef.GetMapDtAR() : 10.0f,
                out tbDtAR, out nudDtAR, out btnDtARAuto);
            // HT AR 상한 10 — HT는 AR을 낮추는 mod라 10 이상은 의미가 없다.
            // (DT만 10 초과 유지: AR10 맵+DT의 체감 AR은 10을 넘는다.)
            AddValueRow(grpDiff, 156, "HT",
                settings.ArHtValue, OverlaySettings.HtArMin, OverlaySettings.HtArMax, 2,
                (v) => { settings.ArHtValue = v; Save(); if (overlayRef != null) overlayRef.RefreshDifficulty(); },
                () => overlayRef != null ? overlayRef.GetMapHtAR() : 8.0f,
                out tbHtAR, out nudHtAR, out btnHtARAuto);

            contentPanel.Controls.Add(grpDiff);
            y += grpDiff.Height + 10;

            // ── Cursor ──
            Card grpCursor = new Card("OVERLAY CURSOR", cardX, y, cardW, 130);

            chkCursorAutoSize = MakeCheck("Auto Cursor Size", 16, 36, 160);
            chkCursorAutoSize.Checked = settings.CursorAutoSize;
            chkCursorAutoSize.CheckedChanged += (s, e) => { settings.CursorAutoSize = chkCursorAutoSize.Checked; Save(); };
            grpCursor.Controls.Add(chkCursorAutoSize);

            grpCursor.Controls.Add(MakeLabel("Cursor Size:", 16, 66, 74, Theme.Text));

            nudCursorSize = new NumericUpDown();
            nudCursorSize.Location = new Point(92, 63);
            nudCursorSize.Width = 64;
            StyleNud(nudCursorSize);
            nudCursorSize.Minimum = (decimal)OverlaySettings.CursorSizeMin;
            nudCursorSize.Maximum = (decimal)OverlaySettings.CursorSizeMax;
            nudCursorSize.DecimalPlaces = 2;
            nudCursorSize.Increment = 0.05m;
            // 방어: NaN/Infinity는 (decimal) 캐스트에서 OverflowException을 던지므로 float 단계에서
            // 먼저 살균한 뒤 범위로 클램프한다. (Normalize()가 이미 처리하지만 이중 방어.)
            float cursorSize = settings.CursorSize;
            if (float.IsNaN(cursorSize) || float.IsInfinity(cursorSize)) cursorSize = 1.0f;
            cursorSize = Math.Max(OverlaySettings.CursorSizeMin, Math.Min(OverlaySettings.CursorSizeMax, cursorSize));
            nudCursorSize.Value = (decimal)cursorSize;
            nudCursorSize.ValueChanged += (s, e) => { settings.CursorSize = (float)nudCursorSize.Value; Save(); };
            grpCursor.Controls.Add(nudCursorSize);

            // Cursor Pack
            grpCursor.Controls.Add(MakeLabel("Cursor Pack:", 16, 96, 74, Theme.Text));

            cmbCursorPack = new ComboBox();
            cmbCursorPack.Location = new Point(92, 93);
            cmbCursorPack.Width = 146;
            cmbCursorPack.DropDownStyle = ComboBoxStyle.DropDownList;
            StyleCombo(cmbCursorPack);
            RefreshCursorPacks();
            cmbCursorPack.SelectedIndexChanged += (s, e) =>
            {
                if (cmbCursorPack.SelectedIndex == 0)
                {
                    // Auto — pack 비활성화
                    settings.CursorPackEnabled = false;
                    settings.CursorPackName = "";
                }
                else
                {
                    settings.CursorPackEnabled = true;
                    settings.CursorPackName = (string)cmbCursorPack.SelectedItem;
                }
                Save();
                if (overlayRef != null) overlayRef.ReloadCursorPack();
            };
            grpCursor.Controls.Add(cmbCursorPack);

            btnRefreshCursorPacks = new Button();
            btnRefreshCursorPacks.Text = "Refresh";
            btnRefreshCursorPacks.Location = new Point(244, 92);
            btnRefreshCursorPacks.Width = 78;
            btnRefreshCursorPacks.Height = 25;
            StyleButton(btnRefreshCursorPacks);
            btnRefreshCursorPacks.Click += (s, e) => { RefreshCursorPacks(); };
            grpCursor.Controls.Add(btnRefreshCursorPacks);

            contentPanel.Controls.Add(grpCursor);
            y += grpCursor.Height + 10;

            // ── HUD ──
            Card grpHud = new Card("GAMEPLAY HUD", cardX, y, cardW, 196);

            chkHudFps = MakeCheck("FPS", 16, 36, 150);
            chkHudFps.Checked = settings.HudEnabled[0];
            chkHudFps.CheckedChanged += (s, e) => { settings.HudEnabled[0] = chkHudFps.Checked; Save(); };
            grpHud.Controls.Add(chkHudFps);

            chkHudAcc = MakeCheck("Accuracy", 172, 36, 150);
            chkHudAcc.Checked = settings.HudEnabled[1];
            chkHudAcc.CheckedChanged += (s, e) => { settings.HudEnabled[1] = chkHudAcc.Checked; Save(); };
            grpHud.Controls.Add(chkHudAcc);

            chkHudCombo = MakeCheck("Combo", 16, 60, 150);
            chkHudCombo.Checked = settings.HudEnabled[2];
            chkHudCombo.CheckedChanged += (s, e) => { settings.HudEnabled[2] = chkHudCombo.Checked; Save(); };
            grpHud.Controls.Add(chkHudCombo);

            chkHudHitError = MakeCheck("Hit Error Bar", 172, 60, 150);
            chkHudHitError.Checked = settings.HudEnabled[3];
            chkHudHitError.CheckedChanged += (s, e) => { settings.HudEnabled[3] = chkHudHitError.Checked; Save(); };
            grpHud.Controls.Add(chkHudHitError);

            btnEditLayout = new Button();
            btnEditLayout.Text = "Edit Layout";
            btnEditLayout.Location = new Point(16, 90);
            btnEditLayout.Width = 306;
            btnEditLayout.Height = 30;
            StyleAccentButton(btnEditLayout);
            btnEditLayout.Click += (s, e) =>
            {
                settings.HudEditMode = !settings.HudEditMode;
                btnEditLayout.Text = settings.HudEditMode ? "Stop Editing (Esc)" : "Edit Layout";
                Save();
            };
            grpHud.Controls.Add(btnEditLayout);

            // edit 모드 단축키 안내 (NEWNEWOVERLAY overlay_tab.cpp 힌트 텍스트)
            lblEditHint1 = new Label();
            lblEditHint1.Text = "Tab 순환 | Up/Down 크기 | Shift ×10";
            lblEditHint1.Location = new Point(16, 128);
            lblEditHint1.AutoSize = true;
            lblEditHint1.ForeColor = Theme.Hint;
            grpHud.Controls.Add(lblEditHint1);

            lblEditHint2 = new Label();
            lblEditHint2.Text = "드래그로 이동 | Esc 저장+종료";
            lblEditHint2.Location = new Point(16, 146);
            lblEditHint2.AutoSize = true;
            lblEditHint2.ForeColor = Theme.Hint;
            grpHud.Controls.Add(lblEditHint2);

            lblEditHint3 = new Label();
            lblEditHint3.Text = "S: 스냅 | X: 가로고정 | Y: 세로고정";
            lblEditHint3.Location = new Point(16, 164);
            lblEditHint3.AutoSize = true;
            lblEditHint3.ForeColor = Theme.Hint;
            grpHud.Controls.Add(lblEditHint3);

            contentPanel.Controls.Add(grpHud);
            y += grpHud.Height + 10;

            // ── Skin ──
            Card grpSkin = new Card("SKIN", cardX, y, cardW, 106);

            cmbSkin = new ComboBox();
            cmbSkin.Location = new Point(16, 36);
            cmbSkin.Width = 208;
            cmbSkin.DropDownStyle = ComboBoxStyle.DropDownList;
            StyleCombo(cmbSkin);
            RefreshSkins();
            cmbSkin.SelectedIndexChanged += (s, e) =>
            {
                string skinName = (string)cmbSkin.SelectedItem;
                settings.SkinName = skinName;
                Save();
                if (overlayRef != null)
                    overlayRef.ReloadSkin(skinName);
            };
            grpSkin.Controls.Add(cmbSkin);

            btnRefreshSkin = new Button();
            btnRefreshSkin.Text = "Refresh";
            btnRefreshSkin.Location = new Point(232, 35);
            btnRefreshSkin.Width = 90;
            btnRefreshSkin.Height = 25;
            StyleButton(btnRefreshSkin);
            btnRefreshSkin.Click += (s, e) => { RefreshSkins(); };
            grpSkin.Controls.Add(btnRefreshSkin);

            CheckBox chkInstaFade = MakeCheck("InstaFade (no hit animation)", 16, 68, 280);
            chkInstaFade.Checked = settings.InstaFade;
            chkInstaFade.CheckedChanged += (s, e) =>
            { settings.InstaFade = chkInstaFade.Checked; Save(); };
            grpSkin.Controls.Add(chkInstaFade);

            contentPanel.Controls.Add(grpSkin);
            y += grpSkin.Height + 10;

            // ── 하단 정보 ──
            Label lblInfo = new Label();
            lblInfo.Text = "F9 show/hide  ·  F10 quit  ·  close panel to exit";
            lblInfo.Location = new Point(cardX, y);
            lblInfo.Width = cardW;
            lblInfo.Height = 20;
            lblInfo.TextAlign = ContentAlignment.MiddleCenter;
            lblInfo.ForeColor = Theme.Muted;
            lblInfo.Font = new Font("Segoe UI", 8.25f);
            contentPanel.Controls.Add(lblInfo);

            // 스크롤 영역 하단 여백
            y += lblInfo.Height + 10;
            contentPanel.AutoScrollMinSize = new Size(340, y);

            // 스크롤 휠이 NumericUpDown, TrackBar, ListBox에서 값을 변경하지 않도록 차단
            // 스크롤 휠은 contentPanel의 스크롤바에서만 작동
            DisableMouseWheelOnControls(contentPanel);

            // 상태 동기화 타이머 — edit 버튼 텍스트 + 상태 표시
            Timer statusSync = new Timer();
            statusSync.Interval = 150;
            statusSync.Tick += (s, e) =>
            {
                string want = settings.HudEditMode ? "Stop Editing (Esc)" : "Edit Layout";
                if (btnEditLayout.Text != want) btnEditLayout.Text = want;

                if (overlayRef != null)
                {
                    if (lblStatus.Text != overlayRef.StatusText)
                    {
                        lblStatus.Text = overlayRef.StatusText;
                        lblStatus.ForeColor = overlayRef.StatusText.StartsWith("●") ? Theme.Green : Theme.Muted;
                    }
                    if (lblBeatmap.Text != overlayRef.BeatmapText)
                        lblBeatmap.Text = overlayRef.BeatmapText;

                    // CS 하한 강제 — 맵 CS(HR/EZ 반영) 아래로는 못 내려간다.
                    // 곡을 바꿔서 내 값이 새 곡의 CS보다 작아지면 즉시 새 곡 값으로 올린다.
                    // Compute도 Math.Max(CsValue, 맵CS)로 같은 하한을 걸지만, 여기서 슬라이더까지
                    // 올려야 사용자가 실제 적용값을 보게 된다.
                    if (overlayRef.LiveCS > 0)
                    {
                        decimal floor = (decimal)Math.Min(10, Math.Round(overlayRef.LiveCS, 2));
                        if (floor > nudCS.Value)
                            nudCS.Value = floor; // ValueChanged → settings.CsValue 반영 + Save
                    }
                }
            };
            statusSync.Start();
            // 폼 종료 시 타이머 정지·해제 (G6) — 예전엔 로컬 Timer라 프로세스 종료까지 계속 돌았다.
            this.FormClosed += (s, e) => { statusSync.Stop(); statusSync.Dispose(); };
        }

        /// <summary>
        /// TrackBar + NumericUpDown + Auto Button 행 추가.
        ///
        /// Auto는 모드가 아니라 일회성 동작이다 — 누르면 getMapValue()(= 현재 맵의 값,
        /// HR/EZ 반영)를 슬라이더에 채워넣고 끝이다. 슬라이더는 항상 편집 가능하고,
        /// 슬라이더 값이 곧 적용되는 값이다. "Manual로 바꿔놨는데 원래 맵 값으로
        /// 되돌리고 싶다"에 쓰는 버튼.
        /// </summary>
        void AddValueRow(Control parent, int yPos, string label,
            float value, float min, float max, int decimals,
            Action<float> onValueChanged,
            Func<float> getMapValue,
            out TrackBar outTb, out NumericUpDown outNud, out Button outBtnAuto)
        {
            parent.Controls.Add(MakeLabel(label, 16, yPos + 3, 34, Theme.Text));

            // 저장된 값이 [min,max] 밖이면 클램프 — TrackBar.Value/NumericUpDown.Value는 범위 밖
            // 대입 시 예외를 던진다. (settings.ini 손편집이나 슬라이더 상한 축소 후 로드에서 발생.)
            // 클램프됐으면 아래에서 설정에도 반영해야 한다 — 안 그러면 슬라이더는 10을
            // 보여주는데 settings에는 12가 남아 Compute가 12를 계속 쓴다.
            //
            // NaN/Infinity는 Math.Min/Max를 그대로 통과(전파)한 뒤 (decimal) 캐스트에서
            // OverflowException을 낸다 — 클램프 전에 먼저 살균해야 한다. 원본과 비교해
            // wasClamped를 판정하므로 NaN도 (min과 !=이라) 정상적으로 재저장된다.
            float original = value;
            if (float.IsNaN(value) || float.IsInfinity(value)) value = min;
            float clampedValue = Math.Max(min, Math.Min(max, value));
            bool wasClamped = clampedValue != original;
            value = clampedValue;

            TrackBar tb = new TrackBar();
            tb.Location = new Point(52, yPos);
            tb.Width = 144;
            tb.BackColor = Theme.Card;
            tb.TickStyle = TickStyle.None;
            tb.Minimum = (int)(min * 10);
            tb.Maximum = (int)(max * 10);
            tb.Value = (int)(value * 10);
            tb.TickFrequency = 10;
            NumericUpDown nud = new NumericUpDown();
            nud.Location = new Point(202, yPos + 1);
            nud.Width = 56;
            StyleNud(nud);
            nud.Minimum = (decimal)min;
            nud.Maximum = (decimal)max;
            nud.DecimalPlaces = decimals;
            nud.Increment = 0.1m;
            nud.Value = (decimal)value;
            tb.Scroll += (s, e) =>
            {
                float v = tb.Value / 10f;
                nud.Value = (decimal)v;
                onValueChanged(v);
            };
            nud.ValueChanged += (s, e) =>
            {
                float v = (float)nud.Value;
                tb.Value = Math.Max(tb.Minimum, Math.Min(tb.Maximum, (int)(v * 10)));
                onValueChanged(v);
            };
            parent.Controls.Add(tb);
            parent.Controls.Add(nud);

            Button btnAuto = new Button();
            btnAuto.Location = new Point(266, yPos);
            btnAuto.Width = 56;
            btnAuto.Height = 25;
            btnAuto.Text = "Auto";
            StyleButton(btnAuto);
            btnAuto.Click += (s, e) =>
            {
                float mapVal = getMapValue();
                if (float.IsNaN(mapVal) || float.IsInfinity(mapVal)) return;
                mapVal = Math.Max(min, Math.Min(max, mapVal));
                // nud.Value 변경이 ValueChanged를 타고 onValueChanged로 설정에 반영된다
                nud.Value = (decimal)Math.Round(mapVal, 2);
            };
            parent.Controls.Add(btnAuto);

            // 로드 값이 범위 밖이라 클램프됐다면 설정에도 반영(+저장). 핸들러는 위에서
            // 붙었으므로 이 시점 호출은 정상적으로 onValueChanged를 탄다.
            if (wasClamped)
                onValueChanged(value);

            outTb = tb;
            outNud = nud;
            outBtnAuto = btnAuto;
        }

        /// <summary>
        /// 컨트롤 패널 내의 NumericUpDown, TrackBar, ListBox에서 스크롤 휠이 값을 변경하지 않도록 차단.
        /// 스크롤 휠은 패널 스크롤바에서만 작동.
        /// </summary>
        void DisableMouseWheelOnControls(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                // ComboBox가 빠져 있어서 스킨/커서 목록이 휠에 반응했다.
                // 이 패널에 ListBox는 없지만(스킨·커서 모두 ComboBox), 타입 가드는 남겨둔다.
                if (c is NumericUpDown || c is TrackBar || c is ListBox || c is ComboBox)
                {
                    c.MouseWheel += (s, e) =>
                    {
                        // Handled=true면 Control.WmMouseWheel이 DefWndProc을 건너뛰어
                        // 네이티브 컨트롤의 기본 휠 처리(값/선택 변경)가 일어나지 않는다.
                        HandledMouseEventArgs h = e as HandledMouseEventArgs;
                        if (h != null) h.Handled = true;
                    };
                }
                // 재귀적으로 자식 컨트롤도 처리
                if (c.HasChildren)
                    DisableMouseWheelOnControls(c);
            }
        }

        void RefreshSkins()
        {
            string prevSkin = settings.SkinName;

            cmbSkin.Items.Clear();
            cmbSkin.Items.Add("Default");

            // osu! Skins 폴더에서 스킨 목록 스캔
            string osuDir = null;
            if (overlayRef != null)
                osuDir = overlayRef.GetOsuInstallDir();

            int selectedIndex = 0;

            if (osuDir != null && System.IO.Directory.Exists(osuDir))
            {
                string skinsPath = System.IO.Path.Combine(osuDir, "Skins");
                if (System.IO.Directory.Exists(skinsPath))
                {
                    // Exists가 true여도 ACL로 GetDirectories가 예외를 낼 수 있다 — 생성자에서 죽지 않도록 (A1).
                    string[] dirs;
                    try { dirs = System.IO.Directory.GetDirectories(skinsPath); }
                    catch { dirs = new string[0]; }
                    var skinNames = new System.Collections.Generic.List<string>();

                    foreach (string dir in dirs)
                    {
                        string name = System.IO.Path.GetFileName(dir);
                        skinNames.Add(name);
                    }

                    skinNames.Sort(System.StringComparer.OrdinalIgnoreCase);

                    foreach (string name in skinNames)
                    {
                        cmbSkin.Items.Add(name);
                        if (string.Equals(name, prevSkin, System.StringComparison.OrdinalIgnoreCase))
                            selectedIndex = cmbSkin.Items.Count - 1;
                    }
                }
            }

            cmbSkin.SelectedIndex = selectedIndex;
        }

        /// <summary>
        /// overlay-cursors\ 폴더에서 커서 팩 목록 스캔.
        /// cursor.png 또는 cursor@2x.png가 있는 폴더만 유효한 팩으로 인식.
        /// </summary>
        void RefreshCursorPacks()
        {
            string prevSelection = settings.CursorPackEnabled ? settings.CursorPackName : "";

            cmbCursorPack.Items.Clear();
            cmbCursorPack.Items.Add("Auto");

            string exeDir = System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            string packsRoot = System.IO.Path.Combine(exeDir, "overlay-cursors");

            // overlay-cursors 폴더가 없으면 생성 — 쓰기 불가 폴더(예: Program Files)에서 실행 시
            // CreateDirectory가 예외를 던져 생성자(=메인 폼)가 죽으면 앱이 아예 안 뜬다 (A1).
            if (!System.IO.Directory.Exists(packsRoot))
            {
                try { System.IO.Directory.CreateDirectory(packsRoot); }
                catch { /* 팩 목록 없이 진행 — Auto만 표시 */ }
            }

            int selectedIndex = 0; // Auto

            if (System.IO.Directory.Exists(packsRoot))
            {
                // Exists가 true여도 ACL/리파스포인트로 GetDirectories가 access-denied를 낼 수 있다.
                string[] dirs;
                try { dirs = System.IO.Directory.GetDirectories(packsRoot); }
                catch { dirs = new string[0]; }
                var validPacks = new System.Collections.Generic.List<string>();

                foreach (string dir in dirs)
                {
                    string name = System.IO.Path.GetFileName(dir);
                    // cursor.png 또는 cursor@2x.png가 있어야 유효
                    if (System.IO.File.Exists(System.IO.Path.Combine(dir, "cursor.png")) ||
                        System.IO.File.Exists(System.IO.Path.Combine(dir, "cursor@2x.png")))
                    {
                        validPacks.Add(name);
                    }
                }

                validPacks.Sort(System.StringComparer.OrdinalIgnoreCase);

                foreach (string name in validPacks)
                {
                    cmbCursorPack.Items.Add(name);
                    if (string.Equals(name, prevSelection, System.StringComparison.OrdinalIgnoreCase))
                        selectedIndex = cmbCursorPack.Items.Count - 1;
                }
            }

            cmbCursorPack.SelectedIndex = selectedIndex;
        }

        void Save()
        {
            SettingsSerializer.Save(settings);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Save();
            base.OnFormClosing(e);
        }
    }
}
