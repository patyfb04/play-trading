namespace Play.Trading.Service.Settings
{
    public class QueueSettings
    {
        public string GrantItemsQueueAddress { get; set; } = null!;
        public string DebitGilQueueAddress { get; set; } = null!;
        public string PurchaseCompleteQueueAddress { get; set; } = null!;
        public string SubtractItemsQueueAddress { get; set; } = null!;

    }
}
