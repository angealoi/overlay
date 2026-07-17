# I 섹션 감사 — "못 본 곳" 미감사 파일 전수 정독 결과

> ISSUES.md의 **I. 못 본 곳**은 해결 목록이 아니라 원래 분석에서 "표적 확인만 하고 정독 안 함"으로 남긴 파일 목록이었다.
> 이 문서는 그 11개 파일(약 3,700줄)을 워크플로로 전수 정독 + osu-stable 코드 대조 + 적대적 2렌즈 검증한 결과다.
>
> **감사 규모**: 85 에이전트(파일별 감사 9단위 → 발견마다 재현/정확성 2렌즈 검증) · 확정 **36건** / 탈락 2건.
> **탈락 2건**(적대적 검증이 반증): SpinnerOsu "memEndTime 보정 죽은 블록"(호출자 때문에 절대 실행 안 됨) · CursorRenderer "new-style 트레일 시간역행 리셋 죽음"(재현 반증).
>
> 검증 노트에서 에이전트가 스스로 과장을 정정한 예: [1] 스킨 I/O 크래시는 "두 호출 경로 다 죽음" 주장이었으나 렌더 틱 경로는 이미 `OnApplicationIdle` try/catch로 보호됨 → **기동(OnLoad) 경로 한정**으로 확정.

## 티어 요약

| 티어 | 뜻 | 건수 | 항목 |
|---|---|---|---|
| 🔴 실질 결함 | 실행 중 체감/크래시/누수 | 6묶음(8건) | 1, 2+7, 3+5+28, 6, 30+31 |
| 🟡 충실도/에딧 | stable 1:1 강화, 대체로 조건부 | 8건 | 11, 14, 15, 16, 17, 18, 19, 20 |
| ⚪ 죽은코드/거짓주석 | 안전 정리 | 7건 | 21, 22, 23, 24, 25, 26, 27 — ✅ **완료** |
| ⛔ 무영향/수정불가 | document만 | 10건 | 8, 9, 10, 12, 13, 32, 33, 34, 35, 36 |

**상태 범례**: `미착수` / `수정` / `보류` / `document` / `완료`

---

## 🔴 티어 A — 실질 결함 (실행 중 체감)

