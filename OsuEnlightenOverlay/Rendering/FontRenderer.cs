using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using OsuEnlightenOverlay.Rendering.Textures;
using OsuEnlightenOverlay.Skinning;
using SDGraphics = System.Drawing.Graphics;

namespace OsuEnlightenOverlay.Rendering
{
    /// <summary>
    /// 텍스트 렌더링 — GDI+ Bitmap → OpenGL 텍스처.
    /// NEWNEWOVERLAY의 DirectWrite 텍스트 렌더링을 OpenGL로 포팅.
    /// 폰트: Roboto Mono (600/700), Montserrat (600/700) — resources/fonts/에서 로드.
    /// </summary>
    internal class FontRenderer : IDisposable
    {
        TextureManager textureManager;
        PrivateFontCollection fontCollection;
        Dictionary<string, FontFamily> fontFamilies = new Dictionary<string, FontFamily>();

        // 텍스트 캐시 — 같은 텍스트+폰트+크기면 텍스처 재사용
        Dictionary<string, pTexture> textCache = new Dictionary<string, pTexture>();

        public FontRenderer(TextureManager tm)
        {
            this.textureManager = tm;
            fontCollection = new PrivateFontCollection();

            // 임베디드 리소스에서 폰트 로드 — 항상 사용 가능
            LoadEmbeddedFont("OsuEnlightenOverlay.roboto-mono-v23-latin-600.ttf");
            LoadEmbeddedFont("OsuEnlightenOverlay.roboto-mono-v23-latin-700.ttf");
            LoadEmbeddedFont("OsuEnlightenOverlay.montserrat-v26-latin-600.ttf");
            LoadEmbeddedFont("OsuEnlightenOverlay.montserrat-v26-latin-700.ttf");

            // 로드된 폰트 패밀리 이름 출력
            Console.WriteLine("[Font] Loaded " + fontCollection.Families.Length + " font families:");
            foreach (FontFamily ff in fontCollection.Families)
                Console.WriteLine("[Font]   " + ff.Name);
        }

