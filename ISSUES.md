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
| 2026-07-17 | **D1 4탄**: PRIVATE(JIT) 우선 스캔 + IMAGE/MAPPED 폴백 (2단계) | `cc6323a` | 실기 확인 — 폴백 로그 미발생(=PRIVATE에서 전량 해결). ngen돼도 IMAGE 폴백이 받아 침묵 실패 없음 |
| 2026-07-17 | **A1**: 손상된 settings.ini 값 하나로 기동 불능 (범위 밖 `.Value`·NaN 캐스트·경로 문자·null 스킨 폴더·쓰기 불가 폴더) | `fcfc0ff` | 감사+적대적 리뷰 워크플로 전수 검증. **실기 확인** — 전 트리거 동시 주입 ini로 정상 기동(값 살균 확인) |
| 2026-07-17 | **A4**: 문화권 의존 직렬화 → InvariantCulture 고정 (+ 구파일 현재-로케일 폴백) | `fcfc0ff` | 적대적 리뷰로 로케일 라운드트립·구파일 회귀 검증 |
| 2026-07-17 | **E1~E7 전부**: 잠복 결함 7건 (버퍼 스레드안전·AOB slot 검증·QuadBatch 셰이더·Fields enum 충돌·GameField 경합·낡은 파싱 폐기·문자열 캐시) | `68de5ad` | 감사 정독 → 적대적 리뷰(설계 14 + 구현 4 에이전트, fable5) 전수 검증. 리뷰가 실결함 3건 잡아 반영(모드 범위 오탈락·GameField float 절단·폐기 시 영구 blank). 빌드 통과(경고 0) + **실기: 정상 플레이 무이상(공통 경로 회귀 확인, E2 slot 오탈락 없음·E4 렌더 정상)**. E3 오버플로 등 희귀 트리거는 미노출 |
| 2026-07-17 | **A2·A3·A5**: 크래시 방어 3건 (깨진 스킨 `Arm()` NRE · `LoadAll` null · 콤보색 범위 초과) | `67e6a7f` | 적대적 리뷰(3 에이전트, fable5) 전수 검증 — 남은 크래시 지점 0, 정상 경로 회귀 0. 유효 맵은 클램프 항등이라 무변화. 빌드 통과(경고 0) |
| 2026-07-17 | **C5·C6** (+C3 오탐 확인): z-플리커 안정 정렬(=H21) · 슬라이더 틱 NaN/무한루프 방어 | `07c396e` | 적대적 리뷰(2 에이전트, fable5) — 둘 다 hold, 정상 맵 항등. C3은 stable과 byte 동일이라 오탐(코드 무변경). 빌드 통과(경고 0) + **실기: 정상 플레이 무이상(공통 경로 회귀 확인)**. C6 degenerate/에일리언 맵 트리거는 미노출 |
| 2026-07-17 | **C4 분석 + C6 후속**: C4 동일 StartTime 오매칭(편차 실재·실측 영향 극소·document-only) · C6 잔여 NaN(슬라이더 길이 NaN/Inf 파싱 차단) | `6063c3f` | 설계+적대적 리뷰(opus) — C4는 std 랭크맵 0개라 코드 보류(안전 fix 경로만 기록), C6-ball 렌즈가 찾은 Length-NaN 가드 추가. 빌드 통과(경고 0) |
| 2026-07-17 | **포팅 충실도 H 배치 8건**: H7(v<8 틱)·H8(v≤8 스피너콤보)·H9(HD 틱페이드)·H10(old-layout 리버스화살표)·H12(스피너 AC 조건)·H13(스피너 turnRatio)·H15(followpoint Movement)·**H18(AC 소멸 타이밍)** | `d877dc8` `f69e76c` | **codegraph로 오버레이+stable 동시 인덱싱** 후 triage(13건)→설계→적대적 리뷰(opus) 3단. 전부 stable 조건에 게이트돼 모던 경로 무변화. **triage가 H19를 오탐, H6를 자기교정으로 확정**. H18은 "단순 제거"가 컬링 모델상 악화라 타이밍 교정으로 재작업+검증. H11·H14·H16은 의도적 미구현으로 결정. 빌드 통과(경고 0). 실기 확인 대기 |
| 2026-07-17 | **견고성 G 섹션 전체 (G1~G9)**: MessageBox 안내·Process 누수·**자동 재접속**·Menu HUD편집 지오메트리·타이머 Dispose·문자열 버퍼 재사용·**DPI 인식**·**회귀 테스트 프로젝트** | `af38a50` | G3(재접속)은 설계+적대적 리뷰(opus 2렌즈) — PID 종속 캐시 전수 리셋 검증. G5는 A1에서 이미 해결(문서만). G9 테스트 15개 통과(충실도도 검증). 빌드+테스트 통과. **G8은 고DPI 실기 확인 필요** |

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

