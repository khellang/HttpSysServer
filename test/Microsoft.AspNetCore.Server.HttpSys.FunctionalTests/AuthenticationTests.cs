// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Testing.xunit;
using Xunit;

namespace Microsoft.AspNetCore.Server.HttpSys
{
    public class AuthenticationTests
    {
        private static bool AllowAnoymous = true;
        private static bool DenyAnoymous = false;

        [ConditionalTheory]
        [InlineData(AuthenticationSchemes.Negotiate)]
        [InlineData(AuthenticationSchemes.NTLM)]
        [InlineData(AuthenticationSchemes.Basic)]
        [InlineData(AuthenticationSchemes.Negotiate | AuthenticationSchemes.NTLM | /*AuthenticationSchemes.Digest |*/ AuthenticationSchemes.Basic)]
        public async Task CanAuthenticate(AuthenticationSchemes authType)
        {
            var authTypeList = authType.ToString().Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            using (var server = Utilities.CreateDynamicHost(string.Empty, authType, AllowAnoymous, out var address, out var baseAddress, async httpContext =>
            {
                foreach (var scheme in authTypeList)
                {
                    var result = await httpContext.AuthenticateAsync(scheme);
                    Assert.True(result.Succeeded);
                }
                httpContext.Response.StatusCode = 200;
            }))
            {
                var response = await SendRequestAsync(address);
                Assert.NotNull(response);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        [ConditionalTheory]
        [InlineData(AuthenticationSchemes.None)]
        [InlineData(AuthenticationSchemes.Negotiate)]
        [InlineData(AuthenticationSchemes.NTLM)]
        // [InlineData(AuthenticationSchemes.Digest)]
        [InlineData(AuthenticationSchemes.Basic)]
        [InlineData(AuthenticationSchemes.Negotiate | AuthenticationSchemes.NTLM | /*AuthenticationSchemes.Digest |*/ AuthenticationSchemes.Basic)]
        public async Task AuthTypes_AllowAnonymous_NoChallenge(AuthenticationSchemes authType)
        {
            using (var server = Utilities.CreateDynamicHost(string.Empty, authType, AllowAnoymous, out var address, out var baseAddress, httpContext =>
            {
                Assert.NotNull(httpContext.User);
                Assert.NotNull(httpContext.User.Identity);
                Assert.False(httpContext.User.Identity.IsAuthenticated);
                return Task.FromResult(0);
            }))
            {
                var response = await SendRequestAsync(address);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(0, response.Headers.WwwAuthenticate.Count);
            }
        }

        [ConditionalTheory(Skip = "HttpClientHandler issue (https://github.com/aspnet/ServerTests/issues/82).")]
        [InlineData(AuthenticationSchemes.Negotiate)]
        [InlineData(AuthenticationSchemes.NTLM)]
        // [InlineData(AuthenticationSchemes.Digest)] // TODO: Not implemented
        [InlineData(AuthenticationSchemes.Basic)]
        public async Task AuthType_RequireAuth_ChallengesAdded(AuthenticationSchemes authType)
        {
            using (var server = Utilities.CreateDynamicHost(string.Empty, authType, DenyAnoymous, out var address, out var baseAddress, httpContext =>
            {
                throw new NotImplementedException();
            }))
            {
                var response = await SendRequestAsync(address);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.Equal(authType.ToString(), response.Headers.WwwAuthenticate.ToString(), StringComparer.OrdinalIgnoreCase);
            }
        }

        [ConditionalTheory(Skip = "HttpClientHandler issue (https://github.com/aspnet/ServerTests/issues/82).")]
        [InlineData(AuthenticationSchemes.Negotiate)]
        [InlineData(AuthenticationSchemes.NTLM)]
        // [InlineData(AuthenticationSchemes.Digest)] // TODO: Not implemented
        [InlineData(AuthenticationSchemes.Basic)]
        public async Task AuthType_AllowAnonymousButSpecify401_ChallengesAdded(AuthenticationSchemes authType)
        {
            using (var server = Utilities.CreateDynamicHost(string.Empty, authType, AllowAnoymous, out var address, out var baseAddress, httpContext =>
            {
                Assert.NotNull(httpContext.User);
                Assert.NotNull(httpContext.User.Identity);
                Assert.False(httpContext.User.Identity.IsAuthenticated);
                httpContext.Response.StatusCode = 401;
                return Task.FromResult(0);
            }))
            {
                var response = await SendRequestAsync(address);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.Equal(authType.ToString(), response.Headers.WwwAuthenticate.ToString(), StringComparer.OrdinalIgnoreCase);
            }
        }

        [ConditionalTheory(Skip = "HttpClientHandler issue (https://github.com/aspnet/ServerTests/issues/82).")]
        public async Task MultipleAuthTypes_AllowAnonymousButSpecify401_ChallengesAdded()
        {
            string address;
            using (Utilities.CreateHttpAuthServer(
                AuthenticationSchemes.Negotiate
                | AuthenticationSchemes.NTLM
                /* | AuthenticationSchemes.Digest TODO: Not implemented */
                | AuthenticationSchemes.Basic,
                true,
                out address,
                httpContext =>
            {
                Assert.NotNull(httpContext.User);
                Assert.NotNull(httpContext.User.Identity);
                Assert.False(httpContext.User.Identity.IsAuthenticated);
                httpContext.Response.StatusCode = 401;
                return Task.FromResult(0);
            }))
            {
                var response = await SendRequestAsync(address);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.Equal("Negotiate, NTLM, basic", response.Headers.WwwAuthenticate.ToString(), StringComparer.OrdinalIgnoreCase);
            }
        }

        [ConditionalTheory]
        [InlineData(AuthenticationSchemes.Negotiate)]
        [InlineData(AuthenticationSchemes.NTLM)]
        // [InlineData(AuthenticationSchemes.Digest)] // TODO: Not implemented
        // [InlineData(AuthenticationSchemes.Basic)] // Doesn't work with default creds
        [InlineData(AuthenticationSchemes.Negotiate | AuthenticationSchemes.NTLM | /* AuthenticationSchemes.Digest |*/ AuthenticationSchemes.Basic)]
        public async Task AuthTypes_AllowAnonymousButSpecify401_Success(AuthenticationSchemes authType)
        {
            int requestId = 0;
            using (var server = Utilities.CreateDynamicHost(string.Empty, authType, AllowAnoymous, out var address, out var baseAddress, httpContext =>
            {
                Assert.NotNull(httpContext.User);
                Assert.NotNull(httpContext.User.Identity);
                if (requestId == 0)
                {
                    Assert.False(httpContext.User.Identity.IsAuthenticated);
                    httpContext.Response.StatusCode = 401;
                }
                else if (requestId == 1)
                {
                    Assert.True(httpContext.User.Identity.IsAuthenticated);
                }
                else
                {
                    throw new NotImplementedException();
                }
                requestId++;
                return Task.FromResult(0);
            }))
            {
                var response = await SendRequestAsync(address, useDefaultCredentials: true);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        [ConditionalTheory]
        [InlineData(AuthenticationSchemes.Negotiate)]
        [InlineData(AuthenticationSchemes.NTLM)]
        // [InlineData(AuthenticationSchemes.Digest)] // TODO: Not implemented
        // [InlineData(AuthenticationSchemes.Basic)] // Doesn't work with default creds
        [InlineData(AuthenticationSchemes.Negotiate | AuthenticationSchemes.NTLM | /* AuthenticationSchemes.Digest |*/ AuthenticationSchemes.Basic)]
        public async Task AuthTypes_RequireAuth_Success(AuthenticationSchemes authType)
        {
            using (var server = Utilities.CreateDynamicHost(string.Empty, authType, AllowAnoymous, out var address, out var baseAddress, httpContext =>
            {
                Assert.NotNull(httpContext.User);
                Assert.NotNull(httpContext.User.Identity);
                Assert.True(httpContext.User.Identity.IsAuthenticated);
                return Task.FromResult(0);
            }))
            {
                var response = await SendRequestAsync(address, useDefaultCredentials: true);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        [ConditionalTheory]
        [InlineData(AuthenticationSchemes.Negotiate)]
        [InlineData(AuthenticationSchemes.NTLM)]
        // [InlineData(AuthenticationSchemes.Digest)]
        [InlineData(AuthenticationSchemes.Basic)]
        [InlineData(AuthenticationSchemes.Negotiate | AuthenticationSchemes.NTLM | /*AuthenticationSchemes.Digest |*/ AuthenticationSchemes.Basic)]
        public async Task AuthTypes_AuthenticateWithNoUser_NoResults(AuthenticationSchemes authType)
        {
            var authTypeList = authType.ToString().Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            using (var server = Utilities.CreateDynamicHost(string.Empty, authType, AllowAnoymous, out var address, out var baseAddress, async httpContext =>
            {
                Assert.NotNull(httpContext.User);
                Assert.NotNull(httpContext.User.Identity);
                Assert.False(httpContext.User.Identity.IsAuthenticated);
                foreach (var scheme in authTypeList)
                {
                    var authResults = await httpContext.AuthenticateAsync(scheme);
                    Assert.False(authResults.Succeeded);
                    Assert.True(authResults.Nothing);
                }
            }))
            {
                var response = await SendRequestAsync(address);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(0, response.Headers.WwwAuthenticate.Count);
            }
        }

        [ConditionalTheory]
        [InlineData(AuthenticationSchemes.Negotiate)]
        [InlineData(AuthenticationSchemes.NTLM)]
        // [InlineData(AuthenticationSchemes.Digest)]
        // [InlineData(AuthenticationSchemes.Basic)] // Doesn't work with default creds
        [InlineData(AuthenticationSchemes.Negotiate | AuthenticationSchemes.NTLM | /*AuthenticationSchemes.Digest |*/ AuthenticationSchemes.Basic)]
        public async Task AuthTypes_AuthenticateWithUser_OneResult(AuthenticationSchemes authType)
        {
            var authTypeList = authType.ToString().Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            using (var server = Utilities.CreateDynamicHost(string.Empty, authType, DenyAnoymous, out var address, out var baseAddress, async httpContext =>
            {
                Assert.NotNull(httpContext.User);
                Assert.NotNull(httpContext.User.Identity);
                Assert.True(httpContext.User.Identity.IsAuthenticated);
                var count = 0;
                foreach (var scheme in authTypeList)
                {
                    var authResults = await httpContext.AuthenticateAsync(scheme);
                    if (authResults.Succeeded)
                    {
                        count++;
                    }
                }
                Assert.Equal(1, count);
            }))
            {
                var response = await SendRequestAsync(address, useDefaultCredentials: true);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        [ConditionalTheory(Skip = "HttpClientHandler issue (https://github.com/aspnet/ServerTests/issues/82).")]
        [InlineData(AuthenticationSchemes.Negotiate)]
        [InlineData(AuthenticationSchemes.NTLM)]
        // [InlineData(AuthenticationSchemes.Digest)]
        [InlineData(AuthenticationSchemes.Basic)]
        [InlineData(AuthenticationSchemes.Negotiate | AuthenticationSchemes.NTLM | /*AuthenticationSchemes.Digest |*/ AuthenticationSchemes.Basic)]
        public async Task AuthTypes_ChallengeWithoutAuthTypes_AllChallengesSent(AuthenticationSchemes authType)
        {
            var authTypeList = authType.ToString().Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            using (var server = Utilities.CreateDynamicHost(string.Empty, authType, AllowAnoymous, out var address, out var baseAddress, httpContext =>
            {
                Assert.NotNull(httpContext.User);
                Assert.NotNull(httpContext.User.Identity);
                Assert.False(httpContext.User.Identity.IsAuthenticated);
                return httpContext.ChallengeAsync(authType.ToString());
            }))
            {
                var response = await SendRequestAsync(address);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.Equal(authTypeList.Count(), response.Headers.WwwAuthenticate.Count);
            }
        }

        [ConditionalTheory(Skip = "HttpClientHandler issue (https://github.com/aspnet/ServerTests/issues/82).")]
        [InlineData(AuthenticationSchemes.Negotiate)]
        [InlineData(AuthenticationSchemes.NTLM)]
        // [InlineData(AuthenticationSchemes.Digest)]
        [InlineData(AuthenticationSchemes.Basic)]
        [InlineData(AuthenticationSchemes.Negotiate | AuthenticationSchemes.NTLM | /*AuthenticationSchemes.Digest |*/ AuthenticationSchemes.Basic)]
        public async Task AuthTypes_ChallengeWithAllAuthTypes_AllChallengesSent(AuthenticationSchemes authType)
        {
            var authTypeList = authType.ToString().Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            using (var server = Utilities.CreateDynamicHost(string.Empty, authType, AllowAnoymous, out var address, out var baseAddress, async httpContext =>
            {
                Assert.NotNull(httpContext.User);
                Assert.NotNull(httpContext.User.Identity);
                Assert.False(httpContext.User.Identity.IsAuthenticated);
                foreach (var scheme in authTypeList)
                {
                    await httpContext.ChallengeAsync(scheme);
                }
            }))
            {
                var response = await SendRequestAsync(address);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.Equal(authTypeList.Count(), response.Headers.WwwAuthenticate.Count);
            }
        }

        [ConditionalTheory(Skip = "HttpClientHandler issue (https://github.com/aspnet/ServerTests/issues/82).")]
        [InlineData(AuthenticationSchemes.Negotiate)]
        [InlineData(AuthenticationSchemes.NTLM)]
        // [InlineData(AuthenticationSchemes.Digest)]
        [InlineData(AuthenticationSchemes.Basic)]
        public async Task AuthTypes_ChallengeOneAuthType_OneChallengeSent(AuthenticationSchemes authType)
        {
            var authTypes = AuthenticationSchemes.Negotiate | AuthenticationSchemes.NTLM | /*AuthenticationSchemes.Digest |*/ AuthenticationSchemes.Basic;
            using (var server = Utilities.CreateDynamicHost(string.Empty, authTypes, AllowAnoymous, out var address, out var baseAddress, httpContext =>
            {
                Assert.NotNull(httpContext.User);
                Assert.NotNull(httpContext.User.Identity);
                Assert.False(httpContext.User.Identity.IsAuthenticated);
                return httpContext.ChallengeAsync(authType.ToString());
            }))
            {
                var response = await SendRequestAsync(address);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.Equal(1, response.Headers.WwwAuthenticate.Count);
                Assert.Equal(authType.ToString(), response.Headers.WwwAuthenticate.First().Scheme);
            }
        }

        [ConditionalTheory]
        [InlineData(AuthenticationSchemes.Negotiate)]
        [InlineData(AuthenticationSchemes.NTLM)]
        // [InlineData(AuthenticationSchemes.Digest)]
        [InlineData(AuthenticationSchemes.Basic)]
        public async Task AuthTypes_ChallengeDisabledAuthType_Error(AuthenticationSchemes authType)
        {
            var authTypes = AuthenticationSchemes.Negotiate | AuthenticationSchemes.NTLM | /*AuthenticationSchemes.Digest |*/ AuthenticationSchemes.Basic;
            authTypes = authTypes & ~authType;
            var authTypeList = authType.ToString().Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            using (var server = Utilities.CreateDynamicHost(string.Empty, authType, AllowAnoymous, out var address, out var baseAddress, httpContext =>
            {
                Assert.NotNull(httpContext.User);
                Assert.NotNull(httpContext.User.Identity);
                Assert.False(httpContext.User.Identity.IsAuthenticated);
                return Assert.ThrowsAsync<InvalidOperationException>(() => httpContext.Authentication.ChallengeAsync(authType.ToString()));
            }))
            {
                var response = await SendRequestAsync(address);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(0, response.Headers.WwwAuthenticate.Count);
            }
        }

        [ConditionalTheory]
        [InlineData(AuthenticationSchemes.Negotiate)]
        [InlineData(AuthenticationSchemes.NTLM)]
        // [InlineData(AuthenticationSchemes.Digest)]
        [InlineData(AuthenticationSchemes.Basic)]
        public async Task AuthTypes_Forbid_Forbidden(AuthenticationSchemes authType)
        {
            var authTypes = AuthenticationSchemes.Negotiate | AuthenticationSchemes.NTLM | /*AuthenticationSchemes.Digest |*/ AuthenticationSchemes.Basic;
            using (var server = Utilities.CreateDynamicHost(string.Empty, authTypes, AllowAnoymous, out var address, out var baseAddress, httpContext =>
            {
                Assert.NotNull(httpContext.User);
                Assert.NotNull(httpContext.User.Identity);
                Assert.False(httpContext.User.Identity.IsAuthenticated);
                return httpContext.ForbidAsync(authType.ToString());
            }))
            {
                var response = await SendRequestAsync(address);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Equal(0, response.Headers.WwwAuthenticate.Count);
            }
        }

        [ConditionalTheory]
        [InlineData(AuthenticationSchemes.Negotiate)]
        [InlineData(AuthenticationSchemes.NTLM)]
        // [InlineData(AuthenticationSchemes.Digest)] // Not implemented
        // [InlineData(AuthenticationSchemes.Basic)] // Can't log in with UseDefaultCredentials
        public async Task AuthTypes_ChallengeAuthenticatedAuthType_Forbidden(AuthenticationSchemes authType)
        {
            using (var server = Utilities.CreateDynamicHost(string.Empty, authType, DenyAnoymous, out var address, out var baseAddress, httpContext =>
            {
                Assert.NotNull(httpContext.User);
                Assert.NotNull(httpContext.User.Identity);
                Assert.True(httpContext.User.Identity.IsAuthenticated);
                return httpContext.ChallengeAsync(authType.ToString());
            }))
            {
                var response = await SendRequestAsync(address, useDefaultCredentials: true);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                // for some reason Kerberos and Negotiate include a 2nd stage challenge.
                // Assert.Equal(0, response.Headers.WwwAuthenticate.Count);
            }
        }

        [ConditionalTheory]
        [InlineData(AuthenticationSchemes.Negotiate)]
        [InlineData(AuthenticationSchemes.NTLM)]
        // [InlineData(AuthenticationSchemes.Digest)] // Not implemented
        // [InlineData(AuthenticationSchemes.Basic)] // Can't log in with UseDefaultCredentials
        public async Task AuthTypes_ChallengeAuthenticatedAuthTypeWithEmptyChallenge_Forbidden(AuthenticationSchemes authType)
        {
            using (var server = Utilities.CreateDynamicHost(string.Empty, authType, DenyAnoymous, out var address, out var baseAddress, httpContext =>
            {
                Assert.NotNull(httpContext.User);
                Assert.NotNull(httpContext.User.Identity);
                Assert.True(httpContext.User.Identity.IsAuthenticated);
                return httpContext.ChallengeAsync(authType.ToString());
            }))
            {
                var response = await SendRequestAsync(address, useDefaultCredentials: true);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                // for some reason Kerberos and Negotiate include a 2nd stage challenge.
                // Assert.Equal(0, response.Headers.WwwAuthenticate.Count);
            }
        }

        [ConditionalTheory]
        [InlineData(AuthenticationSchemes.Negotiate)]
        [InlineData(AuthenticationSchemes.NTLM)]
        // [InlineData(AuthenticationSchemes.Digest)] // Not implemented
        // [InlineData(AuthenticationSchemes.Basic)] // Can't log in with UseDefaultCredentials
        public async Task AuthTypes_UnathorizedAuthenticatedAuthType_Unauthorized(AuthenticationSchemes authType)
        {
            using (var server = Utilities.CreateDynamicHost(string.Empty, authType, DenyAnoymous, out var address, out var baseAddress, httpContext =>
            {
                Assert.NotNull(httpContext.User);
                Assert.NotNull(httpContext.User.Identity);
                Assert.True(httpContext.User.Identity.IsAuthenticated);
                return httpContext.ChallengeAsync(authType.ToString(), null, ChallengeBehavior.Unauthorized);
            }))
            {
                var response = await SendRequestAsync(address, useDefaultCredentials: true);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.Equal(1, response.Headers.WwwAuthenticate.Count);
                Assert.Equal(authType.ToString(), response.Headers.WwwAuthenticate.First().Scheme);
            }
        }

        private async Task<HttpResponseMessage> SendRequestAsync(string uri, bool useDefaultCredentials = false)
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.UseDefaultCredentials = useDefaultCredentials;
            using (HttpClient client = new HttpClient(handler))
            {
                return await client.GetAsync(uri);
            }
        }
    }
}
