# PostgreSQL 사용자 조회

## 구현 범위

Npgsql로 `public.users`를 조회한다. `--find-user`는 로컬 관리용 명령이며 TCP 수신 포트를 열지 않는다. 데이터 조회만 수행하며 테이블 생성이나 사용자 추가는 실행하지 않는다.

구글 인증과 게임 세션 연결은 아직 구현하지 않았다. 조회에 성공했다는 사실은 구글 계정 인증을 의미하지 않는다. 실제 로그인에서는 서버에서 ID 토큰을 검증한 뒤 확인한 `sub`를 사용해야 한다.

## 테이블 준비

이미 같은 구조로 테이블을 만들었다면 다음 SQL을 다시 실행할 필요가 없다.

```sql
CREATE TABLE public.users (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    google_sub text NOT NULL UNIQUE,
    nickname text NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
);
```

## 로컬 실행

psql에 접속 중이라면 `\q`로 종료한 뒤 CMD에서 실행한다. 서버 저장소의 `release/*` 브랜치에서 진행한다.

```cmd
cd /d D:\Git\YuJanggi\YuJanggi.Server
set "YUJANGGI_DATABASE_CONNECTION_STRING=Host=localhost;Port=5432;Database=yujanggi;Username=postgres;Password=본인비밀번호"
dotnet run --project src\YuJanggi.Server.csproj -- --find-user test-google-001
```

`본인비밀번호`는 로컬 PostgreSQL에 설정한 값으로 바꾼다. 연결 문자열을 소스 파일이나 Git에 저장하지 않는다. 위 `set`은 현재 CMD와 그 CMD에서 실행하는 프로그램에만 적용된다.

테스트 사용자 `test-google-001`을 미리 저장했다면 해당 사용자의 ID / 구글 계정 식별자 / 닉네임 / 생성 시각이 출력된다. `created_at`은 UTC 오프셋 `+00:00`으로 읽히므로 psql에서 보던 `+09:00` 표시와 시각 표기가 다를 수 있다.

조회 완료 후 환경 변수를 지우려면 다음을 실행한다.

```cmd
set "YUJANGGI_DATABASE_CONNECTION_STRING="
```

인자 없이 실행하면 기존 TCP 게임 서버가 실행된다. 현재 대국 기능은 DB를 사용하지 않으며, DB 환경 변수가 없어도 기존 실행 경로는 유지된다.

## 직접 확인할 항목

- 프로젝트 빌드와 기존 서버 테스트 실행
- 기존 `google_sub` 조회 시 psql에서 확인한 ID와 일치하는지 확인
- 없는 `google_sub` 조회 시 미조회 안내와 종료 코드 `2` 확인
- `--find-user` 조회가 계정 생성 / 닉네임 변경 / 로그인 세션 등록을 하지 않는지 확인
- 인자 없이 실행했을 때 기존 서버의 대국 기능 확인

이번 변경에서는 빌드 / 테스트 / 실제 DB 접속을 실행하지 않았다.

## 참고

- [Npgsql 기본 사용법](https://www.npgsql.org/doc/basic-usage.html)
- [PostgreSQL 타입과 .NET 타입 매핑](https://www.npgsql.org/doc/types/basic.html)
