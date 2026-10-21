# 로컬 구글 로그인 확인

## 구현 범위

`--google-login`은 Windows PC에서 브라우저를 열어 구글 로그인과 ID 토큰 검증을 확인하는 진단용 명령이다. 서버 프로젝트에 포함되어 있지만 AWS에서 실행하는 로그인 API가 아니다.

1. 저장소 외부에 보관한 데스크톱 OAuth JSON 설정을 읽는다.
2. 기본 브라우저를 열고 Authorization Code / PKCE 방식으로 인증한다.
3. 같은 PC의 임시 루프백 주소로 인증 코드를 받는다.
4. Google.Apis.Auth로 ID 토큰의 서명 / 발급자 / 유효기간 / 대상 클라이언트 ID를 검증한다.
5. 검증한 `google_sub`와 이름을 출력한다.

DB 사용자 생성 / 조회, Unity 연동, TCP 게임 세션 인증은 아직 연결하지 않았다. 기존 `--find-user`는 DB 조회용으로 유지한다.

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
DB 저장과 게임 세션 연결은 아직 수행하지 않습니다.
```

`Ctrl+C`로 중단할 수 있고, 5분이 지나면 대기를 종료한다. 브라우저의 인증 코드 수신 안내만으로는 검증 성공을 판단하지 않고 CMD의 검증 완료 출력을 확인한다.

## 직접 확인할 항목

- 빌드 / 기존 서버 테스트
- 올바른 JSON과 테스트 계정으로 로그인했을 때 검증 완료 출력
- 같은 구글 계정으로 다시 로그인하면 동일한 `google_sub` 출력
- 없는 파일 / 잘못된 JSON / 웹 앱 유형 JSON이 실패로 처리되는지 확인
- 브라우저에서 로그인을 취소하거나 CMD에서 Ctrl+C를 눌렀을 때 성공 출력 없이 종료
- 토큰 파일이 만들어지지 않고 ID 토큰 / 액세스 토큰 / 리프레시 토큰 / 클라이언트 시크릿이 출력되지 않는지 확인
- 기존 `--find-user` 조회와 인자 없는 TCP 서버 실행 경로 확인

이번 변경에서는 빌드 / 테스트 / 실제 구글 로그인을 실행하지 않았다. 실제 Unity 로그인과 원격 서버 연결 시에는 서버에서 허용한 클라이언트 ID로 토큰을 검증하고 HTTPS 등 암호화된 경로를 사용해야 한다. 현재의 평문 TCP로 구글 토큰을 보내는 기능은 추가하지 않았다.

## 참고

- [구글 데스크톱 OAuth / PKCE](https://developers.google.com/identity/protocols/oauth2/native-app)
- [Google.Apis.Auth 패키지](https://www.nuget.org/packages/Google.Apis.Auth/1.77.0)
- [ID 토큰 검증 설정](https://docs.cloud.google.com/dotnet/docs/reference/Google.Apis/latest/Google.Apis.Auth.GoogleJsonWebSignature.ValidationSettings)
