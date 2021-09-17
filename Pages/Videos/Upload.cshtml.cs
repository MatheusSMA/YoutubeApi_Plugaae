using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;
using Google.Apis.YouTube.v3;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Hosting;
using System.Text;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using System.IO;
using System.Text.Json;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.YouTube.v3.Data;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using System.Reflection;
using Google.Apis.Upload;
using System.Timers;

namespace YoutubeApiTest.Pages.Videos
{
    public class UploadModel : PageModel
    {
        private ILogger<UploadModel> _logger;
        private IHttpClientFactory clientFactory;
        private IWebHostEnvironment hostingEnvironment;
        const string TOKEN_FILE = "youtube_token.json";
        /*teste pq estava dando erro de datetime*/ private const string V = "2030-12-10";
        private string _tempFilePath;

        [BindProperty]
        public VideoUploadModel VideoUpload { get; set; }


        public UploadModel(ILogger<UploadModel> logger, IHttpClientFactory clientFactory, IWebHostEnvironment hostingEnvironment)
        {
            _logger = logger;
            this.clientFactory = clientFactory;
            this.hostingEnvironment = hostingEnvironment;
        }

        public async void OnPostAsync() //Método de apoio
        {
            var video = GetVideoData(VideoUpload.Title, VideoUpload.Description);

            var user = OAuthClientInfo.LoadClientSecretsInfo();

            if (!System.IO.File.Exists(TOKEN_FILE))
            {
                throw new SystemException("Está faltando o arquivo de token");
            }

            var tokenResponse = await FetchToken(user);

            var youTubeService = FetchYouTubeService(tokenResponse, user.ClientId, user.ClientSecret);

            using (var fileStream = new FileStream(_tempFilePath, FileMode.Open))
            {
                var videosInsertRequest = youTubeService.Videos.Insert(video, "snippet, status", fileStream, "video/*");
                videosInsertRequest.ProgressChanged += VideoUploadProgressChanged;
                videosInsertRequest.ResponseReceived += VideoUploadResponseReceived;

                await videosInsertRequest.UploadAsync();
            }
        }

        private static Video GetVideoData(string title, string description)
        {
            var video = new Video()
            {
                Status = new VideoStatus
                {
                    PrivacyStatus = "private", //mudar para publico para mudar o status para publico...
                    SelfDeclaredMadeForKids = false,
                    //PublishAt = "10-10-2030"  //só pode ser setado se estiver privaro (TESTAR AINDA, COM ERROS DE DATETIME)
                },
                Snippet = new VideoSnippet
                {
                    CategoryId = "28", // Importante para implementar, VER DEPOIS https://developers.google.com/youtube/v3/docs/videoCategories/list
                    Title = title,
                    Description = description,
                    Tags = new string[] { "Tag1", "Tag 2", "Tag3" },
                }
            };
            return video;
        }

        #region async's de Tasks: FetchToken, IsValid, RefreshToken.
        private async Task<TokenResponse> FetchToken(OAuthUserCredential user)
        {
            var token = JsonSerializer.Deserialize<Token>(System.IO.File.ReadAllText(TOKEN_FILE));

            var isValid = await IsValid(token.AccessToken);
            if (!isValid)
            {
                token = await RefreshToken(user, token.RefreshToken);
            }

            var tokenResponse = new TokenResponse
            {
                AccessToken = token.AccessToken,
                RefreshToken = token.RefreshToken,
                Scope = token.Scope,
                TokenType = token.TokenType
            };

            return tokenResponse;
        }

        private async Task<bool> IsValid(string accessToken)//Validando o token
        {
            const string TOKEN_INFO_URL = "https://www.googleapis.com/oauth2/v3/tokeninfo?access_token=";

            var url = $"{TOKEN_INFO_URL}{accessToken}";
            var response = await clientFactory.CreateClient().GetAsync(url);
            var jsonString = await response.Content.ReadAsStringAsync();

            return !jsonString.Contains("error_description");
        }

        private async Task<Token> RefreshToken(OAuthUserCredential user, string refreshToken)// Atualiza o token
        {
            Token token = null;

            var payload = new Dictionary<string, string>
        {
          { "client_id" , user.ClientId } ,
          { "client_secret" , user.ClientSecret } ,
          { "refresh_token" , refreshToken } ,
          { "grant_type" , "refresh_token" }
        };

            var content = new FormUrlEncodedContent(payload);

            var client = clientFactory.CreateClient();
            var response = await client.PostAsync(user.TokenUri, content);
            response.EnsureSuccessStatusCode();

            if (response.IsSuccessStatusCode)
            {
                var jsonResponse = await response.Content.ReadAsStringAsync();

                token = JsonSerializer.Deserialize<Token>(jsonResponse);
                token.RefreshToken = refreshToken;

                var jsonString = JsonSerializer.Serialize(token);

                string fileName = System.IO.Path.Combine(hostingEnvironment.ContentRootPath, TOKEN_FILE);
                await System.IO.File.WriteAllTextAsync(fileName, jsonString, Encoding.UTF8);
            }

            return token;
        }
        #endregion

