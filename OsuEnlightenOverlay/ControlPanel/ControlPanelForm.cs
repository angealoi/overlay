using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using OsuEnlightenOverlay.Overlay;

namespace OsuEnlightenOverlay.ControlPanel
{
    /// <summary>
    /// 컨트롤 패널 — AByteCheat 의 커스텀 VGUI 메뉴 스타일을 WinForms owner-draw 컨트롤로 재현.
    ///
    /// 레이아웃/동작은 기존과 동일(세로 스크롤 + 5개 카드)하되, 컨트롤은 모두 AByteCheat
    /// 원본 색상(28,28,28 다크 + 초록 액센트)과 owner-draw 스타일을 따른다.
    /// 외부 접점(생성자 시그니처, OverlayForm API 호출, 설정 저장/로드)은 변경 없다.
    /// </summary>
    public class ControlPanelForm : Form
    {
        OverlaySettings settings;

        // Overlay 섹션
        AbCheckBox chkEnabled, chkCaptureBlocked, chkHiddenOverride;
        AbSlider slFpsCap;
        OverlayForm overlayRef;

        // Difficulty 섹션
        AbSlider slAR, slCS, slDtAR, slHtAR;
        AbButton btnARAuto, btnCSAuto, btnDtARAuto, btnHtARAuto;

        // Cursor 섹션
        AbCheckBox chkCursorAutoSize;
        AbSlider slCursorSize;
        AbComboBox cmbCursorPack;
        AbButton btnRefreshCursorPacks;

        // HUD 섹션
        AbCheckBox chkHudFps, chkHudAcc, chkHudCombo, chkHudHitError;
        AbButton btnEditLayout;
        Label lblEditHint1, lblEditHint2;

        // Skin 섹션
        AbComboBox cmbSkin;
        AbButton btnRefreshSkin;

        // statusSync 타이머가 갱신할 컨트롤 — 로컬 변수로 잡히면 타이머 클로저에서 접근 불가.
        Label lblStatus, lblBeatmap;

        // ── FormBorderStyle=None 폼의 외곽선 ──
        // AByteCheat 원본 스타일(GUI.cpp:411-424) — 3중 외곽선:
        //   본체(28,28,28) → 6px 회색 띠(40,40,40) → 2중 가는 라인(60,60,60 + 10,10,10)
        // 원본은 초록이 아니다 — 초록 액센트는 체크박스/슬라이더 채움에만 쓰인다.
        // 폼 OnPaint 로 그려야 3중이 가능하므로, contentPanel/titleBar 는 Padding(8) 안쪽.
        const int BorderWidth = 8;  // 외곽선 총 두께 (6px 띠 + 2중 라인)

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            System.Drawing.Graphics g = e.Graphics;
            Rectangle r = ClientRectangle;

            // 1) 6px 회색 띠 (40,40,40) — 윈도우 둘레. 원본 Render::Clear 6px 사각형 4개.
            using (SolidBrush band = new SolidBrush(AbTheme.LightGray))
            {
                g.FillRectangle(band, 0, 0, r.Width, BorderWidth);                    // 상
                g.FillRectangle(band, 0, r.Height - BorderWidth, r.Width, BorderWidth); // 하
                g.FillRectangle(band, 0, 0, BorderWidth, r.Height);                   // 좌
                g.FillRectangle(band, r.Width - BorderWidth, 0, BorderWidth, r.Height); // 우
            }

            // 2) 2중 외곽 라인 — 안쪽 (60,60,60), 바깥 (10,10,10). 원본 Render::Outline 2중.
            using (Pen darkOut = new Pen(AbTheme.Outline))              // 바깥 어두운 라인 (10,10,10)
            using (Pen lightIn = new Pen(Color.FromArgb(60, 60, 60)))   // 안쪽 밝은 라인
            {
                // 바깥 라인 — 폭 전체 가장자리.
                g.DrawRectangle(darkOut, r.X, r.Y, r.Width - 1, r.Height - 1);
                // 안쪽 라인 — 6px 띠와 본체 경계.
                int ix = BorderWidth - 1;
                g.DrawRectangle(lightIn, ix, ix, r.Width - 2 * BorderWidth + 1, r.Height - 2 * BorderWidth + 1);
            }
        }

