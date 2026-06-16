# rotate+ — 설계 문서 (v1)

> 현대화된 Windows 화면 회전 유틸리티. iRotate의 대체재이며, **NVIDIA 앱이
> 디스플레이 설정을 건드린 뒤 회전 시 마우스 축이 어긋나는 버그**를 해결하는 것이
> 핵심 존재 이유다.

---

## 1. 목표 (Goals)

- 트레이 상주 + 글로벌 핫키로 모니터를 0/90/180/270° 즉시 회전.
- 모니터별 원하는 방향을 기억하고, 재부팅·드라이버 리셋 후 복원.
- **NVIDIA 등 외부 요인이 방향을 덮어쓰면 자동으로 다시 적용** (마우스 축 desync 회피).
- 런타임 설치 없이 실행되는 단일 exe 배포.

## 2. 핵심 문제: 마우스 축 desync

### 2.1 정상 동작 모델
Windows에서 모니터를 회전하면 **두 가지**가 동기화되어야 한다.

1. **패널 스캔아웃 방향** — GPU가 패널로 내보내는 이미지의 물리적 회전.
2. **데스크톱 좌표계 + 입력 매핑** — 해당 모니터의 논리 사각형 크기(예: 1920×1080 →
   1080×1920)와, 컴포지터가 데스크톱 이미지를 회전 보정하는 방식.

마우스 커서는 **가상 데스크톱 좌표계**에서 움직이고, 컴포지터가 "좌표상의 오른쪽"이
"화면상의 오른쪽"으로 보이도록 데스크톱 이미지를 회전시켜 합성한다. **Windows가 패널
방향을 올바로 알고 있는 한** 마우스 축은 항상 맞는다.

### 2.2 NVIDIA가 깨뜨리는 지점
NVIDIA 앱/드라이버가 디스플레이 설정을 건드리면, 드라이버가 **스캔아웃은 회전시키면서
Windows에는 방향을 identity(0°)로 보고**하거나, 그 반대로 이중 회전 상태를 만든다.
결과:

> 패널은 물리적으로 회전됨 ↔ Windows 컴포지터는 비회전으로 합성
> ⇒ "좌표상의 오른쪽" ≠ "화면상의 오른쪽"
> ⇒ 마우스를 오른쪽으로 움직이면 커서가 위/아래로 가는 **축 desync**

**즉 버그의 정체 = `Windows가 아는 방향` ≠ `실제 패널 방향`.**

### 2.3 수정 전략 (불변식 기반)
> **불변식: `Windows 방향 == 실제 패널 방향` 이면 마우스 축은 자동으로 맞는다.**

따라서 해법은 단순하다 — **이 불변식을 항상 유지**한다.

1. **OS를 방향의 단일 권위(source of truth)로 삼는다.** 회전은 반드시 CCD API
   `SetDisplayConfig`로 path target의 `rotation`을 설정해 수행한다. 이 경로는 OS가
   드라이버에 스캔아웃 회전을 **명령**하면서 동시에 방향 상태를 보유하므로, 둘이
   구조적으로 desync될 수 없다. (NVIDIA의 private 회전과 달리.)
2. **drift 감지 & 재적용.** 디스플레이 변경 이벤트(`WM_DISPLAYCHANGE`)마다
   `QueryDisplayConfig`로 실제 방향을 다시 읽어 **저장된 의도 방향과 비교**. 다르면
   `SetDisplayConfig`로 즉시 재적용 → NVIDIA의 덮어쓰기를 되돌려 불변식 복원.
3. **사용자 가이드.** rotate+가 방향의 유일한 권위가 되도록, NVIDIA 앱의 회전 기능은
   사용하지 않게 안내한다 (앱이 시작 시 OS 방향을 재확정해 private 회전을 정리).

> ⚠️ **검증 필요(가정):** 드라이버 버전에 따라 NVIDIA가 스캔아웃을 완전히 private하게
> 돌려 `QueryDisplayConfig`가 여전히 identity로 보고하는 경우가 있을 수 있다. 이 경우
> "방향 비교" 감지가 놓칠 수 있다. → 실제 사용자 NVIDIA 머신에서 반드시 재현/검증하고,
> 필요 시 보조 신호(데스크톱 rect 치수 변화)도 drift 판정에 활용한다. (§8 참고)

