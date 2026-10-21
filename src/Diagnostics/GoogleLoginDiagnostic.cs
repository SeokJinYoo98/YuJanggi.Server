using Google.Apis.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using System.Text.Json;

namespace YuJanggi.Server.Diagnostics
{
    internal static class GoogleLoginDiagnostic
    {
        public static async Task<int> RunAsync(string[] args)
        {
            if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
            {
                Console.Error.WriteLine("사용법: --google-login <데스크톱 OAuth JSON 파일 경로>");
                return 1;
            }

            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("이 명령은 브라우저를 사용할 수 있는 로컬 Windows PC에서 실행해 주세요.");
                return 1;
            }

            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            ConsoleCancelEventHandler cancelHandler = (_, e) =>
            {
                e.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;

            try
            {
                ClientSecrets secrets = await ReadClientSecretsAsync(args[1], cancellation.Token);
                using var flow = new PkceGoogleAuthorizationCodeFlow(
                    new GoogleAuthorizationCodeFlow.Initializer
                    {
                        ClientSecrets = secrets,
                        Scopes = new[] { "openid", "profile", "email" },
                        DataStore = null
                    });

                var login = new AuthorizationCodeInstalledApp(flow, new LocalServerCodeReceiver());
                Console.WriteLine("브라우저에서 테스트 사용자로 등록한 구글 계정으로 로그인해 주세요.");
                Console.WriteLine("5분 동안 기다립니다. 중단하려면 Ctrl+C를 누르세요.");
                UserCredential credential = await login.AuthorizeAsync("local-diagnostic", cancellation.Token);

                if (string.IsNullOrWhiteSpace(credential.Token.IdToken))
                {
                    Console.Error.WriteLine("ID 토큰을 받지 못했습니다. OAuth 설정과 로그인 권한을 확인해 주세요.");
                    return 1;
                }

                var settings = new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { secrets.ClientId }
                };
                GoogleJsonWebSignature.Payload identity = await GoogleJsonWebSignature
                    .ValidateAsync(credential.Token.IdToken, settings)
                    .WaitAsync(cancellation.Token);

                if (string.IsNullOrWhiteSpace(identity.Subject))
                {
                    Console.Error.WriteLine("검증된 ID 토큰에 구글 계정 식별자가 없습니다.");
                    return 1;
                }

                Console.WriteLine("구글 로그인 / ID 토큰 검증 완료");
                Console.WriteLine($"google_sub: {identity.Subject}");
                Console.WriteLine($"name: {identity.Name}");
                Console.WriteLine("DB 저장과 게임 세션 연결은 아직 수행하지 않습니다.");
                return 0;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("로그인을 중단했거나 대기 시간이 지났습니다.");
                return 2;
            }
            catch (InvalidJwtException)
            {
                Console.Error.WriteLine("ID 토큰 검증에 실패했습니다. 클라이언트 ID와 PC의 시간을 확인해 주세요.");
                return 1;
            }
            catch (TokenResponseException)
            {
                Console.Error.WriteLine("구글 인증이 완료되지 않았습니다. 브라우저의 오류와 테스트 사용자 설정을 확인해 주세요.");
                return 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"구글 로그인 확인 실패 ({exception.GetType().Name}). JSON 경로 / 파일 형식 / 네트워크 연결을 확인해 주세요.");
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }

        private static async Task<ClientSecrets> ReadClientSecretsAsync(
            string path,
            CancellationToken cancellationToken)
        {
            using var stream = File.OpenRead(path);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("installed", out JsonElement installed))
                throw new InvalidDataException("데스크톱 앱 유형의 OAuth JSON 파일이 필요합니다.");

            string? clientId = installed.GetProperty("client_id").GetString();
            string? clientSecret = installed.GetProperty("client_secret").GetString();
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                throw new InvalidDataException("OAuth 클라이언트 설정이 비어 있습니다.");

            return new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret };
        }
    }
}