        // ── 라벨 팩토리 — AByteCheat CLabel 과 동일 (200,200,200 / 150,150,150) ──

        static Label MakeLabel(string text, int x, int y, int width, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.Width = width;
            l.ForeColor = color;
            l.BackColor = AbTheme.GroupBg;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Font = new Font("Verdana", 9f);
            return l;
        }

        public ControlPanelForm(OverlaySettings settings, OverlayForm overlay)
        {
            this.settings = settings;
            this.overlayRef = overlay;
            // 외곽 마감 — AByteCheat 3중 외곽선(6px 띠 + 2중 라인). OnPaint 로 그리므로
            // 자식이 덮지 않게 Padding(BorderWidth=8) 안쪽에 contentPanel 배치.
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);

            this.Text = "osu! Enlighten Overlay — Control Panel";
            // 2열 레이아웃 — 카드를 좌/우 열에 배치해 세로 스크롤 없이 한 화면에.
            // 폭은 좌우 여백 대칭 기준으로 유도: marginX*2 + cardW*2 + gapCol.
            // FormBorderStyle=None 이므로 클라이언트 폭 == Width.
            // 폼 폭 = 외곽선(Padding) + 좌우 여백 + 카드 2열 + 열 간격.
            // BorderWidth(8)*2 + marginX(10)*2 + cardW(420)*2 + gapCol(14) = 890.
            // Padding 을 빼면 contentPanel 폭 = 858... 이 아니라 890-16=874 가 되고,
            // 우측 열 끝 col2X+cardW = 864 → 우측 여백 10 = marginX (대칭).
            this.Width = 890;
            this.Height = 520;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = AbTheme.Gray;            // (28,28,28) 본체색
            this.ForeColor = AbTheme.TextRegular;     // (200,200,200)
            this.Font = new Font("Verdana", 9f);
            this.Padding = new Padding(BorderWidth);  // 외곽선 두께만큼 자식 밀어냄

            // Dock 배치는 컨트롤 추가 '역순'으로 처리된다. Fill 패널을 먼저 추가하고
            // 타이틀바(Top)를 나중에 추가해야 타이틀바가 위쪽을 차지, Fill 패널이 그 아래.
            Panel contentPanel = new Panel();
            contentPanel.Dock = DockStyle.Fill;
            contentPanel.AutoScroll = false;
            contentPanel.BorderStyle = BorderStyle.None;
            contentPanel.BackColor = AbTheme.Gray;
            this.Controls.Add(contentPanel);

            // ── 커스텀 타이틀바 — AByteCheat 메뉴 상단 헤더 재현 (Top 도킹, 나중 추가) ──
            AbTitleBar titleBar = new AbTitleBar();
            titleBar.Title = "osu! Enlighten Overlay";
            this.Controls.Add(titleBar);

            // ── 레이아웃 상수 — 2열 배치 기준 ──
            // marginX: 좌우 대칭 외부 여백. 우측 여백도 같은 값이 되도록 폼 폭은
            // marginX*2 + cardW*2 + gapCol 로 유도된다(위 this.Width 참조).
            const int marginX = 10;        // 좌/우 외부 여백 (대칭)
            const int cardW = 420;         // 카드 폭 (열당 하나)
            const int gapCol = 14;         // 좌/우 열 간격
            const int col2X = marginX + cardW + gapCol;  // 우측 열 x
            const int cardGapY = 10;       // 세로 카드 간격
            const int pad = 20;            // 카드 내부 좌측 패딩
            const int colL = pad;          // 카드 내 좌측 컬럼 x
            const int colR = 230;          // 카드 내 우측 컬럼 x (2열 체크박스용)
            const int ctrlW = cardW - pad - 24;        // 컨트롤 가용폭(우측 여유 24)
            // ─ Difficulty 행: label(30) + gap(8) + slider + gap(10) + Auto(60) ─
            const int labelW = 30;
            const int labelGap = 8;
            const int sliderX = pad + labelW + labelGap;     // 슬라이더 시작 x
            const int autoW = 60;                            // Auto 버튼 폭
            const int autoGap = 10;                          // 슬라이더-Auto 간격
            const int autoX = pad + ctrlW - autoW;           // Auto 버튼 x (우측 정렬)
            const int sliderW = autoX - sliderX - autoGap;   // 슬라이더 폭 (Auto와 안 겹치게)
            // ─ 카드 내부 상/하 여백 규칙 (모든 카드 동일) ─
            const int contentTopY = 40;
            const int bottomPad = 12;
            const int checkRowGap = 26;
            // ─ 콤보박스 + Refresh 버튼 공통 규격 (Cursor Pack / Skin 동일) ─
            // 두 섹션이 같은 폭/높이/위치 규칙을 써서 좌우 폭을 통일한다.
            const int refreshW = 90;                       // Refresh 버튼 폭
            const int refreshGap = 10;                     // 콤보-버튼 간격
            const int refreshH = 24;                       // Refresh 버튼 높이
            const int comboW = ctrlW - refreshW - refreshGap; // 콤보박스 폭 (우측 버튼 공간 제외)