        private YouTubeService FetchYouTubeService(TokenResponse tokenResponse, string clientId, string clientSecret)//Métodos do youtube
        {
            
            var initializer = new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret
                }
            };

            var credentials = new UserCredential(new GoogleAuthorizationCodeFlow(initializer), "user", tokenResponse);
            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credentials,
                ApplicationName = Assembly.GetExecutingAssembly().GetName().Name
            });

            return youtubeService;
        }

        void VideoUploadProgressChanged(IUploadProgress progress) //Leitura da progressão de upload.
        {
            switch (progress.Status)
            {
                case UploadStatus.Uploading:
                    _logger.LogInformation("||==> {progress.BytesSent} bytes recebidos.", progress.BytesSent);
                    break;

                case UploadStatus.Failed:
                    _logger.LogError("||==> Um erro impediu seu video de ser enviado :( .{progress.Exception}", progress.Exception);

                    var file = new FileInfo(_tempFilePath);
                    file.Delete();
                    break;
            }
        }

        void VideoUploadResponseReceived(Video video) // Resposta se o vídeo foi enviado
        {
            _logger.LogInformation("\n||==>Video id '{video.Id)}' foi enviado com sucesso :) .\n", video.Id);

            var file = new FileInfo(_tempFilePath);
            file.Delete();
        }

        private async void ExchangeCodeForTokenAsync(string code)
        {
            var user = OAuthClientInfo.LoadClientSecretsInfo();
            var redirectUri = user.RedirectUris.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(redirectUri))
            {
                throw new SystemException("Está faltando o redirecionamento de url nas crecendiais");
            }

            var payload = new Dictionary<string, string>
        {
          { "code" , code } ,
          { "client_id" , user.ClientId } ,
          { "client_secret" , user.ClientSecret } ,
          { "redirect_uri" , redirectUri } ,
          { "grant_type" , "authorization_code" }
        };

            var content = new FormUrlEncodedContent(payload);

            var client = clientFactory.CreateClient();
            var response = await client.PostAsync(user.TokenUri, content);
            response.EnsureSuccessStatusCode();

            if (response.IsSuccessStatusCode)
            {
                var jsonString = await response.Content.ReadAsStringAsync();

                string fileName = System.IO.Path.Combine(hostingEnvironment.ContentRootPath, TOKEN_FILE);
                await System.IO.File.WriteAllTextAsync(fileName, jsonString, Encoding.UTF8);
            }

        }

        public void OnGetRequestCode()//Requisitar o código do google
        {
            const string OAUTH_URL = "https://accounts.google.com/o/oauth2/v2/auth";


            if (System.IO.File.Exists(TOKEN_FILE))
            {
                return;
            }

            var user = OAuthClientInfo.LoadClientSecretsInfo();
            var redirectUri = user.RedirectUris.FirstOrDefault();

            if (redirectUri == null)
            {
                throw new SystemException("Está faltando o redirecionamento de url nas crecendiais");
            }

            var queryParams = new Dictionary<string, string> //N Sei se foi a melhor escolha, verificar se é possivel trocar por lista.
        {
             { "client_id", user.ClientId },
             { "scope", YouTubeService.Scope.YoutubeUpload },
             { "response_type", "code" },
             { "redirect_uri", redirectUri  },
             { "access_type", "offline" }
        };

            var newUrl = QueryHelpers.AddQueryString(OAUTH_URL, queryParams);

            base.Response.Redirect(newUrl);
        }

        public void OnGetAuthorize()//Autorizar
        {
            var request = Request;

            if (request.Query.Keys.Contains("error"))
            {
                var error = QueryHelpers.ParseQuery(request.QueryString.Value)
                            .FirstOrDefault(x => x.Key == "error").Value;
                _logger.LogError("||==> OnGetAuthorize: {error}", error);
            }

            if (request.Query.Keys.Contains("code"))
            {
                var values = QueryHelpers.ParseQuery(request.QueryString.Value);
                var code = values.FirstOrDefault(x => x.Key == "code").Value;
                _logger.LogInformation("||==> Autorização concebida: {code} ", code);

                ExchangeCodeForTokenAsync(code);
                _logger.LogInformation("||==> Token exchanged with access token");

                base.Response.Redirect("./Video/Upload");
            }


        }

        public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
        {
            if (context.HandlerMethod.HttpMethod.ToLower().Equals("post") && this.ModelState.IsValid) // Debugar aqui, dando erro.
            {
                _tempFilePath = Path.GetTempFileName();

                using (var fileStream = new FileStream(_tempFilePath, FileMode.Create))
                {
                    this.VideoUpload.VideoFile.CopyTo(fileStream);
                }
            }
        }
    }



    public class VideoUploadModel //Info do Video. (Teste para depois trocar para data e vizualização de videos=
    {
        public IFormFile VideoFile { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
    }

    public class Token //Acesso ao token gerado pelo google
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public long? ExpiresInSeconds { get; set; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonPropertyName("scope")]
        public string Scope { get; set; }
    }


}


