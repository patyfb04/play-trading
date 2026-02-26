using Microsoft.AspNetCore.SignalR;
using Play.Trading.Service.StatesMachine;

namespace Play.Trading.Service.SignalR
{
    public class MessageHub : Hub
    {
        public async Task SendStatusAsync(PurchaseState status)
        {
            if (Clients is not null)
            {
                await Clients.User(Context.UserIdentifier!)
                    .SendAsync("ReceiveStatus", status);
            }
        }
    }
}
