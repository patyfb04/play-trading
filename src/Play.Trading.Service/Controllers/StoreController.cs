using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Play.Common.Repositories;
using Play.Trading.Service.Dtos;
using Play.Trading.Service.Entities;

namespace Play.Trading.Service.Controllers
{
    [ApiController]
    [Route("store")]
    [Authorize]
    public class StoreController : ControllerBase
    {
        private readonly IRepository<CatalogItem> _catalogRepository;
        private readonly IRepository<InventoryItem> _inventoryRepository;
        private readonly IRepository<ApplicationUser> _userRepository;
        public StoreController(
            IRepository<CatalogItem> catalogRepository,
            IRepository<InventoryItem> inventoryRepository,
            IRepository<ApplicationUser> userRepository)
        {
            _catalogRepository = catalogRepository;
            _inventoryRepository = inventoryRepository;
            _userRepository = userRepository;
        }

        [HttpGet]
        public async Task<ActionResult<StoreDto>> GetAsync()
        {
            string userId = User.FindFirst("sub")!.Value;
            Console.WriteLine($"JWT sub claim: {User.FindFirst("sub")!.Value}");

            var inventoryItems = await _inventoryRepository.GetAllAsync(
                item => item.UserId == Guid.Parse(userId)
             );

            var user = await _userRepository.GetAsync(Guid.Parse(userId));
            if (user == null)
            {
                return NotFound();
            }

            var catalogItems = await _catalogRepository.GetAllAsync();

            var catalogItemsFiltered = catalogItems.Select(catalogItem =>
                           new StoreItemDto(
                               catalogItem.Id,
                               catalogItem.Name,
                               catalogItem.Description,
                               catalogItem.Price,
                               inventoryItems
                                   .FirstOrDefault(inventoryItem =>
                                       inventoryItem.CatalogItemId == catalogItem.Id)
                                   ?.Quantity ?? 0));

            var storeDto = new StoreDto(catalogItemsFiltered, user.Gil);

            return Ok(storeDto);
            
        }
    }
}
