# Prototype-005 런타임 아키텍처 인수인계

이 문서는 현재 프로젝트의 플레이어·적·래그돌·Fusion 네트워크 런타임 구조와 이를 안전하게 확장하는 규칙을 설명한다. 코드보다 이 문서가 우선하는 것은 아니다. 구현과 문서가 다르면 아래의 실제 타입과 검증기를 기준으로 확인하고, 같은 변경에서 문서도 갱신한다.

## 1. 핵심 원칙

1. 네트워크 엔티티 프리팹에는 루트에 정확히 하나의 `NetworkObject`만 둔다.
2. `NetworkBehaviour` 코디네이터는 복제 상태, RPC, 네트워크 틱과 권한 경계만 담당한다.
3. 카메라, 애니메이션, 센서, 탐색, 연출처럼 복제 상태가 필요 없는 기능은 자식의 일반 `MonoBehaviour` 서비스로 분리한다.
4. 모든 복제 상태의 최종 변경과 `Spawn`/`Despawn`은 State Authority가 수행한다. 클라이언트 입력은 요청일 뿐 신뢰하지 않는다.
5. 에셋 구조 변경은 명시적 마이그레이션으로만 수행한다. 에디터 로드, 스크립트 리로드, 플레이 모드 진입 같은 암묵적 시점에 프리팹이나 씬을 저장하지 않는다.
6. 직렬화된 필드와 Fusion 상태 레이아웃은 저장 데이터이자 네트워크 프로토콜이다. 이름, 순서, 용량을 임의로 바꾸지 않는다.
7. 매 프레임 전역 검색하지 않는다. 소유자 기준 참조, 직렬화 참조, 초기 1회 탐색 후 캐시, 런타임 레지스트리를 사용한다.

## 2. 엔티티 소유권과 공통 베이스

### 2.1 타입별 책임

| 타입 | 파일 | 책임 |
| --- | --- | --- |
| `IEntityComponent` | `Assets/2.Script/Entity/IEntityComponent.cs` | `Owner`와 `Initialize(GameObject owner)`를 갖는 최소 엔티티 계약 |
| `INetworkEntityComponent` | `Assets/2.Script/Entity/INetworkEntityComponent.cs` | 네트워크 엔티티 컴포넌트를 구분하는 호환 계약. `IEntityComponent`를 상속 |
| `EntityBehaviour` | `Assets/2.Script/Entity/EntityBehaviour.cs` | 일반 `MonoBehaviour`용 소유자 캐시와 `GetEntityComponent<T>()` 제공 |
| `NetworkEntityBehaviour` | `Assets/2.Script/Entity/EntityBehaviour.cs` | `NetworkBehaviour`용 소유자 캐시와 `GetEntityComponent<T>()` 제공 |
| `EntityOwnerResolver` | `Assets/2.Script/Entity/EntityOwnerResolver.cs` | 계층에서 엔티티 소유자 결정 |
| `NetworkEntityRoot` | `Assets/2.Script/Entity/NetworkEntityRoot.cs` | 한 엔티티 아래의 `IEntityComponent` 수집과 초기화 |
| `RuntimeEntityRegistry<T>` | `Assets/2.Script/Entity/RuntimeEntityRegistry.cs` | 활성 엔티티 캐시의 중복 방지, 해제, 파괴된 항목 정리 |

`EntityOwnerResolver.Resolve(Component)`의 소유자 결정 순서는 다음과 같다.

1. 가장 가까운 부모 `NetworkEntityRoot`의 `Owner`
2. 가장 가까운 부모 `NetworkObject`의 GameObject
3. Transform 루트
4. 위 항목이 없으면 해당 컴포넌트의 GameObject

따라서 서비스가 자식으로 이동해도 같은 `NetworkEntityRoot` 아래에 있으면 루트 엔티티를 소유자로 받는다. 반대로 중간에 다른 `NetworkEntityRoot`나 `NetworkObject`를 넣으면 소유권 경계가 바뀐다.

### 2.2 `NetworkEntityRoot` 초기화

`NetworkEntityRoot`는 실행 순서가 `-1000`이며 기본 설정에서 `Awake`에 초기화하고 `Start`에도 다시 초기화한다. `autoCollectComponents`가 켜져 있거나 수동 배열이 비어 있으면 비활성 자식까지 포함해 `MonoBehaviour`를 수집하고, 그중 `IEntityComponent`만 초기화한다.

- `AllComponents`: 모든 `IEntityComponent`
- `Components`: 기존 API 호환을 위해 `INetworkEntityComponent`만 노출
- `ownerOverride`: 비어 있으면 프리팹 루트가 유효한 암묵적 소유자다. 표준 프리팹은 감사가 쉽도록 루트를 명시적으로 연결한다.
- `autoCollectComponents`를 끄면 `componentBehaviours`에 모든 엔티티 컴포넌트를 빠짐없이 넣어야 한다.

