using System;
using System.Drawing;
using System.Windows.Forms;
using OsuEnlightenOverlay.Overlay;

namespace OsuEnlightenOverlay.ControlPanel
{
    /// <summary>
    /// 컨트롤 패널 — NEWNEWOVERLAY overlay_tab.cpp WinForms 포팅.
    /// Overlay / Difficulty / Cursor / HUD / Skin 섹션.
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

            // 스크롤 가능한 컨텐츠 패널 — 모든 섹션을 담아 아래 잘림 방지.
            Panel contentPanel = new Panel();
            contentPanel.Dock = DockStyle.Fill;
            contentPanel.AutoScroll = true;
            contentPanel.BorderStyle = BorderStyle.None;
            this.Controls.Add(contentPanel);

            int y = 10;

            // ── 상태 표시 (맨 위) ──
            Label lblStatus = new Label();
            lblStatus.Text = "● Ready";
            lblStatus.Location = new Point(10, y);
            lblStatus.Width = 330;
            lblStatus.Height = 20;
            lblStatus.TextAlign = ContentAlignment.MiddleCenter;
            lblStatus.Font = new Font(SystemFonts.DefaultFont.FontFamily, 9, FontStyle.Bold);
            contentPanel.Controls.Add(lblStatus);
            y += lblStatus.Height + 3;

            Label lblBeatmap = new Label();
            lblBeatmap.Text = "";
            lblBeatmap.Location = new Point(10, y);
            lblBeatmap.Width = 330;
            lblBeatmap.Height = 18;
            lblBeatmap.TextAlign = ContentAlignment.MiddleCenter;
            lblBeatmap.ForeColor = Color.DimGray;
            contentPanel.Controls.Add(lblBeatmap);
            y += lblBeatmap.Height + 10;

            // ── Overlay ──
            GroupBox grpOverlay = new GroupBox();
            grpOverlay.Text = "Overlay";
            grpOverlay.Location = new Point(10, y);
            grpOverlay.Width = 330;
            grpOverlay.Height = 120;

            chkEnabled = new CheckBox();
            chkEnabled.Text = "Enable Overlay";
            chkEnabled.AutoSize = false;
            chkEnabled.Width = 140;
            chkEnabled.Location = new Point(15, 22);
            chkEnabled.Checked = settings.Enabled;
            chkEnabled.CheckedChanged += (s, e) =>
            {
                settings.Enabled = chkEnabled.Checked;
                if (overlayRef != null) overlayRef.ApplyFpsCap();
                Save();
            };
            grpOverlay.Controls.Add(chkEnabled);

            chkCaptureBlocked = new CheckBox();
            chkCaptureBlocked.Text = "Capture Blocked";
            chkCaptureBlocked.AutoSize = false;
            chkCaptureBlocked.Width = 140;
            chkCaptureBlocked.Location = new Point(170, 22);
            chkCaptureBlocked.Checked = settings.CaptureBlocked;
            chkCaptureBlocked.CheckedChanged += (s, e) => { settings.CaptureBlocked = chkCaptureBlocked.Checked; Save(); };
            grpOverlay.Controls.Add(chkCaptureBlocked);

            chkHiddenOverride = new CheckBox();
            chkHiddenOverride.Text = "Hidden Override";
            chkHiddenOverride.AutoSize = false;
            chkHiddenOverride.Width = 140;
            chkHiddenOverride.Location = new Point(15, 42);
            chkHiddenOverride.Checked = settings.HiddenOverride;
            chkHiddenOverride.CheckedChanged += (s, e) => { settings.HiddenOverride = chkHiddenOverride.Checked; Save(); };
            grpOverlay.Controls.Add(chkHiddenOverride);

            Label lblFps = new Label();
            lblFps.Text = "FPS Cap:";
            lblFps.Location = new Point(15, 70);
            lblFps.Width = 55;
            lblFps.TextAlign = ContentAlignment.MiddleLeft;
            grpOverlay.Controls.Add(lblFps);

