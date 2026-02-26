using Play.Catalog.Contracts;
using Play.Common.Repositories;
using Play.Trading.Service.Clients;
using Play.Trading.Service.Entities;

namespace Play.Trading.Service.Services
{
    //projection reconciliation
    public class TradingCatalogSyncService
    {
        private readonly CatalogClient _catalogClient;
        private readonly IRepository<CatalogItem> _catalogRepo;

        public TradingCatalogSyncService(
            CatalogClient catalogClient,
            IRepository<CatalogItem> catalogRepo)
        {
            _catalogClient = catalogClient;
            _catalogRepo = catalogRepo;
        }

        public async Task RunAsync()
        {
            Console.WriteLine(">>> Starting Catalog → Trading delta sync...");

            // 1. Fetch all items from Catalog
            var catalogItems = await _catalogClient.GetCatalogItemsAsync();
            var catalogDict = catalogItems.ToDictionary(i => i.Id);

            // 2. Fetch all items from Inventory
            var inventoryItems = await _catalogRepo.GetAllAsync();
            var inventoryDict = inventoryItems.ToDictionary(i => i.Id);

            // 3. Insert or update differences
            foreach (var item in catalogItems)
            {
                if (!inventoryDict.TryGetValue(item.Id, out var existing))
                {
                    // New item
                    Console.WriteLine($"[SYNC] Creating missing item {item.Id}");
                    await _catalogRepo.CreateAsync(new CatalogItem
                    {
                        Id = item.Id,
                        Name = item.Name,
                        Description = item.Description,
                        Price = item.Price
                    });
                }
                else if (existing.Name != item.Name ||
                         existing.Description != item.Description ||
                         existing.Price != item.Price)
                {
                    // Updated item
                    Console.WriteLine($"[SYNC] Updating changed item {item.Id}");
                    existing.Name = item.Name;
                    existing.Description = item.Description;
                    existing.Price = item.Price;

                    await _catalogRepo.UpdateAsync(existing);
                }
            }

            // 4. Remove items that no longer exist in Catalog (optional)
            foreach (var invItem in inventoryItems)
            {
                if (!catalogDict.ContainsKey(invItem.Id))
                {
                    Console.WriteLine($"[SYNC] Removing deleted item {invItem.Id}");
                    await _catalogRepo.RemoveAsync(invItem.Id);
                }
            }

            Console.WriteLine(">>> Catalog → Trading delta sync complete.");
        }
    }
}