### ~~A1. settings.ini 값 하나로 기동 불능~~ ✅ 해결 (`fcfc0ff`)
| | |
|---|---|
| ~~증상~~ | ~~범위 밖/손상 INI 값이 생성자에서 예외 → 패널이 메인 폼이라 앱이 아예 안 뜸~~ → `OverlaySettings.Normalize()`가 로드값을 안전 범위로 강제(`Load()` 끝에서 **항상** 호출) + 컨트롤 대입·IO 방어 |
| ~~트리거~~ | ~~`FpsCap=-1`, `Size=0/NaN`, `AR=NaN`, `DtAR=Infinity`, `Name=bad\|name`, 쓰기 불가 폴더~~ → 전부 무해화 |
| 수정 | ① 범위 클램프 + NaN/Infinity 살균 ② 컨트롤 Min/Max를 `OverlaySettings` 상수로 일원화(Normalize와 동일 출처 — 분리되면 재발) ③ `nudFpsCap`/`nudCursorSize`/`AddValueRow`의 `(decimal)` 캐스트 방어(NaN은 `Math.Min/Max`를 통과해 캐스트에서 OverflowException) ④ 스킨/커서팩 이름 경로안전화(불법 문자·`.`/`..` 거부) ⑤ `CreateDirectory`/`GetDirectories` try/catch ⑥ `SkinManager.LoadSkin` null 폴더 → 임베디드 기본 |
| 비고 | `d79570d`가 난이도 행(451·460)만 선(先)클램프했었고, 이번에 FpsCap·CursorSize·NaN·경로·IO·null 스킨 폴더까지 완결. A4와 함께라 사용자 잘못 없이도 나던 경로도 차단 |
| 검증 | 감사 워크플로(전 크래시 지점 열거) → 적대적 리뷰 워크플로(verify 포함)로 3건 추가 발견·수정: null 스킨 폴더 크래시·`..` 경로탈출·구파일 로케일 회귀. **실기 확인 완료** |

### ~~A2. 텍스처 없는 스킨에서 판정 오는 순간 NullReferenceException~~ ✅ 해결 (`67e6a7f`)
| | |
|---|---|
| ~~증상~~ | ~~hitcircle/approachcircle 텍스처 로드 실패 시 생성자가 스프라이트 전부 null인 채 早退하는데 `Arm()`이 null 무검사 접근 → 판정 순간 NRE~~ → `Arm()` 초입(IsArmed/IsHit/ArmTime 기록 후)에 `if (spriteHitCircle == null) return;` 가드 |
| 트리거 | 깨진 스킨 + 판정 발생 |
| 수정 | 단일 가드가 hit/miss 경로의 무가드 접근 5곳(스케일아웃·페이드·miss 페이드·Scale 리셋)을 전부 지배. 파일 내 다른 스프라이트 접근은 이미 전부 null 가드됨. `HitCircleSliderStart/End`는 `Arm()` 미오버라이드라 상속으로 함께 보호 |
| 검증 | 적대적 리뷰로 전 메서드·서브클래스·호출부 전수 확인 — 남은 무가드 접근 0, 정상 스킨 경로 무변화 |

### ~~A3. `TextureManager.LoadAll` null 반환 미처리~~ ✅ 해결 (`67e6a7f`)
| | |
|---|---|
| ~~증상~~ | ~~`sliderb`/`sliderfollowcircle`이 유저·임베디드 기본 스킨 모두에 없으면 `LoadAll`이 null → `sliderBallTextures.Length`에서 NRE~~ → `LoadAll(...) ?? new pTexture[0]`로 빈 배열 합침 |
| 트리거 | sliderb / sliderfollowcircle이 어느 스킨에도 없는 경우 |
| 수정 | 빈 배열이면 `.Length > 0` 가드가 자연히 걸려 `sliderBall`/`sliderFollower`가 null로 남는다 — 전 사용처(Add/Remove·매 프레임 Update·Dispose·스택)가 이미 null 가드. `usingDefault`도 short-circuit 안전 |
| 검증 | 적대적 리뷰로 빈 배열 다운스트림 전수 확인 + 다른 `LoadAll` 호출부(HitBurst·HitCircleOsu·HOM)도 이미 null/빈 가드됨 확인 |

