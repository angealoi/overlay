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
| 2026-07-15 | **H17**: 보너스 flash가 흰색으로 굳던 문제 (H3 수정으로 드러남) | `8a2442e` | 실기 확인 (실제 osu!와 동일 동작 확인) |
| 2026-07-16 | **H5**: 브레이크 파싱 + 브레이크 뒤 강제 NewCombo (+ `NewCombo`를 Type 파생 프로퍼티로) | `743c16d` | 실제 맵 135개 회귀 + 합성 맵으로 새 경로 검증. **단 실사용 영향은 0맵 — "모든 맵에서 어긋남" 주장은 오류였음** |
| 2026-07-16 | **H2 + H22**: 리턴 패스 틱(세그먼트 리셋·스티키 skipTick·경계 미러) + 틱 소멸 일괄화 | `3bc67c8` | 실제 커브로 수정 전/후 대조 — 리턴 틱 복구 205개(2.8%), 위치 변경 496개(6.7%), 미러 대칭 207/207. 실기 확인 대기 |
| 2026-07-16 | **C1/H23 + H20**: 스택 시 슬라이더 전체 이동 (+ 재로드 커브 뒤틀림 · HitBurst 부호 버그 함께 수정) | `8384faf` | 커브 28438개 머리 정렬 불변식 확인. 영향 실측 0.4%(111개). 실기 확인 대기 |
| 2026-07-16 | **C2/H24**: 리버스 화살표가 SpriteManager에 영구 잔류 (`RemoveFromSpriteManager`가 virtual이 아니었음) | `9db56f9` | Add/Remove 짝 전수 확인. 잔류 실측 중앙값 24개·최대 3071개(투명이라 순회 비용만) |
| 2026-07-16 | **B1+B2+B3**: 슬라이더 바디 FBO · 커서팩 텍스처 · 콤보 숫자 유령 누수 | `952c5e8` | 세 곳 다 코드로 경로 확인. B1은 맵당 평균 211개 FBO+텍스처. 실기 확인 대기 |
| 2026-07-16 | **G10**: 기동 시 좌상단 흰 사각형 (사용자 제보 — 목록에 없던 항목) | `e2e630d` | 첫 프레임 전까지 알파 0. 실기 확인 대기 |
| 2026-07-16 | **D1~D5 전부**: AOB 매칭 버킷팅+unsafe · HOM syscall 폭발 · 스네이킹 FBO 재사용 · SpriteManager 제거 · 에러바 할당 | `f4d21e0` `f13a42a` `2595e40` `77aa70d` | **실측**: 기동 스캔 2568→1117ms(2.3배), 커서 재스캔 1505→753ms(2.0배), gen0 128→3회. slot 주소 구버전과 완전 일치 |
| 2026-07-16 | **D1 3탄**: 실행 영역 한정 + 2바이트 프리필터 | `1fbf691` | Initialize 15배·커서 재스캔 8배 누적(원본 대비). slot 주소 완전 일치 |
| 2026-07-17 | **E1~E7 전부**: 잠복 결함 7건 (버퍼 스레드안전·AOB slot 검증·QuadBatch 셰이더·Fields enum 충돌·GameField 경합·낡은 파싱 폐기·문자열 캐시) | `68de5ad` | 감사 정독 → 적대적 리뷰(설계 14 + 구현 4 에이전트, fable5) 전수 검증. 리뷰가 실결함 3건 잡아 반영(모드 범위 오탈락·GameField float 절단·폐기 시 영구 blank). 빌드 통과(경고 0) |

