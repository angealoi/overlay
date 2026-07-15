using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using OpenTK.Graphics.OpenGL;
using OsuEnlightenOverlay.Skinning;

namespace OsuEnlightenOverlay.Rendering.Textures
{
    /// <summary>
    /// OpenGL 텍스처 래퍼 — osu! stable Graphics/Textures/pTexture.cs 포팅.
    /// </summary>
    internal class pTexture : IDisposable
    {
        public int TextureId { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public string AssetName { get; private set; }
        public SkinSource Source { get; set; }
        public bool IsDisposable { get; set; }
        /// <summary>
        /// DPI 스케일 — osu! stable DpiScale 포팅.
        /// @2x 텍스처는 DpiScale=2, 일반은 1.
        /// 렌더링 시 textureWidth / DpiScale 로 실제 표시 크기 계산.
        /// </summary>
        public float DpiScale { get; set; }

        public pTexture(int textureId, int width, int height, string assetName)
        {
            TextureId = textureId;
            Width = width;
            Height = height;
            AssetName = assetName;
            Source = SkinSource.Osu;
            IsDisposable = true;
            DpiScale = 1;
        }

        public void Bind()
        {
            GL.BindTexture(TextureTarget.Texture2D, TextureId);
        }

        public void Dispose()
        {
            if (TextureId > 0 && IsDisposable)
            {
                GL.DeleteTexture(TextureId);
                TextureId = 0;
            }
        }
    }

    /// <summary>
    /// 텍스처 로드/캐싱 — osu! stable Graphics/Textures/TextureManager.cs 포팅.
    /// SkinSource 기반 fallback: Skin > Osu (default skin)
    /// </summary>
    internal class TextureManager : IDisposable
    {
        // 텍스처 캐시 — key = "source:name"
        Dictionary<string, pTexture> cache = new Dictionary<string, pTexture>();

        string defaultSkinFolder;  // 기본 스킨 폴더 (ref/default skin) — null이면 임베디드 리소스 사용
        string userSkinFolder;     // 사용자 스킨 폴더

        static readonly string[] imageExtensions = { ".png", ".jpg" };

        // 임베디드 default skin 리소스 이름 캐시
        static HashSet<string> embeddedResourceNames;
        static System.Reflection.Assembly embeddedAssembly;

        static void InitEmbeddedResources()
        {
            if (embeddedResourceNames != null) return;
            embeddedAssembly = System.Reflection.Assembly.GetExecutingAssembly();
            embeddedResourceNames = new HashSet<string>(embeddedAssembly.GetManifestResourceNames());
        }

        /// <summary>
        /// 임베디드 default skin 리소스에서 텍스처 로드.
        /// 리소스명: OsuEnlightenOverlay.resource.defaultskin.{filename}
        /// </summary>
        pTexture LoadFromEmbedded(string name, SkinSource source)
        {
            InitEmbeddedResources();

            bool hasExtension = name.IndexOf('.') >= 0;

            foreach (string ext in imageExtensions)
            {
                string fileName = hasExtension ? name : name + ext;

                // @2x — osu! stable: UseHighResolutionSprites일 때만 로드, DpiScale=2 설정
                if (UseHighResolutionSprites)
                {
                    string filename2x;
                    if (hasExtension)
                    {
                        int dotIdx = name.LastIndexOf('.');
                        filename2x = name.Substring(0, dotIdx) + "@2x" + name.Substring(dotIdx);
                    }
                    else
                    {
                        filename2x = name + "@2x" + ext;
                    }
                    string resName2x = "OsuEnlightenOverlay.resource.defaultskin." + filename2x.Replace('/', '.');
                    if (embeddedResourceNames.Contains(resName2x))
                    {
                        pTexture tex = LoadFromEmbeddedResource(resName2x, name, source);
                        if (tex != null)
                            tex.DpiScale = 2;
                        return tex;
                    }
                }

                // 일반 텍스처
                string resName = "OsuEnlightenOverlay.resource.defaultskin." + fileName.Replace('/', '.');
                if (embeddedResourceNames.Contains(resName))
                    return LoadFromEmbeddedResource(resName, name, source);
            }
            return null;
        }

