using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OsuEnlightenOverlay.ControlPanel
{
    // ════════════════════════════════════════════════════════════════════════
    //  AByteCheat 스타일 owner-draw 컨트롤 — Controls.cpp 의 각 Draw()를
    //  WinForms OnPaint 로 1:1 이식. AByteCheat은 Source 엔진 ISurface 위에
    //  직접 그리는 커스텀 메뉴라 WinForms 컨트롤과는 근본적으로 다르지만,
    //  시각적 스타일(그라디언트/2중 외곽선/9×9 체크/7px 슬라이더)은 동일하게
    //  재현한다.
    //
    //  모든 Ab* 컨트롤은 AbCard 내부에 놓인다고 가정한다 — 배경을 GroupBg(25,25,25)
    //  으로 칠해서 카드 배경과 자연스럽게 이어진다.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 배경 칠하기 공통 헬퍼 — UserPaint 컨트롤은 자기 배경을 직접 그려야 한다.
    /// 카드 자식이므로 GroupBg(25,25,25)로 통일.
    /// </summary>
    static class AbPaint
    {
        // 프로젝트에 OsuEnlightenOverlay.Graphics 네임스페이스가 있어 System.Drawing.Graphics 와
        // 충돌 — 인자를 완전 한정명으로 받는다.
        public static void FillCardBg(System.Drawing.Graphics g, Rectangle r)
        {
            using (SolidBrush b = new SolidBrush(AbTheme.GroupBg))
                g.FillRectangle(b, r);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  AbCard — CGroupBox 재현 (Controls.cpp:359 CGroupBox::Draw)
    //  배경 (25,25,25) + 2중 외곽선((48,48,48) 안 + (10,10,10) 바깥) +
    //  좌측 상단 타이틀. AByteCheat 원본은 라인 4개로 외곽을 그렸다.
    // ────────────────────────────────────────────────────────────────────────
    internal class AbCard : Panel
    {
        static readonly Font TitleFont = new Font("Tahoma", 9f, FontStyle.Bold);
        readonly string title;

        public AbCard(string title, int x, int y, int width, int height)
        {
            this.title = title;
            Location = new Point(x, y);
            Size = new Size(width, height);
            BackColor = AbTheme.GroupBg;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.ContainerControl | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            System.Drawing.Graphics g = e.Graphics;
            Rectangle r = ClientRectangle;

            // AByteCheat은 라인 4개(상/하/좌/우)로 외곽을 그린다. 우리는 단순히
            // 외곽 사각형 2중(바깥 어둡게, 안쪽 밝게)으로 재현. 원본은 (48,48,48) +
            // (10,10,10) 2중선. g.DrawRectangle 은 1px 두께라 2번 그린다.
            using (Pen darkOut = new Pen(AbTheme.GroupDark))   // 바깥 (10,10,10)
            using (Pen lightIn = new Pen(AbTheme.GroupLight))  // 안쪽 (48,48,48)
            {
                // 외곽 — 클라이언트 영역 안쪽 1px 안으로.
                Rectangle outer = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
                g.DrawRectangle(darkOut, outer);
                g.DrawRectangle(lightIn, outer.X + 1, outer.Y + 1, outer.Width - 2, outer.Height - 2);
            }

            // 타이틀 — 외곽선 '안쪽' 상단 헤더 영역. 외곽선과 겹치지 않게 하여
            // 텍스트가 밖으로 튀어나가 보이지 않게 한다. 헤더 하단에 구분선 1px 추가로
            // 타이틀과 본문 컨텐츠를 시각적으로 분리.
            const int headerH = 26;
            Rectangle header = new Rectangle(3, 3, r.Width - 6, headerH);
            // 타이틀 텍스트 — 좌측 패딩 14, 수직 중앙 정렬.
            TextRenderer.DrawText(g, title, TitleFont,
                new Rectangle(header.X + 6, header.Y, header.Width - 12, header.Height),
                AbTheme.TextRegular,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // 헤더 구분선 — (48,48,48) 단선. 타이틀 아래 본문 영역과 분리.
            int sepY = header.Bottom;
            using (Pen sep = new Pen(AbTheme.GroupLight))
                g.DrawLine(sep, header.X, sepY, header.Right, sepY);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  AbCheckBox — CCheckBox 재현 (Controls.cpp:263 CCheckBox::Draw)
    //  9×9px 박스. hover/non-hover 수직 그라디언트 배경. 체크 시 MenuColor
    //  그라디언트로 채움. (10,10,10) 아웃라인. 라벨은 박스 우측.
    // ────────────────────────────────────────────────────────────────────────
    internal class AbCheckBox : Control
    {
        bool @checked;
        bool hovered;

        // AByteCheat 원본 박스 사이즈 — 9×9.
        const int BoxSize = 13; // 9는 WinForms에서 너무 작아 클릭 어려움 → 13으로 살짝 키움 (스타일 유지)

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return @checked; }
            set
            {
                if (@checked != value)
                {
                    @checked = value;
                    Invalidate();
                    CheckedChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public AbCheckBox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.StandardClick, true);
            BackColor = AbTheme.GroupBg;
            Font = new Font("Verdana", 9f);
            Cursor = Cursors.Hand;
            Height = 20;
        }

        // owner-draw — Control.Text setter 가 자동 Invalidate 하지 않으므로 Text 변경 시 다시 그림.
        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            // AByteCheat CCheckBox::OnClick — Checked 토글
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            System.Drawing.Graphics g = e.Graphics;
            AbPaint.FillCardBg(g, ClientRectangle);

            // 박스 — 좌측, 수직 중앙
            int boxY = (Height - BoxSize) / 2;
            Rectangle box = new Rectangle(0, boxY, BoxSize, BoxSize);

            // 배경 그라디언트 — hover/non-hover (Controls.cpp:289-296)
            Color g1 = hovered ? AbTheme.GradHoverFirst : AbTheme.GradNotHoverFirst;
            Color g2 = hovered ? AbTheme.GradHoverSecond : AbTheme.GradNotHoverSecond;
            using (LinearGradientBrush gb = AbTheme.VerticalGradient(box, g1, g2))
                g.FillRectangle(gb, box);

            // 체크 시 MenuColor 그라디언트 덮어칠 (Controls.cpp:299-303)
            if (@checked)
            {
                using (LinearGradientBrush ab = AbTheme.VerticalGradient(box, AbTheme.Accent, AbTheme.AccentDark))
                    g.FillRectangle(ab, box);
            }

            // 외곽 (10,10,10)
            using (Pen p = new Pen(AbTheme.Outline))
                g.DrawRectangle(p, box.X, box.Y, box.Width - 1, box.Height - 1);

            // 라벨 텍스트 — 박스 우측. CLabel 과 동일 색 (200,200,200).
            string text = Text ?? "";
            Rectangle textRect = new Rectangle(box.Right + 6, 0, Width - box.Right - 6, Height);
            TextRenderer.DrawText(g, text, Font, textRect, AbTheme.TextRegular,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  AbSlider — CSlider 재현 (Controls.cpp:542 CSlider::Draw)
    //  7px 높이 막대. 그라디언트 배경 + 채워진 부분은 MenuColor 그라디언트.
    //  값이 막대 위(중앙)에 굵은 폰트로 표시. TrackBar + NumericUpDown 통합.
    // ────────────────────────────────────────────────────────────────────────
    internal class AbSlider : Control
    {
        float min, max, value;
        int decimals = 2;
        bool hovered;
        bool dragging;
        string extension = "";

        // Ctrl+클릭 직접 값 입력 — 임시 TextBox 를 슬라이더 위에 띄운다.
        // editing 플래그 대신 editBox != null 여부로 상태 판단 (재진입 가드 겸용).
        TextBox editBox;

        // 막대 높이 — AByteCheat 원본 7px. 값 텍스트가 위에 오므로 컨트롤은 더 큼.
        const int BarHeight = 7;

        public event EventHandler ValueChanged;
        public event EventHandler Scroll; // 드래그 중 연속 발생 (TrackBar.Scroll 호환)

        public float Minimum { get { return min; } set { min = value; Invalidate(); } }
        public float Maximum { get { return max; } set { max = value; Invalidate(); } }
        public int DecimalPlaces { get { return decimals; } set { decimals = value; Invalidate(); } }
        public string Extension { get { return extension; } set { extension = value; Invalidate(); } }

        public float Value
        {
            get { return value; }
            set
            {
                float clamped = Clamp(value);
                if (this.value != clamped)
                {
                    this.value = clamped;
                    Invalidate();
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        float Clamp(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) v = min;
            if (v < min) v = min;
            if (v > max) v = max;
            return v;
        }

        public AbSlider()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = AbTheme.GroupBg;
            Height = 22;
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            // Ctrl+클릭 — 직접 값 입력창을 띄운다. 일반 클릭/드래그와 분기.
            if (e.Button == MouseButtons.Left && (Control.ModifierKeys & Keys.Control) == Keys.Control)
            {
                BeginEditValue();
                return;
            }
            // 입력창이 열려 있으면 드래그 무시.
            if (e.Button == MouseButtons.Left && editBox == null)
            {
                dragging = true;
                UpdateValueFromMouse(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging && e.Button == MouseButtons.Left)
                UpdateValueFromMouse(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            base.OnMouseUp(e);
        }

        // ── Ctrl+클릭 직접 값 입력 ──
        // 임시 TextBox 를 슬라이더 위에 띄워 사용자가 숫자를 직접 입력하게 한다.
        // Enter: 확정(파싱→Clamp→Value 반영), Esc/포커스 상실: 취소.
        // decimals 에 맞춰 현재값을 미리 채워둔다.
        void BeginEditValue()
        {
            if (editBox != null) return; // 이미 입력창이 열려 있으면 무시

            dragging = false;

            editBox = new TextBox();
            editBox.Font = new Font("Tahoma", 8.25f, FontStyle.Bold);
            editBox.BackColor = AbTheme.Gray;
            editBox.ForeColor = AbTheme.TextRegular;
            editBox.BorderStyle = BorderStyle.FixedSingle;
            // 막대 영역(아래쪽) 위에 덮되 컨트롤 전체폭으로.
            editBox.Location = new Point(0, 0);
            editBox.Size = new Size(Width, Height);
            editBox.TextAlign = HorizontalAlignment.Center;
            // 현재값을 decimals 자리로 미리 채움 — 위에 뜬 값 텍스트와 동일 형식.
            editBox.Text = value.ToString("F" + decimals.ToString());
            editBox.SelectionStart = 0;
            editBox.SelectionLength = editBox.Text.Length;

            editBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitEdit(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CancelEdit(); }
            };
            editBox.LostFocus += (s, e) =>
            {
                // 포커스를 잃으면 취소 — 사용자가 다른 곳 클릭 시 입력창이 닫힌다.
                CancelEdit();
            };

            this.Controls.Add(editBox);
            editBox.BringToFront();
            editBox.Focus();
            editBox.SelectAll();
        }

        void CommitEdit()
        {
            // editBox 가 없으면 이미 닫힌 상태 — 재진입 무시.
            if (editBox == null) return;
            string raw = editBox.Text ?? "";
            float parsed;
            // float.TryParse — InvariantCulture 로 소수점 '.' 파싱 (문화권 무관).
            // 실패(빈 문자열/비숫자)면 조용히 취소(원래값 유지).
            if (float.TryParse(raw.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out parsed))
            {
                EndEdit();      // 먼저 입력창 닫기 (그래야 ValueChanged 중 재진입 안 됨)
                Value = parsed; // Clamp + ValueChanged 발생 → onValueChanged → settings 저장
                return;
            }
            EndEdit();
        }

        void CancelEdit()
        {
            EndEdit();
        }

        void EndEdit()
        {
            // LostFocus 가 Controls.Remove 중에 재발생해 EndEdit 이 재진입할 수 있다.
            // editBox 를 먼저 로컬로 잡고 null 로 만든 뒤 처리 — 두 번째 진입은 즉시 반환.
            TextBox box = editBox;
            if (box == null) return;
            editBox = null;

            // 핸들러가 박스 참조를 쥐고 있어도, 박스가 이미 컨트롤에서 떨어진 뒤라면
            // LostFocus 가 다시 트리거되지 않게 먼저 핸들러를 뗀다.
            try { this.Controls.Remove(box); } catch { }
            try { box.Dispose(); } catch { }
            Invalidate();
            // Parent.Focus() 는 이전 버전에서 NullReferenceException 을 냈다 —
            // 폼/다이얼로그가 닫히는 도중에는 Parent 가 null 일 수 있고, Focus 자체도
            // 윈도우 핸들 상태에 따라 예외를 던진다. 포커스 복귀는 필수가 아니므로 제거.
        }

        void UpdateValueFromMouse(int mx)
        {
            // AByteCheat CSlider::OnUpdate — NewX = mouse.x - a.x, 비율로 값 변환.
            int barY = Height - BarHeight - 2;
            int barW = Width;
            if (barW <= 0) return;
            float ratio = (float)mx / barW;
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;
            float newVal = min + (max - min) * ratio;
            newVal = Clamp(newVal);
            if (value != newVal)
            {
                value = newVal; // 직접 세팅 — Scroll 이벤트(연속)는 ValueChanged 와 같이 발생
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
                Scroll?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            System.Drawing.Graphics g = e.Graphics;
            AbPaint.FillCardBg(g, ClientRectangle);

            int barW = Width;
            int barY = Height - BarHeight - 2;
            Rectangle bar = new Rectangle(0, barY, barW, BarHeight);

            // 배경 그라디언트 — hover/non-hover (Controls.cpp:548-555)
            Color g1 = hovered ? AbTheme.GradHoverFirst : AbTheme.GradNotHoverSecond;
            Color g2 = hovered ? AbTheme.GradHoverSecond : AbTheme.GradNotHoverSecond;
            using (LinearGradientBrush gb = AbTheme.VerticalGradient(bar, g1, g2))
                g.FillRectangle(gb, bar);

            // 채워진 부분 — MenuColor 그라디언트 (Controls.cpp:557-577)
            float ratio = (max > min) ? (value - min) / (max - min) : 0;
            int filledW = (int)(ratio * barW);
            if (filledW > 0)
            {
                Rectangle filled = new Rectangle(0, barY, filledW, BarHeight);
                using (LinearGradientBrush ab = AbTheme.VerticalGradient(filled, AbTheme.Accent, AbTheme.AccentDark))
                    g.FillRectangle(ab, filled);
            }

            // 외곽 (Controls.cpp:579)
            using (Pen p = new Pen(AbTheme.Outline))
                g.DrawRectangle(p, bar.X, bar.Y, bar.Width - 1, bar.Height - 1);

            // 값 텍스트 — 막대 중앙 위. MenuBold 풍 굵은 폰트. (Controls.cpp:590-591)
            string fmt = "F" + decimals.ToString();
            string valText = value.ToString(fmt) + extension;
            using (Font valFont = new Font("Tahoma", 8.25f, FontStyle.Bold))
            {
                SizeF ts = g.MeasureString(valText, valFont);
                float tx = filledW - ts.Width / 2;
                if (tx < 0) tx = 0;
                if (tx + ts.Width > barW) tx = barW - ts.Width;
                float ty = barY - ts.Height - 1;
                if (ty < 0) ty = 0;
                TextRenderer.DrawText(g, valText, valFont,
                    new Point((int)tx, (int)ty), AbTheme.TextRegular,
                    TextFormatFlags.NoPadding);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  AbButton — CButton 재현 (Controls.cpp:768~)
    //  25px 그라디언트 배경 박스. hover/non-hover 구분. 가운데 정렬 텍스트.
    // ────────────────────────────────────────────────────────────────────────
    internal class AbButton : Control
    {
        bool hovered;
        bool mouseDown;
        bool accent;

        public bool Accent
        {
            get { return accent; }
            set { accent = value; Invalidate(); }
        }

        // owner-draw 컨트롤은 Control.Text setter 가 자동 Invalidate 하지 않으므로,
        // Text 가 바뀌면(예: Edit Layout ↔ Stop Editing) 즉시 다시 그린다.
        // 이게 없으면 타이머가 Text 를 바꿔도 hover 같은 트리거가 있어야 화면에 반영된다.
        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        public AbButton()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = AbTheme.GroupBg;
            Font = new Font("Verdana", 9f);
            Cursor = Cursors.Hand;
            Height = 25;
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; mouseDown = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { mouseDown = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { mouseDown = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            System.Drawing.Graphics g = e.Graphics;
            AbPaint.FillCardBg(g, ClientRectangle);

            Rectangle r = ClientRectangle;

            // 배경 그라디언트 — hover/non-hover
            Color g1, g2;
            Color textColor = AbTheme.TextRegular;
            if (accent)
            {
                // 액센트 버튼 — MenuColor 계열
                g1 = hovered ? AbTheme.Accent : AbTheme.Accent;
                g2 = hovered ? AbTheme.Accent : AbTheme.AccentDark;
                textColor = Color.Black; // 초록 위에 검은 글자
                if (mouseDown) { g1 = AbTheme.AccentDark; g2 = AbTheme.AccentDark; }
            }
            else
            {
                g1 = hovered ? AbTheme.GradHoverFirst : AbTheme.GradNotHoverFirst;
                g2 = hovered ? AbTheme.GradHoverSecond : AbTheme.GradNotHoverSecond;
                if (mouseDown) { g1 = AbTheme.DarkerGray; g2 = AbTheme.DarkerGray; }
            }

            using (LinearGradientBrush gb = AbTheme.VerticalGradient(r, g1, g2))
                g.FillRectangle(gb, r);

            using (Pen p = new Pen(AbTheme.Outline))
                g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);

            // 텍스트 — 가운데 정렬
            string text = Text ?? "";
            TextRenderer.DrawText(g, text, Font, r, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  AbComboBox — CComboBoxYeti 재현 (Controls.cpp:816)
    //  20px 헤더. 그라디언트 배경 + 우측 삼각 화살표. 열리면 항목이 20px씩 세로 펼침.
    //  드롭다운은 부모 카드 클리핑을 피하기 위해 별도 TopMost 폼으로 띄운다.
    // ────────────────────────────────────────────────────────────────────────
    internal class AbComboBox : Control
    {
        readonly List<string> items = new List<string>();
        int selectedIndex = -1;
        bool hovered;
        bool dropdownOpen;
        AbDropdownForm dropdown;

        public event EventHandler SelectedIndexChanged;

        public List<string> Items { get { return items; } }
        public ComboBoxStyle DropDownStyle { get; set; } = ComboBoxStyle.DropDownList; // 호환성

        public int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                if (selectedIndex != value)
                {
                    selectedIndex = value;
                    Invalidate();
                    SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public object SelectedItem
        {
            get { return (selectedIndex >= 0 && selectedIndex < items.Count) ? (object)items[selectedIndex] : null; }
        }

        public AbComboBox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = AbTheme.GroupBg;
            Font = new Font("Verdana", 9f);
            Cursor = Cursors.Hand;
            Height = 20;
        }

        public void BeginUpdate() { }
        public void EndUpdate() { }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            ToggleDropdown();
            base.OnClick(e);
        }

        void ToggleDropdown()
        {
            if (dropdownOpen)
            {
                CloseDropdown();
                return;
            }
            if (items.Count == 0) return;

            // 헤더의 스크린 좌표 — 드롭다운 폼을 바로 아래에 띄운다
            Point loc = PointToScreen(new Point(0, Height));
            dropdown = new AbDropdownForm(this, items, selectedIndex, loc, Width);
            dropdown.FormClosed += (s, e) =>
            {
                dropdownOpen = false;
                dropdown = null;
                Invalidate();
            };
            dropdown.ItemSelected += (idx) =>
            {
                SelectedIndex = idx;
            };
            dropdownOpen = true;
            dropdown.Show();
        }

        void CloseDropdown()
        {
            if (dropdown != null && !dropdown.IsDisposed)
            {
                dropdown.Close();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            System.Drawing.Graphics g = e.Graphics;
            AbPaint.FillCardBg(g, ClientRectangle);

            Rectangle r = ClientRectangle;
            r.Height--; r.Width--;

            // 배경 그라디언트 — hover/non-hover (Controls.cpp:821-822)
            Color g1 = hovered ? AbTheme.GradHoverFirst : AbTheme.GradNotHoverFirst;
            Color g2 = hovered ? AbTheme.GradHoverSecond : AbTheme.GradNotHoverSecond;
            using (LinearGradientBrush gb = AbTheme.VerticalGradient(r, g1, g2))
                g.FillRectangle(gb, r);

            using (Pen p = new Pen(AbTheme.Outline))
                g.DrawRectangle(p, r);

            // 선택 항목 텍스트 — 좌측 (Controls.cpp:828-829)
            string sel = (selectedIndex >= 0 && selectedIndex < items.Count) ? items[selectedIndex] : "";
            Rectangle textRect = new Rectangle(8, 0, r.Width - 16, Height);
            TextRenderer.DrawText(g, sel, Font, textRect, AbTheme.TextOff,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // 우측 삼각 화살표 (Controls.cpp:853-861)
            Point p1 = new Point(r.Right - 11, r.Top + 8);
            Point p2 = new Point(r.Right - 5, r.Top + 8);
            Point p3 = new Point(r.Right - 8, r.Top + 12);
            using (Pen pen = new Pen(AbTheme.TextOff))
            {
                g.DrawLine(pen, p1, p3);
                g.DrawLine(pen, p2, p3);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  AbDropdownForm — AbComboBox 의 드롭다운을 별도 폼으로 띄운다.
    //  부모 카드/패널의 클리핑에 잘리지 않고 스킨 목록 전체를 보여주기 위함.
    //  AByteCheat CComboBoxYeti::Draw 의 열린 상태 항목 렌더링과 동일 스타일.
    //
    //  스크롤: WinForms AutoScroll 은 DoubleBuffered + UserPaint 폼에서 비트맵 갱신이
    //  꼬여 스크롤 시 항목이 번지는(깨지는) 문제가 있다. 따라서 스크롤을 완전히 수동으로
    //  구현한다 — scrollOffset 정수값 하나만 유지하고 OnPaint 에서 직접 오프셋을 반영.
    //  세로 스크롤바도 직접 그려 AByteCheat 다크 테마와 색을 맞춘다.
    //
    //  - 최대 10개 항목까지 표시, 초과 시 세로 스크롤바
    //  - 휠 / 항목 클릭 / 스크롤바 끌기 / 선택 항목 자동 스크롤 지원
    // ────────────────────────────────────────────────────────────────────────
    internal class AbDropdownForm : Form
    {
        const int ItemH = 20;
        const int MaxVisibleItems = 10;
        const int ScrollBarW = 6;

        readonly AbComboBox owner;
        readonly List<string> items;
        readonly int listWidth;     // 스크롤바를 뺀 항목 영역 폭
        readonly int contentH;      // 항목 전체 높이 (가상)
        int scrollOffset;           // 현재 스크롤 위치 (0 = 맨 위, 양수 = 아래로)
        int hoverIndex = -1;
        int selectedIndex;
        bool thumbDrag;
        int thumbDragY;
        int thumbDragOffset;

        public event Action<int> ItemSelected;

        // 가시 항목 영역(스크롤바 제외)의 높이
        int ViewportH { get { return ClientSize.Height; } }
        int MaxScroll { get { return Math.Max(0, contentH - ViewportH); } }

        public AbDropdownForm(AbComboBox owner, List<string> items, int selectedIdx, Point screenLoc, int width)
        {
            this.owner = owner;
            this.items = items;
            this.selectedIndex = selectedIdx;
            this.contentH = items.Count * ItemH;

            // 드롭다운 전체 폭(formWidth)은 헤더(콤보박스) 폭과 동일 — 좌우 폭 정렬.
            // 스크롤이 필요하면 항목 영역(listWidth)을 ScrollBarW 만큼 줄이고 우측에 스크롤바 배치.
            bool needsScroll = items.Count > MaxVisibleItems;
            this.listWidth = needsScroll ? width - ScrollBarW : width;
            int formWidth = width;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = screenLoc;
            TopMost = true;
            BackColor = AbTheme.Gray;

            int clientH = Math.Min(contentH, MaxVisibleItems * ItemH);
            ClientSize = new Size(formWidth, clientH);

            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;

            // 화면 밖으로 나가면 위로 띄우기
            var screen = Screen.FromPoint(screenLoc);
            if (screenLoc.Y + clientH > screen.WorkingArea.Bottom)
            {
                int newY = screenLoc.Y - owner.Height - clientH;
                if (newY >= screen.WorkingArea.Top)
                    Location = new Point(screenLoc.X, newY);
            }

            // 열 때 선택 항목이 보이도록 스크롤
            ScrollToIndex(selectedIndex);
        }

        // 클라이언트 Y → 항목 인덱스 (스크롤바 영역 클릭은 -1)
        int ItemAtPoint(int x, int y)
        {
            if (x > listWidth) return -1; // 스크롤바 영역
            int idx = (y + scrollOffset) / ItemH;
            if (idx < 0 || idx >= items.Count) return -1;
            return idx;
        }

        // 스크롤바 thumb 사각형 (클라이언트 좌표계)
        Rectangle GetThumbRect()
        {
            if (contentH <= ViewportH) return Rectangle.Empty;
            int trackH = ViewportH;
            int thumbH = Math.Max(20, trackH * ViewportH / contentH);
            int thumbY = (trackH - thumbH) * scrollOffset / MaxScroll;
            return new Rectangle(listWidth, thumbY, ScrollBarW, thumbH);
        }

        void SetScroll(int value)
        {
            int clamped = Math.Max(0, Math.Min(MaxScroll, value));
            if (clamped == scrollOffset) return;
            scrollOffset = clamped;
            Invalidate();
        }

        void ScrollToIndex(int idx)
        {
            if (idx < 0 || idx >= items.Count) return;
            int itemTop = idx * ItemH;
            if (itemTop < scrollOffset)
                SetScroll(itemTop);                                  // 위에 가려짐
            else if (itemTop + ItemH > scrollOffset + ViewportH)
                SetScroll(itemTop + ItemH - ViewportH);              // 아래에 가려짐
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            // 스크롤바 끌기 중
            if (thumbDrag)
            {
                Rectangle thumb = GetThumbRect();
                int trackH = ViewportH;
                int thumbH = thumb.Height;
                int newThumbY = e.Y - thumbDragOffset;
                newThumbY = Math.Max(0, Math.Min(trackH - thumbH, newThumbY));
                int newScroll = (trackH - thumbH) == 0 ? 0 : newThumbY * MaxScroll / (trackH - thumbH);
                SetScroll(newScroll);
                return;
            }

            int idx = ItemAtPoint(e.X, e.Y);
            if (hoverIndex != idx)
            {
                hoverIndex = idx;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            // 스크롤바 thumb 클릭 → 끌기 시작
            Rectangle thumb = GetThumbRect();
            if (!thumb.IsEmpty && e.X >= listWidth && thumb.Contains(e.Location))
            {
                thumbDrag = true;
                thumbDragY = e.Y;
                thumbDragOffset = e.Y - thumb.Y;
                Capture = true;
                return;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (thumbDrag)
            {
                thumbDrag = false;
                Capture = false;
                return;
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int delta = e.Delta > 0 ? -ItemH * 3 : ItemH * 3;
            SetScroll(scrollOffset + delta);
            base.OnMouseWheel(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int idx = ItemAtPoint(e.X, e.Y);
            if (idx >= 0 && idx < items.Count)
            {
                selectedIndex = idx;
                ItemSelected?.Invoke(idx);
                Close();
                return;
            }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            System.Drawing.Graphics g = e.Graphics;
            Rectangle r = ClientRectangle;

            // 전체 배경 그라디언트 (Controls.cpp:833)
            using (LinearGradientBrush gb = AbTheme.VerticalGradient(r, AbTheme.GradNotHoverFirst, AbTheme.GradNotHoverSecond))
                g.FillRectangle(gb, r);

            // 가시 항목 범위만 렌더링 — scrollOffset 기준
            int firstVisible = scrollOffset / ItemH;
            int lastVisible  = (scrollOffset + ViewportH - 1) / ItemH;
            if (firstVisible < 0) firstVisible = 0;
            if (lastVisible >= items.Count) lastVisible = items.Count - 1;

            for (int i = firstVisible; i <= lastVisible; i++)
            {
                int y = i * ItemH - scrollOffset; // 클라이언트 좌표로 변환
                Rectangle itemR = new Rectangle(0, y, listWidth, ItemH);
                if (i == hoverIndex)
                {
                    using (LinearGradientBrush hb = AbTheme.VerticalGradient(itemR, AbTheme.GradHoverFirst, AbTheme.GradHoverSecond))
                        g.FillRectangle(hb, itemR);
                }
                Rectangle textRect = new Rectangle(8, y, listWidth - 16, ItemH);
                Color c = (i == selectedIndex) ? AbTheme.Accent : AbTheme.TextOff;
                TextRenderer.DrawText(g, items[i], owner.Font, textRect, c,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            // 스크롤바 — 트랙은 항목 배경 그라디언트가 그대로 비치도록 따로 칠하지 않고,
            // thumb 만 밝게 그려 인디케이터처럼 보이게 한다. 외곽선과 겹침 현상 해결.
            if (contentH > ViewportH)
            {
                Rectangle thumb = GetThumbRect();
                Color thumbColor = thumbDrag ? AbTheme.TextBright : AbTheme.TextOff;
                using (SolidBrush thb = new SolidBrush(thumbColor))
                    g.FillRectangle(thb, thumb);
            }

            // 외곽
            using (Pen p = new Pen(AbTheme.Outline))
                g.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  AbTitleBar — AByteCheat 메뉴 상단 헤더 재현.
    //  AByteCheat 은 게임 내장 VGUI 메뉴라 Windows 타이틀바가 없다. 대신 상단에
    //  시그니처 레인보우 그라디언트 바(파랑→보라→노랑, GUI.cpp:507-517) 2줄과
    //  다크 헤더 + 드래그 영역을 가진다. 이를 Windows FormBorderStyle=None 위에서
    //  커스텀 타이틀바로 재현한다.
    //
    //  - 헤더 높이 32px, 다크 배경 (21,21,19)
    //  - 상단 1px 레인보우 그라디언트 2줄 (밝은 줄 + 어두운 보조 줄)
    //  - 타이틀 텍스트 좌측
    //  - 닫기(×) 버튼 우측 — 클릭 시 부모 폼 닫기
    //  - 헤더 드래그로 창 이동 (더블클릭은 무시)
    // ────────────────────────────────────────────────────────────────────────
    internal class AbTitleBar : Control
    {
        const int HeaderHeight = 32;
        const int CloseBtnW = 32;
        bool hovered;
        bool closeHovered;
        bool closeDown;
        Rectangle closeRect;

        public string Title { get; set; }

        // 드래그로 창 이동 — 마우스 캡처 중 추적.
        bool dragging;
        Point dragOrigin;
        Point formOrigin;

        static readonly Font TitleFont = new Font("Tahoma", 9f, FontStyle.Bold);

        public AbTitleBar()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = AbTheme.DarkGray;
            Dock = DockStyle.Top;
            Height = HeaderHeight;
            Title = "";
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            closeRect = new Rectangle(Width - CloseBtnW, 0, CloseBtnW, Height);
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; closeHovered = false; closeDown = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            // 닫기 버튼 hover 추적
            bool overClose = closeRect.Contains(e.Location);
            if (overClose != closeHovered) { closeHovered = overClose; Invalidate(); }

            // 드래그로 창 이동 — 캡처 중일 때만.
            if (dragging && e.Button == MouseButtons.Left)
            {
                Form f = FindForm();
                if (f != null && f.WindowState != FormWindowState.Maximized)
                {
                    Point screen = Cursor.Position;
                    f.Location = new Point(formOrigin.X + (screen.X - dragOrigin.X),
                                           formOrigin.Y + (screen.Y - dragOrigin.Y));
                }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (closeRect.Contains(e.Location))
                {
                    closeDown = true;
                    Invalidate();
                }
                else
                {
                    // 헤더 빈 영역 — 창 드래그 시작.
                    Form f = FindForm();
                    if (f != null)
                    {
                        dragging = true;
                        dragOrigin = Cursor.Position;
                        formOrigin = f.Location;
                        Cursor = Cursors.SizeAll;
                    }
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (closeDown && closeRect.Contains(e.Location))
            {
                // 닫기 버튼 클릭 — 부모 폼 종료. OnFormClosing → Save 까지 정상 흐름.
                Form f = FindForm();
                if (f != null) f.Close();
            }
            closeDown = false;
            dragging = false;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            System.Drawing.Graphics g = e.Graphics;
            Rectangle r = ClientRectangle;

            // 헤더 배경 — (21,21,19). hover 시 살짝 밝게((28,28,28)) 반응.
            Color headerBg = hovered ? AbTheme.Gray : AbTheme.DarkGray;
            using (SolidBrush bg = new SolidBrush(headerBg))
                g.FillRectangle(bg, r);

            // 타이틀 텍스트 — 좌측, 수직 중앙.
            Rectangle textRect = new Rectangle(10, 0, closeRect.X - 20, Height);
            TextRenderer.DrawText(g, Title ?? "", TitleFont, textRect, AbTheme.TextBright,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // 하단 구분선 — 헤더와 본문 분리.
            using (Pen sep = new Pen(AbTheme.GroupLight))
                g.DrawLine(sep, 0, Height - 1, Width, Height - 1);

            // 닫기(×) 버튼 — hover 색 변화 없음 (요청에 따라 붉은 배경 제거).
            // × 아이콘 — 항상 동일 색(TextOff). 두께 효과를 위해 2px 라인.
            using (Pen xp = new Pen(AbTheme.TextOff, 2f))
            {
                int m = 10;
                Rectangle cb = closeRect;
                g.DrawLine(xp, cb.Left + m, cb.Top + m, cb.Right - m, cb.Bottom - m);
                g.DrawLine(xp, cb.Right - m, cb.Top + m, cb.Left + m, cb.Bottom - m);
            }
        }
    }
}