            // ── 상태 표시 바 — 폼 전체 폭, 타이틀바와 카드 사이 정중앙 ──
            // 게임 모드(Playing/Menu 등) + 비트맵 정보를 가운데 정렬로 보여준다.
            // 양쪽 카드 열은 이 바 아래 같은 y에서 시작한다.
            int contentW = col2X + cardW - marginX;  // 전체 콘텐츠 폭 (marginX~marginX 대칭)
            lblStatus = new Label();
            lblStatus.Text = "● Ready";
            lblStatus.Location = new Point(marginX, 12);
            lblStatus.Width = contentW;
            lblStatus.Height = 22;
            lblStatus.TextAlign = ContentAlignment.MiddleCenter;
            lblStatus.Font = new Font("Tahoma", 10f, FontStyle.Bold);
            lblStatus.BackColor = AbTheme.Gray;
            lblStatus.ForeColor = AbTheme.Green;
            contentPanel.Controls.Add(lblStatus);

            lblBeatmap = new Label();
            lblBeatmap.Text = "";
            lblBeatmap.Location = new Point(marginX, 36);
            lblBeatmap.Width = contentW;
            lblBeatmap.Height = 18;
            lblBeatmap.TextAlign = ContentAlignment.MiddleCenter;
            lblBeatmap.BackColor = AbTheme.Gray;
            lblBeatmap.ForeColor = AbTheme.TextOff;
            lblBeatmap.Font = new Font("Verdana", 8.25f);
            contentPanel.Controls.Add(lblBeatmap);

            // 카드 시작 y — 상태 바(36+18=54) 아래 충분한 여백(18)을 두어 타이틀바와 분리.
            int cardY0 = 54 + 18;
            // 좌/우 열 모두 같은 y에서 시작 — yR 이 yL 과 어긋나던 문제 수정.
            int yL = cardY0;
            int yR = cardY0;

            // ── Overlay ── [좌측 열] 카드높이 = 마지막 객체 끝(114) + bottomPad(12) = 126
            int overlayH = 114 + bottomPad;
            AbCard grpOverlay = new AbCard("OVERLAY", marginX, yL, cardW, overlayH);

            chkEnabled = new AbCheckBox { Text = "Enable Overlay" };
            chkEnabled.Location = new Point(colL, contentTopY);
            chkEnabled.Width = colR - colL;
            chkEnabled.Checked = settings.Enabled;
            chkEnabled.CheckedChanged += (s, e) =>
            {
                settings.Enabled = chkEnabled.Checked;
                if (overlayRef != null) overlayRef.ApplyFpsCap();
                Save();
            };
            grpOverlay.Controls.Add(chkEnabled);

            chkCaptureBlocked = new AbCheckBox { Text = "Capture Blocked" };
            chkCaptureBlocked.Location = new Point(colR, contentTopY);
            chkCaptureBlocked.Width = ctrlW - colR + pad;
            chkCaptureBlocked.Checked = settings.CaptureBlocked;
            chkCaptureBlocked.CheckedChanged += (s, e) => { settings.CaptureBlocked = chkCaptureBlocked.Checked; Save(); };
            grpOverlay.Controls.Add(chkCaptureBlocked);