현재 전환은 점진적이다. `NetworkInventory`와 `NetworkPlayerHidingComponent`는 `NetworkEntityBehaviour`를 상속하지만, `PlayerMovement`, `NetworkHealthComponent`, `RagdollEntityComponent`, `CSHEnemy` 등 일부 기존 타입은 아직 `INetworkEntityComponent`를 직접 구현한다. 새 코드에서 불필요한 대규모 상속 변경을 다시 만들지 말고, 수정 범위에서 공통 베이스가 실제 중복을 줄일 때 적용한다.

### 2.3 런타임 레지스트리

`PlayerRuntimeRegistry`와 `EnemyRuntimeRegistry`는 각각 활성 `PlayerMovement`, `CSHEnemy`를 보관한다. 등록은 각 엔티티의 생성/해제 생명주기에서 수행하며, Subsystem Registration 때 목록을 초기화한다. 여러 플레이어나 적을 열거할 때 `FindObjectsByType`를 반복하는 대신 이 목록을 사용한다.

레지스트리는 검색 비용을 줄이는 캐시이지 권한이나 생존 상태의 진실 원천은 아니다. 반환 항목의 `Object`, 권한, 사망 상태는 사용하는 시스템에서 다시 확인한다.

## 3. 플레이어 프리팹 구조

### 3.1 네트워크 플레이어 표준 구조

`Assets/4.Prefabs/Network/NetworkPlayer.prefab`의 표준 저장 구조는 다음과 같다. `ProductionArchitectureMigration`이 이 구조와 직렬화 참조를 만든다.

```text
NetworkPlayer                         # 유일한 NetworkObject, NetworkEntityRoot
├─ Simulation
│  ├─ Locomotion                     # PlayerStamina
│  ├─ Inventory                      # NetworkInventory, PlayerInventoryInput
│  ├─ Hiding                         # NetworkPlayerHidingComponent
│  ├─ Emotes                         # NetworkEmoteAudioPlayer
│  └─ PlayerEntityComponents         # Health, Death, Ragdoll, Blood 등
│     └─ RagdollServices
│        ├─ RigPresentation           # RagdollRigPresentation
│        └─ SurfaceBlood              # RagdollSurfaceBloodController
├─ Presentation
│  ├─ PlayerPresentationComponents   # 아래의 presentation/bridge 컴포넌트
│  ├─ CameraComponent
│  └─ DeadCamera                     # DeadCameraController
├─ Sensors
│  ├─ GroundChecker
│  └─ ItemChecker
└─ Rig
   ├─ Visual
   └─ HeldItemPoseTargets
```

루트에는 `PlayerMovement`, `NetworkCharacterController`와 플레이어 단위 네트워크 코디네이터가 유지된다. 자식에 있는 `NetworkBehaviour`도 모두 루트의 유일한 `NetworkObject`에 속한다. 계층 정리를 이유로 자식 `NetworkObject`를 추가하지 않는다.

`Assets/4.Prefabs/Player.prefab`은 같은 `Simulation`/`Presentation`/`Sensors`/`Rig` 원칙을 쓰는 로컬 프리팹이다. 루트 이름은 `Player`이며 `NetworkObject`가 없어야 하고, 카메라는 `Presentation/Camera`에 둔다. 공유 스크립트가 있더라도 이 프리팹을 Fusion 스폰 프리팹으로 취급하지 않는다.

### 3.2 플레이어 런타임 역할

`PlayerMovement`는 이동 코디네이터다. `FixedUpdateNetwork`에서 `NetworkPlayerInput`을 소비하고 `NetworkCharacterController`로 이동하며 `PlayerStamina.Tick`을 호출한다. 카메라와 애니메이션 구현은 다음 컴포넌트에 위임한다.

| 컴포넌트 | 역할 |
| --- | --- |
| `PlayerStamina` | 스태미나 도메인 상태, 스냅샷과 변경 이벤트, 틱 처리 |
| `PlayerCameraPresentation` | 로컬 권한 카메라 선택, FOV, AudioListener와 Cinemachine 연동 |
| `PlayerAnimationPresentation` | 1인칭/3인칭 Animator와 로컬 시각 표시 |
| `PlayerNetworkPowerBridge` | `PlayerMovement`의 기존 RPC 표면과 로컬 도메인 기능 사이의 전송 어댑터 |
| `NetworkInventory` | 슬롯, 수량, 장착 아이템의 복제 상태와 서버 처리 |
| `PlayerInventoryInput` | 드롭/슬롯 입력을 읽어 `NetworkInventory`에 전달하는 로컬 입력 컴포넌트 |
| `InventoryHeldItemPresentation` | 장착 아이템의 시각 복제본 생성, 포즈 배치, 게임플레이/네트워크 컴포넌트 제거 |
| `NetworkPlayerHidingComponent` | 숨기 상태와 전환의 네트워크 코디네이터 |
| `PlayerHidingPresentation` | 숨기 중 비주얼 부모, 카메라, 물리 표시 전환 |

