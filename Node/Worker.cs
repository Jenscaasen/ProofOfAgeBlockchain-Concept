namespace Node
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private DataStore _dataStore;
        private NegotiationService _negotiationService;
        private bool initialStartupPerformed = false;
        public Worker(ILogger<Worker> logger, DataStore dataStore, NegotiationService negotiationService)
        {
            _logger = logger;
            _dataStore = dataStore;
            _negotiationService = negotiationService;            
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            
           
            while (!stoppingToken.IsCancellationRequested)
            {
                if (string.IsNullOrEmpty(Globals.Kestrel_Host))
                {
                    _logger.LogInformation("Kestrel Host not known yet, waiting..");
                    await Task.Delay(5000);
                    continue;
                }
                if (!initialStartupPerformed) PerformInitialStartup();
                _logger.LogInformation("Checking if new Blocks need to be made");
                _dataStore.CheckInLastBlock();
                _dataStore.CleanupTransactions();

                if(_dataStore.Transactions.Count > 0)
                {
                    _logger.LogInformation(_dataStore.Transactions.Count + " Transactions waiting");
                    _negotiationService.RefreshConnections();
                    _negotiationService.MakeTop50List();
                    if (_negotiationService.MeIsInTop50())
                    {
                        _negotiationService.MakeBlock();
                     await   _negotiationService.SyncNewestBlockAsync();
                    }
                } else
                {
                    _logger.LogInformation("No new Transactions, skipping Block creation");
                }

                if (_dataStore.SaveNeeded)
                {
                    _dataStore.SaveConfig();
                    _dataStore.SaveNeeded = false;
                }
                await Task.Delay(50000, stoppingToken);
            }
        }

        private void PerformInitialStartup()
        {
            _logger.LogInformation("Starting up");
            if (_dataStore.BlockChain.Count == 0 && _dataStore.Neighbors.Count == 0)
            {
                _logger.LogInformation("Blockchain is empty and no neighbors configured: making the first block");
                //I am the first one?
                _negotiationService.MakeBlock();
            }

            if (_dataStore.BlockChain.Count == 0 && _dataStore.Neighbors.Count > 0)
            {
                _logger.LogInformation($"Blockchain is empty but got {_dataStore.Neighbors.Count} neighbors, requesting Blockchain from others");
                //not the first one, download blockchain
                _negotiationService.RefreshConnections();
                _negotiationService.HelloToEveryone();
                _negotiationService.RequestAllBlockchains();
            }
            if (_dataStore.BlockChain.Count > 0)
            {
                //i have been online before, need to get updates
                _negotiationService.RefreshConnections();
                _negotiationService.HelloToEveryone();
                _negotiationService.UpdateBlockchainFrom(_dataStore.BlockChain.Last().Index);
            }

            initialStartupPerformed = true;
        }
    }
}