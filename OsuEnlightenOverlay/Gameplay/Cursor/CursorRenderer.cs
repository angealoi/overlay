using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK;
using OsuEnlightenOverlay.Memory;
using OsuEnlightenOverlay.Overlay;
using OsuEnlightenOverlay.Rendering;
using OsuEnlightenOverlay.Rendering.Sprites;
using OsuEnlightenOverlay.Rendering.Textures;
using OsuEnlightenOverlay.Skinning;

namespace OsuEnlightenOverlay.Gameplay.Cursor
{
    /// <summary>
    /// 커서 렌더링 — osu! stable InputManager + SkinManager.LoadCursor 포팅.
    ///
    /// osu-stable 스케일 구조 (pSprite.cs:427):
    ///   drawScaleVector = VectorScale × drawScale
    ///   drawScale = Scale × Field별Ratio
    ///
    ///   Scale        = 1 (expand 미구현)
    ///   VectorScale  = mapScale × CursorSize  (UpdateCursorSize에서 설정)
    ///
    ///   NativeStandardScale: 위치 변환 없음, drawScale *= RatioInverse(Height/768)
    ///
    /// 메모리 커서 좌표 = 화면 픽셀 좌표 (screen-space).
    /// </summary>
    internal class CursorRenderer
    {
        SpriteManager spriteManager;
        TextureManager textureManager;
        GameField gameField;

        pSprite cursor;
        pSprite cursorMiddle;
        pTexture cursorTexture;
        pTexture cursorMiddleTexture;
        pTexture cursorTrailTexture;

        // 스킨 설정
        bool cursorCentre = true;
        bool cursorRotate = true;
        bool cursorTrailRotate = false;

        // mapScale (UpdateCursorSize) — VectorScale에 들어감
        float mapScale = 1.0f;
        const float kAutoCursorSizingCoeff = 0.7f;
        const float kAutoCursorSizingRefCS = 4.0f;
        const float kAutoCursorSizingRange = 5.0f;
        const float kAutoCursorSizingMin = 0.5f;

        // Trail
        Vector2 lastTrailPosition = -Vector2.One;
        int lastTrailTime = -1000;
        const int kTrailNewLifetimeMs = 500;
        const int kTrailOldLifetimeMs = 150;
        const float kTrailNewSpacingMult = 0.625f;
        const float kTrailNewSpacingDiv = 2.5f;
        const int kTrailOldEmitIntervalMs = 16;

        struct TrailParticle
        {
            public Vector2 pos;
            public int spawnMs;
        }
        List<TrailParticle> trailParticles = new List<TrailParticle>();
        List<pSprite> trailSprites = new List<pSprite>();

        bool initialized = false;

        // Cursor Pack 설정
        bool cursorPackEnabled = false;
        string cursorPackName = "";

        public void SetCursorPack(bool enabled, string packName)
        {
            cursorPackEnabled = enabled;
            cursorPackName = packName ?? "";
        }

        // 현재 커서 텍스처를 커서팩에서 만들었는지 — true면 우리가 소유자라 해제 책임이 있다.
        // 스킨 경로(textureManager.Load)는 캐시가 소유하므로 건드리면 안 된다.
        bool packTexturesOwned;

        /// <summary>
        /// 커서팩에서 만든 텍스처 해제. 스킨 캐시 텍스처에는 절대 호출하지 말 것.
        /// </summary>
        void DisposePackTextures()
        {
            if (!packTexturesOwned) return;
            if (cursorTexture != null) cursorTexture.Dispose();
            if (cursorMiddleTexture != null) cursorMiddleTexture.Dispose();
            if (cursorTrailTexture != null) cursorTrailTexture.Dispose();
            packTexturesOwned = false;
        }

        /// <summary>
        /// Cursor Pack 변경 시 텍스처 재로드.
        /// </summary>
        public void Reload()
        {
            if (cursor != null) { spriteManager.Remove(cursor); cursor = null; }
            if (cursorMiddle != null) { spriteManager.Remove(cursorMiddle); cursorMiddle = null; }
            // 기존 trail 스프라이트들도 제거
            foreach (pSprite s in trailSprites)
                spriteManager.Remove(s);
            trailSprites.Clear();
            trailParticles.Clear();
            lastTrailPosition = -Vector2.One;
            lastTrailTime = -1000;
            // 스킨 경로의 텍스처는 TextureManager 캐시가 소유하므로 버리기만 한다.
            // 커서팩 경로는 CreateFromBitmap이라 캐시에 안 들어가고 우리가 소유자다.
            // 해제하지 않으면 Reload마다 최대 3텍스처가 샌다 (B2).
            DisposePackTextures();
            cursorTexture = null;
            cursorMiddleTexture = null;
            cursorTrailTexture = null;
            initialized = false;
            Load();
            // Reload 후 즉시 Draw 호출하여 스프라이트 추가
            Draw();
        }