| # | 심각도 | 위치 | 내용 | 발현 시나리오 | stable 대비 | fix | 표결 | 상태 |
|---|---|---|---|---|---|---|---|---|
| 1 | CRASH | `Skinning/SkinManager.cs:377` | 커스텀 스킨 `skin.ini` 파일 I/O 예외(`File.Exists` 통과 후 잠김/삭제/권한거부)가 무방비. `LoadSkin`→`skin.Load`→`new StreamReader/ReadLine`에 try/catch 없음 | 비Default 스킨 지정 + 기동 시 `overlay.Show()`→`OnLoad`(메시지 루프 이전)에서 I/O 폴트 → **미처리 하드 크래시**(창 안 뜸). 렌더 틱 재로드 경로는 이미 try/catch 보호됨 | stable `LoadSkinRaw`는 릴리스에서 try/catch→`error.txt` 기록 후 `LoadSkinRaw(DEFAULT_SKIN)` 폴백 | `LoadSkin`의 `skin.Load`를 try/catch로 감싸 예외 시 Default(임베디드) 폴백 | 2C/0R | 미착수 |
| 2 | LEAK | `Gameplay/HitObjects/SpinnerOsu.cs:570` | `NotStarted→Started` 전이에서 `spriteSpin.Transformations.Add(Fade)`를 Clear 없이 추가. `ResetState`도 spriteSpin 미리셋 → 재시도마다 트랜스폼 1개씩 누적 | 스피너 맵을 같은 세션 반복 재시도하면 spin 페이드 트랜스폼이 무한 append(pSprite.Update 매 프레임 전체 순회) | stable `spriteSpin.FadeOut`은 재생마다 재초기화 컨텍스트라 누적 없음 | (아래 7과 함께) `ResetState`에서 spriteSpin을 fadeIn/fadeOut 2개로 재구성 | 1C/1R | 미착수 |
| 3 | LEAK | `Rendering/FontRenderer.cs:25` | `textCache`가 (텍스트\|폰트\|크기\|색\|그림자) 키마다 `GL.GenTextures`로 새 텍스처 생성, **상한/LRU/축출 전무**. 런타임 중 `ClearCache` 호출자 0 (Dispose에서만) | 정확도 HUD(`98.74%`→`98.71%`…)·콤보 HUD(`1..maxCombo`)가 판정마다 값 변경 → 캐시 미스마다 GL 텍스처 영구 적재. 32bit 프로세스라 장시간 세션에서 계단식 VRAM/RAM 증가 | stable은 숫자 글리프(`score-0..9`) 자릿수별 재사용 — 값마다 새 텍스처 없음 | textCache LRU 상한+초과분 Dispose, **또는** 맵/스킨 변경 시 `fontRenderer.ClearCache()` 실제 배선 (참조 중 텍스처 dispose 순서 주의) | 2C/0R | 미착수 |
| 5 | LEAK | `Rendering/HudRenderer.cs:276` | (3과 동일 뿌리) `RenderAccuracy`가 매 프레임 `{0:F2}%` 텍스트→`AddText`→`RenderText`로 텍스처 캐싱. 세션 내내 evict 없음 | 여러 맵 연속 플레이 시 수천~1만 텍스트 텍스처 적재, 맵/스킨 변경으로도 안 비워짐 | stable pSpriteText 글리프 조합 | 3과 같은 수정으로 해소 | 2C/0R | 미착수 |
| 28 | MINOR | `Rendering/FontRenderer.cs:224` | 주석 "캐시 클리어 — 스킨 변경 시"가 거짓. 실제 스킨 재로드는 `TextureManager.ClearCache`만 부름. 주석이 3의 누수를 은폐 | — | — | 주석을 실동작에 맞추거나 3 수정과 함께 배선 | 2C/0R | 미착수 |
| 7 | FIDELITY | `Gameplay/HitObjects/SpinnerOsu.cs:88` | `ResetState`가 state·glow만 복원하고 `spriteClear` 트랜스폼 미리셋. 스피너 객체는 재시도마다 재사용 | 1차 시도서 클리어(spriteClear에 reveal 트랜스폼 남음) → 재시도서 클리어 못 해도 옛 시각에 **"Clear!" 그래픽이 잘못 페이드인**. 재시도는 매우 흔함 | stable은 재생마다 HitObject 재생성/재초기화라 잔여 트랜스폼 없음 | `ResetState`에서 `spriteClear.Transformations.Clear()` 후 초기 `Fade(0,0,Start,End)` 복원 + `ComputeTimeRange`. spriteSpin도 동일(2 해결) | 2C/0R | 미착수 |
| 6 | CORRECTNESS | `Overlay/HudEditController.cs:241` | `CaptureLockCenter`가 `DragElement<0`이면 조기 return. 그러나 축고정(X/Y키)은 드래그 전에 켜는 지속 모드 → 캡처 실패로 `LockCenterY=0` | edit 진입→Combo HUD(y≈700) 선택→X키→드래그: 기대는 y유지 수평이동, 실제는 posY=0으로 clamp돼 **HUD가 화면 최상단으로 점프** | (stable 무관 — NEWNEWOVERLAY C++ 포팅 기능) | `int idx = DragElement>=0 ? DragElement : settings.HudEditSelected;` 로 잡고 `0<=idx<4`일 때 `HudRects[idx]`로 Lock 설정 | 2C/0R | 미착수 |
| 30 | MINOR | `Overlay/CaptureBlock.cs:26` | `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` 실패 진단이 `Debug.WriteLine`(overlay.log 미기록, Release 제거) + 두 호출부 반환 bool 무시 (F10과 같은 부류) | Win10 1903 미만/정책 제약서 캡처 제외 실패 시 **오버레이가 캡처에 노출**되는데 로그에 흔적 0 | — | `Console.WriteLine`으로 로깅 + 호출부/내부서 실패 로그 | 1C/0R | 미착수 |
| 31 | MINOR | `Overlay/CaptureBlock.cs:23` | 주석 "재시도"·로그 "재시도만 수행"이 거짓(재시도 로직 없음). line16 "폴밭"→"폴백" 오타 | 유지보수자가 "실패해도 재시도되니 괜찮다" 오판 | — | "재시도" 문구 제거, 실동작(단발·폴백없음·실패시 미차단) 기술 | 2C/0R | 미착수 |

---

## 🟡 티어 B — 충실도/에딧 (stable 1:1 강화, 대체로 조건부)