            chkHiddenOverride = new AbCheckBox { Text = "Hidden Override" };
            chkHiddenOverride.Location = new Point(colL, contentTopY + checkRowGap);
            chkHiddenOverride.Width = ctrlW;
            chkHiddenOverride.Checked = settings.HiddenOverride;
            chkHiddenOverride.CheckedChanged += (s, e) => { settings.HiddenOverride = chkHiddenOverride.Checked; Save(); };
            grpOverlay.Controls.Add(chkHiddenOverride);

            // "FPS Cap:" 라벨에 (0 = unlimited) 힌트 통합 — 별도 라벨은 카드 밖으로 튀어나가는 문제가 있어 제거.
            grpOverlay.Controls.Add(MakeLabel("FPS Cap (0 = unlimited):", colL, 96, 160, AbTheme.TextRegular));

            // FPS Cap 은 정수 — AbSlider 를 정수 모드로. 0=unlimited 의미 살리기 위해
            // Min=0, Max=FpsCapMax, DecimalPlaces=0.
            slFpsCap = new AbSlider();
            slFpsCap.Location = new Point(colL + 164, 94);
            slFpsCap.Width = ctrlW - 164;
            slFpsCap.Height = 22;
            slFpsCap.DecimalPlaces = 0;
            slFpsCap.Minimum = OverlaySettings.FpsCapMin;
            slFpsCap.Maximum = OverlaySettings.FpsCapMax;
            // 방어: Normalize()가 이미 범위 안으로 맞추지만, 범위 밖 값이 새어들어와도 예외 없이.
            int fpsVal = (int)Math.Min(slFpsCap.Maximum, Math.Max(slFpsCap.Minimum, settings.FpsCap));
            slFpsCap.Value = fpsVal;
            settings.FpsCap = fpsVal;
            slFpsCap.ValueChanged += (s, e) =>
            {
                settings.FpsCap = (int)slFpsCap.Value;
                if (overlayRef != null) overlayRef.ApplyFpsCap();
                Save();
            };
            grpOverlay.Controls.Add(slFpsCap);

            contentPanel.Controls.Add(grpOverlay);
            yL += grpOverlay.Height + cardGapY;

            // ── Difficulty Changer ── [우측 열] 최상단
            // 행 간격 40, 첫 행 contentTopY(40) → 40/80/120/160. 마지막 끝 182 + bottomPad 12 = 카드높이 194.
            // 우측 열 시작 y는 상태 라벨과 같은 높이(14)에서 시작해 정렬을 맞춘다.
            AbCard grpDiff = new AbCard("DIFFICULTY CHANGER", col2X, yR, cardW, 194);

            const int diffRowGap = 40;
            AddValueRow(grpDiff, contentTopY + 0 * diffRowGap, sliderX, sliderW, autoX, autoW, pad, labelW, "AR",
                settings.ArValue, OverlaySettings.ArMin, OverlaySettings.ArMax, 2,
                (v) => { settings.ArValue = v; Save(); if (overlayRef != null) overlayRef.RefreshDifficulty(); },
                () => overlayRef != null ? overlayRef.GetMapAR() : 9.0f,
                out slAR, out btnARAuto);
            AddValueRow(grpDiff, contentTopY + 1 * diffRowGap, sliderX, sliderW, autoX, autoW, pad, labelW, "CS",
                settings.CsValue, OverlaySettings.CsMin, OverlaySettings.CsMax, 2,
                (v) => { settings.CsValue = v; Save(); if (overlayRef != null) overlayRef.RefreshDifficulty(); },
                () => overlayRef != null ? overlayRef.GetAutoCS() : 4.0f,
                out slCS, out btnCSAuto);
            AddValueRow(grpDiff, contentTopY + 2 * diffRowGap, sliderX, sliderW, autoX, autoW, pad, labelW, "DT",
                settings.ArDtValue, OverlaySettings.DtArMin, OverlaySettings.DtArMax, 2,
                (v) => { settings.ArDtValue = v; Save(); if (overlayRef != null) overlayRef.RefreshDifficulty(); },
                () => overlayRef != null ? overlayRef.GetMapDtAR() : 10.0f,
                out slDtAR, out btnDtARAuto);
            AddValueRow(grpDiff, contentTopY + 3 * diffRowGap, sliderX, sliderW, autoX, autoW, pad, labelW, "HT",
                settings.ArHtValue, OverlaySettings.HtArMin, OverlaySettings.HtArMax, 2,
                (v) => { settings.ArHtValue = v; Save(); if (overlayRef != null) overlayRef.RefreshDifficulty(); },
                () => overlayRef != null ? overlayRef.GetMapHtAR() : 8.0f,
                out slHtAR, out btnHtARAuto);

