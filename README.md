# YuJanggi.Server

## Tech Stack

<p align="center">
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon@latest/icons/csharp/csharp-original.svg" height="40" alt="C#" title="C#" />
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon@latest/icons/dotnetcore/dotnetcore-original.svg" height="40" alt=".NET" title=".NET" />
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon@latest/icons/githubactions/githubactions-original.svg" height="40" alt="GitHub Actions" title="GitHub Actions" />
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon@latest/icons/amazonwebservices/amazonwebservices-original-wordmark.svg" height="40" alt="AWS EC2" title="AWS EC2" />
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon@latest/icons/ubuntu/ubuntu-original.svg" height="40" alt="Ubuntu" title="Ubuntu" />
</p>

<p align="center">
  C# / .NET 10 · TCP / NetworkStream · async / await · CancellationToken · Lock<br />
  Length-Prefixed JSON · NuGet · MSTest<br />
  CI/CD · GitHub Actions · AWS EC2 / Ubuntu · OIDC · SSH / SCP · systemd
</p>

## 서버 소개

AWS EC2의 Ubuntu 환경에 배포하는 .NET 10 기반 1:1 장기 TCP 서버입니다.<br />
Unity 클라이언트의 연결·매칭·대국 메시지 중계·리매치·채팅을 처리합니다.

## 주요 기능

| 기능 | 구현 내용 |
| --- | --- |
| 연결 · Handshake | 비동기 TCP 송수신 · 세션 관리 · Engine / Protocol 버전 확인 |
| 매칭 · 포진 | FIFO 매칭 · 매칭 취소 · 참가자 확정 · 양측 포진 제출 |
| 대국 진행 | 양측 게임 씬 준비 후 시작 · 이동 Response / Event 전달 |
| 대국 종료 | 기권 처리 · 양측 종료 제출 일치 확인 · 종료 Event 전달 |
| 한 수 쉼 · 리매치 | 한 수 쉼 요청 처리 · 양측 리매치 요청 후 확정 Event 전달 |
| 채팅 | 방 참가자에게 메시지 전달 · 공백·20자 초과 검사 · 세션별 1초 전송 간격 제한 |

## 로컬 실행 방법

.NET 10 SDK와 GitHub Packages 읽기 인증을 준비합니다.<br />
[NuGet.Config](NuGet.Config)은 Engine / Protocol 패키지를 GitHub Packages에서 가져옵니다.

로컬 인증은 `NuGetPackageSourceCredentials_github` 환경변수 등 사용하는 환경에 설정한 뒤, 저장소 루트에서 실행합니다.

```bash
dotnet restore YuJanggi.Server.slnx --configfile NuGet.Config
dotnet run --project src/YuJanggi.Server.csproj
```

Unity 클라이언트를 TCP **7777**에 연결합니다.<br />
시작 로그에서 Engine / Protocol 버전을 확인하고, 콘솔의 `Rooms` 명령으로 방과 참가자를 조회합니다.

## 서버 구조

기능별로 메시지 처리, 유스케이스 판단, 컬렉션 관리와 방 상태의 책임을 구분합니다.

| 구성 요소 | 역할 |
| --- | --- |
| Handler | 요청 검증 · Service 호출 · Response / Event 생성·전송 |
| Service | 요청 수락 판단 · 상태 변경 · 기능 흐름 조정 |
| Manager | 세션·매칭·방 컬렉션 생성·조회·제거와 동시성 보호 |
| Domain Object | MatchState / GameRoom의 상태와 전환 조건 관리 |
| Core | Handler·Session 공통 계약과 요청 검증 |
| Transport / Connection | TCP 송수신과 세션 생명주기 관리 |
| Server | 객체 조립 · 메시지 라우팅 · 연결 종료와 서버 종료 시 정리 |

Login / Lobby / Game 단위로 구성하며, 채팅은 Game 내부의 `ChatHandler` / `ChatService`로 분리합니다.<br />
클래스에 공통 상속 계층을 강제하지 않고 각 기능에 필요한 역할만 구현합니다.

## 세션 · 매칭 · GameRoom 생명주기