### ~~A4. 문화권 의존 직렬화~~ ✅ 해결 (`fcfc0ff`)
| | |
|---|---|
| ~~증상~~ | ~~`float.TryParse`/`ToString("F2")`가 현재 로케일 사용 → ',' 로케일에서 "9.20"이 920으로 읽혀 A1 직행~~ → SettingsSerializer의 float/int I/O 전부 `CultureInfo.InvariantCulture` + `NumberStyles` 고정. '.'로 쓰인 값은 어느 로케일에서든 맞게 읽힘 |
| 하위호환 | 구버전이 현재 로케일로 저장한 ini(예: de-DE "1,50")는 **현재-로케일 폴백 파싱**으로 복구. `NumberStyles.Float`(천단위 불허) 유지 → '.' 로케일에서 "9,20"이 920으로 오독되지 않고 실패→기본값 (A4 수정을 되돌리지 않음) |
| 비고 | NaN/Infinity도 거부. BeatmapParser는 원래 Invariant였고 이번에 SettingsSerializer만 맞춤. (적대적 리뷰가 폴백 없으면 구파일 침묵 초기화됨을 발견 → 폴백 추가) |

### ~~A5. 비트맵 Combo 색상 범위 초과~~ ✅ 해결 (`67e6a7f`)
| | |
|---|---|
| ~~증상~~ | ~~`Color.FromArgb(r,g,b)` 무클램프 — `Combo1: 300,0,0` 같은 맵에서 ArgumentException → 파싱 Task의 catch가 삼켜 그 맵이 **조용히** 로드 실패~~ → r/g/b를 `Math.Max(0, Math.Min(255, .))`로 `[0,255]` 클램프 후 FromArgb |
| 수정 | 유효 맵은 클램프가 항등이라 무변화. 파서 내 **유일한** Color 생성 지점(`Combo#`만). skin.ini 색상은 원래 try/catch 보호(`SkinManager.ParseColor`) |
| 검증 | 적대적 리뷰로 파서·저장소 전 `FromArgb` 감사 — 남은 무클램프 user-data Color 지점 0. `ComboColours`는 다운스트림 미소비(콤보 순환은 `SkinManager.GetComboColours`)라 순환 회귀 없음 |

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

### ~~C3. HR flip이 clamp 뒤에 적용~~ ✅ 오탐 — stable과 동일 확인 (코드 변경 없음)
- ~~**위치**: `Gameplay/Beatmap/BeatmapParser.cs` — y를 512까지 허용해놓고 `384-y`~~
- ~~**증상**: y∈(384,512] 객체가 HR에서 음수 좌표~~
- **결론**: **결함 아님.** stable `HitObjectManager_LoadSave.cs:815-817`이 우리와 **동일한 순서/상수**로 `y = (int)Max(0, Min(512, y))`(clamp 512·pivot 384) 후 `verticalFlip ? 384 - y : y`를 한다. 즉 y∈(384,512]에서 음수가 되는 건 stable의 동작 그대로이며 우리가 1:1이다. 초기 분석의 오탐 (stable 대조로 확정)
- **별개 미세 차이(무시 가능)**: 우리는 좌표 파싱에 `double.TryParse`, stable은 `Decimal.Parse`/`Convert.ToDouble` — int 캐스트가 flip 전에 걸려서, ~17유효자리의 병적 **비정수** 좌표라면 1px 다를 여지가 이론상 있다. 그러나 `.osu` 좌표는 스펙상 정수라 **실제 맵에선 절대 발화 안 함**. C3 판정(오탐)과 무관한 별도 항목으로만 기록