기존 프리팹과 직렬화 데이터의 호환을 위해 `PlayerMovement`에는 일부 카메라/애니메이션 필드와 RPC가 남아 있다. `EnsureSupportComponents()`는 서비스가 없을 때 자식에서 찾고, 그래도 없으면 실행 중인 인스턴스에 추가해 기존 프리팹을 살린다. `NetworkInventory`와 숨기 시스템도 유사한 폴백을 갖는다.

이 폴백은 저장 구조가 아니다. 새 프리팹은 위 계층에 컴포넌트를 명시적으로 두고 직렬화 참조를 연결해야 한다. 런타임 `AddComponent`는 프리팹 에셋을 저장하지 않으며, 누락된 구성을 영구적으로 고치는 수단으로 사용하지 않는다.

## 4. CSHEnemy 구조

`Assets/4.Prefabs/Network/NetworkCSHEnemy.prefab`의 표준 구조는 다음과 같다.

```text
NetworkCSHEnemy                      # 유일한 NetworkObject, NetworkEntityRoot
├─ Services
│  ├─ Perception                    # EnemyPerceptionComponent
│  ├─ Navigation                    # EnemyNavigationComponent
│  └─ Combat                        # EnemyCombatComponent
├─ Presentation                    # EnemyAnimationDriver
│  └─ HeadmanSound
└─ Rig
   └─ HeadmanVisual
```

`CSHEnemy`는 루트에 남는 네트워크 코디네이터다. 상태 머신, 복제 상태, 공격/처치 시퀀스와 외부 공개 API를 유지하고 세부 동작은 다음 서비스에 위임한다.

- `EnemyPerceptionComponent`: 가시 대상, 거리/시야 판정, 대상 claim, 숨기·사망 대상 제외
- `EnemyNavigationComponent`: NavMesh 이동, 순찰·추적·조사, 적 간 분리, 문 파괴 이동 처리
- `EnemyCombatComponent`: 충돌, 피해, 처치, 넉백과 처치 애니메이션 처리
- `EnemyAnimationDriver`: 표현 애니메이션 처리

`CSHEnemy.EnsureServiceComponents()`는 기존 프리팹 호환을 위해 자식에서 서비스를 찾고 누락 시 런타임 인스턴스에 추가한다. 기존 코디네이터의 직렬화 설정은 `ConfigureLegacyComponents()`와 각 서비스의 legacy 설정 구조를 통해 전달된다. 표준 프리팹에서는 `ProductionArchitectureMigration`이 서비스를 만들고 `perceptionComponent`, `navigationComponent`, `combatComponent`, `animationDriver` 참조를 연결한다.

`CSHEnemy`는 `EnemyRuntimeRegistry`에 등록된다. 다른 시스템이 모든 적을 대상으로 작업할 때 이 레지스트리를 사용하고, 틱마다 씬 전체를 검색하지 않는다.

## 5. 래그돌 구조

`RagdollEntityComponent`는 루트 엔티티 상태와 Fusion 전송을 조정하는 코디네이터다. 사망/부활, 넉백, 네트워크 시퀀스와 프록시 포즈 적용을 조율하며, 리그 탐색과 표면 피 연출은 아래 서비스로 분리되어 있다.

| 컴포넌트 | 표준 위치 | 책임 |
| --- | --- | --- |
| `RagdollEntityComponent` | `Simulation/PlayerEntityComponents` | 복제 상태, RPC, 사망/부활/넉백 시퀀스 조정 |
| `RagdollRigPresentation` | `RagdollServices/RigPresentation` | 리그/파트 탐색, Animator·CharacterController 표시, 속도·충격, 포즈 캡처와 프록시 적용 |
| `RagdollSurfaceBloodController` | `RagdollServices/SurfaceBlood` | 표면 접촉/재진입 검사, 표면 및 사망 피 연출 |
| `RagdollPartComponent` | 물리 파트 | 각 Rigidbody/Collider 캐시와 파트 단위 제어 |

코디네이터는 기존 직렬화 필드와 Networked 상태 레이아웃을 유지한다. 서비스 참조가 없으면 소유자 자식에서 찾고, 그래도 없으면 런타임 인스턴스에 추가한 뒤 기존 설정을 전달한다. 이 역시 마이그레이션 기간의 호환 폴백이며 표준 프리팹을 대신하지 않는다.

래그돌 리그 안에 별도 `NetworkObject`를 넣지 않는다. 프록시 포즈는 루트 네트워크 엔티티의 상태와 시퀀스를 통해 적용된다.