            nudFpsCap = new NumericUpDown();
            nudFpsCap.Location = new Point(75, 68);
            nudFpsCap.Width = 80;
            nudFpsCap.Minimum = 0;
            nudFpsCap.Maximum = 10000;
            nudFpsCap.Value = settings.FpsCap;
            nudFpsCap.ValueChanged += (s, e) =>
            {
                settings.FpsCap = (int)nudFpsCap.Value;
                if (overlayRef != null) overlayRef.ApplyFpsCap();
                Save();
            };
            grpOverlay.Controls.Add(nudFpsCap);

            Label lblFpsHint = new Label();
            lblFpsHint.Text = "(0 = unlimited)";
            lblFpsHint.Location = new Point(160, 70);
            lblFpsHint.Width = 120;
            lblFpsHint.ForeColor = Color.Gray;
            lblFpsHint.TextAlign = ContentAlignment.MiddleLeft;
            grpOverlay.Controls.Add(lblFpsHint);

            contentPanel.Controls.Add(grpOverlay);
            y += grpOverlay.Height + 10;

            // ── Difficulty Changer ──
            GroupBox grpDiff = new GroupBox();
            grpDiff.Text = "Difficulty Changer";
            grpDiff.Location = new Point(10, y);
            grpDiff.Width = 330;
            grpDiff.Height = 200;

            AddValueRow(grpDiff, 20, "AR",
                settings.ArValue, settings.ArAuto, 0, 12, 2,
                (v) => { settings.ArValue = v; Save(); if (overlayRef != null) overlayRef.RefreshDifficulty(); },
                (b) => { settings.ArAuto = b; Save(); if (overlayRef != null) overlayRef.RefreshDifficulty(); },
                () => overlayRef != null ? overlayRef.GetMapAR() : 9.0f,
                out tbAR, out nudAR, out btnARAuto);
            AddValueRow(grpDiff, 60, "CS",
                settings.CsValue, settings.CsAuto, 0, 10, 2,
                (v) => { settings.CsValue = v; Save(); if (overlayRef != null) overlayRef.RefreshDifficulty(); },
                (b) => { settings.CsAuto = b; Save(); if (overlayRef != null) overlayRef.RefreshDifficulty(); },
                () => overlayRef != null ? overlayRef.GetMapCS() : 4.0f,
                out tbCS, out nudCS, out btnCSAuto);
            AddValueRow(grpDiff, 100, "DT",
                settings.ArDtValue, settings.ArDtAuto, 0, 12, 2,
                (v) => { settings.ArDtValue = v; Save(); if (overlayRef != null) overlayRef.RefreshDifficulty(); },
                (b) => { settings.ArDtAuto = b; Save(); if (overlayRef != null) overlayRef.RefreshDifficulty(); },
                () => overlayRef != null ? overlayRef.GetMapDtAR() : 10.0f,
                out tbDtAR, out nudDtAR, out btnDtARAuto);
            AddValueRow(grpDiff, 140, "HT",
                settings.ArHtValue, settings.ArHtAuto, 0, 12, 2,
                (v) => { settings.ArHtValue = v; Save(); if (overlayRef != null) overlayRef.RefreshDifficulty(); },
                (b) => { settings.ArHtAuto = b; Save(); if (overlayRef != null) overlayRef.RefreshDifficulty(); },
                () => overlayRef != null ? overlayRef.GetMapHtAR() : 8.0f,
                out tbHtAR, out nudHtAR, out btnHtARAuto);

            contentPanel.Controls.Add(grpDiff);
            y += grpDiff.Height + 10;

            // ── Cursor ──
            GroupBox grpCursor = new GroupBox();
            grpCursor.Text = "Overlay Cursor";
            grpCursor.Location = new Point(10, y);
            grpCursor.Width = 330;
            grpCursor.Height = 115;