### C4. 동일 StartTime 객체 판정 오매칭 (2B/aspire 맵) — 📋 분석 완료: 실측 영향 극소·코드 변경 보류
- **위치**: `HitObjectManagerOsu.cs`(판정 매칭 — `j.StartTime == …Data.StartTime && 타입비트`, 첫 매치 `break`) + `HitBurst.cs`(`startTimeToIndex`/`hitSeen` 첫 승)
- **실체 (진짜 편차 맞음)**: stable은 판정을 객체 **참조**로 연결한다 — `HitObjectManager.Hit(HitObject h)` → `hitObjects.BinarySearch(h)`(시간 조회 없음), `IsHit`/`StartIsHit`가 per-instance라 **동일 StartTime 충돌이 구조적으로 불가능**. 우리는 메모리에서 읽은 평평한 판정 리스트를 `StartTime + 타입`으로 되짚어 첫 매치를 써서, 같은 StartTime·같은 타입 객체가 여럿이면 전부 첫 판정에 묶인다
- **실측 영향 — 극소**: osu!std는 Ranking Criteria가 2B(동시 객체)를 금지 → **랭크/러브드 std 맵은 사실상 0개** 해당(Aspire 소수 예외). Aspire/2B 장난 맵만 영향, 증상도 (1) 같은 시각 N객체가 첫 판정으로 동일 arm/track, (2) HitBurst가 그 StartTime에 1개만 생성 — 둘 다 **경미**. (일반 스택은 같은 위치·**다른** StartTime이라 무관.) ⚠️ 이 경로엔 게임모드 게이트가 없어 **mania 코드**(동시 노트)도 이론상 걸리나, 오버레이는 std 게임필드 렌더러
- **안전한 수정 경로(보류)**: 필요 시 `HitObjectManagerOsu.Update`에 재사용 `bool[] claimedJudgements`로 각 판정을 **한 번만** 소비 — 순서 무관이라 불안정 정렬·±2 인덱스 허용과 무관하게 안전하고, 정상 맵은 StartTime+타입이 유일해 **동작 무변화**. HitBurst 완전 대칭(StartTime당 N버스트)은 추가로 더 침습적. **장난 맵 한정 이득 대비 99.99% 경로 회귀 리스크가 커 코드 변경 보류**, 분석만 기록
- **검증**: stable 대조(`HitObjectManager.cs:851-858·1494-1534`, per-instance `HitObject.IsHit`) + 적대적 리뷰 2렌즈 모두 **편차 실재·영향 극소·document-only** 지지

### ~~C5. 불안정 정렬 z-플리커 가능성~~ ✅ 해결 (`07c396e`) [= H21]
- ~~**위치**: `Rendering/SpriteManager.cs` — `List.Sort`는 불안정 정렬~~ → Depth 동점 스프라이트가 프레임마다 앞뒤 뒤바뀜
- **수정**: `pSprite`에 `long StableOrder`(오버플로 불가) 추가, `Add`에서 전역 삽입 순서 부여, 정렬을 `(Depth, StableOrder)` **전순서**로 → 결과가 유일하게 결정돼 불안정 정렬이어도 깜빡임 없음. stable `ListHelper.StableSort`와 동치. 캐싱된 `IComparer`로 매 호출 할당도 제거
- **검증**: 적대적 리뷰로 전순서·프레임 간 결정성·distinct Depth 무영향·유일 정렬 지점 확인. HUD(모두 0.5f)·트레일 파티클(cursor.Depth-0.001f)·스피너 레이어의 동점군이 삽입 순서로 결정화됨. **실기 확인 — 정상 플레이 무이상(렌더·z-순서 회귀 없음)**