        public CursorRenderer(SpriteManager sm, TextureManager tm, GameField gf)
        {
            spriteManager = sm;
            textureManager = tm;
            gameField = gf;
        }

        public void Load()
        {
            LoadTextures();
            CreateSprites();
            initialized = true;
        }

        /// <summary>
        /// 커서 텍스처 로드 — Cursor Pack 우선, 없으면 스킨에서.
        /// 우선순위: overlay-cursors\<PackName>\ → 스킨 → default skin
        /// Cursor Pack 활성화 시: Pack에서 찾지 못한 텍스처는 null (fallback 안 함)
        /// Cursor Pack 비활성화 시: 스킨 → default skin fallback
        /// </summary>
        void LoadTextures()
        {
            // Cursor Pack 폴더 확인
            string packFolder = GetCursorPackFolder();
            if (packFolder != null)
            {
                // Pack 폴더에서 로드 — Pack에서 찾지 못한 텍스처는 null 유지 (fallback 안 함)
                cursorTexture = LoadTextureFromFolder(packFolder, "cursor");
                cursorMiddleTexture = LoadTextureFromFolder(packFolder, "cursormiddle");
                cursorTrailTexture = LoadTextureFromFolder(packFolder, "cursortrail");
                // CreateFromBitmap으로 새로 만든 것들 — 캐시에 없으므로 우리가 해제해야 한다
                packTexturesOwned = true;
            }
            else
            {
                // Pack 비활성화 — 스킨에서 로드
                // osu! stable SkinManager.cs:221: cursor = Load("cursor", Skin|Osu)
                // osu! stable SkinManager.cs:269: cursormiddle = Load("cursormiddle", t_cursor.Source)
                // osu! stable: cursortrail = Load("cursortrail", Skin|Osu) — cursor와 같은 fallback 체인
                cursorTexture = textureManager.Load("cursor");
                // cursor middle은 cursor와 같은 소스에서만 (fallback 안 함)
                SkinSource cursorSource = cursorTexture != null ? cursorTexture.Source : SkinSource.All;
                cursorMiddleTexture = textureManager.Load("cursormiddle", cursorSource);
                // cursortrail은 Skin|Osu fallback 허용 (cursor middle과 달리 독립적으로 로드)
                cursorTrailTexture = textureManager.Load("cursortrail", SkinSource.Skin | SkinSource.Osu);
                // 캐시가 소유하는 텍스처다 — 여기서 해제하면 다른 사용자의 텍스처를 깨뜨린다
                packTexturesOwned = false;
            }

            cursorCentre = SkinManager.Current != null ? SkinManager.Current.CursorCentre : true;
            cursorRotate = SkinManager.Current != null ? SkinManager.Current.CursorRotate : true;
            cursorTrailRotate = SkinManager.Current != null ? SkinManager.Current.CursorTrailRotate : false;
        }

        /// <summary>
        /// Cursor Pack 폴더 경로 반환 — overlay-cursors\<PackName>\
        /// Pack이 비활성화되거나 폴더가 없으면 null.
        /// </summary>
        string GetCursorPackFolder()
        {
            if (!cursorPackEnabled || string.IsNullOrEmpty(cursorPackName))
                return null;

            string exeDir = System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            string packPath = System.IO.Path.Combine(exeDir, "overlay-cursors", cursorPackName);

            if (System.IO.Directory.Exists(packPath) && HasCursorTexture(packPath))
                return packPath;

            return null;
        }

        /// <summary>
        /// 폴더에 cursor.png 또는 cursor@2x.png가 있는지 확인.
        /// </summary>
        bool HasCursorTexture(string folder)
        {
            return System.IO.File.Exists(System.IO.Path.Combine(folder, "cursor.png")) ||
                   System.IO.File.Exists(System.IO.Path.Combine(folder, "cursor@2x.png"));
        }