## 6. NetworkGameManager와 서비스

`NetworkGameManager`는 `Assets/2.Script/Manager/NetworkGameManager.cs`의 호환 facade다. 씬이 이미 저장하고 있는 다음 필드 이름과 공개 API를 유지한다.

- `playerPrefab`, `enemyPrefab`
- `playerSpawnPoints`, `enemySpawnPoints`
- `sessionName`, `enemySpawnDifficulty`
- `Runner`, `IsServer`, `EnemyPrefab`, `SpawnEnemyNear`
- `HostMigrationRequested`, `SessionEnded`

실제 책임은 다음 클래스로 분리되어 있다.

| 서비스 | 책임 |
| --- | --- |
| `NetworkSessionService` | `NetworkRunner` 생성/재사용, 콜백 등록·해제, 세션 시작/종료, 실패 정리 |
| `NetworkPlayerSpawnService` | 서버의 플레이어 스폰/디스폰, `PlayerRef`와 PlayerObject 연결, 플레이어 수 이벤트 |
| `NetworkEnemySpawnDirector` | 서버의 목표 적 수 조정과 근처 적 스폰 |
| `NetworkInputProvider` | 레거시 Input 축/버튼을 `NetworkPlayerInput`으로 수집 |
| `NetworkVoiceRuntimeInstaller` | Recorder, Fusion Voice 및 프로젝트 음성 런타임 컴포넌트 설치/비활성화/실패 정리 |
| `NetworkRunnerCallbacksAdapter` | 서비스가 필요한 Fusion 콜백만 재정의하도록 하는 빈 어댑터 |

### 6.1 세션 생명주기

`NetworkGameManager.Awake`는 싱글턴을 확정하고 `DontDestroyOnLoad` 후 네트워크 시작을 요청한다. `NetworkSessionService`는 같은 GameObject에서 `NetworkRunner`, `NetworkSceneManagerDefault`, `NetworkObjectProviderDefault`를 재사용하거나 런타임에 추가하고 `Runner.ProvideInput = true`로 설정한다.

시작 모드는 현재 `GameMode.AutoHostOrClient`이며 `sessionName`을 사용한다. 활성 씬의 build index가 유효할 때 그 씬을 `NetworkSceneInfo`에 Single 모드로 넣는다. 서비스 자신, 플레이어 스폰, 입력, 존재하는 경우 `FusionVoiceClient`를 Runner 콜백으로 등록하고 종료나 시작 실패 때 해제한다.

호스트 마이그레이션은 구현 완료 기능이 아니다. `NetworkSessionService.OnHostMigration`이 `HostMigrationRequested` 이벤트를 올리는 확장 지점만 제공한다. 새 Runner 생성, 토큰 재시작, 상태 복구, 재스폰 정책은 별도로 구현해야 한다. 현재 코드는 로컬 상태를 호스트 마이그레이션처럼 위장하지 않는다.

### 6.2 스폰 지점

씬에 직렬화된 `playerSpawnPoints`와 `enemySpawnPoints`가 현재 기본 경로이며, 검증기는 두 목록이 비어 있지 않은지 검사한다. 서비스는 유효하고 아직 사용하지 않은 레거시 Transform을 먼저 선택한다.

`NetworkSpawnPoint`는 동적/추가 씬용 보조 마커다.

- `kind`: `Player` 또는 `Enemy`
- `priority`: 큰 값이 먼저 선택됨
- `weight`: 같은 최상위 priority 그룹 안의 가중치. 0이면 사용하지 않음
- 활성 상태이고 로드된 유효 씬에 속한 마커만 후보가 됨
- 한 주기에는 같은 지점을 재사용하지 않고, 레거시와 마커 후보가 모두 소진되면 사용 기록을 초기화함
- 직렬화 목록과 같은 Transform인 마커는 중복 후보에서 제거됨

현재 `NetworkSpawnPoint.Collect`는 스폰 지점을 고를 때 씬의 마커를 `FindObjectsByType`로 수집하고 결정적 순서로 정렬한다. 이는 스폰 시점의 제한된 검색 예외다. 스폰 빈도가 커지면 씬 로드/언로드 이벤트 기반 레지스트리로 교체하고, 이 검색을 프레임 루프에 호출하지 않는다.

플레이어는 서버가 스폰하고 `runner.SetPlayerObject`로 소유자를 연결하며, 퇴장 시 연결을 지우고 디스폰한다. 적 목표 수는 현재 난이도에 따라 Easy `ceil(player * 0.5)` 최소 1, Normal `player`, Hard `ceil(player * 1.5)`, Hardcore `player * 2`다. 플레이어가 없으면 0이다.

## 7. Fusion 권한과 RPC 보안 규칙

### 7.1 권한별 책임