        pTexture LoadFromEmbeddedResource(string resName, string assetName, SkinSource source)
        {
            try
            {
                using (Stream stream = embeddedAssembly.GetManifestResourceStream(resName))
                {
                    if (stream == null) return null;
                    using (Bitmap bmp = new Bitmap(stream))
                    {
                        pTexture tex = CreateFromBitmap(bmp, assetName);
                        if (tex != null)
                            tex.Source = source;
                        return tex;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        public TextureManager(string defaultSkinFolder)
        {
            this.defaultSkinFolder = defaultSkinFolder;
        }

        /// <summary>
        /// 사용자 스킨 폴더 설정.
        /// </summary>
        public void SetUserSkin(string folder)
        {
            userSkinFolder = folder;
        }

        /// <summary>
        /// 텍스처 캐시 전체 클리어 — 스킨 변경 시 호출.
        /// 기존 텍스처는 OpenGL에서 삭제.
        /// </summary>
        public void ClearCache()
        {
            foreach (var texKv in cache)
            {
                if (texKv.Value != null && texKv.Value.IsDisposable)
                    texKv.Value.Dispose();
            }
            cache.Clear();
        }

        /// <summary>
        /// 창 높이 설정 — UseHighResolutionSprites 계산용.
        /// </summary>
        public void SetWindowHeight(int height)
        {
            windowHeight = height;
        }

        /// <summary>
        /// 파일에서 텍스처 로드 (SkinSource.All 기본).
        /// 캐싱됨. osu! stable TextureManager.Load 포팅.
        /// fallback: Skin > Osu
        /// </summary>
        public pTexture Load(string name, SkinSource source = SkinSource.All)
        {
            if (name == null) return null;

            // 통합 캐시 확인
            string cacheKey = source + ":" + name;
            if (cache.ContainsKey(cacheKey))
                return cache[cacheKey];

            pTexture tex = null;

            // 1. 사용자 스킨 (우선순위 최고)
            if ((source & SkinSource.Skin) != 0 && userSkinFolder != null && !SkinManager.IsDefault)
            {
                tex = LoadFromFolder(name, userSkinFolder, SkinSource.Skin);
                if (tex != null)
                {
                    cache[cacheKey] = tex;
                    return tex;
                }
            }

            // 2. 기본 스킨 (fallback) — 임베디드 리소스 우선, 없으면 폴더
            if ((source & SkinSource.Osu) != 0)
            {
                // 임베디드 default skin에서 로드
                tex = LoadFromEmbedded(name, SkinSource.Osu);
                if (tex == null && defaultSkinFolder != null)
                    tex = LoadFromFolder(name, defaultSkinFolder, SkinSource.Osu);
                if (tex != null)
                {
                    cache[cacheKey] = tex;
                    return tex;
                }
            }

            // 못 찾음 — null 캐싱
            cache[cacheKey] = null;
            return null;
        }

        /// <summary>
        /// 여러 이름 중 첫 번째로 발견된 텍스처 로드 (fallback).
        /// osu! stable TextureManager.LoadFirstAvailable 포팅.
        /// </summary>
        public pTexture LoadFirstAvailable(string[] names, SkinSource source = SkinSource.All)
        {
            foreach (string name in names)
            {
                pTexture tex = Load(name, source);
                if (tex != null)
                    return tex;
            }
            return null;
        }

        /// <summary>
        /// 애니메이션 프레임 로드 — osu! stable TextureManager.LoadAll 정확 포팅.
        /// sliderb-0, sliderb-1, ... (dashSeparator=true) 또는 sliderb0, sliderb1, ...
        /// osu! stable: textureFromMostSpecificSkin 체크로 소스 혼용 방지.
        /// </summary>
        public pTexture[] LoadAll(string s, SkinSource source = SkinSource.All, bool dashSeparator = true)
        {
            int frameSuffixPosition = s.LastIndexOf('.');
            string dash = dashSeparator ? "-" : "";

            // frame0Name — osu! stable: frameSuffixPosition == -1 ? s + dash + 0 : s.Insert(frameSuffixPosition, dash + 0)
            string frame0Name = frameSuffixPosition == -1 ? s + dash + "0" : s.Insert(frameSuffixPosition, dash + "0");
            pTexture animated = Load(frame0Name, source);
            pTexture sprite = Load(s, source);

            // osu! stable: textureFromMostSpecificSkin 체크
            if (animated != null && animated == TextureFromMostSpecificSkin(animated, sprite))
            {
                System.Collections.Generic.List<pTexture> textures = new System.Collections.Generic.List<pTexture>();

                for (int i = 1; animated != null; i++)
                {
                    textures.Add(animated);

                    string frameIName = frameSuffixPosition == -1 ? s + dash + i : s.Insert(frameSuffixPosition, dash + i);
                    animated = Load(frameIName, animated.Source); // osu! stable: animated.Source 사용
                }

                return textures.ToArray();
            }

            if (sprite != null)
                return new pTexture[] { sprite };

            return null;
        }

        /// <summary>
        /// osu! stable TextureManager.textureFromMostSpecificSkin 포팅.
        /// 더 구체적인 소스(Beatmap > Skin > Osu)의 텍스처를 반환.
        /// </summary>
        pTexture TextureFromMostSpecificSkin(pTexture first, pTexture second)
        {
            if (first != null && (
                second == null ||
                first.Source == SkinSource.Beatmap ||
                (first.Source == SkinSource.Skin && second.Source != SkinSource.Beatmap) ||
                (first.Source == SkinSource.Osu && second.Source != SkinSource.Beatmap && second.Source != SkinSource.Skin))
            )
                return first;
            return second;
        }

        // UseHighResolutionSprites — osu! stable GameBase.UseHighResolutionSprites 포팅
        // Height >= 800 이거나 고해상도 설정일 때만 @2x 로드
        bool UseHighResolutionSprites
        {
            get { return windowHeight >= 800; }
        }
        int windowHeight;

        pTexture LoadFromFolder(string name, string folder, SkinSource source)
        {
            // osu! stable: name.IndexOf('.') < 0 ? name + ext : name
            // 이름에 이미 확장자가 있으면 추가하지 않음
            bool hasExtension = name.IndexOf('.') >= 0;

            foreach (string ext in imageExtensions)
            {
                string fileName = hasExtension ? name : name + ext;

                // @2x — osu! stable: UseHighResolutionSprites일 때만 로드, DpiScale=2 설정
                if (UseHighResolutionSprites)
                {
                    string filename2x;
                    if (hasExtension)
                    {
                        // name에 확장자가 있으면 @2x를 확장자 앞에 삽입
                        int dotIdx = name.LastIndexOf('.');
                        filename2x = Path.Combine(folder, name.Substring(0, dotIdx) + "@2x" + name.Substring(dotIdx));
                    }
                    else
                    {
                        filename2x = Path.Combine(folder, name + "@2x" + ext);
                    }
                    if (File.Exists(filename2x))
                    {
                        pTexture tex = LoadFromBitmap(filename2x, name, source);
                        if (tex != null)
                            tex.DpiScale = 2;
                        return tex;
                    }
                }

                // 일반 텍스처
                string filename = Path.Combine(folder, fileName);
                if (File.Exists(filename))
                    return LoadFromBitmap(filename, name, source);
            }
            return null;
        }

        pTexture LoadFromBitmap(string filepath, string assetName, SkinSource source)
        {
            try
            {
                using (Bitmap bmp = new Bitmap(filepath))
                {
                    pTexture tex = CreateFromBitmap(bmp, assetName);
                    if (tex != null)
                        tex.Source = source;
                    return tex;
                }
            }
            catch
            {
                return null;
            }
        }

        public pTexture CreateFromBitmap(Bitmap bmp, string assetName)
        {
            int texId;
            GL.GenTextures(1, out texId);
            GL.BindTexture(TextureTarget.Texture2D, texId);

            // osu-stable 방식 — straight alpha (premultiplied 변환 없음).
            // pTexture.cs bgraToRgba: BGRA→RGBA 변환만 수행, RGB *= A 하지 않음.
            // 블렌딩은 BlendFunc(SrcAlpha, OneMinusSrcAlpha) 사용 (straight alpha용).
            int pixelCount = bmp.Width * bmp.Height;
            byte[] pixels = new byte[pixelCount * 4];

            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            bmp.UnlockBits(data);

            // BGRA → RGBA 변환만 (osu-stable bgraToRgba 포팅). premultiplied 변환 없음.
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                // R,G,B 교환 (BGRA → RGBA)
                pixels[i] = pixels[i + 2];     // R → B 슬롯
                pixels[i + 2] = b;             // B → R 슬롯
                // G, A는 그대로
            }

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                bmp.Width, bmp.Height, 0,
                OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            return new pTexture(texId, bmp.Width, bmp.Height, assetName);
        }

        /// <summary>
        /// 단색 텍스처 생성 (슬라이더 바디 등).
        /// </summary>
        public pTexture CreateSolidTexture(System.Drawing.Color colour, string name)
        {
            if (cache.ContainsKey(name))
                return cache[name];

            using (Bitmap bmp = new Bitmap(1, 1))
            {
                bmp.SetPixel(0, 0, colour);
                pTexture tex = CreateFromBitmap(bmp, name);
                if (tex != null)
                    cache[name] = tex;
                return tex;
            }
        }

        public void Dispose()
        {
            foreach (var kv in cache)
            {
                if (kv.Value != null)
                    kv.Value.Dispose();
            }
            cache.Clear();
        }
    }
}