            contentPanel.Controls.Add(grpDiff);
            yR += grpDiff.Height + cardGapY;

            // ── Cursor ── [좌측 열] OVERLAY 아래
            // (마지막 객체 끝 132 + bottomPad 12 = 카드높이 144)
            AbCard grpCursor = new AbCard("OVERLAY CURSOR", marginX, yL, cardW, 144);

            chkCursorAutoSize = new AbCheckBox { Text = "Auto Cursor Size" };
            chkCursorAutoSize.Location = new Point(colL, contentTopY);
            chkCursorAutoSize.Width = ctrlW;
            chkCursorAutoSize.Checked = settings.CursorAutoSize;
            chkCursorAutoSize.CheckedChanged += (s, e) => { settings.CursorAutoSize = chkCursorAutoSize.Checked; Save(); };
            grpCursor.Controls.Add(chkCursorAutoSize);

            grpCursor.Controls.Add(MakeLabel("Cursor Size:", colL, 68, 90, AbTheme.TextRegular));

            slCursorSize = new AbSlider();
            slCursorSize.Location = new Point(colL, 86);
            slCursorSize.Width = ctrlW;
            slCursorSize.Height = 22;
            slCursorSize.DecimalPlaces = 2;
            slCursorSize.Minimum = OverlaySettings.CursorSizeMin;
            slCursorSize.Maximum = OverlaySettings.CursorSizeMax;
            float cursorSize = settings.CursorSize;
            if (float.IsNaN(cursorSize) || float.IsInfinity(cursorSize)) cursorSize = 1.0f;
            cursorSize = Math.Max(OverlaySettings.CursorSizeMin, Math.Min(OverlaySettings.CursorSizeMax, cursorSize));
            slCursorSize.Value = cursorSize;
            slCursorSize.ValueChanged += (s, e) => { settings.CursorSize = slCursorSize.Value; Save(); };
            grpCursor.Controls.Add(slCursorSize);

            // Cursor Pack — 콤보 + 우측 Refresh 버튼. "Pack:" 라벨은 콤보가 역할을 대신하므로 생략.
            // 공통 규격(refreshW/comboW) 사용 — Skin 섹션과 좌우 폭/버튼 크기 동일.
            cmbCursorPack = new AbComboBox();
            cmbCursorPack.Location = new Point(colL, 112);
            cmbCursorPack.Width = comboW;
            cmbCursorPack.Height = 22;
            RefreshCursorPacks();
            cmbCursorPack.SelectedIndexChanged += (s, e) =>
            {
                int idx = cmbCursorPack.SelectedIndex;
                if (idx == 0)
                {
                    settings.CursorPackEnabled = false;
                    settings.CursorPackName = "";
                }
                else if (idx > 0)
                {
                    settings.CursorPackEnabled = true;
                    settings.CursorPackName = (string)cmbCursorPack.SelectedItem;
                }
                Save();
                if (overlayRef != null) overlayRef.ReloadCursorPack();
            };
            grpCursor.Controls.Add(cmbCursorPack);

            btnRefreshCursorPacks = new AbButton { Text = "Refresh" };
            btnRefreshCursorPacks.Location = new Point(colL + comboW + refreshGap, 111);
            btnRefreshCursorPacks.Width = refreshW;
            btnRefreshCursorPacks.Height = refreshH;
            btnRefreshCursorPacks.Click += (s, e) => { RefreshCursorPacks(); };
            grpCursor.Controls.Add(btnRefreshCursorPacks);