| 주체 | 허용 책임 | 금지 사항 |
| --- | --- | --- |
| State Authority | Networked 상태 확정, Spawn/Despawn, 요청 검증, 결과 전파 | 클라이언트가 보낸 위치·회전·피해량을 검증 없이 적용 |
| Input Authority | 입력 수집, 예측 가능한 입력 전송, 서버에 변경 요청 | Networked 상태의 최종값 직접 변경, 다른 플레이어를 대신한 요청 |
| Proxy | 보간, 애니메이션, 오디오와 시각 연출 | 게임플레이 상태 변경 RPC의 일반 발신자 역할 |

새 클라이언트 발신 RPC는 가장 좁은 `RpcSources`를 사용한다. 플레이어 자신의 기능이면 일반적으로 `InputAuthority -> StateAuthority`, 월드 상호작용처럼 대상의 프록시에서 보내야 하면 `Proxies -> StateAuthority`를 사용한다. 단순히 호출이 편하다는 이유로 `RpcSources.All`을 선택하지 않는다.

### 7.2 공통 서버 검증

`ServerRequestValidator`와 `ServerRequestValidationPolicy`는 현재 다음을 검사한다.

1. Runner가 실행 중인지
2. 대상 `NetworkObject`가 유효하고 현재 State Authority인지
3. `RpcInfo.Source`가 실제 플레이어인지
4. 요청자의 PlayerObject가 존재하고 요청자 권한과 일치하는지
5. Runner/요청자/대상/기능 scope 단위 토큰 버킷 rate limit
6. 서버가 다시 계산한 거리
7. 정책이 요구하면 서버 Physics 기준 line of sight
8. owner 요청이면 요청자의 PlayerObject와 대상이 같은지

기본 상호작용 정책은 거리 5m, 초당 8회, burst 2이며 LOS는 꺼져 있다. 기본 owner 요청 정책은 거리 2m, 초당 6회, burst 2이며 LOS는 꺼져 있다. 필요한 상호작용은 프리팹에서 LOS와 LayerMask를 명시적으로 켠다.

서버 내부 호출은 `RpcInfo.Source == PlayerRef.None`이고 `allowServerRequest`가 켜진 경우 플레이어 거리/rate 검사를 건너뛸 수 있다. 이것은 신뢰된 서버 경로에만 사용하며, 피해량 범위나 상태 전이 같은 도메인 불변식은 서버 코드에서 별도로 검사한다. float와 Vector 입력에는 `ServerRequestValidator.IsFinite`를 사용하고 범위를 제한한다.

새 요청의 기본 형태는 다음과 같다.

```csharp
[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
private void RPC_RequestAction(float amount, RpcInfo info = default)
{
    if (!ServerRequestValidator.IsFinite(amount) || amount <= 0f)
        return;

    if (!ServerRequestValidator.TryValidateOwnerRequest(
            Runner, Object, transform, info, requestPolicy, RequestScope,
            out _, out _, allowServerRequest: false))
        return;

    ApplyOnStateAuthority(Mathf.Min(amount, MaxAllowedAmount));
}
```

월드 상호작용은 `TryValidate`를 사용하고, 요청자가 보낸 Transform이나 최종 회전을 그대로 신뢰하지 말고 `ServerRequestContext.PlayerObject`와 서버 월드 상태에서 결과를 계산한다.

### 7.3 현재 적용 범위와 남은 범위

현재 공통 검증기가 직접 적용된 코드는 다음과 같다.

- `Door`: 열기 요청 검증, 클라이언트 요청의 열림 방향을 서버에서 재계산. 클라이언트 잠금/파괴 요청은 기본적으로 비활성화됨.
- `CabinetSingle`: 열기 요청 검증.
- `NetworkHealthComponent`: Input Authority owner 요청 검증, 유한하고 양수인 피해만 허용. 클라이언트 부활 요청은 기본적으로 비활성화됨.

모든 기존 RPC가 이 수준으로 통합된 것은 아니다. `RagdollEntityComponent`에는 아직 `RpcSources.All` 요청이 있고, 인벤토리·아이템 사용·숨기·이모트·아이템 홀더는 각자의 권한/거리 검사만 사용하거나 공통 rate/LOS 검증을 사용하지 않는 경로가 있다. 기존 동작을 수정할 때 다음 순서로 좁힌다.

1. 누가 요청할 수 있는지 결정하고 `RpcSources` 축소
2. `RpcInfo.Source`와 PlayerObject 소유권 확인
3. 거리, LOS, rate, 유한값과 상한 검증
4. State Authority에서 상태 전이 가능 여부를 재검사
5. 잘못된 요청을 조용히 거절하고 필요한 경우 진단 로그/지표 추가