            chkCursorAutoSize = new CheckBox();
            chkCursorAutoSize.Text = "Auto Cursor Size";
            chkCursorAutoSize.Location = new Point(15, 20);
            chkCursorAutoSize.AutoSize = false;
            chkCursorAutoSize.Width = 140;
            chkCursorAutoSize.Checked = settings.CursorAutoSize;
            chkCursorAutoSize.CheckedChanged += (s, e) => { settings.CursorAutoSize = chkCursorAutoSize.Checked; Save(); };
            grpCursor.Controls.Add(chkCursorAutoSize);

            Label lblCursorSize = new Label();
            lblCursorSize.Text = "Cursor Size:";
            lblCursorSize.Location = new Point(15, 48);
            lblCursorSize.Width = 70;
            grpCursor.Controls.Add(lblCursorSize);

            nudCursorSize = new NumericUpDown();
            nudCursorSize.Location = new Point(90, 45);
            nudCursorSize.Width = 70;
            nudCursorSize.Minimum = 0.1m;
            nudCursorSize.Maximum = 2.0m;
            nudCursorSize.DecimalPlaces = 2;
            nudCursorSize.Increment = 0.05m;
            nudCursorSize.Value = (decimal)settings.CursorSize;
            nudCursorSize.ValueChanged += (s, e) => { settings.CursorSize = (float)nudCursorSize.Value; Save(); };
            grpCursor.Controls.Add(nudCursorSize);

            // Cursor Pack
            Label lblCursorPack = new Label();
            lblCursorPack.Text = "Cursor Pack:";
            lblCursorPack.Location = new Point(15, 78);
            lblCursorPack.Width = 70;
            grpCursor.Controls.Add(lblCursorPack);

            cmbCursorPack = new ComboBox();
            cmbCursorPack.Location = new Point(90, 75);
            cmbCursorPack.Width = 145;
            cmbCursorPack.DropDownStyle = ComboBoxStyle.DropDownList;
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
            btnRefreshCursorPacks.Location = new Point(240, 75);
            btnRefreshCursorPacks.Width = 65;
            btnRefreshCursorPacks.Click += (s, e) => { RefreshCursorPacks(); };
            grpCursor.Controls.Add(btnRefreshCursorPacks);

            contentPanel.Controls.Add(grpCursor);
            y += grpCursor.Height + 10;

            // ── HUD ──
            GroupBox grpHud = new GroupBox();
            grpHud.Text = "Gameplay HUD";
            grpHud.Location = new Point(10, y);
            grpHud.Width = 330;
            grpHud.Height = 165;

            chkHudFps = new CheckBox();
            chkHudFps.Text = "FPS";
            chkHudFps.Location = new Point(15, 20);
            chkHudFps.Checked = settings.HudEnabled[0];
            chkHudFps.CheckedChanged += (s, e) => { settings.HudEnabled[0] = chkHudFps.Checked; Save(); };
            grpHud.Controls.Add(chkHudFps);

            chkHudAcc = new CheckBox();
            chkHudAcc.Text = "Accuracy";
            chkHudAcc.Location = new Point(170, 20);
            chkHudAcc.Checked = settings.HudEnabled[1];
            chkHudAcc.CheckedChanged += (s, e) => { settings.HudEnabled[1] = chkHudAcc.Checked; Save(); };
            grpHud.Controls.Add(chkHudAcc);

            chkHudCombo = new CheckBox();
            chkHudCombo.Text = "Combo";
            chkHudCombo.Location = new Point(15, 45);
            chkHudCombo.Checked = settings.HudEnabled[2];
            chkHudCombo.CheckedChanged += (s, e) => { settings.HudEnabled[2] = chkHudCombo.Checked; Save(); };
            grpHud.Controls.Add(chkHudCombo);

            chkHudHitError = new CheckBox();
            chkHudHitError.Text = "Hit Error Bar";
            chkHudHitError.Location = new Point(170, 45);
            chkHudHitError.Checked = settings.HudEnabled[3];
            chkHudHitError.CheckedChanged += (s, e) => { settings.HudEnabled[3] = chkHudHitError.Checked; Save(); };
            grpHud.Controls.Add(chkHudHitError);

