using Microsoft.AspNetCore.SignalR;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Play.Trading.Service.SignalR
{
    public class UserIdProvider :IUserIdProvider
    {
        public string? GetUserId(HubConnectionContext connection)
        {
            return connection.User?.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? string.Empty;
        }
    }
}
