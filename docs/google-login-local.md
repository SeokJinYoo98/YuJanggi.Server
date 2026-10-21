# 로컬 구글 로그인 확인

## 구현 범위

`--google-login`은 Windows PC에서 브라우저를 열어 구글 로그인과 ID 토큰 검증을 확인하는 진단용 명령이다. 서버 프로젝트에 포함되어 있지만 AWS에서 실행하는 로그인 API가 아니다.

1. 저장소 외부에 보관한 데스크톱 OAuth JSON 설정을 읽는다.
2. 기본 브라우저를 열고 Authorization Code / PKCE 방식으로 인증한다.
3. 같은 PC의 임시 루프백 주소로 인증 코드를 받는다.
4. Google.Apis.Auth로 ID 토큰의 서명 / 발급자 / 유효기간 / 대상 클라이언트 ID를 검증한다.
5. 검증한 `google_sub`와 이름을 출력한다.
6. `--save-user` 옵션이 있으면 해당 계정의 DB 사용자를 생성하거나 기존 사용자를 조회한다.

Unity 연동과 TCP 게임 세션 인증은 아직 연결하지 않았다. 기존 `--find-user`는 DB 조회용으로 유지한다.

## 준비

- Google Cloud 프로젝트에서 Google 인증 플랫폼 설정 완료
- 대상 사용자를 외부로 설정하고 로그인할 계정을 테스트 사용자에 추가
- 데스크톱 앱 유형으로 `YuJanggi Windows` OAuth 클라이언트 생성
- 다운로드한 JSON을 `D:` 드라이브의 개인 보관 폴더 등 저장소 외부에 보관

JSON 내용이나 토큰을 소스 코드에 복사하지 않는다. 이 명령은 JSON 설정을 읽기만 하며, 받은 토큰을 파일이나 콘솔에 저장하지 않는다. 토큰 저장소를 지정하지 않아 다음 실행에서도 브라우저 인증 흐름을 진행한다. 구글 계정의 기존 브라우저 로그인 상태에 따라 계정 선택 화면은 생략될 수 있다.

## 실행

서버 저장소의 `release/*` 브랜치에서 CMD로 실행한다. 아래 JSON 경로는 예시이므로 본인이 저장한 실제 폴더와 파일명으로 바꾼다.

```cmd
cd /d D:\Git\YuJanggi\YuJanggi.Server
dotnet run --project src\YuJanggi.Server.csproj -- --google-login "D:\Secure\다운로드한파일.json"
```

명령에 지정할 값은 JSON 파일의 경로다. JSON 내용이나 클라이언트 시크릿을 직접 입력하지 않는다. 이 단계에는 DB 연결 문자열이 필요하지 않다.

브라우저에서 테스트 사용자로 등록한 계정으로 로그인하고 동의한다. 브라우저에 인증 코드를 받았다는 안내가 나오면 CMD에서 토큰 검증 결과를 확인한다.

성공하면 다음 형태로 출력된다. 아래는 실제 계정 정보가 아닌 출력 형식이다.

```text
구글 로그인 / ID 토큰 검증 완료
google_sub: <구글 계정 식별자>
name: <구글 프로필 이름>
DB 사용자를 생성 / 조회하려면 --save-user 옵션을 추가해 주세요.
```

`Ctrl+C`로 중단할 수 있고, 5분이 지나면 대기를 종료한다. 브라우저의 인증 코드 수신 안내만으로는 검증 성공을 판단하지 않고 CMD의 검증 완료 출력을 확인한다.

## DB 사용자 생성 / 조회

먼저 [PostgreSQL 사용자 조회](postgresql-user-lookup.md)의 `public.users` 테이블을 준비한다. 이미 생성했다면 다시 만들 필요가 없다.

같은 CMD 창에서 DB 연결 문자열을 설정하고 `--save-user`를 붙여 실행한다. 비밀번호와 JSON 경로는 본인 환경의 값으로 바꾼다.

```cmd
cd /d D:\Git\YuJanggi\YuJanggi.Server
set "YUJANGGI_DATABASE_CONNECTION_STRING=Host=localhost;Port=5432;Database=yujanggi;Username=postgres;Password=본인비밀번호"
dotnet run --project src\YuJanggi.Server.csproj -- --google-login "D:\Secure\다운로드한파일.json" --save-user
```

