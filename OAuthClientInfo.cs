using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace YoutubeApiTest.Pages
{
    public class OAuthClientInfo //OAuth método de autorização do google 
    {
        const string CLIENT_SECRETS_FILE = "client_secrets.json"; //Json Gerado pelo google
        private static string _secretsFile;

        public static OAuthUserCredential LoadClientSecretsInfo(string clientSecretsFile = "")
        {
            string clientSecrets;
            _secretsFile = string.IsNullOrWhiteSpace(clientSecretsFile)
                  ? CLIENT_SECRETS_FILE
                  : clientSecretsFile;
            try
            {
                clientSecrets = File.ReadAllText(_secretsFile);
            }
            catch (SystemException ex)
            {
                throw ex;
            }

            var options = new JsonSerializerOptions { AllowTrailingCommas = true };            
            var clientInfo = JsonSerializer.Deserialize<Dictionary<string, OAuthUserCredential>>(clientSecrets, options).Values.SingleOrDefault();
            
            if (clientInfo == null)
                throw new SystemException($"Missing data or malformed client secrets file '{_secretsFile}'.");

            return clientInfo;
        }
    }

    public class OAuthUserCredential //Variaveis encapsuladas para o Json
    {
        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; }

        [JsonPropertyName("client_id")]
        public string ClientId { get; set; }

        [JsonPropertyName("client_secret")]
        public string ClientSecret { get; set; }

        [JsonPropertyName("auth_uri")]
        public string AuthUri { get; set; }

        [JsonPropertyName("token_uri")]
        public string TokenUri { get; set; }

        [JsonPropertyName("redirect_uris")]
        public string[] RedirectUris { get; set; }
    }
}