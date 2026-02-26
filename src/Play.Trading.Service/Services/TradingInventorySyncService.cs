using Play.Common.Repositories;
using Play.Trading.Service.Clients;
using Play.Trading.Service.Entities;

namespace Play.Trading.Service.Services
{
    public class TradingInventorySyncService
    {
        private readonly InventoryClient _inventoryClient;
        private readonly IRepository<InventoryItem> _inventoryRepo;

        public TradingInventorySyncService(
            InventoryClient InventoryClient,
            IRepository<InventoryItem> catalogRepo)
        {
            _inventoryClient = InventoryClient;
            _inventoryRepo = catalogRepo;
        }

        public async Task RunAsync()
        {
            Console.WriteLine(">>> Starting Inventory → Trading delta sync...");

            // 1. Fetch all items from Inventory
            var inventoryItems = await _inventoryClient.GetInventoryItemsAsync();
            var inventoryDict = inventoryItems.ToDictionary(i => i.Id);

            // 2. Fetch all items from Inventory
            var tradingInventoryItems = await _inventoryRepo.GetAllAsync();
            var tradingInventoryDict = tradingInventoryItems.ToDictionary(i => i.Id);

            // 3. Insert or update differences
            foreach (var item in inventoryItems)
            {
                if (!tradingInventoryDict.TryGetValue(item.Id, out var existing))
                {
                    // New item
                    Console.WriteLine($"[SYNC] Creating missing item {item.Id}");
                    await _inventoryRepo.CreateAsync(new InventoryItem
                    {
                        Id = item.Id,
                        Name = item.Name,
                        Description = item.Description,
                        CatalogItemId = item.CatalogItemId,
                        UserId = item.UserId,
                        Quantity = item.Quantity,
                        AcquiredDate = item.AcquiredDate
                    });
                }
                else if (existing.Name != item.Name ||
                         existing.Description != item.Description ||
                         existing.Quantity != item.Quantity)
                {
                    // Updated item
                    Console.WriteLine($"[SYNC] Updating changed item {item.Id}");
                    existing.Name = item.Name;
                    existing.Description = item.Description;
                    existing.Quantity = item.Quantity;

                    await _inventoryRepo.UpdateAsync(existing);
                }
            }

            // 4. Remove items that no longer exist in Inventory (optional)
            foreach (var invItem in inventoryItems)
            {
                if (!inventoryDict.ContainsKey(invItem.Id))
                {
                    Console.WriteLine($"[SYNC] Removing deleted item {invItem.Id}");
                    await _inventoryRepo.RemoveAsync(invItem.Id);
                }
            }

            Console.WriteLine(">>> Inventory → Trading delta sync complete.");
        }
    }
}
