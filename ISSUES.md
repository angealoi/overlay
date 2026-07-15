# 코드베이스 문제점 분석

> **분석일**: 2026-07-15 · **기준 커밋**: `c358f3c` (PR #1 머지 직후)
> **방법**: 전체 소스 ~15,400줄 중 ~13,000줄 직접 정독 + 표적 검증. 모든 항목에 실제 확인한 파일:줄 명시.
> 읽지 않은 파일은 [여기](#i-못-본-곳)에 기재. 추측성 항목은 표기함.
>
> **총 70건**
> - 일반 결함 45건: 크래시 5 · 리소스 누수 3 · 시각 정확성 6 · 성능 5 · 잠복 결함 7 · 죽은/거짓 코드 10 · 기타 9
> - **포팅 충실도 25건** (H 섹션) — `ref/osu-stable` 1:1 대조

## ✅ 해결 로그

| 날짜 | 항목 | 커밋 | 검증 |
|---|---|---|---|
| 2026-07-15 | **DT/HT 배속 이중 적용** (오버레이 전체 1.5배속) | `9df24fd` | 수치 검증 + 실기 확인 |
| 2026-07-15 | **Auto 버튼 개편** (일회성 채우기 + HR/EZ 반영 + CS 하한) | `9df24fd` | 실기 확인 |
| 2026-07-15 | **F9 일부**: 스킨/커서 목록 휠 차단 (ComboBox 누락) | `9df24fd` | 실기 확인 |
| 2026-07-15 | **F1~F8**: 죽은 코드 8건 제거 (아래 F 섹션 참조) | `babf942` | 빌드 통과(컴파일러 검증) |
| 2026-07-15 | **H1**: FadeIn 상수화 (lazer 공식 → 400) | `b3958e1` | 실기 확인 |
| 2026-07-15 | **H4**: 스네이킹 병합 forceEnd/straight 반영 | `b3958e1` | 실기 확인 |
| 2026-07-15 | **Difficulty Changer 오버라이드가 맵 로드 시 리셋되던 문제** (apply 블록이 nomod 값 대입) | `1eddd17` | 실기 확인 |
| 2026-07-15 | **고AR 오버라이드 히트 애니메이션 충돌** (FadeIn 클램프 복원 — 충실도 무손실) | `846221b` | 실기 확인 |
| 2026-07-15 | **NOMOD/HT AR 슬라이더 상한 10** (DT는 12) + 로드값 클램프(A1 계열 크래시 예방) | `d79570d` | 실기 확인 |
| 2026-07-15 | **H3**: newStyle 스피너 glow가 아예 안 보이던 문제 (+ UpdateTransformations 분리, 종료 페이드) | `4261cad` | 실기 확인 |
| 2026-07-15 | **H17**: 보너스 flash가 흰색으로 굳던 문제 (H3 수정으로 드러남) | `8a2442e` | 실기 확인 대기 |

> DT 배속은 [H1과 별개](#h1-fadein이-stable-상수가-아니라-lazer-공식)로, `speedMultiplier`/`scalePreEmpt` 이중 적용 문제였다.

---

## 목차

- [A. 크래시 가능 (5건)](#a-크래시-가능)
- [B. 리소스 누수 (3건)](#b-리소스-누수)
- [C. 시각적 정확성 (6건)](#c-시각적-정확성)
- [D. 성능 (5건)](#d-성능)
- [E. 잠복 결함 (7건)](#e-잠복-결함)
- [F. 죽은 코드 / 거짓 코드 (10건)](#f-죽은-코드--거짓-코드)
- [G. 견고성 / 기타 (9건)](#g-견고성--기타)
- [**H. 포팅 충실도 — osu-stable 대조 (25건)**](#h-포팅-충실도--osu-stable-대조)
- [I. 못 본 곳](#i-못-본-곳)
- [부록: 아키텍처 사실 / 우선순위 제안](#부록)

---

## A. 크래시 가능

### A1. settings.ini 값 하나로 기동 불능 ⚠️ 최우선
| | |
|---|---|
| 위치 | `ControlPanel/ControlPanelForm.cs:451, 460, 136, 217, 581` |
| 증상 | 범위 밖 INI 값이 있으면 생성자에서 예외 → 패널이 메인 폼이라 **앱이 아예 안 뜸** |
| 트리거 | `AR=15`, `FpsCap=-1`, `Size=0`, `AR=NaN` 등 손편집/손상된 INI. `:581 CreateDirectory`는 쓰기 불가 폴더에서 동일 |
| 비고 | INI를 지우기 전까지 복구 불가. A4(문화권)와 결합하면 사용자 잘못 없이도 발생 |

`tb.Value = (int)(value * 10)` — TrackBar/NumericUpDown의 Min/Max 밖 값 대입은 예외를 던진다. 로드 직후 클램프가 없다.

### A2. 텍스처 없는 스킨에서 판정 오는 순간 NullReferenceException
| | |
|---|---|
| 위치 | `Gameplay/HitObjects/HitCircleOsu.cs:221 (원인), 466, 516 (폭발 지점)` |
| 증상 | hitcircle/approachcircle 텍스처 로드 실패 시 생성자가 스프라이트 전부 null인 채 早退하는데, `Arm()`은 null 검사 없이 접근 |
| 트리거 | 깨진 스킨 + 판정 발생 |
| 비고 | `:328`은 `spriteHitCircle?.`인데 `:466`은 검사 없음 — 같은 파일 안에서 비일관 |

### A3. `TextureManager.LoadAll` null 반환 미처리
| | |
|---|---|
| 위치 | `Rendering/Textures/TextureManager.cs:289 (null 반환) → Gameplay/HitObjects/SliderOsu.cs:189-190 (역참조)` |
| 증상 | `sliderBallTextures.Length` 에서 NRE |
| 트리거 | sliderb / sliderfollowcircle이 유저 스킨에도 임베디드 기본 스킨에도 없는 경우 |
| 비고 | followpoint 호출부(`HitObjectManagerOsu.cs:463`)만 null 체크함 — 호출부마다 다름 |

### A4. 문화권 의존 직렬화
| | |
|---|---|
| 위치 | `ControlPanel/SettingsSerializer.cs:146, 169` |
| 증상 | `float.TryParse`/`ToString("F2")`가 현재 로케일 사용. 소수점이 `,`인 로케일에서 `"9.20"` → **920**으로 읽힘 → A1 크래시 직행 |
| 비고 | 한국어 로케일(`.`)에서는 무증상. `CultureInfo.InvariantCulture`로 통일해야 함 (BeatmapParser는 이미 Invariant 사용 중 — 여기만 빠짐) |

### A5. 비트맵 Combo 색상 범위 초과
| | |
|---|---|
| 위치 | `Gameplay/Beatmap/BeatmapParser.cs:199` |
| 증상 | `Color.FromArgb(r,g,b)` 무클램프 — `Combo1: 300,0,0` 같은 맵에서 ArgumentException |
| 비고 | 파싱 Task의 catch가 받아 앱은 살지만, 그 맵은 **조용히** 로드 실패 (로그 한 줄뿐) |

---

## B. 리소스 누수

### B1. 슬라이더 바디 FBO 누수 — 맵 바뀔 때마다 ⚠️ 최우선
| | |
|---|---|
| 위치 | `Gameplay/HitObjects/SliderOsu.cs:66 (cachedFbo), 747-751 (rebuild시만 Dispose)` + `Gameplay/HitObjects/HitObjectManagerOsu.cs:59 (리스트만 Clear)` |
| 증상 | 맵 전환 시 그려졌던 **모든 슬라이더의 FBO+텍스처가 Dispose 없이 드롭** |
| 규모 | 슬라이더 200개 맵 × 10판 = FBO/텍스처 수천 개. `RenderTarget2D`에 파이널라이저 없음 → 영구 누수 |
| 비고 | `MmSliderRenderer.Draw` 주석에 "호출자가 Dispose 책임"이라 적혀 있는데 호출자가 안 지킴 |

### B2. 커서팩 텍스처 누수 — 맵 바뀔 때마다
| | |
|---|---|
| 위치 | `Gameplay/Cursor/CursorRenderer.cs:210 (CreateFromBitmap — 무캐시)` + `Overlay/OverlayForm.cs:699 (비트맵 적용마다 Reload)` |
| 증상 | 커서팩 켜면 맵당 최대 3텍스처(cursor/middle/trail) 누수 |
| 비고 | `CursorRenderer.cs:95` 주석 *"텍스처는 캐시되어 있으므로"*는 스킨 경로만 참 — 팩 경로는 `CreateFromBitmap`이라 캐시에 안 들어감 |

### B3. 콤보 숫자 유령 스프라이트
| | |
|---|---|
| 위치 | `Gameplay/HitObjects/HitCircleOsu.cs:106 (리스트만 Clear)` |
| 증상 | `CreateComboNumberSprites`가 자기 리스트만 비우고 SpriteManager에 이미 추가된 옛 숫자는 제거 안 함 |
| 트리거 | 화면에 노트가 떠 있는 상태에서 난이도 슬라이더 조작 → `UpdateDifficulty` → 옛 숫자가 화면에 잔류 (맵 리로드 전까지) |

---

## C. 시각적 정확성

### C1. 스택된 슬라이더 — 몸통 따로 머리 따로
- **위치**: `Gameplay/HitObjects/SliderOsu.cs:134` (커브가 생성자에서 스택 **전** 좌표로 계산) vs `HitObjectManagerOsu.cs:132` (스택은 그 뒤에 실행) vs `SliderOsu.cs:532` (시작원만 이동)
- **증상**: 스택 맵에서 슬라이더 시작원은 스택 오프셋만큼 이동하는데 바디/볼/틱은 제자리 — 어긋난 채 렌더링
- **참고**: osu! stable은 슬라이더 전체를 이동시킴

### C2. 리버스 화살표 제거 누락
- **위치**: `Gameplay/HitObjects/HitCircleOsu.cs:785` — `HitCircleSliderEnd`가 `AddToSpriteManager`만 override, `RemoveFromSpriteManager`는 안 함
- **증상**: 시간 윈도우 이탈 후에도 화살표가 SpriteManager에 잔류. 투명하지만 매 프레임 순회 비용이고 맵 끝까지 누적

### C3. HR flip이 clamp 뒤에 적용
- **위치**: `Gameplay/Beatmap/BeatmapParser.cs:216-217` — y를 512까지 허용해놓고 `384-y`
- **증상**: y∈(384,512] 객체가 HR에서 음수 좌표

### C4. 동일 StartTime 객체 판정 오매칭 (2B/aspire 맵)
- **위치**: `Gameplay/HitObjects/HitObjectManagerOsu.cs:836, 861, 893` (StartTime 동등 비교, 첫 매치) + `Gameplay/Scoring/HitBurst.cs:63` (Dictionary 첫 승)
- **증상**: 동시 타이밍 객체가 있는 맵에서 판정/히트버스트가 엉뚱한 객체로 감

### C5. 불안정 정렬 z-플리커 가능성
- **위치**: `Rendering/SpriteManager.cs:126` — `List.Sort`는 불안정 정렬
- **증상**: 동일 Depth 스프라이트의 앞뒤 순서가 재정렬 때마다 뒤바뀔 수 있음 (프레임 간 깜빡임)

### C6. 0 나눗셈 → NaN 좌표
- **위치**: `Gameplay/HitObjects/SliderOsu.cs:273` (p1==p2인 선분에서 틱 위치), `:476` (zero-length 슬라이더에서 볼 위치)
- **증상**: NaN 위치의 스프라이트 — 에일리언 맵에서만

---

## D. 성능

### D1. AOB 스캔 — 스캔마다 수백 MB 재할당 ⚠️ 실측 증거 있음
- **위치**: `Memory/AobScanner.cs:24, 59` — 리전마다 `new byte[최대 100MB]`, 시그니처마다 전체 패스 반복 (기동 시 8회+)
- **실측**: 커서 재스캔이 걸린 세션에서 렌더 스레드 **~1초 정지** (overlay.log의 MODE 전환→스캔 완료 간격 4회 확인)
- **방향**: 버퍼 재사용 + 여러 시그니처 일괄 스캔

### D2. HOM 오프셋 브루트포스가 매 프레임
- **위치**: `Memory/OsuMemoryReader.cs:691-738` (~127×40 조합 × ReadProcessMemory syscall), `:784` (미감지 동안 매 프레임 재시도)
- **증상**: 맵 시작/리트라이 직후 감지 성공 전까지 프레임당 수십 ms 가능

### D3. 스네이킹 중 FBO 생성/파괴 ~30회/슬라이더
- **위치**: `Graphics/Renderers/MmSliderRenderer.cs:340` (매 rebuild마다 new RenderTarget2D) + `SliderOsu.cs:737` (1/30 양자화)

### D4. SpriteManager.Remove가 O(n)
- **위치**: `Rendering/SpriteManager.cs:77` — `List.Remove` 선형 탐색. 윈도우 이탈로 다발 제거 시 O(n²)

### D5. 히트에러바 중앙값 — 프레임당 할당+정렬
- **위치**: `Rendering/HudRenderer.cs:521` — `new List<int>(errors)` + `Sort()` 매 프레임 (300fps면 초당 300회)

---

## E. 잠복 결함 (지금은 안 터지지만 한 발짝 거리)

### E1. ProcessMemory 재사용 버퍼가 스레드 비안전
- **위치**: `Memory/ProcessMemory.cs:76-82` — `bytesRead`/`buf4`/`buf8`이 인스턴스 필드
- **현황**: 현재 모든 호출이 UI 스레드라 무해 (검증함). 그러나 누가 Task에서 `pm.Read*` 하나만 불러도 조용한 값 섞임. **경고 주석조차 없음**

### E2. AOB 첫-매치 무검증
- **위치**: `Memory/AobScanner.cs:40` (첫 매치 즉시 반환), `:96` (오퍼랜드 무검증 신뢰)
- **이력**: 커서 40% 버그의 뿌리가 이 설계. 커서·해상도는 개별 방어를 얻었지만 `timeSlot`/`modeSlot`/`modsSlot`은 여전히 무방비 — 패턴 충돌 시 조용히 쓰레기 값

### E3. QuadBatch 오버플로 시 `Flush(null)`
- **위치**: `Rendering/Batches/QuadBatch.cs:46` — 셰이더 바인딩 중인데 fixed-function 폴백 경로로 그림
- **트리거**: 한 배치에 ~10,900쿼드 초과 시에만 — 현실적으론 드묾

### E4. Fields enum 값 충돌
- **위치**: `Rendering/Sprites/pSprite.cs:12-29` — `TopCentre==Gamefield(1)`, `TopRight==GamefieldWide(2)`, `Centre==StoryboardCentre(4)`, `BottomLeft==NativeStandardScale(6)` 등 6쌍
- **현황**: 충돌 멤버는 현재 미사용임을 확인. 그러나 누가 `Fields.Centre`를 쓰는 순간 Gamefield 좌표 변환을 타는 지뢰

### E5. 파싱 Task ↔ 렌더 스레드 경합 읽기
- **위치**: `Overlay/OverlayForm.cs:471` — Task에서 `renderer.GameField.Width/Ratio` 읽는 동안 렌더 스레드가 Resize 가능 → 찢어진 쌍 (새 Width + 옛 Ratio)
- **참고**: `pendingBeatmapApply` volatile 플래그의 쓰기/읽기 순서 자체는 **올바름** (검증함)

### E6. 낡은 파싱 결과 잠깐 적용
- 맵 A 파싱 완료가 맵 B 전환 직후 도착하면 몇 프레임 A가 표시. 다음 프레임 감지로 자가 치유 — 낮은 우선순위

### E7. 비트맵 문자열 캐시가 포인터 주소로 동일성 판정
- **위치**: `Memory/OsuMemoryReader.cs:469-487` — GC 주소 재사용 시 이론상 다른 맵의 폴더명 잔류. 확률 극히 낮음

---

## F. 죽은 코드 / 거짓 코드

> 죽은 코드가 위험한 이유: 이번 분석 전 외부 도구가 이 코드들을 "동작하는 코드"로 오인해 존재하지 않는 버그를 리포트했음. 사람도 같은 함정에 빠진다.

| # | 위치 | 내용 |
|---|---|---|
| ~~F1~~ ✅ | `Overlay/OverlayForm.cs` | ~~`syncTimer` — `Start()` 없이 죽어있음~~ → **제거** (`babf942`). `ApplyFpsCap`은 `fpsCapInterval`만 세팅하도록 정리 |
| ~~F2~~ ✅ | `Memory/Signatures.cs` | ~~`Mask`/`ScanAll` 필드 — 읽는 곳 0~~ → **제거** (`babf942`), 대입 18곳 삭제 |
| ~~F3~~ ✅ | `ControlPanel/OverlaySettings.cs` | ~~`Clone()`에 `CursorPack*` 누락~~ → **필드 채워 살림** (`9df24fd`). 호출부는 여전히 0이나 미래 버그 예방 |
| ~~F4~~ ✅ | `Memory/ProcessMemory.cs` | ~~`GetModuleInfo` + 전용 P/Invoke 4개~~ → **제거** (`babf942`) |
| ~~F5~~ ✅ | `Rendering/Textures/TextureManager.cs` | ~~`sourceCache` — 빈 순회~~ → **제거** (`babf942`) |
| ~~F6~~ ✅ | `Graphics/Renderers/MmSliderRenderer.cs` | ~~`gradientLineBatch` — 사용 0~~ → **제거** (`babf942`) |
| ~~F7~~ ✅ | `Gameplay/Scoring/HitBurst.cs` | ~~`GetHitObjectInfo(int)` + `judgementTime`~~ → **제거** (`babf942`) |
| ~~F8~~ ✅ | `SliderOsu.cs`, `HitObjectManagerOsu.cs`, `HitCircleOsu.cs` | ~~미사용 메서드 3+1개~~ → **제거** (`babf942`) |
| F9 (부분 ✅) | 여러 곳 | **틀린 주석**: ① "ControlPanelForm 스레드에서 OpenGL 작업 불가"(`OverlayForm.cs:71, 197, 219`) — 실제로는 단일 UI 스레드 ② `HitBurst.cs:105` "매 프레임 Clear" — 실제는 retry 시만 ③ `OverlayForm.cs:376` "alpha=8" — 실제 인자는 4x MSAA. **미해결** (휠 차단 주석만 `9df24fd`에서 수정됨) |
| F10 | `Rendering/Shader.cs:49, 78` | 셰이더 컴파일/링크 실패가 `Debug.WriteLine` — **overlay.log에 안 남음**. 실패 시 조용히 fixed-function 폴백으로 그려져 원인 추적 불가. **미해결** |

---

## G. 견고성 / 기타

| # | 위치 | 내용 |
|---|---|---|
| G1 | `Program.cs:31` | WinExe라 콘솔이 없어 `Console.ReadLine()`이 즉시 null 반환 — "대기 후 종료" 의도가 동작 안 함 |
| G2 | `Memory/ProcessMemory.cs:152-157` | osu! 다중 실행 시 첫 프로세스 임의 선택. 배열의 나머지 `Process` 객체 미해제 |
| G3 | (설계) | 재접속 로직 없음 — osu! 재시작 시 죽은 핸들을 영구 보유, 오버레이도 재시작해야 함 |
| G4 | `Memory/OsuMemoryReader.cs:330` | Menu(모드 0)에서는 해상도 갱신이 안 도는데, HUD 편집 모드는 Menu에서도 오버레이를 표시 → 낡은 지오메트리로 편집 |
| G5 | (설정) | `CursorPackName`/`SkinName`에 `..\..` 경로 탈출 가능 — 자기 설정 파일로 자기 공격 수준, 실위험 낮음 |
| G6 | `ControlPanelForm.cs:394`, `OverlayForm.cs:390` | `statusSync`/`syncTimer` Dispose 안 됨 — 프로세스 수명이라 실해 없음 |
| G7 | `Memory/ProcessMemory.cs:373` | `ReadSharpString` 호출당 `byte[]` 할당 — Config Dictionary 스캔 시 234회/회 |
| G8 | (미확인) | High-DPI 매니페스트 유무 — **검증 안 함**, 이전 외부 분석의 주장만 있음 |
| G9 | (일반론) | 테스트 0개 — AOB/난이도/파서 등 핵심 로직 회귀 검증 불가 |

---

## H. 포팅 충실도 — osu-stable 대조

> **목표**: 오버레이에 렌더링되는 circle / slider / spinner의 모든 사항이 `C:\Users\sadisty\Downloads\VSCODE\1\ref\osu-stable`과 100% 일치.
> **방법**: 양쪽 소스 나란히 정독. stable 측은 `HitCircleOsu.cs`(342줄), `HitCircleSliderEnd.cs`(152), `HitCircleSliderStart.cs`(30), `SliderOsu.cs`(2241 중 핵심 ~700), `SpinnerOsu.cs`(556), `HitObjectManager.cs`(1959 중 핵심 ~400) 확인.
> **제외**: 스피너 RPM 카운터는 **의도적 미구현**으로 확인됨 — 불일치로 세지 않음.
>
> **경로 표기**: stable = `ref/osu-stable/osu!/GameplayElements/...`

### H-1. 최우선 — 모든 맵에서 실제로 다르게 렌더링됨 (5건)

#### ~~H1. `FadeIn`이 stable 상수가 아니라 lazer 공식~~ ✅ 해결 (`b3958e1`)
| | |
|---|---|
| stable | `HitObjectManager.cs:120` — `internal static readonly int FadeIn = 400;` **상수** |
| ~~ours~~ | ~~`FadeIn = 400 × min(1, PreEmpt/450)` (lazer 공식)~~ → `dv.FadeIn = 400` 상수로 수정 |
| 함께 수정 | ~~소비처 `Math.Min(FadeIn, PreEmpt)` 클램프 제거~~ → **되돌림** (`846221b`). 클램프는 PreEmpt<400(오버라이드 AR>10.6)에서만 작동하고 stable 범위(AR≤10)에선 무의미해 stable과 동일. 제거 시 AR12 오버라이드에서 페이드인이 히트를 넘겨 팝 애니메이션과 충돌했다. 어프로치서클 클램프는 처음부터 유지 |
| 검증 | 빌드 통과 · **실기 확인 대기** (AR12 오버라이드 히트 애니메이션) |

#### H2. 반복 슬라이더 리턴 패스의 틱이 통째로 사라짐
| | |
|---|---|
| stable | `SliderOsu.cs:818` `distanceToEnd = total` — **세그먼트마다 리셋**. `skipTick`은 세그먼트 내내 유지(스티키). `:921-931`에서 세그먼트 경계 **미러 보정** (`scoringDistance = tickDistance - scoringDistance`) |
| ours | `Gameplay/HitObjects/SliderOsu.cs:258` — `distanceToEnd = data.Length × (segCount − i) − scoringLengthTotal − scoringDistance` **선분마다 재계산**. skipTick도 선분마다 리셋. 미러 보정 **없음** |
| 검산 | segCount=2, i=1 진입 시 진행거리 ≈ L → `L×1 − L − d = −d` → **음수** → 첫 틱부터 skipTick → **리턴 패스 틱 전부 미생성**. 역으로 첫 패스에선 stable이 스킵하는 끝부분 틱을 ours가 스킵 안 함 |
| 추가 | **틱 소멸 시점도 다름**: stable은 세그먼트 종료 시 일괄(`:933-935`), ours는 각 틱의 scoreTime에 개별(`SliderOsu.cs:307-308`) → stable은 볼이 지나가도 틱이 세그먼트 끝까지 남음 |

#### ~~H3. newStyle 스피너 glow가 아예 안 보임~~ ✅ 해결 (`4261cad`)
| | |
|---|---|
| 원인 | `spriteGlow.Alpha` 직접 설정이 생성 시 `Fade(0,0)` 변환에 매 프레임 덮여 무효 |
| 수정 | stable(:444-446) 방식 — **Fade 변환의 Start/EndFloat 자체를 진행도로 수정**. 진행 중 파란색(:443), 스케일 OutQuad(:448 easeOutVal — 기존 cos 공식은 easing 방향 반대), percent 무클램프 |
| 전제 수정 | `UpdateTransformations`가 spin/clear/glow 변환까지 Clear하던 것을 메인 스프라이트만으로 분리 — stable은 생성자에서 셋이 생기기 전에 돌고, 셋은 자기 변환을 소유 |
| 종료 처리 | glow가 보이게 되면서 드러난 잔상(종료 후 ~2초) — stable `Hit()`의 `FadeOut(300)` 대응으로 종료 감지 시 1회 페이드아웃, ResetState에서 캔버스 복원 |
| 검증 | 빌드 통과 · **실기 확인 대기** (스피너 돌릴 때 glow 표시/통과 시 파란 유지/종료 페이드) |

#### ~~H4. 스네이킹 선분 병합에서 `forceEnd` 무시~~ ✅ 해결 (`b3958e1`)
| | |
|---|---|
| stable | `SliderOsu.cs:1074` — 병합 중단 조건에 **`\|\| sliderCurveSmoothLines[i].forceEnd`** 포함, `min_dist`는 `.straight ? 32 : 6` |
| ~~ours~~ | ~~`dist > minDist \|\| last \|\| (i==count-2)`, minDist 항상 6~~ → forceEnd 추가 + `minDist = straight ? 32 : 6`. `Line.forceEnd`/`straight`는 SliderCurve가 이미 세팅 중이라 배선만 |
| 검증 | 빌드 통과 · **실기 확인 대기** (레드앵커 슬라이더 모서리) |

#### H5. 브레이크 뒤 콤보 번호/색 리셋 누락
| | |
|---|---|
| stable | `HitObjectManager.cs:1258-1263` — 브레이크를 지난 첫 객체에 **강제 NewCombo** |
| ours | `Gameplay/Beatmap/BeatmapParser.cs` — `[Events]` 섹션의 브레이크를 **파싱하지 않음** |
| 증상 | 브레이크가 있는 **모든 맵**에서 콤보 번호·콤보 색이 stable과 어긋남 |

### H-2. 조건부 — 특정 맵 / 스킨 / 모드에서만 (17건)

| # | 항목 | stable | ours |
|---|---|---|---|
| H6 | **옛 맵 베지어** | v≤6 / v7-8 / v>8 알고리즘 3종. v<10은 `CreateBezierWrong` (슬라이더가 1/50 짧음) — `SliderOsu.cs:479-567` | 항상 최신 알고리즘 → **v9 이하 맵의 곡선 모양/길이가 다름** |
| H7 | **옛 맵 틱 간격** | v<8은 BpmMultiplier로 나누지 **않음** — `SliderOsu.cs:673` | 항상 나눔 |
| H8 | **v≤8 스피너 콤보** | 스피너면 무조건 `forceNew` — `HitObjectManager.cs:1269` | NewCombo일 때만 |
| H9 | **HD 틱 페이드** | hidden이면 틱에 `Fade 1→0 (…→scoreTime)` 추가 — `SliderOsu.cs:895, 903` | 없음 → HiddenOverride 시 틱 잔존 |
| H10 | **old-layout 리버스 화살표** | Scale + **Rotation ±π/32 진동** — `HitCircleSliderEnd.cs:90-95` | new-layout(Scale만) 고정 |
| H11 | **SpinnerFadePlayfield** | 검은 배킹 레이어 2장 — `SpinnerOsu.cs:126-144` | 누락 |
| H12 | **스피너 어프로치서클 조건** | `SpriteCircleTop.Texture.Source != SkinSource.Osu` — 실제 로드된 텍스처 출처로 판단 — `SpinnerOsu.cs:194` | `!SkinManager.IsDefault` — **스킨이 스피너 텍스처만 없을 때 오판** |
| H13 | **스피너 회전** | 부호 있는 누적(반시계 반영). `turnRatio`는 middle2 텍스처 없으면 **1** — `SpinnerOsu.cs:272-279` | `Math.Abs()` 절대값(항상 한 방향) + **0.5 고정** → oldstyle에서 절반 속도 |
| H14 | **metre 블링크** | `RNG.NextBool(((int)percent % 10) / 10f)` **확률적** 깜빡임 — `SpinnerOsu.cs:456` | `>= 0.5f` **결정적** 반올림 |
| H15 | **followpoint 등장** | `SkinManager.IsDefault && GameBase.NewGraphicsAvailable`일 때만 Scale + **Movement**(posStart→pos, Out) — `HitObjectManager.cs:1887-1891` | Scale을 **무조건** 적용 + **Movement 누락**. ours의 `posStart` 계산(`HitObjectManagerOsu.cs:488`)은 **데드 코드** |
| H16 | **sliderBall FlipVertical** | 곡선 시작 각도로 상하 반전 — `SliderOsu.cs:796-800` | 없음 |
| ~~H17~~ ✅ | ~~**glow flash 복귀**~~ → 해결 (`8a2442e`) | `FlashColour(White, 200)` — 200ms 후 파란색 복귀 — `SpinnerOsu.cs:386` | ~~White로 바꾸고 복귀 없음~~ → glow 전용 수동 보간(`ApplyGlowColour`). pSprite에 Colour 변환 지원이 없어 인프라 추가 대신 국소 처리 |
| H18 | **어프로치서클 소멸** | 생성 시 소멸 fade **없음**. `Arm()`에서만 (히트: 즉시 / 미스: 60ms) — `HitCircleOsu.cs:245, 264` | 생성 시 `0.9→0 @ startTime→+60` **하드코딩** + Arm 것도 추가 → 근사이나 1:1 아님 |
| H19 | **Disarm 후 Scale 리셋** | `SpriteHitCircle1.Scale = 1`, `Text.Scale = TEXT_SIZE` — `HitCircleOsu.cs:223-225` | 누락 |
| H20 | **스택 위치 재적용** | 범위 내 **전 객체** 무조건 `ModifyPosition` — `HitObjectManager.cs:1761-1765` | `StackCount != 0`인 것만 → 재계산 시 낡은 위치 잔존 가능 |
| H21 | **정렬 안정성** | `ListHelper.StableSort` — `HitObjectManager.cs:1240` | `List.Sort`(불안정) → 동시각 객체 순서 흔들림 (= [C5](#c5-불안정-정렬-z-플리커-가능성)) |
| H22 | **틱 소멸 방식** | 세그먼트 끝에 일괄 `Fade(0,0)` — `SliderOsu.cs:933-935` | 각 틱의 scoreTime에 개별 (H2에 포함) |

### H-3. 이미 A~G에 기재된 것 중 fidelity 위반이기도 한 것 (3건)

| # | 항목 | stable 기준 |
|---|---|---|
| H23 | [C1. 스택된 슬라이더 바디 미이동](#c1-스택된-슬라이더--몸통-따로-머리-따로) | stable `ModifyPosition`은 **슬라이더 전체**(바디·볼·틱 포함)를 옮김 |
| H24 | [C2. 리버스 화살표 제거 누락](#c2-리버스-화살표-제거-누락) | stable은 `SpriteCollection` 통째 관리 → 누락 불가능한 구조 |
| H25 | [C4. 동시 StartTime 객체 오매칭](#c4-동일-starttime-객체-판정-오매칭-2baspire-맵) | stable은 객체 참조로 직접 처리 (StartTime 조회 없음) |

### 검증 결과 — **일치 확인**된 것 (안심 목록)

수정 시 건드리지 말 것. 이미 stable과 1:1임을 확인함:

- **커브 수학 전체**: Catmull(`SLIDER_DETAIL_LEVEL=50` ✓) · PerfectCurve(원 통과 계산 · 세그먼트 `curveLength*0.125` ✓) · BezierApproximator(v>8 경로 · subdivision 버퍼 ✓) · SpatialLength 자르기(`MIN_SEGMENT_LENGTH` ✓)
- `VirtualEndTime` 공식 · `Velocity` 공식 · 틱 반경 제외(`HitObjectRadius²`) · endCircle `appearTime` · `sliderBody` depth(`drawOrderBwd(EndTime+10)`) 및 HD 페이드아웃 · sliderBall `FrameDelay`
- **스태킹 알고리즘 본체** — 양방향 패스, 슬라이더 음수 스택 특수 케이스 포함 (적용부 H20만 예외)
- 팔로워 수치 — InitSlide(60ms fade / 180ms scale Out), 종료(200ms In/Out). 메모리 tracking 기반 재해석이나 **수치 등가**
- HitCircle `Arm`/`Disarm` 타이밍 · HD 분기 · 콤보숫자 레이아웃 수학 · DrawOrder 3종 공식 · 스피너 clear/bonus(`1000×excess/2` ✓)

### 구조적으로 1:1 불가 — 등가로 봐야 하는 것

메모리 재해석 방식이라 stable과 코드 형태가 다를 수밖에 없는 부분. **불일치로 세지 않음**:

| 항목 | stable | ours | 판정 |
|---|---|---|---|
| **sliderball 위치** | 선분마다 Movement 변환을 미리 구움 (`SliderOsu.cs:851-857`) | `GetBallPosition(timeMs)` 매 프레임 계산 (`SliderOsu.cs:455-486`) | **수학 등가 확인** — 경로가 SpatialLength로 잘린 후라 진행률 계산 일치. 메모리에서 읽는 건 `TimeMs`뿐 |
| **팔로우서클** | `InitSlide`/`KillSlide` 호출 | 메모리 `IsTracking`(hoPtr+0x120) 기반 | 수치 등가 |
| **스피너 회전량** | 마우스 각도로 물리 시뮬 | 메모리 `FloatRotationCount` | 등가. 단 **H13의 부호/배율은 고칠 수 있는 부분** |
| **시작원 판정** | 내부 히트 판정 | 메모리 `StartIsHit`(SliderStartCircle+0x84) | 등가 |

> **핵심**: 이 프로그램은 osu!의 렌더링 결과를 읽는 게 아니라, **"osu!와 같은 시계(TimeMs) + 같은 수학"으로 독립 재현**한다. 그래서 커브 수학·타이밍 공식이 stable과 어긋나면 화면 전체에 파급된다 (예: DT 1.5배속 버그).

### H 섹션 권고 순위

| 순위 | 항목 | 이유 |
|---|---|---|
| 1 | **H1** (FadeIn 상수) | 뿌리. 6개 스프라이트 계열에 파급. 수정은 한 줄 |
| 2 | **H2** (리턴 패스 틱) | 반복 슬라이더 = 매우 흔함. 틱이 통째로 없음 |
| 3 | **H3** (스피너 glow) | newStyle 기본 스킨에서 항상 발생 |
| 4 | **H4** (forceEnd) | 레드앵커 슬라이더 = 흔함 |
| 5 | **H5** (브레이크 콤보) | 브레이크 있는 모든 맵 |
| 6 | H6~H8 | 옛 맵(v≤9) 한정 |
| 7 | H9~H22 | 특정 스킨/모드 조건부 |

---

## I. 못 본 곳

아래는 **표적 확인만 했고 전문 정독은 안 한** 파일들. 여기서 추가 발견이 나올 수 있다:

- `Gameplay/HitObjects/SpinnerOsu.cs` (607줄)
- `Gameplay/HitObjects/SliderCurve.cs` (426줄)
- `Gameplay/Cursor/CursorRenderer.cs` 트레일 파티클 부분 (~200줄)
- `Overlay/HudEditController.cs` (262줄)
- `Rendering/FontRenderer.cs`, `Rendering/HudRenderer.cs` 상단부
- `Overlay/WindowInterop.cs`, `Overlay/ClickThrough.cs`, `Overlay/CaptureBlock.cs`
- `Graphics/Batches/LinearBatch.cs`, `Rendering/ShaderManager.cs`
- `Skinning/SkinManager.cs` 전문 (LoadColour/skin.ini 색상 파싱은 확인 — try로 보호됨)

---

## 부록

### 검증으로 확정한 아키텍처 사실

이번 분석에서 코드 주석/통념과 달랐던 것들:

1. **단일 UI 스레드다.** `Program.cs:82` — `overlay.Show()` 후 `Application.Run(panel)`. 렌더 루프(`Application.Idle`), 패널 이벤트, 상태 타이머 전부 한 스레드. 유일한 별도 스레드는 비트맵 파싱 `Task.Run`이며 파일만 읽는다. → "스레드 때문에 지연 처리" 주석들은 전부 틀린 전제.
2. **`ReadProcessMemory`는 요청 범위에 못 읽는 페이지가 있으면 실패를 반환한다** (부분 성공 없음) — `bytesRead` 미확인은 이론적 문제에 그침.
3. **DT/HT는 osu! stable 게임플레이에서 PreEmpt/HitWindow를 건드리지 않는다** — `ApplyModsToTime`은 곡 선택 툴팁 전용 (이미 수정 완료된 건).

### 우선순위 제안

두 축이 있다. **"안정성"** 축(A/B/D/F)과 **"stable 100% 일치"** 축(H). 프로젝트 방향상 H가 핵심 목표이므로 병행 권장.

**안정성 축**

| 순위 | 항목 | 이유 |
|---|---|---|
| 1 | A1+A4 (INI 검증+Invariant) | 기동 불능은 최악의 실패 모드, 수정 저렴 |
| 2 | B1 (슬라이더 FBO 누수) | 규모가 가장 큰 누수, 장시간 세션에서 확실히 악화 |
| 3 | B2+B3 (커서팩/콤보숫자) | 같은 계열 누수, 함께 처리 |
| 4 | D1 (AOB 버퍼 재사용) | 실측 1초 스톨, 전후 비교 검증 가능 |
| 5 | F 전체 (죽은 코드 일괄 삭제) | 싸고, 이후 모든 분석의 정확도를 올림 |
| 6 | 나머지 E/G | 증거·재현 확보 후 |

**포팅 충실도 축** (프로젝트 핵심 목표)

| 순위 | 항목 | 이유 |
|---|---|---|
| 1 | H1 (FadeIn 상수) | 뿌리. 한 줄 수정으로 6개 스프라이트 계열 교정 |
| 2 | H2 (리턴 패스 틱) | 반복 슬라이더에서 틱이 통째로 사라짐 |
| 3 | H3 (스피너 glow) | 기본 스킨 newStyle에서 항상 |
| 4 | H4 (forceEnd) + H23/C1 (스택 슬라이더) | 슬라이더 형상 정확도 |
| 5 | H5 (브레이크 콤보) | 브레이크 있는 모든 맵 |
| 6 | H6~H22 | 옛 맵 / 특정 스킨 조건부 |

> **주의**: H 축 수정 시 [일치 확인된 것](#검증-결과--일치-확인된-것-안심-목록)과 [등가 항목](#구조적으로-11-불가--등가로-봐야-하는-것)은 건드리지 말 것. 이미 stable과 1:1임을 대조로 확인함.