## 3. 기술 스택

- **언어/런타임:** C# / .NET 10 (현 LTS; 이 머신에 SDK 10.0.301 설치됨)
- **Win32 바인딩:** **CsWin32** (소스 제너레이터) — 복잡한 `DISPLAYCONFIG_*` 구조체와
  CCD 함수의 타입 안전 P/Invoke 자동 생성. 축 버그 수정의 핵심인 CCD 정밀 제어에 필수.
- **UI:** WinForms `NotifyIcon` (트레이 전용, 메인 창 없음).
- **배포:** self-contained + single-file + trimming → 런타임 없이 exe 1개 (~15MB).
  추후 NativeAOT로 축소 검토.

## 4. 아키텍처 (모듈)

```
rotate+ (트레이 앱, 보이는 창 없음)
├── AppHost          단일 인스턴스(Mutex), 트레이 수명주기, 메시지 펌프
├── DisplayService   CCD 래퍼: 토폴로지 질의, 방향 get/set (Query/SetDisplayConfig)
├── MonitorIdentity  안정적 모니터 식별 (monitorDevicePath 기반)
├── OrientationStore 모니터별 의도 방향 영속화 (JSON, %AppData%\rotate+\config.json)
├── DisplayWatcher   숨은 메시지 창에서 WM_DISPLAYCHANGE / WM_DEVICECHANGE 수신
├── ReapplyController drift 비교 → 재적용 (디바운스 + 자기유발 이벤트 가드)
├── HotkeyManager    RegisterHotKey → 대상 모니터 회전
└── TrayUI           NotifyIcon, 컨텍스트 메뉴(모니터 목록 + 방향 서브메뉴), 알림
```

## 5. 핵심 기술 결정

### 5.1 안정적 모니터 식별 (모니터별 기억의 전제)
모니터 핸들/인덱스는 재연결·드라이버 리셋 시 바뀐다. **변하지 않는 키**가 필요하다.
- `DisplayConfigGetDeviceInfo(GET_TARGET_NAME)` → `DISPLAYCONFIG_TARGET_DEVICE_NAME`
  - `.monitorDevicePath` → **저장 키** (물리 모니터당 안정적)
  - `.monitorFriendlyDeviceName` → 트레이 UI 표시명
- OrientationStore는 `monitorDevicePath → rotation` 맵으로 저장.

### 5.2 메시지 수신
트레이 앱은 숨은 폼의 `WndProc`을 오버라이드해 `WM_DISPLAYCHANGE`, `WM_HOTKEY`,
`WM_DEVICECHANGE`를 받는다. 모니터 도착/제거는 `RegisterDeviceNotification`으로 보강.

### 5.3 재적용 루프 (anti-NVIDIA)
- `WM_DISPLAYCHANGE` 수신 → 짧은 디바운스(예: 400ms, NVIDIA가 이벤트를 연발하므로 합침).
- 각 활성 모니터의 `실제 rotation` vs `저장된 의도`를 비교 → 불일치 시 `SetDisplayConfig`.
- **무한 루프 가드:** 우리의 `SetDisplayConfig`도 `WM_DISPLAYCHANGE`를 유발한다. 직접
  유발한 변경은 타임스탬프/플래그 윈도우로 무시한다.

### 5.4 핫키 기본값 (충돌 주의)
> **주의:** `Ctrl+Alt+화살표`는 Intel/NVIDIA 그래픽 핫키 서비스가 회전용으로 이미
> 가로채는 경우가 많다. 충돌을 피해 기본값을 다음으로 한다.
- 기본: `Ctrl+Alt+Shift+↑/→/↓/←` = 0/90/180/270°. (설정에서 변경 가능)
- 대상 모니터 기본값: **커서 아래 모니터** (가장 직관적). 설정으로 primary/all 선택.

### 5.5 영속화 / 자동시작 / 단일 인스턴스
- 설정: `%AppData%\rotate+\config.json` (방향 맵 + 핫키 + 동작 옵션).
- 자동시작: `HKCU\...\Run` 토글 (트레이 메뉴).
- 단일 인스턴스: 명명된 Mutex.