            contentPanel.Controls.Add(grpCursor);
            yL += grpCursor.Height + cardGapY;

            // ── HUD ── [우측 열] DIFFICULTY 아래
            // 힌트 2줄(압축) 끝 178 + bottomPad 12 = 카드높이 190.
            // 우측 열 = 72 + 194 + 10 + 190 = 466 → 좌측 열(SKIN 끝 466)과 하단 일치.
            AbCard grpHud = new AbCard("GAMEPLAY HUD", col2X, yR, cardW, 190);

            chkHudFps = new AbCheckBox { Text = "FPS" };
            chkHudFps.Location = new Point(colL, contentTopY);
            chkHudFps.Width = colR - colL;
            chkHudFps.Checked = settings.HudEnabled[0];
            chkHudFps.CheckedChanged += (s, e) => { settings.HudEnabled[0] = chkHudFps.Checked; Save(); };
            grpHud.Controls.Add(chkHudFps);

            chkHudAcc = new AbCheckBox { Text = "Accuracy" };
            chkHudAcc.Location = new Point(colR, contentTopY);
            chkHudAcc.Width = ctrlW - colR + pad;
            chkHudAcc.Checked = settings.HudEnabled[1];
            chkHudAcc.CheckedChanged += (s, e) => { settings.HudEnabled[1] = chkHudAcc.Checked; Save(); };
            grpHud.Controls.Add(chkHudAcc);

            chkHudCombo = new AbCheckBox { Text = "Combo" };
            chkHudCombo.Location = new Point(colL, contentTopY + checkRowGap);
            chkHudCombo.Width = colR - colL;
            chkHudCombo.Checked = settings.HudEnabled[2];
            chkHudCombo.CheckedChanged += (s, e) => { settings.HudEnabled[2] = chkHudCombo.Checked; Save(); };
            grpHud.Controls.Add(chkHudCombo);

            chkHudHitError = new AbCheckBox { Text = "Hit Error Bar" };
            chkHudHitError.Location = new Point(colR, contentTopY + checkRowGap);
            chkHudHitError.Width = ctrlW - colR + pad;
            chkHudHitError.Checked = settings.HudEnabled[3];
            chkHudHitError.CheckedChanged += (s, e) => { settings.HudEnabled[3] = chkHudHitError.Checked; Save(); };
            grpHud.Controls.Add(chkHudHitError);

            btnEditLayout = new AbButton { Text = "Edit Layout", Accent = true };
            btnEditLayout.Location = new Point(colL, 96);
            btnEditLayout.Width = ctrlW;
            btnEditLayout.Height = 32;
            btnEditLayout.Click += (s, e) =>
            {
                settings.HudEditMode = !settings.HudEditMode;
                btnEditLayout.Text = settings.HudEditMode ? "Stop Editing (Esc)" : "Edit Layout";
                Save();
            };
            grpHud.Controls.Add(btnEditLayout);

            // edit 모드 단축키 힌트 — 3줄을 2줄로 압축해 HUD 카드를 줄이고
            // 우측 열 하단을 좌측 열(SKIN) 하단과 맞춘다.
            lblEditHint1 = new Label();
            lblEditHint1.Text = "Tab 순환 | Up/Down 크기 | Shift ×10 | 드래그로 이동";
            lblEditHint1.Location = new Point(colL, 138);
            lblEditHint1.AutoSize = true;
            lblEditHint1.BackColor = AbTheme.GroupBg;
            lblEditHint1.ForeColor = AbTheme.Hint;
            lblEditHint1.Font = new Font("Verdana", 8.25f);
            grpHud.Controls.Add(lblEditHint1);

            lblEditHint2 = new Label();
            lblEditHint2.Text = "Esc 저장+종료 | S: 스냅 | X: 가로고정 | Y: 세로고정";
            lblEditHint2.Location = new Point(colL, 158);
            lblEditHint2.AutoSize = true;
            lblEditHint2.BackColor = AbTheme.GroupBg;
            lblEditHint2.ForeColor = AbTheme.Hint;
            lblEditHint2.Font = new Font("Verdana", 8.25f);
            grpHud.Controls.Add(lblEditHint2);