| # | 심각도 | 위치 | 내용 | 발현 조건 | stable 대비 | fix | 표결 | 상태 |
|---|---|---|---|---|---|---|---|---|
| 11 | FIDELITY | `Gameplay/HitObjects/SliderCurve.cs:71` | 길이 클램프가 `if(total>spatialLength)` trim만 하고 stable의 **extend(excess<0 시 마지막 선분 연장)** 누락 | 제어점 경로<pixelLength인 "과길이" 슬라이더(일부 테크니컬 맵) → stable보다 짧게 렌더 + 볼 타이밍/끝 위치 어긋남 | stable은 excess 부호 무관 동일 루프로 trim+extend | 가드 제거, `total>0`이면 항상 `excess=total-spatialLength` 루프. extend 시 p2≠p1 가드 유지 | 2C/0R | 미착수 |
| 14 | FIDELITY | `Gameplay/Cursor/CursorRenderer.cs:424` | old-style 트레일이 무조건 `Additive=true`+`Color.White`. stable old-style은 일반 블렌드+`drawColour` | `cursormiddle.png` 없는 커스텀 스킨(=old-style)서 트레일이 stable보다 밝게 가산+색조 미반영 | stable old-style: Additive 미설정+drawColour | `hasMiddle`일 때만 Additive, old-style은 일반 블렌드+drawColour | 2C/0R | 미착수 |
| 15 | FIDELITY | `Gameplay/Cursor/CursorRenderer.cs:422` | new-style 트레일도 `CursorTrailRotate` 시 회전. stable new-style 렌더러는 회전 안 함(old-style만 존중) | `cursormiddle` 있고 `CursorTrailRotate:1`인 드문 스킨 | stable `CursorTrailRenderer.add`: 회전 미적용 | `hasMiddle`(new)면 회전 0, old-style만 CursorTrailRotate 적용 | 2C/0R | 미착수 |
| 16 | FIDELITY | `Gameplay/Cursor/CursorRenderer.cs:343` | stable의 `DisplayWidth<5` 트레일 비활성 가드 누락 + `minSpacing=Max(1,acceptedWidth)` 클램프 이탈 | 폭 5px 미만 cursortrail 극단 커스텀 스킨서 stable은 미표시인데 오버레이는 1px 간격 대량 방출 | stable: `DisplayWidth<5`면 리셋 후 return | trailW<5 시 `lastTrailPosition` 리셋 후 return 추가 | 2C/0R | 미착수 |
| 17 | FIDELITY | `Rendering/HudRenderer.cs:546` | `HudRects[3]`(에러바) top을 posY로 잡지만 화살표는 posY 위에 그려짐 → 히트영역이 arrowSize+2 아래로 통째 밀림 | edit 모드서 에러바 막대/화살표 위 클릭이 안 잡히고 막대 아래 빈 공간 클릭해야 잡힘 + 하이라이트 어긋남 | — (HUD 편집 자체가 포팅 기능) | `HudRects[3] = RectangleF(posX, posY-arrowSize-2, totalW, barH+arrowSize+2)` | 2C/0R | 미착수 |
| 18 | FIDELITY | `Rendering/HudRenderer.cs:139` | HUD 텍스트 `Depth=0.5`라 히트서클(~0.8)·커서(0.999)보다 아래. 에러바 등 즉시모드 도형은 `PreDrawCallback`(SpriteManager.Draw 이전)이라 모든 스프라이트 뒤 | 상단/코너 HUD 위로 히트오브젝트·큰 어프로치서클 겹치면 **게임플레이가 HUD 가림**(stable은 HUD가 위) | stable HUD depth ~0.95 > 히트오브젝트 ~0.8 | HUD 텍스트 depth를 ~0.95(커서 아래)로, 에러바 즉시모드는 SpriteManager.Draw 이후(PostDraw)로 이동 | 2C/0R | 미착수 |
| 19 | FIDELITY | `Skinning/SkinManager.cs:409` | `GetComboColours`가 스킨 Colours만 반환, 비트맵 `[Colours]` 오버라이드 계층 없음. 파서가 채운 `BeatmapData.ComboColours`가 어디서도 소비 안 됨 | 커스텀 `[Colours]` 비트맵 플레이 시 stable은 비트맵 색으로 칠하는데 오버레이는 스킨/기본 색. **커스텀 콤보색 맵은 흔함 → 자주 발현** | stable `LoadColour`가 `BeatmapColours` 먼저 조회, HOM이 병합 | HOM이 `beatmap.ComboColours`를 스킨색보다 우선 적용하도록 브리지(파서→소비자, cross-file) | 2C/0R | 미착수 |
| 20 | FIDELITY | `Skinning/SkinManager.cs:307` | `ParseColor`가 `Color.FromArgb(int,int,int)` — 성분 0~255 벗어나면 ArgumentException→catch→Empty→색 버려지고 기본색 대체 | `Combo1: 300,0,0` 같은 범위밖 값 손상 스킨서 stable은 wrap(300→44) 표시, 오버레이는 기본색 | stable `(byte)Convert.ToInt32(...)` 256 모듈로 wrap | int→byte 캐스팅(wrap) 또는 `&0xFF` 마스킹 | 2C/0R | 미착수 |