현재 보안 적용 범위를 프로젝트 전체가 완전히 강화된 것으로 간주하지 않는다. 특히 숨기 진입, 인벤토리 상호작용, 래그돌 요청을 외부 입력 표면으로 변경할 때는 공통 검증 적용 여부를 코드 리뷰 항목으로 둔다.

## 8. 새 기능 추가 절차

### 8.1 기존 엔티티에 기능 추가

1. 로컬 전용인지 복제 상태가 필요한지 먼저 결정한다.
2. 복제 상태/RPC/네트워크 틱만 기존 코디네이터 또는 작은 `NetworkBehaviour`에 둔다.
3. 연출·센서·도메인 계산은 일반 `MonoBehaviour`로 만들고 알맞은 `Simulation`, `Presentation`, `Sensors`, `Rig`, `Services` 자식에 둔다.
4. 소유자 접근이 필요하면 `EntityBehaviour` 또는 `NetworkEntityBehaviour`를 사용하거나 기존 호환 계약인 `IEntityComponent`를 구현한다.
5. `NetworkEntityRoot.InitializeComponents()`로 초기화되고 비활성 상태에서도 참조가 수집되는지 확인한다.
6. 직렬화 참조를 기본 경로로 연결한다. 런타임 전역 검색이나 `AddComponent`를 새 기능의 정상 구성 경로로 만들지 않는다.
7. 클라이언트 요청이 있으면 7장의 권한/검증 규칙을 적용한다.
8. 프리팹 구조를 바꾸면 마이그레이션과 `ProjectArchitectureValidator` 검사를 함께 갱신한다.

Presentation 서비스는 Networked 상태를 직접 쓰지 않는다. 코디네이터가 읽기 전용 스냅샷이나 명시적 메서드로 값을 넘긴다. 반대로 코디네이터가 카메라, Renderer, Animator, AudioSource를 직접 찾고 제어하는 범위를 다시 늘리지 않는다.

### 8.2 새 네트워크 엔티티 추가

1. 프리팹 루트에 `NetworkObject`와 `NetworkEntityRoot`를 각각 하나만 둔다.
2. 네트워크 코디네이터를 루트 또는 그 유일한 NetworkObject 하위에 둔다. 자식 `NetworkObject`는 만들지 않는다.
3. 서버 스폰 주체, Input Authority 할당, State Authority 생명주기와 디스폰 책임자를 문서화한다.
4. 서비스 계층을 만들고 직렬화 참조를 연결한다. 소유자 탐색은 `EntityOwnerResolver` 규칙을 따른다.
5. Fusion prefab table에 명시적으로 등록하고 reimport/codegen 후 `NetworkObject.NetworkedBehaviours` 등록 상태를 확인한다.
6. 전역 열거가 필요하면 생성/해제 생명주기에 등록하는 전용 `RuntimeEntityRegistry<T>` wrapper를 만든다.
7. 검증기에 루트 이름, 필수 계층, 필수 컴포넌트, 단일 NetworkObject와 핵심 직렬화 참조 검사를 추가한다.
8. 호스트, 클라이언트, 프록시, late join, 퇴장/재접속, 스폰/디스폰, 씬 전환을 실제로 시험한다.

로컬 전용 엔티티에는 `NetworkObject`를 붙이지 않는다. 나중에 네트워크화할 가능성만으로 NetworkObject 경계를 미리 중첩시키지 않는다.

### 8.3 직렬화와 Fusion 상태 호환

- 기존 `.cs`의 `.meta` GUID를 보존한다. 타입을 분리할 때 기존 코디네이터 파일과 클래스 정체성을 유지하고 서비스를 새 파일로 추가한다.
- 기존 `[SerializeField]` 이름을 바꿔야 하면 `[FormerlySerializedAs]` 또는 명시적 에디터 마이그레이션을 사용한다.
- Networked 프로퍼티의 순서, 타입, 배열 용량을 임의로 바꾸지 않는다. 변경이 필요하면 동일 빌드 강제, prefab rebake/codegen, 혼합 버전 접속 차단 정책까지 함께 처리한다.
- `NetworkBehaviour`를 추가·삭제·이동한 뒤 Fusion의 serialized behaviour table과 prefab table을 재생성하고 검증한다.
- RPC 시그니처와 authority 방향 변경은 프로토콜 변경이다. 서로 다른 클라이언트 빌드의 호환을 가정하지 않는다.

## 9. 명시적 마이그레이션

### 9.1 에디터 실행

Unity 메뉴에서 다음을 실행한다.

```text
Tools/Prototype005/Architecture/Migrate Prefabs
```

진입점은 `ProductionArchitectureMigration.Run`이며 직접 여는 대상은 다음 세 프리팹이다.

- `Assets/4.Prefabs/Player.prefab`
- `Assets/4.Prefabs/Network/NetworkPlayer.prefab`
- `Assets/4.Prefabs/Network/NetworkCSHEnemy.prefab`