            btnEditLayout = new Button();
            btnEditLayout.Text = "Edit Layout";
            btnEditLayout.Location = new Point(15, 75);
            btnEditLayout.Width = 290;
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
            lblEditHint1.Location = new Point(15, 103);
            lblEditHint1.AutoSize = true;
            lblEditHint1.ForeColor = Color.FromArgb(180, 140, 0);
            grpHud.Controls.Add(lblEditHint1);

            lblEditHint2 = new Label();
            lblEditHint2.Text = "드래그로 이동 | Esc 저장+종료";
            lblEditHint2.Location = new Point(15, 123);
            lblEditHint2.AutoSize = true;
            lblEditHint2.ForeColor = Color.FromArgb(180, 140, 0);
            grpHud.Controls.Add(lblEditHint2);

            lblEditHint3 = new Label();
            lblEditHint3.Text = "S: 스냅 | X: 가로고정 | Y: 세로고정";
            lblEditHint3.Location = new Point(15, 143);
            lblEditHint3.AutoSize = true;
            lblEditHint3.ForeColor = Color.FromArgb(180, 140, 0);
            grpHud.Controls.Add(lblEditHint3);

            contentPanel.Controls.Add(grpHud);
            y += grpHud.Height + 10;

            // ── Skin ──
            GroupBox grpSkin = new GroupBox();
            grpSkin.Text = "Skin";
            grpSkin.Location = new Point(10, y);
            grpSkin.Width = 330;
            grpSkin.Height = 105;

            cmbSkin = new ComboBox();
            cmbSkin.Location = new Point(15, 20);
            cmbSkin.Width = 200;
            cmbSkin.DropDownStyle = ComboBoxStyle.DropDownList;
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
            btnRefreshSkin.Location = new Point(225, 18);
            btnRefreshSkin.Width = 80;
            btnRefreshSkin.Click += (s, e) => { RefreshSkins(); };
            grpSkin.Controls.Add(btnRefreshSkin);

            CheckBox chkInstaFade = new CheckBox();
            chkInstaFade.Text = "InstaFade (no hit animation)";
            chkInstaFade.AutoSize = false;
            chkInstaFade.Width = 250;
            chkInstaFade.Location = new Point(15, 50);
            chkInstaFade.Checked = settings.InstaFade;
            chkInstaFade.CheckedChanged += (s, e) =>
            { settings.InstaFade = chkInstaFade.Checked; Save(); };
            grpSkin.Controls.Add(chkInstaFade);

            contentPanel.Controls.Add(grpSkin);
            y += grpSkin.Height + 10;