1. **접속**: 세션을 등록하고 버전 Handshake를 처리합니다.
2. **매칭**: Lobby에서 상대를 찾고 양측 포진을 모아 GameRoom을 생성합니다.
3. **준비 · 시작**: `WaitingForReady`에서 양쪽 게임 씬 준비를 확인한 뒤 `Playing`으로 전환합니다.
4. **종료 · 리매치**: 종료 시 `Ended` 상태로 방을 유지합니다. 양쪽이 리매치를 요청하면 준비 상태를 초기화하고 `WaitingForReady`로 돌아갑니다.
5. **연결 종료**: 세션·매칭 상태와 참가 중인 방을 정리하고, 채팅 전송 간격 기록도 제거합니다.

방 컬렉션 접근과 개별 GameRoom 상태 변경은 각각 Lock으로 보호합니다.<br />
정상 종료 결과는 현재 양측 클라이언트의 제출 일치를 확인하며, 서버 Engine이 독립적으로 계산한 결과는 아닙니다.

## CI/CD · AWS EC2 배포

| 단계 | GitHub Actions 구성 |
| --- | --- |
| [CI](.github/workflows/ci.yml) | main / release 대상 PR에서 Restore → Release Build → MSTest |
| [Release](.github/workflows/release.yml) | 버전 Tag에서 검증 → Linux x64 Publish → 실행 Artifact 업로드 |
| [CD](.github/workflows/cd.yml) | 성공한 Release Run의 Artifact를 받아 AWS EC2에 배포 |

- **실행 결과물**: [PublishServer.sh](scripts/PublishServer.sh)가 framework-dependent Linux x64 결과물을 생성합니다. EC2에는 .NET 10 런타임이 필요합니다.
- **AWS 인증**: GitHub OIDC로 AWS Role을 사용하고, Runner IPv4 `/32`에만 SSH를 임시 허용합니다.
- **서비스 갱신**: [DeployServer.sh](scripts/DeployServer.sh)가 Host Key를 검증하며 SSH / SCP로 배포하고, `yujanggi` systemd 서비스를 재시작·확인합니다.
- **후처리**: 생성한 Security Group Rule ID로 임시 SSH 규칙을 제거합니다. CD에서 Build / Test / Publish를 반복하지 않습니다.

[Ubuntu EC2 운영 문서](docs/systemd-ec2-ubuntu.md) · [systemd 서비스 예시](docs/deploy/systemd/yujanggi.service)

```bash
systemctl status yujanggi
journalctl -u yujanggi
```

## 테스트 · 현재 구현 범위

[YuJanggi.Server.Tests](YuJanggi.Server.Tests)에 Connection / Lobby / Game 단위 MSTest를 작성했습니다.

- **Connection**: 세션 등록·해제와 연결 정리
- **Lobby**: 매칭·취소·포진과 게임 준비 흐름
- **Game**: 이동 메시지 전달, 대국 준비·종료와 리매치 상태 전환

현재 이동 테스트는 메시지 전달 흐름을 대상으로 하며, 장기 규칙의 서버 권위 검증을 의미하지 않습니다.

**구현**: 연결·버전 확인, 매칭·포진, 게임 시작·메시지 중계, 기권·한 수 쉼·리매치, 채팅과 AWS EC2 배포 자동화.

**남은 과제**: Engine 기반 이동·최종 결과 검증, 사용자 인증·DB, 이탈 알림·재접속 복구, 채팅 검증 테스트 확장.

배포 실패 시 자동 롤백과 Runner 강제 종료 시 임시 SSH 규칙 정리는 보완할 영역입니다.

## 관련 저장소 · 문서

- [YuJanggi.Unity](https://github.com/SeokJinYoo98/YuJanggi.Unity) — 게임 입력·화면·클라이언트 네트워크 흐름
- [YuJanggi.Protocol](https://github.com/SeokJinYoo98/YuJanggi.Protocol) — 공용 메시지 계약·직렬화·패킷 프레이밍
- [YuJanggi.Engine](https://github.com/SeokJinYoo98/YuJanggi.Engine) — Unity 독립 장기 규칙·게임 상태. 서버는 현재 공용 타입과 버전을 사용
