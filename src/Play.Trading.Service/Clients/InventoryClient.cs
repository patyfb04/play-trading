using Play.Trading.Service.Dtos;

namespace Play.Trading.Service.Clients
{
    public class InventoryClient
    {
        private readonly HttpClient _httpClient;

        public InventoryClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<IReadOnlyCollection<InventoryItemDto>> GetInventoryItemsAsync()
        {
            var items = await _httpClient.GetFromJsonAsync<IReadOnlyCollection<InventoryItemDto>>("/items/sync");
            return items;
        }
    }
}
