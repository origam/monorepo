#region license

/*
Copyright 2005 - 2021 Advantage Solutions, s. r. o.

This file is part of ORIGAM (http://www.origam.org).

ORIGAM is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

ORIGAM is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with ORIGAM. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion

using System.Linq;
using Microsoft.Extensions.Configuration;
using Origam.Extensions;

namespace Origam.Server.Configuration
{
    public class IdentityServerConfig
    {
        public string PathToJwtCertificate { get; }
        public string PasswordForJwtCertificate { get; }
        public string GoogleClientId { get; }
        public string GoogleClientSecret { get; }
        public bool UseGoogleLogin { get;}
        
        public string MicrosoftClientId { get; }
        public string MicrosoftClientSecret { get; }
        public bool UseMicrosoftLogin { get; }
        
        public bool UseAzureAdLogin { get; }
        public string AzureAdClientId { get; }
        public string AzureAdTenantId { get; }

        public WebClient WebClient { get; }
        public MobileClient MobileClient { get; }
        public ServerClient ServerClient { get; }
        
        public bool CookieSlidingExpiration { get; } 
        public int CookieExpirationMinutes { get; }

        public IdentityServerConfig(IConfiguration configuration)
        {
            IConfigurationSection identityServerSection = configuration.GetSectionOrThrow("IdentityServerConfig");
            PathToJwtCertificate = identityServerSection.GetStringOrThrow("PathToJwtCertificate");
            PasswordForJwtCertificate = identityServerSection.GetValue<string>("PasswordForJwtCertificate");
            UseGoogleLogin = identityServerSection.GetValue("UseGoogleLogin", false);
            GoogleClientId = identityServerSection["GoogleClientId"] ?? "";
            GoogleClientSecret = identityServerSection["GoogleClientSecret"] ?? "";
            UseMicrosoftLogin = identityServerSection.GetValue("UseMicrosoftLogin", false);
            MicrosoftClientId = identityServerSection["MicrosoftClientId"] ?? "";
            MicrosoftClientSecret = identityServerSection["MicrosoftClientSecret"] ?? "";
            UseAzureAdLogin = identityServerSection.GetValue("UseAzureAdLogin", false);
            AzureAdClientId = identityServerSection["AzureAdClientId"] ?? "";
            AzureAdTenantId = identityServerSection["AzureAdTenantId"] ?? "";
            CookieSlidingExpiration = identityServerSection.GetValue("CookieSlidingExpiration", true);
            CookieExpirationMinutes = identityServerSection.GetValue("CookieExpirationMinutes", 60);

            var webClientSection = identityServerSection
                .GetSection("WebClient");
            if (webClientSection.GetChildren().Count() != 0)
            {
                WebClient = new WebClient
                {
                    PostLogoutRedirectUris = webClientSection
                        .GetSectionOrThrow("PostLogoutRedirectUris")
                        .GetStringArrayOrThrow(),
                    RedirectUris = webClientSection
                        .GetSectionOrThrow("RedirectUris")
                        .GetStringArrayOrThrow(),
                    AllowedCorsOrigins = webClientSection
                        .GetSection("AllowedCorsOrigins")
                        ?.Get<string[]>()
                };
            }
            
            var mobileClientSection = identityServerSection
                .GetSection("MobileClient");

            if (mobileClientSection.GetChildren().Count() != 0)
            {
                MobileClient = new MobileClient
                {
                    PostLogoutRedirectUris = mobileClientSection
                        .GetSectionOrThrow("PostLogoutRedirectUris")
                        .GetStringArrayOrThrow(),
                    RedirectUris = mobileClientSection
                        .GetSectionOrThrow("RedirectUris")
                        .GetStringArrayOrThrow(),
                    ClientSecret= mobileClientSection.GetStringOrThrow("ClientSecret")
                }; 
            }
            
            var serverClientSection = identityServerSection
                .GetSection("ServerClient");
            if (identityServerSection.GetChildren().Count() != 0)
            {
                ServerClient = new ServerClient
                {
                    ClientSecret = serverClientSection.GetStringOrThrow("ClientSecret")
                };
            }
        }
    }

    public class ServerClient
    {
        public string ClientSecret { get; set; }
    }

    public class WebClient
    {
        public string[] RedirectUris { get; set; }
        public string[] PostLogoutRedirectUris { get; set; }
        public string[] AllowedCorsOrigins { get; set; }  
    }
    
    public class MobileClient
    {
        public string[] RedirectUris { get; set; }
        public string[] PostLogoutRedirectUris { get; set; }
        public string ClientSecret { get; set; }
    }
}