            contentPanel.Controls.Add(grpHud);
            yR += grpHud.Height + cardGapY;

            // ── Skin ── [좌측 열] OVERLAY CURSOR 아래
            // (InstaFade 체크 끝 92 + bottomPad 12 = 카드높이 104)
            AbCard grpSkin = new AbCard("SKIN", marginX, yL, cardW, 104);

            // 공통 규격(refreshW/comboW) 사용 — Cursor Pack 섹션과 좌우 폭/버튼 크기 동일.
            cmbSkin = new AbComboBox();
            cmbSkin.Location = new Point(colL, contentTopY);
            cmbSkin.Width = comboW;
            cmbSkin.Height = 22;
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

            btnRefreshSkin = new AbButton { Text = "Refresh" };
            btnRefreshSkin.Location = new Point(colL + comboW + refreshGap, contentTopY - 1);
            btnRefreshSkin.Width = refreshW;
            btnRefreshSkin.Height = refreshH;
            btnRefreshSkin.Click += (s, e) => { RefreshSkins(); };
            grpSkin.Controls.Add(btnRefreshSkin);

            AbCheckBox chkInstaFade = new AbCheckBox { Text = "InstaFade (no hit animation)" };
            chkInstaFade.Location = new Point(colL, 72);
            chkInstaFade.Width = ctrlW;
            chkInstaFade.Checked = settings.InstaFade;
            chkInstaFade.CheckedChanged += (s, e) =>
            { settings.InstaFade = chkInstaFade.Checked; Save(); };
            grpSkin.Controls.Add(chkInstaFade);

            contentPanel.Controls.Add(grpSkin);
            yL += grpSkin.Height + cardGapY;

            // 스크롤 휠이 Ab* 컨트롤에서 값을 변경하지 않도록 차단.
            // 2열 레이아웃에선 스크롤이 없지만 컨트롤 위에서 휠이 의도치 않게 동작하지 않게.
            DisableMouseWheelOnControls(contentPanel);