            // ── 하단 정보 ──
            Label lblInfo = new Label();
            lblInfo.Text = "Close this panel to exit";
            lblInfo.Location = new Point(10, y);
            lblInfo.Width = 330;
            lblInfo.Height = 20;
            lblInfo.TextAlign = ContentAlignment.MiddleCenter;
            lblInfo.ForeColor = Color.Gray;
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
                        lblStatus.ForeColor = overlayRef.StatusText.StartsWith("●") ? Color.FromArgb(0, 180, 0) : Color.Gray;
                    }
                    if (lblBeatmap.Text != overlayRef.BeatmapText)
                        lblBeatmap.Text = overlayRef.BeatmapText;

                    // CS override 모드일 때 mod 적용 CS보다 작으면 상향
                    // LiveCS는 이미 HR/EZ mod가 적용된 effective CS
                    if (!settings.CsAuto && overlayRef.LiveCS > 0)
                    {
                        float effectiveCs = overlayRef.LiveCS;
                        if ((decimal)effectiveCs > nudCS.Value)
                        {
                            nudCS.Value = (decimal)Math.Min(10, Math.Round(effectiveCs, 2));
                            settings.CsValue = (float)nudCS.Value;
                        }
                    }
                }
            };
            statusSync.Start();
        }

        /// <summary>
        /// TrackBar + NumericUpDown + Auto Button 행 추가.
        /// Auto 버튼: 클릭 시 현재 파싱된 맵 값으로 채우고 수동 모드로 전환.
        /// 수동 모드에서 다시 클릭하면 Auto 모드로 복귀.
        /// </summary>
        void AddValueRow(GroupBox parent, int yPos, string label,
            float value, bool auto, float min, float max, int decimals,
            Action<float> onValueChanged, Action<bool> onAutoChanged,
            Func<float> getMapValue,
            out TrackBar outTb, out NumericUpDown outNud, out Button outBtnAuto)
        {
            Label lbl = new Label();
            lbl.Text = label + ":";
            lbl.Location = new Point(15, yPos + 3);
            lbl.Width = 35;
            parent.Controls.Add(lbl);

            TrackBar tb = new TrackBar();
            tb.Location = new Point(55, yPos);
            tb.Width = 140;
            tb.Minimum = (int)(min * 10);
            tb.Maximum = (int)(max * 10);
            tb.Value = (int)(value * 10);
            tb.TickFrequency = 10;
            NumericUpDown nud = new NumericUpDown();
            nud.Location = new Point(200, yPos);
            nud.Width = 50;
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
                tb.Value = (int)(v * 10);
                onValueChanged(v);
            };
            parent.Controls.Add(tb);
            parent.Controls.Add(nud);

            Button btnAuto = new Button();
            btnAuto.Location = new Point(255, yPos);
            btnAuto.Width = 55;
            btnAuto.Height = 23;
            btnAuto.Tag = (object)auto; // Tag에 auto 상태 저장 (Button.Checked 없음)
            UpdateAutoButton(btnAuto, auto, tb, nud);
            btnAuto.Click += (s, e) =>
            {
                bool curAuto = (bool)btnAuto.Tag;
                bool newAuto = !curAuto; // 토글
                if (newAuto)
                {
                    // Auto 모드 진입 — 파싱된 맵 값으로 채우기
                    float mapVal = getMapValue();
                    mapVal = Math.Max(min, Math.Min(max, mapVal));
                    tb.Value = (int)(mapVal * 10);
                    nud.Value = (decimal)mapVal;
                }
                btnAuto.Tag = (object)newAuto;
                UpdateAutoButton(btnAuto, newAuto, tb, nud);
                onAutoChanged(newAuto);
            };
            parent.Controls.Add(btnAuto);

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
                if (c is NumericUpDown || c is TrackBar || c is ListBox)
                {
                    c.MouseWheel += (s, e) =>
                    {
                        // 스크롤 휠 이벤트를 무시하고 부모 패널의 스크롤로 전달
                        ((HandledMouseEventArgs)e).Handled = true;
                    };
                }
                // 재귀적으로 자식 컨트롤도 처리
                if (c.HasChildren)
                    DisableMouseWheelOnControls(c);
            }
        }

        void UpdateAutoButton(Button btn, bool auto, TrackBar tb, NumericUpDown nud)
        {
            if (auto)
            {
                btn.Text = "Auto";
                btn.BackColor = SystemColors.Control;
                btn.ForeColor = Color.Green;
                tb.Enabled = false;
                nud.Enabled = false;
            }
            else
            {
                btn.Text = "Manual";
                btn.BackColor = SystemColors.Control;
                btn.ForeColor = Color.Black;
                tb.Enabled = true;
                nud.Enabled = true;
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
                    string[] dirs = System.IO.Directory.GetDirectories(skinsPath);
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

            // overlay-cursors 폴더가 없으면 생성
            if (!System.IO.Directory.Exists(packsRoot))
                System.IO.Directory.CreateDirectory(packsRoot);

            int selectedIndex = 0; // Auto

            if (System.IO.Directory.Exists(packsRoot))
            {
                string[] dirs = System.IO.Directory.GetDirectories(packsRoot);
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