        /// <summary>
        /// 폴더에서 텍스처 로드 — @2x 우선, DpiScale 설정.
        /// </summary>
        pTexture LoadTextureFromFolder(string folder, string name)
        {
            // @2x 우선
            string path2x = System.IO.Path.Combine(folder, name + "@2x.png");
            string path1x = System.IO.Path.Combine(folder, name + ".png");
            string path = null;
            float dpiScale = 1;

            if (System.IO.File.Exists(path2x))
            {
                path = path2x;
                dpiScale = 2;
            }
            else if (System.IO.File.Exists(path1x))
            {
                path = path1x;
            }
            else
                return null;

            try
            {
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(path))
                {
                    pTexture tex = textureManager.CreateFromBitmap(bmp, name);
                    tex.DpiScale = dpiScale;
                    return tex;
                }
            }
            catch { return null; }
        }

        void CreateSprites()
        {
            Origins origin = cursorCentre ? Origins.Centre : Origins.TopLeft;

            // osu-stable SkinManager.cs:233 — Fields.NativeStandardScale, depth 0.999
            cursor = new pSprite(cursorTexture, Fields.NativeStandardScale, origin, Clocks.Game,
                Vector2.Zero, 0.999f, true, Color.White);
            cursor.Alpha = 0;
            cursor.Scale = 1;

            // Rotation 루프 — osu-stable: 0→2π over 10000ms
            if (cursorRotate)
            {
                cursor.Transformations.Add(new Transformation(
                    TransformationType.Rotation, 0f, (float)Math.PI * 2f, 0, 10000)
                { Loop = true });
            }

            // cursor middle — osu-stable SkinManager.cs:236: depth 1.0
            if (cursorMiddleTexture != null)
            {
                cursorMiddle = new pSprite(cursorMiddleTexture, Fields.NativeStandardScale, origin, Clocks.Game,
                    Vector2.Zero, 1.0f, true, Color.White);
                cursorMiddle.Alpha = 0;
                cursorMiddle.Scale = 1;
            }

            initialized = true;
        }

        public void Update(float cursorX, float cursorY, int timeMs, float cs, uint mods, bool isPlayMode,
            bool autoSize, float cursorSize)
        {
            if (!initialized || cursor == null) return;

            // 메모리 커서 좌표 = 화면 픽셀 좌표 (screen-space). 변환 없이 그대로 사용.
            Vector2 screenPos = new Vector2(cursorX, cursorY);

            // ---- UpdateCursorSize (osu-stable InputManager.cs:1355-1367) ----
            mapScale = 1.0f;
            if (isPlayMode && autoSize)
            {
                float moddedCs = cs;
                if ((mods & Offsets.Mod_EZ) != 0) moddedCs *= 0.5f;
                if ((mods & Offsets.Mod_HR) != 0) moddedCs *= 1.4f;
                mapScale = 1.0f - kAutoCursorSizingCoeff * (moddedCs - kAutoCursorSizingRefCS) / kAutoCursorSizingRange;
                if (mapScale < kAutoCursorSizingMin) mapScale = kAutoCursorSizingMin;
            }
            else if (!autoSize)
            {
                // 수동 커서 크기 — 사용자 설정 값 사용
                mapScale = cursorSize;
            }

            // VectorScale = mapScale × CursorSize(1.0) — osu-stable :1365
            Vector2 vecScale = new Vector2(mapScale, mapScale);

            // 위치 + VectorScale 설정.
            cursor.Position = screenPos;
            cursor.Alpha = 1.0f;
            cursor.VectorScale = vecScale;

            if (cursorMiddle != null)
            {
                // osu-stable :1366: s_Cursor2.VectorScale = s_Cursor.VectorScale
                cursorMiddle.Position = screenPos;
                cursorMiddle.Alpha = 1.0f;
                cursorMiddle.VectorScale = vecScale;
            }

            // ---- Trail ----
            UpdateTrail(screenPos, timeMs);
        }