---

## ⚪ 티어 C — 죽은 코드 / 거짓 주석 (안전 정리) — ✅ **완료** (빌드 경고 0/오류 0, 테스트 15/15)

> 7건 전부 제거/수정. 각 제거 전 grep으로 참조 0 확인 + 빌드(컴파일러)로 최종 검증 — 지운 것 중 실제 참조된 건 없었다.

| # | 심각도 | 위치 | 내용 | 처리 | 표결 | 상태 |
|---|---|---|---|---|---|---|
| 21 | DEADCODE | `Gameplay/HitObjects/SliderCurve.cs` | `PositionAt`의 이진탐색 후 `lo--`로 잘못된 세그먼트 → 외삽. **호출자 0**(볼 위치는 `SliderOsu.PositionAtLength`가 담당) | **함수 전체 제거** | 1C/1R | ✅ 완료 |
| 22 | DEADCODE | `Gameplay/Cursor/CursorRenderer.cs` | `UpdateTrail`의 `Origins origin` 지역변수 미사용(실제 origin은 `AddTrailParticle`서 재계산) | **선언 제거** | 1C/0R | ✅ 완료 |
| 23 | DEADCODE | `Overlay/HudEditController.cs` | `HitTestHud`의 `r.Width<0.0f` sentinel이 절대 참 안 됨(미렌더 rect는 width 0) | **`<=0.0f`로 수정** (현재 4요소 양수폭이라 동작 무변화) | 2C/0R | ✅ 완료 |
| 24 | DEADCODE | `Rendering/HudRenderer.cs` | (23과 동일 패턴 복제) `DrawEditOverlay`의 `r.Width<0.0f` sentinel 무효 | **`<=0.0f`로 수정** | 1C/1R | ✅ 완료 |
| 25 | DEADCODE | `Overlay/WindowInterop.cs` | `GetXLParam`/`GetYLParam` + "edit mode 마우스 메시지" 상수 블록(WM_SETCURSOR/WM_MOUSE*/MA_NOACTIVATE/HTCAPTION) 전부 참조 0 | **헬퍼 2개 + 상수 7개 제거** (SetCursor/IDC_SIZEALL은 사용 중이라 유지) | 2C/0R | ✅ 완료 |
| 26 | DEADCODE | `Graphics/Batches/LinearBatch.cs` | 2D `LinearBatch` 클래스 `new` 0건 + doc 주석 거짓(그라디언트는 `MmSliderRenderer.CreateTexture`가 담당) | **클래스 전체 제거** (3D `QuadBatch3D`/`LinearBatch3D`는 유지) | 2C/0R | ✅ 완료 |
| 27 | DEADCODE | `Skinning/SkinManager.cs` | `originalColours`에 stable에 없는 Combo6~8(투명 A=0) — A>0 필터에 걸려 무효 | **Combo6~8 제거** (codegraph로 stable Combo1~5 확인) | 1C/1R | ✅ 완료 |

---

## ⛔ 티어 D — 무영향 / 수정불가 (document만)

