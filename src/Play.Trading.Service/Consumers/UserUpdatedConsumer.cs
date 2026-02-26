using MassTransit;
using Play.Common.Repositories;
using Play.Identity.Contracts;
using Play.Trading.Service.Entities;

namespace Play.Trading.Service.Consumers
{
    public class UserCreatedConsumer : IConsumer<UserUpdated>
    {
        private readonly IRepository<ApplicationUser> _repository;

        public UserCreatedConsumer(IRepository<ApplicationUser> repository)
        {
            _repository = repository;
        }

        public async Task Consume(ConsumeContext<UserUpdated> context)
        {
            var message = context.Message;

            var applicationUser = await _repository.GetAsync(item =>item.Id == message.UserId);

            if (applicationUser is null)
            {
                applicationUser = new ApplicationUser
                {
                    Id = message.UserId,
                    Gil = message.NewTotalGil
                };

                await _repository.CreateAsync(applicationUser);
            }
            else
            {
                applicationUser.Gil = message.NewTotalGil;
                await _repository.UpdateAsync(applicationUser);
            }
        }
    }
}