            // 상태 동기화 타이머 — edit 버튼 텍스트 + 상태 표시 + CS 하한 강제.
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
                        lblStatus.ForeColor = overlayRef.StatusText.StartsWith("●") ? AbTheme.Green : AbTheme.TextOff;
                    }
                    if (lblBeatmap.Text != overlayRef.BeatmapText)
                        lblBeatmap.Text = overlayRef.BeatmapText;

                    // CS 하한 강제 — 맵 CS(HR/EZ 반영) 아래로는 못 내려간다.
                    if (overlayRef.LiveCS > 0)
                    {
                        float floor = Math.Min(10, (float)Math.Round(overlayRef.LiveCS, 2));
                        if (floor > slCS.Value)
                            slCS.Value = floor; // ValueChanged → settings.CsValue 반영 + Save
                    }
                }
            };
            statusSync.Start();
            this.FormClosed += (s, e) => { statusSync.Stop(); statusSync.Dispose(); };
        }

        /// <summary>
        /// AbSlider + Auto 버튼 행 추가 (TrackBar+NUD 통합 버전).
        ///
        /// 기존과 동일 — Auto는 일회성 동작(현재 맵 값을 슬라이더에 채움).
        /// 로드 값이 [min,max] 밖이면 클램프 후 설정에도 반영(wasClamped).
        /// NaN/Infinity는 AbSlider.Value setter에서도 살균하지만 여기서도 이중 방어.
        /// </summary>
        void AddValueRow(Control parent, int yPos, int sliderX, int sliderW, int autoX, int autoW,
            int labelX, int labelW, string label,
            float value, float min, float max, int decimals,
            Action<float> onValueChanged,
            Func<float> getMapValue,
            out AbSlider outSlider, out AbButton outBtnAuto)
        {
            parent.Controls.Add(MakeLabel(label, labelX, yPos + 3, labelW, AbTheme.TextRegular));

            // 방어: 클램프 전 NaN/Infinity 살균. 원본과 비교해 wasClamped 판정.
            float original = value;
            if (float.IsNaN(value) || float.IsInfinity(value)) value = min;
            float clampedValue = Math.Max(min, Math.Min(max, value));
            bool wasClamped = clampedValue != original;
            value = clampedValue;

            AbSlider sl = new AbSlider();
            sl.Location = new Point(sliderX, yPos);
            sl.Width = sliderW;
            sl.Height = 22;
            sl.DecimalPlaces = decimals;
            sl.Minimum = min;
            sl.Maximum = max;
            sl.Value = value;
            sl.ValueChanged += (s, e) => onValueChanged(sl.Value);
            parent.Controls.Add(sl);

            AbButton btnAuto = new AbButton { Text = "Auto" };
            btnAuto.Location = new Point(autoX, yPos);
            btnAuto.Width = autoW;
            btnAuto.Height = 25;
            btnAuto.Click += (s, e) =>
            {
                float mapVal = getMapValue();
                if (float.IsNaN(mapVal) || float.IsInfinity(mapVal)) return;
                mapVal = Math.Max(min, Math.Min(max, mapVal));
                // sl.Value 변경이 ValueChanged → onValueChanged 로 설정에 반영된다
                sl.Value = (float)Math.Round(mapVal, 2);
            };
            parent.Controls.Add(btnAuto);

            // 로드 값이 범위 밖이라 클램프됐다면 설정에도 반영(+저장)
            if (wasClamped)
                onValueChanged(value);

            outSlider = sl;
            outBtnAuto = btnAuto;
        }

        /// <summary>
        /// 컨트롤 패널 내의 AbSlider/AbComboBox 등에서 스크롤 휠이 값을 변경하지 않도록 차단.
        /// 스크롤 휠은 패널 스크롤바에서만 작동.
        /// </summary>
        void DisableMouseWheelOnControls(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                // Ab* 컨트롤은 모두 Control 파생이므로 타입 가드로 걸러야 한다.
                // 표준 NumericUpDown/TrackBar/ListBox/ComboBox 도 호환성 위해 남김.
                if (c is AbSlider || c is AbComboBox || c is AbButton || c is AbCheckBox ||
                    c is NumericUpDown || c is TrackBar || c is ListBox || c is ComboBox)
                {
                    c.MouseWheel += (s, e) =>
                    {
                        HandledMouseEventArgs h = e as HandledMouseEventArgs;
                        if (h != null) h.Handled = true;
                    };
                }
                if (c.HasChildren)
                    DisableMouseWheelOnControls(c);
            }
        }

        void RefreshSkins()
        {
            string prevSkin = settings.SkinName;

            cmbSkin.Items.Clear();
            cmbSkin.Items.Add("Default");

            string osuDir = null;
            if (overlayRef != null)
                osuDir = overlayRef.GetOsuInstallDir();

            int selectedIndex = 0;

            if (osuDir != null && System.IO.Directory.Exists(osuDir))
            {
                string skinsPath = System.IO.Path.Combine(osuDir, "Skins");
                if (System.IO.Directory.Exists(skinsPath))
                {
                    // Exists가 true여도 ACL로 GetDirectories가 예외를 낼 수 있다 (A1).
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

            // overlay-cursors 폴더가 없으면 생성 — 쓰기 불가 폴더에서 CreateDirectory 가 예외를
            // 던지면 생성자(=메인 폼)가 죽어 앱이 아예 안 뜬다 (A1).
            if (!System.IO.Directory.Exists(packsRoot))
            {
                try { System.IO.Directory.CreateDirectory(packsRoot); }
                catch { /* 팩 목록 없이 진행 — Auto만 표시 */ }
            }

            int selectedIndex = 0; // Auto

            if (System.IO.Directory.Exists(packsRoot))
            {
                string[] dirs;
                try { dirs = System.IO.Directory.GetDirectories(packsRoot); }
                catch { dirs = new string[0]; }
                var validPacks = new System.Collections.Generic.List<string>();

                foreach (string dir in dirs)
                {
                    string name = System.IO.Path.GetFileName(dir);
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