ID 토큰을 검증한 뒤 확인한 `sub`와 프로필 이름으로 사용자를 생성한다. 프로필 이름이 비어 있으면 최초 닉네임은 `플레이어`다. `google_sub`가 이미 존재하면 새 행을 만들지 않고 기존 사용자를 조회한다. 기존 `id` / `nickname` / `created_at`은 변경하지 않는다.

생성에는 매개변수 SQL과 `ON CONFLICT (google_sub) DO NOTHING RETURNING ...`을 사용한다. 충돌 시 INSERT의 reader를 닫은 뒤 별도 SELECT로 조회한다. 따라서 같은 계정으로 동시에 생성 요청을 보내도 UNIQUE 제약에 따라 한 행만 생성된다.

인증 완료 출력 이후 아래 형태의 DB 처리 결과를 확인한다.

```text
DB 사용자 생성 / 조회 완료
id: <DB 사용자 UUID>
nickname: <저장된 닉네임>
created_at: <최초 생성 시각>
게임 세션 연결은 아직 수행하지 않습니다.
```

같은 계정으로 명령을 다시 실행했을 때 `id`와 `created_at`이 동일한지 확인한다. psql에서는 다음 쿼리로 확인할 수 있다. `확인한_google_sub`는 로그인 결과의 값으로 바꾼다.

```sql
SELECT id, google_sub, nickname, created_at
FROM public.users
WHERE google_sub = '확인한_google_sub';
```

조회 결과는 한 행이어야 한다. DB 비밀번호나 연결 문자열은 공유하지 않는다. 테스트를 마친 뒤 현재 CMD의 연결 문자열은 다음으로 지울 수 있다.

```cmd
set "YUJANGGI_DATABASE_CONNECTION_STRING="
```

DB 작업이 중단되거나 통신이 끊긴 경우에는 INSERT가 이미 반영됐을 수 있다. 동일 계정으로 다시 실행하면 중복 행을 만들지 않고 기존 사용자를 반환한다.

## 직접 확인할 항목

- 빌드 / 기존 서버 테스트
- 올바른 JSON과 테스트 계정으로 로그인했을 때 검증 완료 출력
- 같은 구글 계정으로 다시 로그인하면 동일한 `google_sub` 출력
- `--save-user`로 첫 로그인 / 재로그인 시 같은 DB 사용자 ID와 생성 시각 반환 / 한 행만 저장
- 기존 사용자의 닉네임을 바꾸지 않고 반환하는지 확인
- 두 CMD에서 같은 계정으로 동시에 실행했을 때 중복 생성 없이 같은 ID 반환
- 인증 실패 / 취소 시 DB 사용자 생성 없음 / `--save-user` 없이 실행하면 DB 변경 없음
- DB 연결 문자열 누락 / 비밀번호 오류 / PostgreSQL 중지 시 실패 안내 확인
- 없는 파일 / 잘못된 JSON / 웹 앱 유형 JSON이 실패로 처리되는지 확인
- 브라우저에서 로그인을 취소하거나 CMD에서 Ctrl+C를 눌렀을 때 성공 출력 없이 종료
- 토큰 파일이 만들어지지 않고 ID 토큰 / 액세스 토큰 / 리프레시 토큰 / 클라이언트 시크릿이 출력되지 않는지 확인
- 기존 `--find-user` 조회와 인자 없는 TCP 서버 실행 경로 확인

사용자가 이전 단계에서 로컬 구글 로그인과 ID 토큰 검증 결과를 확인했다. 이번 DB 연결 변경 이후의 빌드 / 테스트 / 실제 로그인과 DB 생성은 실행하지 않았다. 실제 Unity 로그인과 원격 서버 연결 시에는 서버에서 허용한 클라이언트 ID로 토큰을 검증하고 HTTPS 등 암호화된 경로를 사용해야 한다. 현재의 평문 TCP로 구글 토큰을 보내는 기능은 추가하지 않았다.

## 참고

- [구글 데스크톱 OAuth / PKCE](https://developers.google.com/identity/protocols/oauth2/native-app)
- [Google.Apis.Auth 패키지](https://www.nuget.org/packages/Google.Apis.Auth/1.77.0)
- [ID 토큰 검증 설정](https://docs.cloud.google.com/dotnet/docs/reference/Google.Apis/latest/Google.Apis.Auth.GoogleJsonWebSignature.ValidationSettings)
- [PostgreSQL INSERT / ON CONFLICT / RETURNING](https://www.postgresql.org/docs/18/sql-insert.html)
