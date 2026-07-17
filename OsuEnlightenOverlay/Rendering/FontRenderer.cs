using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
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

        // LRU 상한 (I-감사 #3/#5) — 콤보/정확도 HUD는 판정마다 값이 바뀌어 고유 문자열이
        // 계속 생기고, 그때마다 GL 텍스처가 하나씩 textCache에 영구 적재돼 무한 증가했다.
        // (stable은 숫자 글리프를 재사용해 이런 누적이 없다.) 접근 순서를 기록해 상한 초과 시
        // 가장 오래 안 쓴 항목을 축출한다. 한 프레임에 필요한 문자열은 소수(fps/정확도/콤보 등)라
        // 상한은 넉넉하고, 축출된 문자열은 다음에 필요하면 재렌더될 뿐이다.
        Dictionary<string, long> textCacheLastUse = new Dictionary<string, long>();
        long textCacheAccessCounter = 0;
        public const int MaxCacheEntries = 96;

        // 매니페스트 리소스 이름 접두사 = RootNamespace + csproj의 EmbeddedResource 경로.
        //   <EmbeddedResource Include="resource\fonts\*.ttf" />
        //     -> "OsuEnlightenOverlay.resource.fonts.*.ttf"
        // csproj에서 폰트 경로를 옮기면 이 값도 함께 바꿔야 한다.
        const string FontResourcePrefix = "OsuEnlightenOverlay.resource.fonts.";

        public FontRenderer(TextureManager tm)
        {
            this.textureManager = tm;
            fontCollection = new PrivateFontCollection();

            // 임베디드 리소스에서 폰트 로드 — 항상 사용 가능
            LoadEmbeddedFont(FontResourcePrefix + "roboto-mono-v23-latin-600.ttf");
            LoadEmbeddedFont(FontResourcePrefix + "roboto-mono-v23-latin-700.ttf");
            LoadEmbeddedFont(FontResourcePrefix + "montserrat-v26-latin-600.ttf");
            LoadEmbeddedFont(FontResourcePrefix + "montserrat-v26-latin-700.ttf");

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
                    // 리소스 이름이 csproj 경로와 어긋나면 stream이 null이 된다.
                    // 조용히 넘기면 HUD가 폴백 폰트로 렌더링되며 원인을 추적할 수 없으므로 알린다.
                    if (stream == null)
                    {
                        Console.WriteLine("[Font] MISSING resource: " + resourceName);
                        return;
                    }

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
            catch (Exception ex)
            {
                Console.WriteLine("[Font] FAIL " + resourceName + ": " + ex.Message);
            }
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
            {
                textCacheLastUse[cacheKey] = ++textCacheAccessCounter;
                return textCache[cacheKey];
            }

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
                            textCacheLastUse[cacheKey] = ++textCacheAccessCounter;
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
        /// LRU 상한 초과분 축출 — HudRenderer가 매 프레임 ClearHudSprites() 직후 호출한다.
        /// 그 시점엔 살아있는 HUD 스프라이트가 0개(모두 제거됨)라 어떤 텍스처를 dispose해도
        /// use-after-dispose가 없다. textCache는 오직 HUD 스프라이트만 참조하기 때문이다.
        /// 과축출돼도 해당 문자열은 다음 RenderText에서 재생성될 뿐이다.
        /// </summary>
        public void PruneCache()
        {
            if (textCache.Count <= MaxCacheEntries) return;

            // 접근 순서 오름차순(오래된 것 먼저)으로 정렬해 상한까지 축출.
            var byAge = new List<KeyValuePair<string, long>>(textCacheLastUse);
            byAge.Sort((a, b) => a.Value.CompareTo(b.Value));

            int toEvict = textCache.Count - MaxCacheEntries;
            for (int i = 0; i < byAge.Count && toEvict > 0; i++)
            {
                string key = byAge[i].Key;
                pTexture tex;
                if (textCache.TryGetValue(key, out tex))
                {
                    if (tex != null && tex.IsDisposable)
                        tex.Dispose();
                    textCache.Remove(key);
                    textCacheLastUse.Remove(key);
                    toEvict--;
                }
            }
        }

        /// <summary>
        /// 캐시 전체 비우기 — 현재는 Dispose에서만 호출된다 (I-감사 #28: 예전 "스킨 변경 시"
        /// 주석은 거짓이었다. 스킨 재로드는 TextureManager.ClearCache만 부른다).
        /// 런타임 중 무한 증가는 PruneCache의 LRU 상한이 막는다. 스킨/맵 변경 시 여기를 호출하지
        /// 않는 이유: HUD 텍스트 텍스처는 스킨 텍스처와 무관하고, 아직 그려질 hudSprite가 참조
        /// 중일 수 있어 임의 시점 전체 dispose는 use-after-dispose 위험이 있다.
        /// </summary>
        public void ClearCache()
        {
            foreach (var kv in textCache)
            {
                if (kv.Value != null && kv.Value.IsDisposable)
                    kv.Value.Dispose();
            }
            textCache.Clear();
            textCacheLastUse.Clear();
        }

        public void Dispose()
        {
            ClearCache();
            fontCollection?.Dispose();
        }
    }
}