도구는 플레이어와 적의 표준 계층을 만들고 컴포넌트를 이동하며 참조를 교체한 후 저장한다. 마지막에 Fusion `NetworkProjectConfigUtilities.RebuildPrefabTable()`을 호출하므로 Fusion 설정 에셋도 diff가 생길 수 있다.

실행 전 Unity의 미저장 씬/프리팹을 정리하고 Git 작업 트리를 확인한다. 도구가 `AssetDatabase.SaveAssets()`를 호출하므로 무관한 dirty 에셋을 함께 저장하지 않도록 한다. 실행 후 세 프리팹, Fusion 설정, 예상하지 않은 `.meta`/씬 변경을 반드시 `git diff`로 검토한다.

### 9.2 배치모드 실행

프로젝트 Unity 버전은 `ProjectSettings/ProjectVersion.txt` 기준 `6000.0.67f1`이다. Windows/PowerShell 예시는 다음과 같다.

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.0.67f1\Editor\Unity.exe'
& $unity `
  -batchmode -quit `
  -projectPath (Get-Location).Path `
  -executeMethod ProductionArchitectureMigration.RunFromCommandLine `
  -logFile -
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

같은 프로젝트를 연 Unity Editor가 있으면 닫고 실행한다. 마이그레이션은 쓰기 작업이므로 CI의 일반 검증 단계에서 자동 실행하지 않는다. 구조 변경을 의도한 별도 작업에서 실행하고 결과를 리뷰·커밋한다.

## 10. 읽기 전용 검증과 빌드

### 10.1 아키텍처 검증기

에디터 메뉴:

```text
Tools/Prototype005/Architecture/Validate Project (Read Only)
```

배치모드:

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.0.67f1\Editor\Unity.exe'
& $unity `
  -batchmode -quit `
  -projectPath (Get-Location).Path `
  -executeMethod ProjectArchitectureValidator.ValidateFromCommandLine `
  -logFile -
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

`ProjectArchitectureValidator`는 프리팹·씬을 검사만 하며 serialized property 적용, dirty 표시, 저장, 마이그레이션 호출을 하지 않는다. 주요 검사 범위는 다음과 같다.

- 로컬/네트워크 플레이어와 네트워크 적의 루트 이름, 표준 계층, 필수 컴포넌트와 핵심 직렬화 참조
- 네트워크 프리팹의 루트 `NetworkObject` 정확히 하나, 자식 NetworkObject 금지, NetworkBehaviour 소유 경계
- Fusion serialized `NetworkedBehaviours`의 null, 중복, 외부 참조와 누락 가능성
- 루트 `NetworkEntityRoot` 수, 활성 상태, owner, 자동/수동 초기화 구성
- `Assets/4.Prefabs`와 `Assets/1.Scenes`의 missing MonoScript
- `NetworkHidingSpot.spotId`의 양수/중복/효과적 ID 충돌과 additive scene 위험
- `Assets/1.Scenes/CSHObunga/CSHObunga.unity`의 `NetworkGameManager` 수와 활성 상태, prefab 참조, session name, 레거시 스폰 목록
- 같은 씬의 Runner 수/호스트 위치와 런타임 설치되는 scene manager/object provider

로그 코드는 `ARCH-*` 형식이다. 오류가 하나라도 있으면 배치 진입점이 예외를 던져 실패한다. 경고만 있으면 로그는 남지만 실패하지 않는다. 특히 Fusion behaviour table 누락 경고는 Unity 재임포트/codegen 후 다시 확인한다.

검증기는 플레이 모드를 실행하지 않고 패킷, 권한 전환, RPC 악용, NavMesh 동작, 카메라/음성 장치와 late join을 시험하지 않는다. 읽기 전용 구조 검증을 통과해도 아래 수동 네트워크 검증이 필요하다.

### 10.2 C# 빌드

Unity에서 스크립트 임포트와 프로젝트 파일 재생성이 끝난 뒤 프로젝트 루트에서 실행한다.

```powershell
dotnet build .\Prototype-005.sln -v minimal
```

Unity가 생성하는 `.csproj`가 새 `.cs`를 아직 포함하지 않을 수 있다. 이 경우 `.csproj`를 수동 편집해 해결하지 말고 Unity를 열거나 위 배치 검증을 실행해 임포트한 뒤 프로젝트 파일을 재생성한다. 최종 컴파일의 기준은 Unity Editor import/compile 결과이며, `dotnet build`는 빠른 통합 확인으로 함께 사용한다.

### 10.3 최소 플레이 검증 행렬

구조나 네트워크 코드를 바꾼 작업은 최소한 다음을 확인한다.