| # | 심각도 | 위치 | 내용 | 왜 document만 | 표결 | 상태 |
|---|---|---|---|---|---|---|
| 8 | FIDELITY | `SpinnerOsu.cs:518` | metre 최상단 바 blink이 RNG 대신 결정적 `>=0.5` 임계 | 코드에 이미 근사 주석 있음. 미세 시각차 | 2C/0R | document |
| 9 | FIDELITY | `SpinnerOsu.cs:489` | 스피너 회전 항상 정방향(`Math.Abs`) — 역회전 미반영 | **메모리 `FloatRotationCount`가 abs 누적이라 부호 원천 소실 → 복원 불가** | 2C/0R | document |
| 10 | FIDELITY | `SpinnerOsu.cs:39` | RPM 표시/`spinner-rpm` 배경 미포팅, `spriteRpmBackground` 죽은 필드 | 헤더 주석에서 spinner-rpm 빼고 죽은 필드 제거 정도(선택). RPM 포팅은 별도 기능 | 2C/0R | document |
| 12 | FIDELITY | `SliderCurve.cs:35` | PerfectCurve 각도정규화가 `double Math.PI` (stable은 `float 3.14159274`) | 차이 ~1.7e-7 rad = **<1e-4 px, 감지 불가** | 1C/1R | document |
| 13 | FIDELITY | `SliderCurve.cs:182` | `CalculateBezier`가 버전 무관 항상 modern. stable은 v<10 `CreateBezierWrong`(1/50 짧게) | **v>=10 현대 맵 완전 일치**, v<10만 이탈(H11/14/16처럼 의도적 미구현감) | 2C/0R | document |
| 32 | MINOR | `Overlay/WindowInterop.cs:49` | `MapWindowPoints` 반환형 `long`(stable native는 `LONG`=int32) 시그니처 불일치 | 유일 호출부가 반환값 무시(ref만 사용) → 잠재. 원하면 `int`로 (cheap) | 1C/1R | document |
| 33 | MINOR | `Graphics/Batches/LinearBatch.cs:135` | 3D 배치 auto-flush가 첫 Draw 이전 발동 시 `autoFlushShader==null`→지오메트리 조용히 폐기 | 세션 최초 롱슬라이더가 오버플로할 때만 1프레임 결손. 원하면 셰이더 선주입(cheap) | 1C/0R | document |
| 34 | MINOR | `Graphics/Batches/LinearBatch.cs:55` | Draw 후 `ArrayBuffer` 언바인드 안 함 → VBO 바인딩 잔류 | 현 실행 경로 무손상(다른 소비자가 재바인드). 잠재 트랩. 원하면 `BindBuffer(...,0)` (cheap) | 1C/1R | document |
| 35 | MINOR | `Skinning/SkinManager.cs:358` | `Path.Combine(skinsFolder, skinName)`가 skinName==null 미방어(ArgumentNullException) | 현 경로 도달 불가(`Normalize()`가 Default 강제). 잠재 | 1C/1R | document |
| 36 | NONE | `Skinning/SkinManager.cs:205` | Version 파싱 실패 시 0 대입(기본 1 아님) | `UseNewLayout(Version>1)`에서 0·1 모두 false → 출력 동일. TryParse가 stable보다 오히려 견고 | 1C/1R | document |

---

## 참고 — 파일별 감사 요약(정독 확인)

| 파일 | 요약 |
|---|---|
| SpinnerOsu (704) | 트랜스폼 계수(glow OutQuad, turnRatio, 회전 frc*PI, bonus 조건)는 stable과 정확히 일치. 실결함은 재시도 상태 리셋 누락(spriteClear/spriteSpin). metre RNG·RPM은 소소한 이탈 |
| SliderCurve (426) | 커브 수학(Bezier/Catmull/Perfect/Linear) stable과 거의 1:1. 길이 extend 누락(충실도), PositionAt 세그먼트 버그(데드), Pi 정밀도·구버전 분기(경미) |
| CursorRenderer (442) | 트레일 수명/간격/2048 상한/텍스처 소유·해제 정상. old/new 스타일 블렌드·색·회전 이탈과 죽은 변수 |
| HudEditController (262) | 배열 인덱싱 안전(길이 4 보장), 좌표 정규화 견고, 폴링이라 이벤트 누수 없음. 핵심은 axis-lock 중심 캡처 버그 |
| FontRenderer (242) | **textCache 무한 누적이 핵심** — 고카디널리티 acc/combo가 GL 텍스처 계속 생성. 임시폰트 미삭제·폴백 FontFamily 미해제 경미 |
| HudRenderer (553) | add/remove 짝 맞음, 인덱싱 안전, acc 표시 stable 일치. 결함: 죽은 sentinel·폰트캐시 누수·HUD depth 역전 |
| WindowInterop 외 2 (352) | x86 P/Invoke 건전, 크래시/누수 없음. 캡처 로깅 데드+거짓주석, MapWindowPoints 시그니처, 데드 헬퍼 |
| LinearBatch (278) | 스트라이드/오프셋 정확, 삼각형 경계 flush 정상. 2D 클래스 데드코드, 3D auto-flush 셰이더 null 드롭 |
| SkinManager (439) | skin.ini 내용 파싱 완전 방어(TryParse/try). 결함은 파일 I/O 예외 무방비(stable 폴백 부재)·비트맵 콤보색 브리지 부재·색 범위 처리 |
