# EC2 Ubuntu에서 systemd로 실행

이 문서는 YuJanggi.Server를 `yujanggi` 서비스로 등록하는 절차다. 실제 EC2 접속·등록은 수행하지 않았으며, 저장소의 설정은 예시다.

## 확인한 실행 산출물

- 프로젝트: `src/YuJanggi.Server.csproj`, 대상 프레임워크 `net10.0`. 새 publish의 실행 DLL 이름은 프로젝트명에 따른 `YuJanggi.Server.dll`이다.
- 배포에는 현재 프로젝트를 publish한 `YuJanggi.Server.dll`, `.deps.json`, `.runtimeconfig.json`, Engine·Protocol DLL 등 산출물 전체를 사용한다. 이전 산출물의 DLL 이름만 수동 변경하지 않는다.
- runtimeconfig는 `Microsoft.NETCore.App` `10.0.0`을 요구한다. 호환되는 .NET 10 런타임이 필요하다.
- 기존 publish 산출물의 존재만 확인했으며, 현재 dev 소스를 새로 publish하거나 산출물의 최신 여부를 검증하지 않았다.

DLL 하나만 옮기지 말고 배포할 publish 산출물 전체를 배포 폴더에 둔다. 대상 EC2의 CPU 아키텍처에 맞는 산출물을 사용한다.

## 등록 전 예시 값 변경

서비스 파일은 `deploy/systemd/yujanggi.service`다. 다음 값은 실제 EC2 설정을 확인한 뒤 변경한다.

| 항목 | 예시 값 | 확인할 내용 |
|---|---|---|
| User | `ubuntu` | 실제 실행 계정 및 배포 파일 접근 권한 |
| WorkingDirectory | `/home/ubuntu/yujanggi` | publish 파일들을 배치한 실제 폴더 |
| ExecStart | `/usr/bin/dotnet /home/ubuntu/yujanggi/YuJanggi.Server.dll` | dotnet 실행 파일과 서버 DLL의 절대 경로 |

EC2에서 `command -v dotnet`과 `dotnet --list-runtimes`로 실행 파일 위치와 런타임을 확인한다. WorkingDirectory와 ExecStart의 DLL 경로는 같은 배포 폴더를 가리키도록 수정한다. 이 경로는 로컬 저장소의 `publish/` 경로와는 별개다.

기존 서버를 직접 실행 중이라면 먼저 종료해 동일한 TCP 포트를 동시에 사용하지 않도록 한다. 현재 서버는 TCP 7777을 사용하며, 서비스 등록 자체는 EC2 Security Group이나 방화벽 설정을 변경하지 않는다.

## 서비스 등록과 부팅 자동 실행

수정한 서비스 파일을 EC2에 준비한 뒤, 해당 파일이 있는 폴더에서 실행한다.

```bash
sudo cp yujanggi.service /etc/systemd/system/yujanggi.service
sudo systemctl daemon-reload
sudo systemctl enable yujanggi
sudo systemctl start yujanggi
sudo systemctl status yujanggi
```

`enable`은 부팅 시 자동 실행을 등록하고, `start`는 지금 실행한다. 서비스 파일을 다시 변경했다면 복사와 `daemon-reload`를 다시 수행한 뒤 `restart`한다.

## 실행 관리

```bash
sudo systemctl start yujanggi
sudo systemctl stop yujanggi
sudo systemctl restart yujanggi
sudo systemctl status yujanggi
```

`Restart=on-failure`는 비정상 종료 시 재시작을 요청하고, `RestartSec=5`는 재시작 전 5초를 기다린다. 정상 종료에는 자동 재시작하지 않으며, `systemctl stop`으로 중지한 경우에도 자동 재시작하지 않는다. 반복 실패 시 systemd의 시작 제한에 걸릴 수 있으므로 로그에서 원인을 확인한다.

현재 서버 코드에 systemd 종료 신호를 이용한 게임·세션의 정상 종료 처리는 이번 작업에서 추가하지 않았다. 서비스 중지·재시작은 진행 중인 연결과 게임을 끊을 수 있다.

## 로그 확인

```bash
sudo journalctl -u yujanggi
sudo journalctl -u yujanggi -f
```

표준 출력과 표준 오류를 journal로 전달하도록 명시했다. journal 로그의 보존 기간과 재부팅 후 유지 여부는 호스트의 journald 설정을 따른다.

`StandardInput=null`이므로 SSH 터미널의 입력은 서비스로 전달되지 않는다. 현재 `Program.ReadCommandsAsync`는 표준 입력이 종료되면 명령 읽기를 끝내고 서버 작업을 기다린다. 따라서 콘솔의 `Rooms`·`Clear` 명령은 이 운영 방식에서 사용할 수 없으며, 서비스 관리는 `systemctl`, 로그 조회는 `journalctl`을 사용한다.

## 확인 범위

서비스 파일·등록 문서만 추가했다. Server 실행 로직, Docker, GitHub Actions, 배포 자동화는 변경하지 않았다. 빌드·테스트·publish·EC2 접속·실제 systemd 등록은 수행하지 않았다. 실제 Ubuntu 호스트에서 등록 후 상태와 시작 로그, 부팅 자동 실행을 확인해야 한다.
