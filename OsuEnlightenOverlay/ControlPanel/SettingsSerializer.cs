using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OsuEnlightenOverlay.ControlPanel
{
    /// <summary>
    /// INI 기반 설정 저장/로드 — NEWNEWOVERLAY config.cpp 포팅.
    /// GetPrivateProfileString / WritePrivateProfileString 사용.
    /// </summary>
    public static class SettingsSerializer
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern uint GetPrivateProfileString(string section, string key, string defaultValue, StringBuilder returnValue, int size, string filePath);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool WritePrivateProfileString(string section, string key, string value, string filePath);

        static string ConfigPath
        {
            get
            {
                // exe 옆에 settings.ini 생성
                string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                return Path.Combine(exeDir, "settings.ini");
            }
        }

        public static void Load(OverlaySettings settings)
        {
            string path = ConfigPath;
            // 파일이 없으면 GetPrivateProfileString이 기본값을 돌려주므로 읽기는 그대로 진행해도
            // 무해하다 — 값은 defaults 그대로 남고, 마지막 Normalize()가 항상 돈다.
            if (File.Exists(path))
                LoadFromFile(settings, path);

            // 손상/범위 밖/비정상 값을 안전 범위로 강제 — 이게 없으면 ini 값 하나로 기동 불능 (A1).
            settings.Normalize();
        }

        static void LoadFromFile(OverlaySettings settings, string path)
        {
            // [Overlay]
            settings.Enabled = ReadBool(path, "Overlay", "Enabled", settings.Enabled);
            settings.CaptureBlocked = ReadBool(path, "Overlay", "CaptureBlocked", settings.CaptureBlocked);
            settings.FpsCap = ReadInt(path, "Overlay", "FpsCap", settings.FpsCap);
            settings.HiddenOverride = ReadBool(path, "Overlay", "HiddenOverride", settings.HiddenOverride);

            // [Difficulty]
            settings.ArValue = ReadFloat(path, "Difficulty", "AR", settings.ArValue);
            settings.CsValue = ReadFloat(path, "Difficulty", "CS", settings.CsValue);
            settings.ArDtValue = ReadFloat(path, "Difficulty", "DtAR", settings.ArDtValue);
            settings.ArHtValue = ReadFloat(path, "Difficulty", "HtAR", settings.ArHtValue);

            // [Cursor]
            settings.CursorAutoSize = ReadBool(path, "Cursor", "AutoSize", settings.CursorAutoSize);
            settings.CursorSize = ReadFloat(path, "Cursor", "Size", settings.CursorSize);
            settings.CursorPackEnabled = ReadBool(path, "Cursor", "PackEnabled", settings.CursorPackEnabled);
            settings.CursorPackName = ReadString(path, "Cursor", "PackName", settings.CursorPackName);

            // [HUD]
            for (int i = 0; i < 4; i++)
            {
                settings.HudEnabled[i] = ReadBool(path, "HUD", "Enabled" + i, settings.HudEnabled[i]);
                settings.HudFontSizes[i] = ReadInt(path, "HUD", "FontSize" + i, settings.HudFontSizes[i]);
                settings.HudUseCustomPos[i] = ReadBool(path, "HUD", "CustomPos" + i, settings.HudUseCustomPos[i]);
                settings.HudPositionX[i] = ReadFloat(path, "HUD", "PositionX" + i, settings.HudPositionX[i]);
                settings.HudPositionY[i] = ReadFloat(path, "HUD", "PositionY" + i, settings.HudPositionY[i]);

                // 구 버전(절대 좌표) 마이그레이션 — 1.0보다 크면 정규화 좌표로 변환
                // 1920×1080 기준으로 edit된 것으로 가정
                if (settings.HudPositionX[i] > 1.0f)
                    settings.HudPositionX[i] /= 1920f;
                if (settings.HudPositionY[i] > 1.0f)
                    settings.HudPositionY[i] /= 1080f;
            }
            settings.HudHitErrorScale = ReadFloat(path, "HUD", "HitErrorScale", settings.HudHitErrorScale);
            settings.HudEditSnap = ReadBool(path, "HUD", "EditSnap", settings.HudEditSnap);
            // HudEditMode는 영속화하지 않음 — 재시작 시 항상 false (NEWNEWOVERLAY 동일).

            // [Skin]
            settings.SkinName = ReadString(path, "Skin", "Name", settings.SkinName);

            // [Paths]
            settings.OsuRoot = ReadString(path, "Paths", "OsuRoot", settings.OsuRoot);
        }

        public static void Save(OverlaySettings settings)
        {
            string path = ConfigPath;

            // [Overlay]
            WriteBool(path, "Overlay", "Enabled", settings.Enabled);
            WriteBool(path, "Overlay", "CaptureBlocked", settings.CaptureBlocked);
            WriteInt(path, "Overlay", "FpsCap", settings.FpsCap);
            WriteBool(path, "Overlay", "HiddenOverride", settings.HiddenOverride);

            // [Difficulty]
            WriteFloat(path, "Difficulty", "AR", settings.ArValue);
            WriteFloat(path, "Difficulty", "CS", settings.CsValue);
            WriteFloat(path, "Difficulty", "DtAR", settings.ArDtValue);
            WriteFloat(path, "Difficulty", "HtAR", settings.ArHtValue);

            // [Cursor]
            WriteBool(path, "Cursor", "AutoSize", settings.CursorAutoSize);
            WriteFloat(path, "Cursor", "Size", settings.CursorSize);
            WriteBool(path, "Cursor", "PackEnabled", settings.CursorPackEnabled);
            WriteString(path, "Cursor", "PackName", settings.CursorPackName);

            // [HUD]
            for (int i = 0; i < 4; i++)
            {
                WriteBool(path, "HUD", "Enabled" + i, settings.HudEnabled[i]);
                WriteInt(path, "HUD", "FontSize" + i, settings.HudFontSizes[i]);
                WriteBool(path, "HUD", "CustomPos" + i, settings.HudUseCustomPos[i]);
                WriteFloat(path, "HUD", "PositionX" + i, settings.HudPositionX[i]);
                WriteFloat(path, "HUD", "PositionY" + i, settings.HudPositionY[i]);
            }
            WriteFloat(path, "HUD", "HitErrorScale", settings.HudHitErrorScale);
            WriteBool(path, "HUD", "EditSnap", settings.HudEditSnap);

            // [Skin]
            WriteString(path, "Skin", "Name", settings.SkinName);

            // [Paths]
            WriteString(path, "Paths", "OsuRoot", settings.OsuRoot);
        }

        // ── 헬퍼 ──

        static string ReadString(string path, string section, string key, string defaultVal)
        {
            StringBuilder sb = new StringBuilder(256);
            GetPrivateProfileString(section, key, defaultVal, sb, sb.Capacity, path);
            return sb.ToString();
        }

        static bool ReadBool(string path, string section, string key, bool defaultVal)
        {
            string val = ReadString(path, section, key, defaultVal ? "On" : "Off");
            return val == "On" || val == "True" || val == "1";
        }

        static int ReadInt(string path, string section, string key, int defaultVal)
        {
            // InvariantCulture 고정 — settings.ini는 기계 데이터라 로케일과 무관해야 한다 (A4).
            string val = ReadString(path, section, key, defaultVal.ToString(CultureInfo.InvariantCulture));
            int result;
            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)) return result;
            return defaultVal;
        }

        static float ReadFloat(string path, string section, string key, float defaultVal)
        {
            // 1차: InvariantCulture + NumberStyles.Float 고정 (A4). 이게 없으면 ',' 소수점 로케일에서
            // "9.20"이 920으로 읽혀 범위를 벗어난다. '.'로 쓰인 값은 어느 로케일에서든 여기서 맞게 읽힌다.
            string val = ReadString(path, section, key, defaultVal.ToString("F2", CultureInfo.InvariantCulture));
            float result;
            if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                && !float.IsNaN(result) && !float.IsInfinity(result))
                return result;

            // 2차(하위호환): 구버전이 현재 로케일로 저장한 ini(예: de-DE의 "1,50")를 복구한다.
            // NumberStyles.Float(천단위 구분자 불허)를 유지하므로 '.' 로케일에서 "9,20"이 920으로
            // 오독되지 않고 그냥 실패해 기본값으로 간다 — A4 수정을 되돌리지 않는다.
            if (float.TryParse(val, NumberStyles.Float, CultureInfo.CurrentCulture, out result)
                && !float.IsNaN(result) && !float.IsInfinity(result))
                return result;

            return defaultVal;
        }

        static void WriteString(string path, string section, string key, string val)
        {
            WritePrivateProfileString(section, key, val, path);
        }

        static void WriteBool(string path, string section, string key, bool val)
        {
            WritePrivateProfileString(section, key, val ? "On" : "Off", path);
        }

        static void WriteInt(string path, string section, string key, int val)
        {
            WritePrivateProfileString(section, key, val.ToString(CultureInfo.InvariantCulture), path);
        }

        static void WriteFloat(string path, string section, string key, float val)
        {
            // InvariantCulture 고정 — 로케일과 무관하게 항상 '.' 소수점으로 기록 (A4).
            WritePrivateProfileString(section, key, val.ToString("F2", CultureInfo.InvariantCulture), path);
        }
    }
}