        void UpdateTrail(Vector2 cursorScreenPos, int timeMs)
        {
            if (cursorTrailTexture == null) return;

            // 시간 역행 감지 (맵 재시작, seek 등) — trail 상태 리셋
            if (lastTrailTime != -1000 && timeMs < lastTrailTime - 1000)
            {
                lastTrailTime = -1000;
                lastTrailPosition = -Vector2.One;
                trailParticles.Clear();
                // 기존 trail 스프라이트들을 SpriteManager에서 제거
                foreach (pSprite s in trailSprites)
                    spriteManager.Remove(s);
                trailSprites.Clear();
            }

            bool hasMiddle = cursorMiddleTexture != null;
            float osuRatio = (float)gameField.windowHeight / 480f;

            float trailDpi = cursorTrailTexture.DpiScale;
            float trailDisplayW = cursorTrailTexture.Width / trailDpi;

            // (미사용 지역변수 origin 제거 — 실제 origin은 AddTrailParticle에서 계산, I-감사 #22)

            if (hasMiddle)
            {
                // ---- New-style trail — osu-stable updateCursorTrail :1263-1291 ----
                float acceptedWidth = (trailDisplayW * kTrailNewSpacingMult * osuRatio * cursor.Scale) / kTrailNewSpacingDiv;
                float minSpacing = Math.Max(1.0f, acceptedWidth);

                if (lastTrailPosition == -Vector2.One)
                {
                    lastTrailPosition = cursorScreenPos;
                    return;
                }

                float dx = cursorScreenPos.X - lastTrailPosition.X;
                float dy = cursorScreenPos.Y - lastTrailPosition.Y;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                if (dist >= minSpacing)
                {
                    float stepsF = dist / minSpacing;
                    int intSteps = (int)stepsF;
                    for (int i = 0; i < intSteps; i++)
                    {
                        float t = (float)i / stepsF;
                        Vector2 p = new Vector2(lastTrailPosition.X + dx * t, lastTrailPosition.Y + dy * t);
                        AddTrailParticle(p, timeMs);
                    }
                    if (intSteps > 0)
                    {
                        float lastT = (float)intSteps / stepsF;
                        lastTrailPosition = new Vector2(lastTrailPosition.X + dx * lastT, lastTrailPosition.Y + dy * lastT);
                    }
                }
            }
            else
            {
                // ---- Old-style trail — osu-stable :1293-1306 ----
                if (timeMs - lastTrailTime >= kTrailOldEmitIntervalMs)
                {
                    lastTrailTime = timeMs;
                    AddTrailParticle(cursorScreenPos, timeMs);
                }
            }

            // 만료된 파티클 제거 + 대응 스프라이트도 제거
            int lifetime = hasMiddle ? kTrailNewLifetimeMs : kTrailOldLifetimeMs;
            for (int i = trailParticles.Count - 1; i >= 0; i--)
            {
                if (timeMs - trailParticles[i].spawnMs >= lifetime)
                {
                    if (i < trailSprites.Count)
                    {
                        spriteManager.Remove(trailSprites[i]);
                        trailSprites.RemoveAt(i);
                    }
                    trailParticles.RemoveAt(i);
                }
            }
        }

        void AddTrailParticle(Vector2 pos, int timeMs)
        {
            trailParticles.Add(new TrailParticle { pos = pos, spawnMs = timeMs });
            if (trailParticles.Count > 2048)
            {
                trailParticles.RemoveAt(0);
                if (trailSprites.Count > 0)
                {
                    spriteManager.Remove(trailSprites[0]);
                    trailSprites.RemoveAt(0);
                }
            }

            // 트레일 스프라이트 생성 — osu-stable: alwaysDraw=false (FadeOut 후 자동 Discard)
            if (cursorTrailTexture != null && cursor != null)
            {
                bool hasMiddle = cursorMiddleTexture != null;
                int lifetime = hasMiddle ? kTrailNewLifetimeMs : kTrailOldLifetimeMs;
                Origins origin = cursorCentre ? Origins.Centre : Origins.TopLeft;

                pSprite trail = new pSprite(cursorTrailTexture, Fields.NativeStandardScale, origin, Clocks.Game,
                    pos, cursor.Depth - 0.001f, false, Color.White);
                trail.Scale = cursor.Scale;
                trail.VectorScale = cursor.VectorScale;
                trail.Rotation = cursorTrailRotate ? cursor.CurrentRotation : 0;
                trail.Alpha = 1.0f;
                trail.Additive = true;
                trail.Transformations.Add(new Transformation(
                    TransformationType.Fade, 1.0f, 0.0f,
                    timeMs, timeMs + lifetime));
                spriteManager.Add(trail);
                trailSprites.Add(trail);
            }
        }

        public void Draw()
        {
            if (!initialized) return;
            if (cursor != null && !spriteManager.Contains(cursor))
                spriteManager.Add(cursor);
            if (cursorMiddle != null && !spriteManager.Contains(cursorMiddle))
                spriteManager.Add(cursorMiddle);
        }
    }
}
