# YuJanggi.Server

TCP 기반 1:1 온라인 장기 게임 서버입니다.

## 프로젝트 개요

- .NET 기반 TCP 게임 서버
- Unity 클라이언트와 실시간 통신
- 클라이언트 세션 관리
- 매치메이킹 및 매칭 확정
- 포진 선택 처리
- GameRoom 생성 및 관리
- YuJanggi.Engine 기반 게임 진행
- 서버 권위형 게임 상태 관리

## 기술 스택

- C#
- .NET 10
- TCP Socket
- async / await
- CancellationToken
- YuJanggi.Engine
- YuJanggi.Protocol
- xUnit
- GitHub Actions

## 서버 구조

    Client
        ↓
    ClientSession
        ↓
    Message Dispatcher
        ↓
    Handler
        ↓
    Service
        ↓
    GameRoomManager
        ↓
    GameRoom
        ↓
    YuJanggi.Engine

## 네트워크 흐름

    Client Connect
        ↓
    Handshake
        ↓
    MatchingRequest
        ↓
    MatchingResponse
        ↓
    ConfirmMatch
        ↓
    MatchingFound
        ↓
    FormationSubmit
        ↓
    GameRoom 생성
        ↓
    GameReady
        ↓
    Game Start

## 주요 기능

- TCP 클라이언트 연결 및 세션 관리
- Handshake 처리
- Request / Response 메시지 처리
- 매치메이킹 신청 및 취소
- 매칭 확정
- 포진 선택 및 제출 처리
- GameRoom 생성 / 조회 / 제거
- 게임 엔진 생성 및 생명주기 관리
- 게임 명령 검증 및 처리
- 게임 상태 클라이언트 전송
- 연결 종료 처리

## 주요 코드

| 구성 요소 | 역할 |
| --- | --- |
| `ClientSession` | 클라이언트 연결 및 메시지 송수신 |
| `MessageDispatcher` | 수신 메시지에 따른 Handler 분배 |
| `MatchingHandler` | 매칭 관련 메시지 처리 |
| `MatchMakingService` | 매칭 큐 및 매칭 상태 관리 |
| `GameHandler` | 게임 관련 메시지 처리 |
| `GameRoomManager` | GameRoom 생성 / 조회 / 제거 |
| `GameRoom` | 게임 진행 및 Engine 생명주기 관리 |

## 실행 방법

### 요구 사항

- .NET 10 SDK
- YuJanggi.Engine
- YuJanggi.Protocol

### 실행

    dotnet restore
    dotnet run

## 관련 프로젝트

- [YuJanggi.Unity](링크)
- [YuJanggi.Engine](링크)
- [YuJanggi.Protocol](링크)

## 포트폴리오

- [YuJanggi 포트폴리오](노션 링크)