### ~~C6. 0 나눗셈 → NaN 좌표~~ ✅ 해결 (`07c396e`)
- ~~**위치**: `SliderOsu.cs` 틱 위치(p1==p2 선분), 볼 위치(zero-length 슬라이더)~~
- **수정**: 틱 위치 `scoringDistance / Vector2.Distance(p1,p2)`가 길이 0 선분(p1==p2, 중복 제어점 에일리언 맵)에서 NaN → 그 틱은 건너뛴다(stable도 radius 검사로 누락). 볼 위치 본경로(`GetBallPosition`/`PositionAtLength`)는 이미 가드돼 있어 무변화
- **함께 강화** (적대적 리뷰가 같은 루프에서 발견한 동류 결함): ① `Length<=0` 슬라이더의 `tickDistance<=0` → **틱 무한루프(로더 행)** 를 `tickDistance > 0` 가드로 차단 ② `UpdateSprites`의 중복 ball-position 나눗셈 `0/0=NaN`(세그먼트 인덱스 뒤틀림) 가드
- **검증**: 적대적 리뷰로 정상 선분 항등 확인. 전부 degenerate/에일리언 맵 한정, 정상 맵 무변화. **실기 — 정상 플레이 무이상(공통 경로 회귀 확인)**. degenerate/에일리언·`NaN` 길이 트리거는 정상 세션 미노출이라 코드 검증 수준으로 남음
- **후속** (`6063c3f`): 재리뷰(C6-ball 렌즈)가 잔여 NaN 경로 1건 발견 — `double.TryParse`가 `"NaN"`·`"Infinity"` 문자열도 성공 파싱해 `data.Length`가 NaN/Inf가 되면 velocity·커브 길이를 타고 볼·틱을 NaN 좌표로 만든다(나눗셈 가드는 값이 NaN이면 못 거른다). 파싱에서 **유한·비음수만 수용**해 차단. 정상 맵은 정수/유한 길이라 무변화

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
- ⚠️ **JIT 힙(PRIVATE)만 스캔은 41배지만 단독 채택은 안 함** — osu!가 이 메서드들을 JIT한다는 가정에 의존한다. ngen되면 IMAGE 영역으로 가 침묵 실패(커서 40% 버그류). 실행 가능 여부는 물리 법칙, PRIVATE/IMAGE는 런타임 정책이라 언제든 바뀜
- ✅ **4탄** (`cc6323a`): 위 딜레마 해소 — **PRIVATE 우선 + IMAGE 폴백 2단계**. ① PRIVATE(JIT) 영역만 스캔(흔한 경우 여기서 전량 해결 = 41배) → ② 하나도 못 찾은 패턴만 나머지 실행 영역(IMAGE/MAPPED = ngen/R2R)에서 재스캔. 두 단계 합집합이 "실행 가능한 전 영역"이라 **정답성은 단일 패스와 동일**하면서 속도는 PRIVATE-only를 취한다. `VirtualQueryEx`는 1회만 호출해 두 단계가 영역 목록 공유. 폴백 발동 시 `[AOB]` 로그 1줄 (실측 미발생 = ngen 아님)
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
>
> **실기 확인(부분)**: 정상 플레이 무이상 — 공통 경로 회귀 없음이 확인됨. 특히 오버레이가 시간/모드/커서/비트맵을 정상 표시 → **E2 slot 오탈락 없음**, 스프라이트가 제자리·제크기로 렌더 → **E4 enum 재번호 정상**. 단 **E3(한 배치 >10,900쿼드 오버플로)·E5/E6 경합 타이밍·E1 스레드 안전**은 정상 세션에서 안 밟히는 희귀/비관측 트리거라 코드 검증 수준으로 남음.

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
| ~~G1~~ ✅ (`af38a50`) | `Program.cs` | ~~WinExe라 `Console.ReadLine()`이 즉시 null → "대기 후 종료" 무동작~~ → 기동 실패 시 **MessageBox**로 안내 후 종료 |
| ~~G2~~ ✅ (`af38a50`) | `ProcessMemory.cs` | ~~다중 실행 시 나머지 `Process` 객체 미해제~~ → `procs` 배열 전부 Dispose |
| ~~G3~~ ✅ (`af38a50`) | (재접속) | ~~osu! 재시작 시 죽은 핸들 영구 보유 → 오버레이 재시작 필요~~ → **자동 재접속**: `GetExitCodeProcess`로 죽음 감지(무할당·무예외·PID재사용 안전) + PID 종속 캐시 **전수 리셋** 후 재스캔, 1초 rate-limit. 설계→적대적 리뷰(opus 2렌즈: 완전성·회귀) 검증. **정상 연결 경로 무변화** |
| ~~G4~~ ✅ (`af38a50`) | `OsuMemoryReader.cs` | ~~Menu에선 해상도 갱신이 안 돌아 HUD 편집이 낡은 지오메트리~~ → `HudEditActive`일 때 Menu에서도 `resolution.Refresh()` |
| ~~G5~~ ✅ (`fcfc0ff`, A1) | (설정) | ~~`..\..` 경로 탈출 가능~~ → **A1의 `IsSafeFolderName`이 이미 `.`/`..`·경로 구분자 거부**. 별도 수정 불필요(문서만 갱신) |
| ~~G6~~ ✅ (`af38a50`) | `ControlPanelForm.cs` | ~~`statusSync` 타이머 Dispose 안 됨~~ → 폼 종료 시 Stop+Dispose. (`syncTimer`는 F1에서 이미 제거됨) |
| ~~G7~~ ✅ (`af38a50`) | `ProcessMemory.cs` | ~~`ReadSharpString` 호출당 `byte[]` 할당~~ → `[ThreadStatic]` 재사용 버퍼 + 길이 지정 디코드(잔여 바이트 혼입 방지) |
| ~~G8~~ ✅ (`af38a50`) | `Program.cs` | ~~High-DPI 인식 미검증~~ → **검증 결과 매니페스트/코드 없어 DPI-unaware였음** → `DpiAwareness.Enable()`(PerMonitorV2→System 폴백) 추가. ⚠️ **고DPI 디스플레이 실기 확인 필요**(현 100% DPI 환경 미검증, 문제 시 1줄 롤백) |
| ~~G9~~ ✅ (`af38a50`) | `OsuEnlightenOverlay.Tests` | ~~테스트 0개~~ → **자체 회귀 테스트 프로젝트**(nuget 무의존, InternalsVisibleTo): ParsePattern·난이도(AR→PreEmpt)·비트맵 파서 **15개 통과**. 포팅 충실도도 검증(AR5=1200·AR9=600·AR0=1800·AR10=450·FadeIn=400) |
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
| H6 📋자기교정 | **옛 맵 베지어** | v≤6/v7-8/v>8 3종, v<10 `CreateBezierWrong`(1/50 짧음) — `SliderOsu.cs:479-567` | 항상 최신 알고리즘. **triage 실측: v9는 양 엔진이 SpatialLength로 잘라 sub-pixel 자기교정, v7-8 기하 동일, v≤6 multipart(레드앵커) 베지어만 수px 차 — 희귀. 저우선** |
| ~~H7~~ ✅ (`d877dc8`) | ~~**옛 맵 틱 간격**~~ | v<8은 BpmMultiplier로 나누지 **않음** — `SliderOsu.cs:673` | ~~항상 나눔~~ → `v<8 ? 거리 : 거리/BpmMultiplier`. 모던(v≥8) 무변화 |
| ~~H8~~ ✅ (`d877dc8`) | ~~**v≤8 스피너 콤보**~~ | 스피너면 무조건 `forceNew` — `HitObjectManager.cs:1267` | ~~NewCombo일 때만~~ → `v≤8`이면 스피너가 무조건 다음 콤보 강제 |
| ~~H9~~ ✅ (`d877dc8`) | ~~**HD 틱 페이드**~~ | hidden이면 틱에 `Fade 1→0(…→scoreTime)` — `SliderOsu.cs:895,903` | ~~없음~~ → `HiddenActive`일 때 각 틱에 scoreTime 페이드아웃. nomod 무변화 |
| ~~H10~~ ✅ (`d877dc8`) | ~~**old-layout 리버스 화살표**~~ | Scale + **Rotation ±π/32 진동** — `HitCircleSliderEnd.cs:90-95` | ~~new-layout(Scale만) 고정~~ → `UseNewLayout` 게이트: old-layout이면 선형 Scale + Rotation ±π/32 흔들림 |
| H11 ⛔미구현(결정) | **SpinnerFadePlayfield** | 검은 배킹 레이어 2장 — `SpinnerOsu.cs:126-144` | 누락. **triage: old-format 스킨만·moderate. 이미 old-style spinner-background(y≈32-464)를 그려 상·하 ~29/19 units 띠만 미검게 — 후속 배치** |
| ~~H12~~ ✅ (`d877dc8`) | ~~**스피너 어프로치서클 조건**~~ | `SpriteCircleTop.Texture.Source != SkinSource.Osu` — `SpinnerOsu.cs:194` | ~~`!SkinManager.IsDefault`(텍스처만 없을 때 오판)~~ → 실제 로드된 `spriteCircleTop.Texture.Source`로 판정 |
| ~~H13~~ ✅부분 (`d877dc8`) | ~~**스피너 회전 배율**~~ | `turnRatio`는 middle2 없으면 **1** — `SpinnerOsu.cs:272-279` | ~~**0.5 고정**(oldstyle 절반 속도)~~ → `spriteMiddleBottom` 유무로 0.5/1. **부호(반시계)는 무부호 메모리 `FloatRotationCount`라 복구 불가 — 잔여** |
| H14 ⛔미구현(재현불가) | **metre 블링크** | `RNG.NextBool(…)` **확률적** — `SpinnerOsu.cs:456` | `>= 0.5f` 결정적. **triage: stable의 프레임별 RNG 시퀀스는 시드 없이 재현 불가 — 확률화해도 frame-exact 아님. old-style 스킨 한정. 문서화만** |
| ~~H15~~ ✅ (`d877dc8`) | ~~**followpoint 등장**~~ | `IsDefault`일 때만 Scale + **Movement**(posStart→pos, Out) — `HitObjectManager.cs:1887-1891` | ~~Scale 무조건 + Movement 누락, `posStart` 데드코드~~ → `IsDefault` 게이트 + Movement 추가(데드 `posStart` 사용) |
| H16 ⛔미구현(결정) | **sliderBall 회전+FlipVertical** | 진행 방향 회전 + 곡선 시작 각도 상하반전 — `SliderOsu.cs:796-800` | 없음. **triage: 회전과 flip은 분리 불가(flip만 넣으면 좌향 슬라이더서 뒤집힘). 기본/원형 sliderb엔 사실상 불가시, 비대칭 커스텀 sliderb만 — moderate, 후속** |
| ~~H17~~ ✅ | ~~**glow flash 복귀**~~ → 해결 (`8a2442e`) | `FlashColour(White, 200)` — 200ms 후 파란색 복귀 — `SpinnerOsu.cs:386` | ~~White로 바꾸고 복귀 없음~~ → glow 전용 수동 보간(`ApplyGlowColour`). pSprite에 Colour 변환 지원이 없어 인프라 추가 대신 국소 처리 |
| ~~H18~~ ✅ (`f69e76c`) | ~~**어프로치서클 소멸**~~ | AC는 판정 순간까지 0.9 유지, 미스는 `StartTime+HitWindow50`에 60ms 페이드 — `HitCircleOsu.cs:245,264` | ~~생성 시 `0.9→0 @ startTime→+60`(startTime을 미스 마감으로 오인 → 늦은 히트/미스서 AC 조기 소멸)~~ → 페이드를 `StartTime+HitWindow50`로 교정, EndTime 확장으로 Arm 변환 컬링 방지. **단순 제거(보류했던 설계)는 컬링 모델상 AC를 startTime에 하드컬해 악화 확인 → 타이밍 교정으로 선회.** 적대적 리뷰 2렌즈(컬링·공존/HD) hold |
| ~~H19~~ ❌오탐 | ~~**Disarm 후 Scale 리셋**~~ → 결함 아님 | Disarm에서 `Scale=1`, `Text.Scale=TEXT_SIZE` — `HitCircleOsu.cs:223-225` | **triage: retry는 인스턴스 전체 재생성, `UpdateDifficulty`는 `Transformations.Clear`로 스케일 복원 → 잔존 불가능. stable은 지속 Scale 필드라 필요했지만 우리는 매 프레임 무상태 평가라 불필요** |
| ~~H20~~ ✅ | ~~**스택 위치 재적용**~~ → 해결 (`8384faf`) | 범위 내 **전 객체** 무조건 `ModifyPosition` — `HitObjectManager.cs:1761-1765` | ~~`StackCount != 0`인 것만~~ → 무조건 적용 (C1/H23과 함께) |
| ~~H21~~ ✅ | ~~**정렬 안정성**~~ → 해결 (`07c396e`, =C5) | `ListHelper.StableSort` — `HitObjectManager.cs:1240` | ~~`List.Sort`(불안정)~~ → `(Depth, StableOrder)` 전순서로 안정화 (C5와 동일) |
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
| ~~1~~ | ~~A1+A4 (INI 검증+Invariant)~~ | ✅ 해결 (`fcfc0ff`) — `Normalize()` + InvariantCulture + 폴백 |
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
