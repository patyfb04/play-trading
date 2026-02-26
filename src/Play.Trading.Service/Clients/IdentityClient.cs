using Play.Trading.Service.Dtos;

namespace Play.Trading.Service.Clients
{
    public class IdentityClient
    {
        private readonly HttpClient _httpClient;

        public IdentityClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<IReadOnlyCollection<UserDto>> GetUsersAsync()
        {
            var items = await _httpClient.GetFromJsonAsync<IReadOnlyCollection<UserDto>>("/users");
            return items;
        }
    }
}