## 6. v1 범위

**포함:** 핫키 4방향 회전 · 모니터별 방향 기억/복원 · 드라이버 리셋 자동 재적용 ·
트레이에서 모니터 선택 회전 · 자동시작 토글.

**제외(후순위):** 프로파일(작업별 레이아웃 세트) · 핫키 GUI 에디터(초기엔 config.json
직접 편집) · 다국어 · 설치 관리자(초기엔 portable exe).

## 7. CCD 호출 흐름 (구현 참고)

회전 적용:
1. `GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, …)`
2. `QueryDisplayConfig(...)` → paths[], modes[]
3. 대상 path의 `targetInfo.rotation` 설정
   (`DISPLAYCONFIG_ROTATION_IDENTITY/ROTATE90/ROTATE180/ROTATE270` = 1/2/3/4)
4. `SetDisplayConfig(SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_SAVE_TO_DATABASE,
   paths, modes)`

방향 질의(감지): 위 1–2 후 각 path의 `targetInfo.rotation` 읽기.

## 8. 리스크 & 검증 계획

| 리스크 | 대응 |
|--------|------|
| NVIDIA가 private 스캔아웃 회전 → Query가 identity로 보고 (§2.3) | 실제 NVIDIA 머신에서 재현 테스트. drift 판정에 rect 치수 변화 보조 신호 추가 검토. |
| 재적용 무한 루프 | 자기유발 이벤트 타임스탬프 가드 (§5.3) |
| 핫키 충돌 | 비충돌 기본값 + 등록 실패 시 사용자 알림 (§5.4) |
| 모니터 키 불안정 | monitorDevicePath 기반, EDID 폴백 검토 (§5.1) |

**검증 방법:** ① 프로그램적으로 회전 set → query 재확인(왕복 일치). ② NVIDIA 앱에서
수동으로 설정 변경 → 우리 재적용이 발동하고 축이 복원되는지 **실기 확인**. ③ 멀티모니터에서
모니터별 방향 독립 유지 확인.

## 9. 다음 단계

1. 프로젝트 스캐폴딩 (.NET 8 트레이 앱 + CsWin32 설정).
2. `DisplayService` 회전 set/query 왕복 + 콘솔 검증 (TDD: query==set).
3. 트레이 UI + 핫키 → 수동 회전 동작.
4. DisplayWatcher + ReapplyController → 자동 재적용.
5. **실 NVIDIA 머신에서 §8 검증.**
6. 영속화 / 자동시작 / 단일 인스턴스 마감.

## 10. 해결 결과 (실증 완료, 2026-06-16)

§2의 "패널 스캔아웃 desync" 모델은 **수정됨**. 실제 메커니즘과 치유법:

- **진짜 원인:** NVIDIA 앱이 디스플레이 설정을 한 번 건드리면 머신이 **반영구적**
  상태가 된다(재부팅에도 지속). 이 상태에서 **평면 CCD 회전**(`SetDisplayConfig`로 path
  rotation만 변경)은 화면 이미지와 모든 Windows 방향 계층(CCD/GDI/DEVMODE)을 갱신하지만,
  **마우스 커서 좌표 변환은 재구축하지 않는다.** → 화면 방향과 커서 매핑이 어긋나
  마우스 축 오작동 + 도달 불가 데드존 발생.
- **치유법(양방향 실증 완료):** `SetDisplayConfig` 플래그에 **`SDC_FORCE_MODE_ENUMERATION`**
  추가. 모드 재열거를 강제해 회전할 때마다 커서 변환을 재구축 → 버그를 *제거*가 아니라
  매 회전마다 *무력화*. `DisplayService.ApplyRotation`에 반영됨.
- **막다른 길:** 레거시 `ChangeDisplaySettingsEx` 적용은 드라이버가 차단(`CDS_TEST`=0,
  `CDS_UPDATEREGISTRY`=-2 BADMODE). CCD가 유일한 경로. NVIDIA는 리셋 시 CCD path 인덱스/
  주모니터를 재정렬하므로 모니터 키는 `monitorDevicePath`로 고정.