> DT 배속은 [H1과 별개](#h1-fadein이-stable-상수가-아니라-lazer-공식)로, `speedMultiplier`/`scalePreEmpt` 이중 적용 문제였다.

---

## 목차

- [A. 크래시 가능 (5건)](#a-크래시-가능)
- [B. 리소스 누수 (3건)](#b-리소스-누수)
- [C. 시각적 정확성 (6건)](#c-시각적-정확성)
- [D. 성능 (5건)](#d-성능)
- [E. 잠복 결함 (7건)](#e-잠복-결함)
- [F. 죽은 코드 / 거짓 코드 (10건)](#f-죽은-코드--거짓-코드)
- [G. 견고성 / 기타 (10건)](#g-견고성--기타)
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

### ~~B1. 슬라이더 바디 FBO 누수 — 맵 바뀔 때마다~~ ✅ 해결 (`952c5e8`)
| | |
|---|---|
| ~~증상~~ | ~~맵 전환 시 그려졌던 모든 슬라이더의 FBO+텍스처가 Dispose 없이 드롭~~ → `SliderOsu`에 `Dispose()` 추가(`IDisposable`), `LoadBeatmap`의 `Clear()` 앞에서 호출 |
| 규모 | **실측 맵당 평균 211개** 슬라이더 (라이브러리 28438개/135맵). `RenderTarget2D`에 파이널라이저 없음 → GC가 못 걷는 영구 누수였음 |
| 비고 | `MmSliderRenderer.Draw` 주석의 "호출자가 Dispose 책임"을 호출자가 안 지키던 것. 이제 지킴 |

### ~~B2. 커서팩 텍스처 누수 — 맵 바뀔 때마다~~ ✅ 해결 (`952c5e8`)
| | |
|---|---|
| ~~증상~~ | ~~커서팩 켜면 맵당 최대 3텍스처 누수~~ → `packTexturesOwned`로 소유권을 추적해 **팩 텍스처만** 해제 |
| 확인 | `Reload()`는 `OverlayForm.cs:691`의 **맵 전환 블록 안**에 있어 맵마다 호출된다 — 주장 확인됨 |
| 주의 | 스킨 텍스처는 `TextureManager` 캐시가 소유하므로 **절대 해제하면 안 된다** (다른 사용자의 텍스처가 깨짐). 그래서 provenance 추적이 필요했다 |
| 남은 것 | 앱 종료 시 팩 텍스처 해제 경로는 없으나 프로세스 종료가 회수 — [G6](#g-견고성--기타)과 같은 성격, 실해 없음 |

### ~~B3. 콤보 숫자 유령 스프라이트~~ ✅ 해결 (`952c5e8`)
| | |
|---|---|
| ~~증상~~ | ~~자기 리스트만 비우고 SpriteManager의 옛 숫자는 제거 안 함~~ → `addedTo`로 어느 SpriteManager에 넣었는지 추적해 제거 |
| 함정 | **제거만 하면 숫자가 아예 사라진다.** `HOM.UpdateSpriteWindow`는 `inWindow && !IsSpriteAdded`일 때만 `AddToSpriteManager`를 부르므로, 재생성한 숫자를 그 자리에서 다시 넣어줘야 한다 |
| 확인 | `UpdateDifficulty`가 재생성하는 건 콤보 숫자뿐 — 나머지 스프라이트는 같은 객체의 Transformation만 교체하므로 같은 문제가 없다 |

---

## C. 시각적 정확성

### ~~C1. 스택된 슬라이더 — 몸통 따로 머리 따로~~ ✅ 해결 (`8384faf`)
- ~~**위치**: 커브가 생성자에서 스택 **전** 좌표로 계산되는데 `UpdateStackedPosition`은 시작원만 이동~~ → stable `SliderOsu.ModifyPosition`(:1395-1436)처럼 **슬라이더 전체**를 이동
- **핵심**: `curvePath`만 옮기면 바디/볼/틱이 전부 따라온다 (셋 다 curvePath에서 위치를 계산). 끝원+리버스 화살표는 자기 `HitObjectData`를 가져서 별도 `ModifyPosition` 필요. 바디 FBO 캐시도 무효화
- **함께 발견/수정** (아래 셋 다 이 작업 중 드러남):
  1. **재로드 시 커브 뒤틀림** — 커브를 `data.Position`(스택이 변형함)으로 만드는데 `data.CurvePoints`는 원본이라, 같은 BeatmapData로 재로드(난이도 슬라이더 조작)되면 **머리만 밀린 커브**가 나왔다 → `BasePosition` 기준으로 변경
  2. **HitBurst 위치 부호 버그** — `UpdateStacking`은 `basePosition **−** StackCount*stackVector`인데 `BaseEndPosition`은 `**+=** StackCount*stackOffset`이라 **반대 방향으로 2배** 밀렸다 → 이동 후 값을 그대로 대입해 부호 실수가 구조적으로 불가능하게
  3. **[H20](#h-2-조건부--특정-맵--스킨--모드에서만-17건) 해결** — 스택 오프셋을 `StackCount != 0`에만 걸던 것을 무조건으로 (stable과 동일). `Position - BasePosition` 불변식 보장
- **실제 영향**: **측정 — 스택된 슬라이더는 28438개 중 111개(0.4%), 18개 맵(13.3%)**. 스택 깊이 1~3 → 오프셋 3~10px. 서클은 6.2%가 스택되지만 **슬라이더는 거의 스택되지 않는다** ("스택은 거의 모든 맵에 있으니 영향이 클 것"이라는 추정은 틀렸음)
- **검증**: 실제 맵 커브 **28438개 전부** `curve[0].p1 == BasePosition` 확인 (불일치 0, 오차 0.0000px) → 평행이동으로 머리와 몸통이 정확히 정렬됨이 보장. **실기 확인 대기**

### ~~C2. 리버스 화살표 제거 누락~~ ✅ 해결 (`9db56f9`)
- ~~**위치**: `HitCircleSliderEnd`가 `AddToSpriteManager`만 override~~ → `RemoveFromSpriteManager`가 애초에 **`virtual`이 아니라 override할 수 없었다**. virtual로 바꾸고 override 추가
- **왜 자동 정리도 안 됐나**: `pSprite`의 Discard 경로 3곳(`pSprite.cs:170, 266, 276`)이 전부 `Clock`이 `Game`이나 `AudioOnce`일 때만 걸리는데, 리버스 화살표는 **`Clocks.Audio`** 라 어디에도 안 걸린다 → `SpriteManager.Update`의 자동 Discard를 못 받고 영원히 `NotVisible` 반환하며 잔류
- **실제 영향**: **측정 — 맵 끝 시점 잔류 화살표 중앙값 24개**(무시할 수준). 다만 마라톤/연습 맵에서 **최대 3071개**까지 쌓인다. 화살표는 투명해서(`startTime`에 Fade 1→0) 보이지는 않았고, 매 프레임 자기 Transformation 전체를 순회하는 비용만 발생
- **전수 확인**: Add/Remove override 짝은 이곳이 **유일한** 불일치였다

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

> **D 섹션 5건 전부 해결** (`f4d21e0`, `f13a42a`, `2595e40`, `77aa70d`).
>
> **실측 완료** — osu! 실행 중(PID 2500, private 441MB) 구/신 어셈블리의 `OsuMemoryReader.Initialize()`를 각각 호출해 비교:
>
> | | BEFORE | AFTER | |
> |---|---|---|---|
> | 기동 스캔 전체(`Initialize`) | 2568 ms | **174 ms** | **약 15배** |
> | 커서 재스캔(패턴 1개) | 1505 ms | **180 ms** | **약 8배** |
> | gen0 GC | 128회/run | **3회/run** | |
>
> **동작 동일성**: 해석된 slot 7개와 cursorSlots 10개가 구 버전과 **주소까지 완전히 일치** (버킷팅·실행영역·2바이트 필터 각 단계마다 재확인).
> D1의 "커서 재스캔 시 ~1초 정지" 주장도 실측 확인됨(1.35~1.6초).

### ~~D1. AOB 스캔 — 스캔마다 수백 MB 재할당~~ ✅ 해결 (`f4d21e0`)
- ~~시그니처마다 전체 패스 반복(기동 시 8회+), 리전마다 `new byte[최대 100MB]`~~
- **실측 증거였던 것**: 커서 재스캔이 걸린 세션에서 렌더 스레드 **~1초 정지** (overlay.log 4회 확인)
- **수정**: `AobScanRequest` + `ScanBatch` — 모든 패턴을 **한 번의 메모리 패스**로. 기동 시 **9패스 → 1패스**
  - 리전 버퍼 재사용(ThreadStatic) — 할당 제거 (gen0 GC 128회/run → 3회)
  - 마스크를 `string` → `bool[]` — 내부 루프가 바이트마다 string 인덱싱을 하고 있었다
  - `CurrentBeatmap`/`PlayMode`는 **패턴이 같고 OperandSkip만 다름**(-0xC/-0x33) → 요청 하나로 둘 다
- ⚠️ **여기까지가 18%밖에 못 줄였다.** 실측으로 원인을 계산: `9R+9M=2558`, `R+8M=2104` → **읽기 R≈24ms, 패턴당 매칭 M≈260ms**. 비용은 메모리 읽기가 아니라 **바이트 매칭이 지배**한다 — 패스 수를 줄인 건 번지수가 틀렸다
- **진짜 수정** (`77aa70d`): 첫 고정 바이트 **버킷팅**(시그니처 9개 전부 고정 바이트로 시작) + 핫 루프 **unsafe 포인터**(수백 MB 바이트 순회라 배열 경계 검사가 그대로 비용) → 2.3배
- **3탄** (`1fbf691`): ① **실행 가능 영역만** 스캔 — 코드 시그니처는 실행 페이지에만 있다. 실측 809MB→250MB, 매치 19개 누락 0. ② **첫 2바이트 프리필터** — 패턴 전부 2바이트 고정이라 TryMatch 진입 7.83%→0.28%(28배↓). 둘 다 slot 주소 완전 일치로 검증. **Initialize 15배·커서 재스캔 8배 누적**(원본 대비)
- ❌ **JIT 힙(PRIVATE)만 스캔은 41배지만 채택 안 함** — osu!가 이 메서드들을 JIT한다는 가정에 의존한다. ngen되면 IMAGE 영역으로 가 침묵 실패(커서 40% 버그류). 실행 가능 여부는 물리 법칙, PRIVATE/IMAGE는 런타임 정책이라 언제든 바뀜
- **함께**: 배선 후 죽은 코드 6건 제거 (`ScanSlot`, `ScanPlayerInstanceSlot`, `ScoreReader`/`ResolutionReader.ScanSlots`, `AobScanner.Scan`/`ScanAll`/`ResolveSlot(2인자)`)

### ~~D2. HOM 오프셋 브루트포스가 매 프레임~~ ✅ 해결 (`2595e40`)
- ~~오프셋마다 개별 ReadProcessMemory — 프레임당 127 + (후보수 × 40) syscall~~
- **수정**: Player 객체 0x200B, 후보 객체 0xA4B를 각각 한 번의 `ReadBytes`로 → **syscall 127 + N×40 → 1 + N**. 버퍼는 필드로 재사용
- **함정**: `ReadBytes`는 범위 안에 못 읽는 페이지가 하나라도 있으면 **통째로 실패**한다. 그냥 바꾸면 객체가 세그먼트 끝에 걸린 경우 후보를 놓친다 → 실패 시 개별 읽기로 폴백(`ReadPtrCached`)

### ~~D3. 스네이킹 중 FBO 생성/파괴 ~30회/슬라이더~~ ✅ 해결 (`f13a42a`)
- ~~FBO 크기를 스네이킹된 선분 기준으로 잡아 진행도마다 크기가 달라짐 → 1/30마다 RenderTarget2D 재생성~~
- **수정**: FBO 영역을 **전체 커브 기준**으로 고정(`ComputeBodyBounds`) → FBO와 pSprite 재사용. **슬라이더당 FBO 생성 ~30회 → 1회**
- **왜 안전한가**: 병합된 선분은 커브 점들을 잇는 직선이라 항상 전체 커브 박스 안(볼록껍질). 내용은 `Bind` 직후 `GL.Clear`로 지우므로 잔상 없음. 스프라이트의 위치·크기·페이드는 전부 진행도와 무관해 재생성이 불필요했다
- 해상도/CS 변경, 스택 이동, Dispose 시에만 재생성

### ~~D4. SpriteManager.Remove가 O(n)~~ ✅ 해결 (`f4d21e0`)
- **Remove**: `spriteSet`(HashSet)으로 먼저 걸러 **없으면 O(1) 종료**. 트레일 스프라이트는 `Update`의 자동 Discard로 이미 빠진 뒤 `CursorRenderer`가 다시 `Remove`를 불러서 **헛도는 전체 스캔이 매 프레임** 발생했다
- **Update**: 항목마다 `RemoveAt`(제거 1건당 O(n) 시프트)하던 것을 **단일 O(n) 압축 패스**로. 순서 보존

### ~~D5. 히트에러바 중앙값 — 프레임당 할당+정렬~~ ✅ 해결 (`f4d21e0`)
- `errors`는 최근 **30개**로 제한되므로 정렬 자체는 싸다 — 순수하게 `new List<int>`의 GC 압박이었다. 스크래치 재사용
- 규모는 작다. "정렬이 비싸다"는 뉘앙스는 과장이었음

---

## E. 잠복 결함 (지금은 안 터지지만 한 발짝 거리)

> **E1~E7 전부 해결** (`68de5ad`). 감사(전 지점 정독) → 적대적 리뷰 워크플로(설계 검증 14 에이전트 + 구현 검증 4 에이전트, **fable5**)로 전수 검증. 리뷰가 잡은 실결함을 반영: 모드 범위 `[0,15]`가 stable OsuModes 16~23(Tourney=22 등)을 오탈락 → `[0,30]`, `GameField.Width`는 float인데 int 스냅샷이면 절단, 낡은 파싱 폐기 시 folder만 검사하면 찢어진 read로 영구 blank. 빌드 통과(경고 0).

### ~~E1. ProcessMemory 재사용 버퍼가 스레드 비안전~~ ✅ 해결 (`68de5ad`)
- ~~`bytesRead`/`buf4`/`buf8`이 인스턴스 필드 → 다른 스레드 호출 시 조용한 값 섞임~~ → 버퍼를 `[ThreadStatic] static` + 지연 초기화로(스레드마다 독립), `bytesRead`는 각 메서드 로컬로. `AobScanner.sharedBuffer`와 동일 패턴. 무할당 최적화 유지
- **범위(정직)**: "동시 read가 서로의 버퍼를 오염시키지 않음"까지. `Handle`/`Dispose` 경합은 별개이며 현재 UI 스레드 전용이라 무해

### ~~E2. AOB 첫-매치 무검증~~ ✅ 해결 (`68de5ad`)
- ~~`timeSlot`/`modeSlot`/`modsSlot`이 첫 매치를 무검증 신뢰 → 패턴 충돌 시 조용히 쓰레기~~ → time/mode/mods를 AllMatches로(커서가 이미 전체 패스라 추가 비용 ~0), 값-도메인 검증 + 충돌 로깅
- **선택 규칙(회귀 0 우선)**: 첫 매치가 검증 통과면 그대로(예전 동작). 첫 매치가 실패**할 때만** 서로 다른 slot 중 **유일하게** 통과하는 대체로 교정. 그 외엔 첫 매치 유지 + 경고. 서로 다른 해석 slot이 2개 이상이면 충돌 경고(같은 slot 다중매치는 정상 — JIT 중복 방출)
- **검증 범위**: Mode ∈ `[0,30]`(OsuModes 0~23 전부 포함), Time `|t|<24h` + AudioState∈`[0,2]`, **Mods는 상한을 안전히 못 잡아**(Mirror=bit30 등) 읽기 가능성만 구조 검증(값 검증 아님을 명시)
- **한계(정직)**: 0을 읽는 임포스터는 모든 검증을 통과 → 올바른 slot보다 먼저 스캔되면 못 거른다. 이 경우 충돌 로깅으로 가시성만 확보(자동 교정 불가). "조용한 실패 → 로깅"이 핵심

### ~~E3. QuadBatch 오버플로 시 `Flush(null)`~~ ✅ 해결 (`68de5ad`)
- ~~배치 초과 자동 flush가 `Flush(null)` → 셰이더 바인딩 중에도 fixed-function 경로로 그림~~ → `SetActiveShader`로 현재 패스 셰이더를 배치에 알려 오버플로 flush도 같은 셰이더 사용. 기본 null이면 예전과 동일(폴백). `SpriteManager`가 유일 소유자 확인

### ~~E4. Fields enum 값 충돌~~ ✅ 해결 (`68de5ad`)
- ~~`TopCentre==Gamefield(1)` 등 6쌍이 값 충돌 (쓰는 멤버끼리는 우연히 안 겹쳐 동작)~~ → stable `Fields` enum의 값(`Gamefield=1`…`NativeStandardScale=16`)으로 재정렬. 충돌 제거 + **stable과 값 일치(충실도 보너스)**
- **안전**: 전 사용이 심볼릭(`(int)`/`(Fields)` 캐스트 0건, 리뷰 grep 확인), 기본값 0은 오직 명시적 `Fields.TopLeft`로만 도달(→ 이제 `TopLeft=6`, 심볼릭이라 투명). `pSprite` 생성자가 Field를 항상 요구

### ~~E5. 파싱 Task ↔ 렌더 스레드 경합 읽기~~ ✅ 해결 (`68de5ad`)
- ~~Task에서 `renderer.GameField.Width/Ratio` 읽는 동안 렌더 스레드 Resize → 찢어진 쌍~~ → 렌더 스레드에서 **float**으로 스냅샷 후 클로저에 캡처(Width/Ratio 둘 다 float — int 스냅샷은 절단). Task는 더 이상 `GameField`를 만지지 않음

### ~~E6. 낡은 파싱 결과 잠깐 적용~~ ✅ 해결 (`68de5ad`)
- ~~맵 A 파싱이 맵 B 전환 후 도착하면 몇 프레임 A 표시~~ → 파싱 시작 시 맵 키(folder/filename)를 실어 보내고, 적용 시 현재 맵 키와 다르면 폐기. **별도 분기**라 화면의 현재 맵/난이도를 지우지 않고, **folder·filename 둘 다 유효**할 때만 판정(한쪽만 보면 찢어진 read로 유효 결과를 오폐기해 영구 blank)

### ~~E7. 비트맵 문자열 캐시가 포인터 주소로 동일성 판정~~ ✅ 해결 (`68de5ad`)
- ~~GC 주소 재사용 시 다른 맵의 폴더명 잔류~~ → 문자열 포인터-동일성 캐시(`lastFolderPtr` 등) 제거, 맵 전환(beatmapPtr 변경)마다 세 문자열 무조건 재읽기. 맵 전환은 사람 조작 빈도라 무비용. read 실패 시 null로 덮지 않아 기존 값 유지(E6가 이 stickiness에 의존)
- **잔여(정직)**: `beatmapPtr` 자체의 객체-동일성 가정은 유지(리더 전반이 같은 가정 — `lastBeatmapObj` 등 — 을 쓰고, osu! Beatmap 객체는 세션 수명이라 실질 도달 불가)

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
| ~~G10~~ ✅ | `Overlay/OverlayForm.cs:490` + `Program.cs:57` | ~~**기동 시 좌상단에 흰 사각형이 잠깐 뜸**~~ → 해결 (`e2e630d`). **사용자 제보로 발견 — 이 목록에 없던 항목**. `Show()`가 `StartOverlay()`(=`SyncToOsu`)보다 먼저라 그 사이 창이 `StartPosition=Manual` 기본값 (0,0) 300x300으로 떠 있었고, GL 서피스 미초기화라 창 전체가 흰색이었다 → 첫 SwapBuffers 전까지 알파 0 |

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

#### ~~H2. 반복 슬라이더 리턴 패스의 틱이 통째로 사라짐~~ ✅ 해결 (`3bc67c8`)
| | |
|---|---|
| stable | `SliderOsu.cs:818` `distanceToEnd = total` — **세그먼트마다 리셋**. `skipTick`은 세그먼트 내내 유지(스티키). `:921-931`에서 세그먼트 경계 **미러 보정** (`scoringDistance = tickDistance - scoringDistance`) |
| ~~ours~~ | ~~`distanceToEnd`를 선분마다 재계산, skipTick도 선분마다 리셋, 미러 보정 없음~~ → 셋 다 stable대로 수정. `total`에 해당하는 값은 `data.Length`가 아니라 **`curveLength`**(잘린 뒤의 실제 커브 길이) |
| ~~검산~~ | ~~"리턴 패스 틱 전부 미생성"~~ → **부분적으로만 맞음**. 그 검산은 세그먼트 1 진입 시 `scoringLengthTotal ≈ L`을 가정했는데 실제로는 `< L`인 경우가 많다. 실측: 전부 사라지는 건 **2.8%**, 위치가 달라지는 건 **6.7%** |
| ~~추가~~ ✅ | **H22 함께 해결** — 틱 소멸을 각 틱의 scoreTime 개별 → 세그먼트 끝 일괄(`:933-935`)로 변경. 볼이 지나가도 틱이 세그먼트 끝까지 남는다 |
| 실제 영향 | **측정: 반복 슬라이더 7361개 중** → 리턴 패스 틱이 통째로 없던 것 **205개(2.8%) 복구** · 리턴 패스 틱 위치가 달라지는 것 **496개(6.7%)** · 미러 대칭 검사 **207개 전부 통과(실패 0)** |
| 틱 총합 감소 주의 | 5569 → 478로 줄었는데 **이건 정상**이다. 줄어든 5331개 중 **5329개가 통과 끝단(`minTickDistanceFromEnd` 이내)에 붙어 있던 가짜 틱** — stable의 가드가 막는 대상이고, 우리 반경 제외 검사(`HitObjectRadius`)에도 이미 걸러지던 위치라 화면엔 안 보이던 것들이다. 끝단이 아닌 위치에서 사라진 틱은 **2개**뿐. 짧은 반복 슬라이더(`tickDistance ≥ spatialLength`로 클램프)에서 리버스 화살표 자리에 틱이 찍히던 게 원인 |
| 검증 | 실제 라이브러리 커브(`SliderCurve`)로 수정 전/후 틱을 뽑아 비교 + 미러 대칭성 독립 검사. **실기 확인 대기** |

#### ~~H3. newStyle 스피너 glow가 아예 안 보임~~ ✅ 해결 (`4261cad`)
| | |
|---|---|
| 원인 | `spriteGlow.Alpha` 직접 설정이 생성 시 `Fade(0,0)` 변환에 매 프레임 덮여 무효 |
| 수정 | stable(:444-446) 방식 — **Fade 변환의 Start/EndFloat 자체를 진행도로 수정**. 진행 중 파란색(:443), 스케일 OutQuad(:448 easeOutVal — 기존 cos 공식은 easing 방향 반대), percent 무클램프 |
| 전제 수정 | `UpdateTransformations`가 spin/clear/glow 변환까지 Clear하던 것을 메인 스프라이트만으로 분리 — stable은 생성자에서 셋이 생기기 전에 돌고, 셋은 자기 변환을 소유 |
| 종료 처리 | glow가 보이게 되면서 드러난 잔상(종료 후 ~2초) — stable `Hit()`의 `FadeOut(300)` 대응으로 종료 감지 시 1회 페이드아웃, ResetState에서 캔버스 복원 |
| 검증 | ✅ **실기 확인** — 실제 osu!와 동일 동작 확인 (glow 표시·통과 시 파란 유지·보너스 flash 복귀·종료 페이드). H17(`8a2442e`)과 함께 검증 |

#### ~~H4. 스네이킹 선분 병합에서 `forceEnd` 무시~~ ✅ 해결 (`b3958e1`)
| | |
|---|---|
| stable | `SliderOsu.cs:1074` — 병합 중단 조건에 **`\|\| sliderCurveSmoothLines[i].forceEnd`** 포함, `min_dist`는 `.straight ? 32 : 6` |
| ~~ours~~ | ~~`dist > minDist \|\| last \|\| (i==count-2)`, minDist 항상 6~~ → forceEnd 추가 + `minDist = straight ? 32 : 6`. `Line.forceEnd`/`straight`는 SliderCurve가 이미 세팅 중이라 배선만 |
| 검증 | 빌드 통과 · **실기 확인 대기** (레드앵커 슬라이더 모서리) |

#### ~~H5. 브레이크 뒤 콤보 번호/색 리셋 누락~~ ✅ 해결 (`743c16d`)
| | |
|---|---|
| stable | `HitObjectManager.cs:1258-1263` — 브레이크를 지난 첫 객체에 **강제 NewCombo** |
| ~~ours~~ | ~~`[Events]` 섹션의 브레이크를 파싱하지 않음~~ → `BeatmapData.EventBreak` + `BeatmapParser.ParseEvent` 추가, 콤보 루프에 stable의 `lastBreakPoint` 순회 이식 |
| 함께 수정 | `HitObjectData.NewCombo`를 **Type 비트의 파생 프로퍼티로 변경** (stable `HitObjectBase.cs:114-124`와 동일). 별도 bool 필드였으면 브레이크가 강제한 NewCombo를 `Type`을 검사하는 분기가 못 봐서 수정이 무효였다 |
| ~~증상~~ | ~~브레이크가 있는 **모든 맵**에서 어긋남~~ → **이 주장은 틀렸음** (아래) |
| 실제 영향 | **측정: 라이브러리 std 맵 135개 중 브레이크 보유 55개 → 렌더링이 달라지는 맵 0개 (0.0%)**. 발행된 맵은 브레이크 뒤 객체에 이미 NewCombo 마커가 붙어 있다 (에디터가 저장 시 강제분을 파일에 구워 넣는 것으로 보임 — stable도 같은 변형을 HitObject에 영구 적용하므로). 즉 **수정은 stable과 1:1이 맞지만 실사용 영향은 사실상 없다** |
| 부수 효과 | 브레이크가 콤보를 끊으면 그 경계의 **followpoint도 억제**된다 (`AddFollowPoints`가 콤보 루프 뒤에 돌고 같은 객체 참조를 읽음) — stable과 동일 |
| 검증 | ✅ 실제 맵 135개 회귀(변화 0 = 무해) + **합성 맵**(브레이크 뒤 NewCombo 마커 없음)으로 새 경로 확인: 콤보번호 `4→1`·`8→1`, 콤보색 순환, **길이 500ms 브레이크는 `MIN_BREAK_LENGTH`(650)로 걸러져 리셋 안 일으킴** |

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
| ~~H20~~ ✅ | ~~**스택 위치 재적용**~~ → 해결 (`8384faf`) | 범위 내 **전 객체** 무조건 `ModifyPosition` — `HitObjectManager.cs:1761-1765` | ~~`StackCount != 0`인 것만~~ → 무조건 적용 (C1/H23과 함께) |
| H21 | **정렬 안정성** | `ListHelper.StableSort` — `HitObjectManager.cs:1240` | `List.Sort`(불안정) → 동시각 객체 순서 흔들림 (= [C5](#c5-불안정-정렬-z-플리커-가능성)) |
| ~~H22~~ ✅ | ~~**틱 소멸 방식**~~ → 해결 (`3bc67c8`) | 세그먼트 끝에 일괄 `Fade(0,0)` — `SliderOsu.cs:933-935` | ~~각 틱의 scoreTime에 개별~~ → 세그먼트 끝 일괄로 변경 (H2와 함께) |

### H-3. 이미 A~G에 기재된 것 중 fidelity 위반이기도 한 것 (3건)

| # | 항목 | stable 기준 |
|---|---|---|
| ~~H23~~ ✅ | ~~[C1. 스택된 슬라이더 바디 미이동]~~ → 해결 (`8384faf`) | stable `ModifyPosition`은 **슬라이더 전체**(바디·볼·틱 포함)를 옮김 — 동일하게 수정. 실측 영향 0.4% |
| ~~H24~~ ✅ | ~~[C2. 리버스 화살표 제거 누락]~~ → 해결 (`9db56f9`) | stable은 `SpriteCollection` 통째 관리 → 누락 불가능한 구조 |
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
| ~~2~~ | ~~**H2** (리턴 패스 틱)~~ | 해결 — 실측 리턴 틱 위치 변경 6.7%, 통째 소실 2.8% |
| 3 | **H3** (스피너 glow) | newStyle 기본 스킨에서 항상 발생 |
| 4 | **H4** (forceEnd) | 레드앵커 슬라이더 = 흔함 |
| ~~5~~ | ~~**H5** (브레이크 콤보)~~ | ~~브레이크 있는 모든 맵~~ → 해결. **실측 영향 0맵** — 순위 근거였던 "모든 맵" 주장이 틀렸다 |
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
| ~~6~~ | ~~E 전부~~ (해결) / 나머지 G | E1~E7 해결 (`68de5ad`). G는 증거·재현 확보 후 |

**포팅 충실도 축** (프로젝트 핵심 목표)

| 순위 | 항목 | 이유 |
|---|---|---|
| 1 | H1 (FadeIn 상수) | 뿌리. 한 줄 수정으로 6개 스프라이트 계열 교정 |
| ~~2~~ | ~~H2 (리턴 패스 틱)~~ | 해결 — 실측 6.7% |
| 3 | H3 (스피너 glow) | 기본 스킨 newStyle에서 항상 |
| 4 | H4 (forceEnd) + H23/C1 (스택 슬라이더) | 슬라이더 형상 정확도 |
| ~~5~~ | ~~H5 (브레이크 콤보)~~ | 해결 — 실측 영향 0맵 |
| 6 | H6~H22 | 옛 맵 / 특정 스킨 조건부 |

> **주의**: H 축 수정 시 [일치 확인된 것](#검증-결과--일치-확인된-것-안심-목록)과 [등가 항목](#구조적으로-11-불가--등가로-봐야-하는-것)은 건드리지 말 것. 이미 stable과 1:1임을 대조로 확인함.