- Host 1명 시작, Client 1명 참가, 두 PlayerObject의 authority가 올바른지
- 두 플레이어가 서로 다른 스폰 지점을 사용하고 퇴장 시 서버가 디스폰하는지
- 플레이어 수에 따라 적 수가 증가·감소하고 서버만 적을 스폰/디스폰하는지
- late join 클라이언트가 체력, 사망/래그돌, 인벤토리, 숨기, 적 상태를 일관되게 보는지
- Input Authority만 자신의 이동/인벤토리 요청을 보내고 Proxy는 표현만 수행하는지
- 비정상 거리, 과도한 요청, NaN/Infinity 입력이 서버 상태를 바꾸지 않는지
- 씬 종료/실패/재시작 시 Runner 콜백, 음성 컴포넌트, 런타임 레지스트리가 중복되지 않는지

호스트 마이그레이션은 현재 미구현이므로 성공 항목으로 체크하지 않는다. 이벤트가 발생했을 때 확장 지점이 호출되는지까지만 현재 범위다.

## 11. 금지 규칙

### 자동 에셋 변경 금지

- `[InitializeOnLoad]`, `AssetPostprocessor`, 스크립트 리로드 콜백, 플레이 모드 상태 콜백에서 프리팹/씬/Fusion 설정을 자동 저장하지 않는다.
- 검증과 수정은 분리한다. 검증기는 읽기 전용, 수정은 사용자가 명시적으로 실행하는 마이그레이션이어야 한다.
- 런타임 호환용 `AddComponent` 결과를 에디터 에셋에 되써 저장하지 않는다.

### 네트워크 경계 위반 금지

- 엔티티 자식에 nested `NetworkObject`를 추가하지 않는다.
- Input Authority나 Proxy에서 Networked 상태의 최종값을 직접 쓰지 않는다.
- 편의를 위해 새 변경 요청을 `RpcSources.All`로 열지 않는다.
- 하나의 게임 세션에 중복 `NetworkRunner`나 중복 callback 등록 경로를 만들지 않는다.
- Fusion prefab/behaviour table을 구조 변경 후 재생성하지 않은 채 완료로 처리하지 않는다.

### 전역 검색 남용 금지

- `Update`, `FixedUpdateNetwork`, `Render`, 상호작용 폴링에서 `FindObject*`, `FindObjectsByType`, `GameObject.Find`, 전체 `GetComponentsInChildren`을 반복하지 않는다.
- 플레이어/적 열거는 `PlayerRuntimeRegistry`, `EnemyRuntimeRegistry`를 사용한다.
- 엔티티 내부 참조는 `Owner`, 직렬화 참조, 초기 1회 자식 탐색 후 캐시 순서로 해결한다.
- `Camera.main`과 Cinemachine 전역 검색은 현재 presentation의 호환 경로가 일부 남아 있다. 새 시스템은 로컬 플레이어 카메라 참조를 주입하고 캐시한다.
- `NetworkSpawnPoint.Collect`의 스폰 시점 검색은 현재 제한된 예외다. 프레임 루프에서 호출하지 않는다.

### 직렬화·책임 재집중 금지

- `.meta` GUID, 직렬화 필드 이름, Networked 필드 순서/용량을 마이그레이션 없이 바꾸지 않는다.
- 이동 코디네이터에 카메라·애니메이션·인벤토리·숨기·래그돌 구현을 다시 합치지 않는다.
- 서비스가 임의로 다른 엔티티의 내부 컴포넌트를 전역 검색해 결합하지 않는다.
- 런타임 폴백이 있으므로 프리팹 구성이 없어도 된다고 판단하지 않는다. 표준 구조와 validator 통과가 완료 조건이다.

## 12. 현재 알려진 경계

- `NetworkSessionService`는 AutoHostOrClient 시작/종료를 관리하지만 로비, 인증 정책, 재접속, 실제 호스트 마이그레이션 복구는 제공하지 않는다.
- 공통 `ServerRequestValidator` 적용은 `Door`, `CabinetSingle`, `NetworkHealthComponent`부터 시작한 상태이며 모든 기존 RPC에 적용된 것은 아니다.
- `NetworkSpawnPoint`는 레거시 직렬화 스폰 목록을 대체하지 않고 보완한다. 현재 검증기는 레거시 목록을 요구한다.
- 일부 기존 컴포넌트는 새 공통 베이스 대신 인터페이스를 직접 구현하고, 일부 presentation은 호환을 위해 전역 카메라 탐색을 사용한다.
- 런타임 서비스 자동 추가는 오래된 프리팹을 구동하기 위한 안전망이다. 명시적 프리팹 구성, read-only validator와 실제 멀티플레이 검증이 최종 기준이다.

이 경계 중 하나를 해결하면 코드, 프리팹 마이그레이션, validator, 이 문서를 같은 변경 단위로 갱신한다.
