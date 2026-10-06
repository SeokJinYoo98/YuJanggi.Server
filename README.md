# YuJanggi.Server.V2

.NET 10 기반 1:1 장기 매칭 서버입니다. TCP 연결부터 매칭, 포진 확정, 양측의 게임 화면 준비와 시작 알림까지 처리합니다.

## 프로젝트 개요

- 클라이언트별 연결·Handshake 상태 관리
- 매칭 대기열과 초·한 참가자 확정
- 양측 포진 접수 후 `GameRoom` 생성
- `YuJanggi.Protocol` 메시지와 `YuJanggi.Engine` 공용 타입·버전 사용

## 기술 스택

- C# / .NET 10
- TCP Socket
- async / await
- YuJanggi.Protocol
- YuJanggi.Engine

## 서버 구조

```text
YuJanggiServer → ClientSession
              → ProtocolHandshakeHandler
              → MatchingHandler → MatchMakingService
              → GameHandler → GameService → GameRoomManager → GameRoom
```

## 네트워크 흐름

```text
HandshakeRequest → ProtocolHandshake
MatchingRequest  → MatchingResponse → MatchingFound
FormationSubmit  → FormationSubmitResponse → GameReady
GameSceneReadyRequest (양쪽) → GameStartEvent
```

`GameReady`는 포진 확정 알림입니다. 실제 시작 알림인 `GameStartEvent`는 양쪽의 화면 준비가 끝나면 전송합니다.

## 주요 기능

- TCP 클라이언트 수락과 세션별 메시지 수신
- Protocol·Engine 버전 Handshake
- 매치메이킹 신청·취소 및 참가자 배정
- 포진 접수와 양측 확정 포진 전달
- `GameRoom`의 준비·시작·종료 상태 관리
- 연결 종료 시 매칭·룸 정리

현재 서버는 게임 시작 알림까지 처리합니다. `MovePieceRequest`의 서버 측 기물 이동 처리는 등록되어 있지 않습니다.

## 주요 코드

| 구성 요소 | 역할 |
| --- | --- |
| [`YuJanggiServer`](src/Server/YuJanggiServer.cs) | 연결 수락과 메시지 핸들러 분배 |
| [`ClientSession`](src/ClientSession/ClientSession.cs) | 클라이언트 연결과 송수신 |
| [`ProtocolHandshakeHandler`](src/Handlers/ProtocolHandshakeHandler.cs) | 버전 확인 |
| [`MatchingHandler`](src/Handlers/MatchingHandler.cs) | 매칭·포진 요청 처리와 이벤트 전송 |
| [`MatchMakingService`](src/Matching/MatchMakingService.cs) | 대기열·매치·포진 상태 관리 |
| [`GameRoom`](src/GameRoom/GameRoom.cs) | 양쪽 준비와 시작 상태 관리 |
| [`GameHandler`](src/GameRoom/Handler/GameHandler.cs) | 게임 화면 준비 요청 처리 |

## 실행 방법

1. .NET 10 SDK와 형제 폴더의 `YuJanggi.Engine`, `YuJanggi.Protocol` NuGet 패키지를 준비합니다.
2. [`NuGet.Config`](NuGet.Config)의 로컬 패키지 경로를 확인합니다.
3. 서버 저장소에서 `dotnet run --project src/YuJanggi.Server.V2.csproj`을 실행합니다.
4. Unity 클라이언트를 서버의 TCP 포트 `7777`에 연결합니다.

## 관련 프로젝트

- `YuJanggi.Unity`: 장기 클라이언트
- `YuJanggi.Engine`: 공용 장기 타입과 규칙
- `YuJanggi.Protocol`: 통신 메시지와 DTO

## 포트폴리오

[매칭과 게임 시작 흐름의 상세 설계를 소개할 때, 포트폴리오 링크]