        void LoadEmbeddedFont(string resourceName)
        {
            try
            {
                System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
                using (System.IO.Stream stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        // PrivateFontCollection.AddFontFile은 파일 경로만 받으므로
                        // 임시 파일로 복사 후 로드 — 파일은 유지 (GDI+가 파일을 참조함)
                        string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                            "oeo_" + System.IO.Path.GetFileName(resourceName));
                        using (var fs = new System.IO.FileStream(tempPath, System.IO.FileMode.Create, System.IO.FileAccess.Write))
                        {
                            stream.CopyTo(fs);
                        }
                        fontCollection.AddFontFile(tempPath);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 폰트 패밀리 이름으로 FontFamily 반환.
        /// "Roboto Mono" → roboto-mono 패밀리
        /// "Montserrat" → montserrat 패밀리
        /// </summary>
        FontFamily GetFontFamily(string familyName, FontStyle style)
        {
            // 커스텀 폰트 컬렉션에서 찾기
            // 로드된 폰트: "Roboto Mono", "Montserrat Thin", "Montserrat Thin SemiBold"
            if (string.Equals(familyName, "Montserrat", StringComparison.OrdinalIgnoreCase))
            {
                // Montserrat: Bold(700) → "Montserrat Thin SemiBold", Regular(600) → "Montserrat Thin"
                bool wantBold = (style & FontStyle.Bold) != 0;
                foreach (FontFamily ff in fontCollection.Families)
                {
                    if (wantBold && ff.Name.IndexOf("SemiBold", StringComparison.OrdinalIgnoreCase) >= 0)
                        return ff;
                    if (!wantBold && ff.Name.IndexOf("Thin", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        ff.Name.IndexOf("SemiBold", StringComparison.OrdinalIgnoreCase) < 0)
                        return ff;
                }
            }

            // 정확 매칭
            foreach (FontFamily ff in fontCollection.Families)
            {
                if (string.Equals(ff.Name, familyName, StringComparison.OrdinalIgnoreCase))
                    return ff;
            }

            // 부분 매칭
            foreach (FontFamily ff in fontCollection.Families)
            {
                if (ff.Name.StartsWith(familyName, StringComparison.OrdinalIgnoreCase))
                    return ff;
            }

            // 시스템 폰트 fallback
            try
            {
                FontFamily ff = new FontFamily(familyName);
                if (ff != null)
                    return ff;
            }
            catch { }

            return FontFamily.GenericSansSerif;
        }

        /// <summary>
        /// 텍스트를 OpenGL 텍스처로 렌더링.
        /// 캐싱됨 — 같은 텍스트+폰트+크기면 재사용.
        /// </summary>
        public pTexture RenderText(string text, string fontFamily, float size, FontStyle style,
            Color color, Color shadowColor, float shadowOffsetX, float shadowOffsetY)
        {
            if (string.IsNullOrEmpty(text)) return null;

            // 캐시 키 — 텍스트+폰트+크기+색+그림자
            string cacheKey = text + "|" + fontFamily + "|" + size + "|" + style + "|" +
                color.ToArgb() + "|" + shadowColor.ToArgb() + "|" + shadowOffsetX + "|" + shadowOffsetY;

            if (textCache.ContainsKey(cacheKey))
                return textCache[cacheKey];

            FontFamily ff = GetFontFamily(fontFamily, style);
            if (ff == null) return null;

            using (Font font = new Font(ff, size, style, GraphicsUnit.Pixel))
            {
                // 텍스트 크기 측정
                using (Bitmap measureBmp = new Bitmap(1, 1))
                using (SDGraphics measureG = SDGraphics.FromImage(measureBmp))
                {
                    measureG.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    SizeF textSize = measureG.MeasureString(text, font);
                    int textW = (int)Math.Ceiling(textSize.Width);
                    int textH = (int)Math.Ceiling(textSize.Height);

                    // 그림자 영역 추가
                    int totalW = textW + (int)Math.Ceiling(Math.Abs(shadowOffsetX)) + 4;
                    int totalH = textH + (int)Math.Ceiling(Math.Abs(shadowOffsetY)) + 4;

                    if (totalW <= 0 || totalH <= 0) return null;

                    using (Bitmap bmp = new Bitmap(totalW, totalH, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    using (SDGraphics g = SDGraphics.FromImage(bmp))
                    {
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        g.Clear(Color.Transparent);

                        // 그림자
                        if (shadowColor.A > 0)
                        {
                            using (Brush shadowBrush = new SolidBrush(shadowColor))
                            {
                                g.DrawString(text, font, shadowBrush,
                                    Math.Max(0, shadowOffsetX), Math.Max(0, shadowOffsetY));
                            }
                        }

                        // 본문
                        using (Brush textBrush = new SolidBrush(color))
                        {
                            g.DrawString(text, font, textBrush, 0, 0);
                        }

                        pTexture tex = textureManager.CreateFromBitmap(bmp, cacheKey);
                        if (tex != null)
                        {
                            tex.Source = SkinSource.Osu;
                            textCache[cacheKey] = tex;
                        }
                        return tex;
                    }
                }
            }
        }

        /// <summary>
        /// 텍스트 크기 측정 — 렌더링 없이.
        /// </summary>
        public void MeasureText(string text, string fontFamily, float size, FontStyle style,
            out float width, out float height)
        {
            width = 0; height = 0;
            if (string.IsNullOrEmpty(text)) return;

            FontFamily ff = GetFontFamily(fontFamily, style);
            if (ff == null) return;

            using (Font font = new Font(ff, size, style, GraphicsUnit.Pixel))
            using (Bitmap bmp = new Bitmap(1, 1))
            using (SDGraphics g = SDGraphics.FromImage(bmp))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                SizeF sz = g.MeasureString(text, font);
                width = sz.Width;
                height = sz.Height;
            }
        }

        /// <summary>
        /// 캐시 클리어 — 스킨 변경 시.
        /// </summary>
        public void ClearCache()
        {
            foreach (var kv in textCache)
            {
                if (kv.Value != null && kv.Value.IsDisposable)
                    kv.Value.Dispose();
            }
            textCache.Clear();
        }

        public void Dispose()
        {
            ClearCache();
            fontCollection?.Dispose();
        }
    }
}