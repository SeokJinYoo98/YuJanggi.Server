<h3 align="center">Tech Stack</h3>

<p align="center">
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon@latest/icons/csharp/csharp-original.svg" height="40" alt="C#" title="C#" />
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon@latest/icons/dotnetcore/dotnetcore-original.svg" height="40" alt=".NET" title=".NET" />
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon@latest/icons/githubactions/githubactions-original.svg" height="40" alt="GitHub Actions" title="GitHub Actions" />
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon@latest/icons/amazonwebservices/amazonwebservices-original-wordmark.svg" height="40" alt="AWS EC2" title="AWS EC2" />
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon@latest/icons/ubuntu/ubuntu-original.svg" height="40" alt="Ubuntu" title="Ubuntu" />
</p>

<p align="center">
  .NET 10 · TCP / NetworkStream · MSTest<br />
  AWS OIDC · SSH / SCP · systemd
</p>


# YuJanggi.Server

.NET 10 기반 1:1 장기 네트워크 서버입니다. TCP 연결·매칭·게임 메시지 처리를 담당하며, <br>
GitHub Actions에서 검증한 Linux 실행 결과물을 AWS EC2에 배포합니다.

## Highlights

- Session·매칭·GameRoom의 상태와 생명주기 관리
- Login / Lobby / Game을 기능 흐름 단위로 구성
- 공통 계약·연결 관리·TCP 송수신 책임 분리
- PR 검증과 Tag 기반 Publish·EC2 배포 자동화

## Getting Started

.NET 10 SDK와 GitHub Packages 읽기 인증을 준비합니다.

[NuGet.Config](NuGet.Config)은 Protocol / Engine 패키지를 GitHub Packages에서 가져옵니다.<br>
로컬 인증은 `NuGetPackageSourceCredentials_github` 환경변수 등 사용하는 환경에서 설정하며, 저장소 루트에서 실행합니다.

```bash
dotnet restore YuJanggi.Server.slnx --configfile NuGet.Config
dotnet run --project src/YuJanggi.Server.csproj
```

Unity 클라이언트를 서버의 TCP **7777**에 연결합니다.<br>콘솔의 `Rooms`는 Room과 참가자를 조회하고, 시작 로그는 Engine / Protocol 버전을 표시합니다.

EC2에서는 .NET 10 런타임과 `yujanggi` systemd 서비스를 사용합니다.<br>
[서비스 파일 예시](docs/deploy/systemd/yujanggi.service)의 계정·DLL·배포 경로를 실제 환경에 맞게 설정합니다.<br>
상태와 로그는 `systemctl status yujanggi`, `journalctl -u yujanggi`로 확인합니다.

## Features

- **Login**: Engine / Protocol 버전 Handshake. 사용자 인증을 추가할 별도 영역으로 구성
- **Lobby**: FIFO 매칭·취소·참가자 확정, 양측 포진 제출 후 GameRoom 생성과 GameReady 알림
- **Game**: 양측 씬 준비 후 시작, 이동 Response / Event 전달, 양측 종료 제출 일치 확인과 Room 정리

현재 이동은 요청을 승인해 참가자에게 전달합니다.<br>
Engine 기반 서버 이동 검증·자체 결과 계산과 실제 사용자 인증은 아직 구현하지 않았습니다.

## Architecture

Features는 `Handler → Service → Manager → Domain Object`의 책임 구분을 따릅니다. <br>
공통 상속 계층으로 강제하지 않으며, 각 기능에서 실제 필요한 역할을 구현합니다.

| 계층 | 책임 |
| --- | --- |
| Handler | Protocol 요청 검증, Service 호출, Response / Event 생성·전송 |
| Service | 유스케이스 판단과 상태 변경·도메인 전환 조정 |
| Manager | 컬렉션 생성·등록·조회·제거와 동시성 보호 |
| Domain Object | MatchState / GameRoom 등 상태의 원본과 무결성 유지 |

- **Core**: Handler·Session 계약과 공통 요청 검증
- **Transport / Connection**: TCP byte 송수신과 Session 생명주기를 분리
- **Server**: 도메인 조립·라우팅과 Disconnect / shutdown 정리 순서 조정

Lobby는 양측 포진을 모아 GameRoom을 한 번 생성합니다.<br>
생성 이후 Room 생명주기는 Game이 담당합니다. <br>
정상 GameEnd는 Response → Event → Room 종료·제거 순서이며, 종료 확정 후 전송이 실패해도 `finally`에서 Room을 정리합니다.

## CI/CD

- **[Server CI](.github/workflows/ci.yml)**: `main` / `release/**` 대상 PR → Restore → Release Build → Test
- **[Server Release](.github/workflows/release.yml)**: `v*.*.*` Tag Push → 검증 → Linux Publish → `yujanggi-server-linux-x64` Artifact 업로드
- **[Server CD](.github/workflows/cd.yml)**: 동일 Repository의 성공한 Release Push 실행 → 해당 Run ID의 Artifact 다운로드 → EC2 배포

[PublishServer.sh](scripts/PublishServer.sh)는 `linux-x64` / framework-dependent 결과물을 `artifacts/publish/linux-x64`에 생성합니다.


Release는 Artifact 생성만 담당하며 GitHub Release를 만들지 않으며, <br>
CI / Release의 NuGet 인증은 `GITHUB_TOKEN`을 사용합니다.<br>
그리고 CD는 GitHub OIDC로 AWS Role을 사용하고 Runner IPv4 `/32`에만 SSH를 임시 허용합니다.


[DeployServer.sh](scripts/DeployServer.sh)가 Host Key 검증을 유지한 SSH / SCP 배포와 `yujanggi` 재시작·active 확인을 수행합니다.

CD는 Build / Test / Publish를 다시 실행하지 않습니다.<br>
임시 SSH 규칙은 생성한 Rule ID로 성공·실패 후 정리합니다.<br>
Runner 강제 종료 시 정리가 보장되지는 않으며, 배포 실패 시 자동 롤백은 구현하지 않았습니다.

MSTest는 Connection / Lobby / Game별로 연결 정리, 매칭·포진·전송 순서, 게임 준비·종료·Room 정리를 검증합니다. <br>
Move 테스트는 현재 메시지 전달 흐름을 검증합니다.

## Project Structure

```text
src/
├─ Core/                  # 서버 공통 계약과 검증
├─ Transport/Tcp/         # 실제 TCP 송수신
├─ Connection/            # Session 생명주기
├─ Features/
│  ├─ Login/              # Handshake와 인증 연결 준비
│  ├─ Lobby/              # 매칭과 게임 시작 준비
│  └─ Game/               # 게임 메시지와 Room 상태
├─ Server/                # 실행·라우팅·도메인 조정
└─ View/                  # 콘솔 출력
YuJanggi.Server.Tests/     # Connection / Lobby / Game / TestSupport
.github/workflows/        # CI / Release / CD
scripts/                  # Linux Publish와 SSH 배포
docs/                     # systemd 문서와 서비스 예시
```

## Related Projects

- [YuJanggi.Unity](https://github.com/SeokJinYoo98/YuJanggi.Unity) — 게임 입력·화면과 Client 네트워크 흐름
- [YuJanggi.Protocol](https://github.com/SeokJinYoo98/YuJanggi.Protocol) — 공유 메시지 계약, JSON 직렬화와 Framing
- [YuJanggi.Engine](https://github.com/SeokJinYoo98/YuJanggi.Engine) — 장기 규칙·게임 상태. 서버는 현재 공용 타입과 버전을 사용
