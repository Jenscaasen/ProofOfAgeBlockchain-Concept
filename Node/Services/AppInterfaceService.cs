using System.Text.Json;

namespace Node
{
    public class AppInterfaceService
    {
        private NegotiationService _negotiationService;
        private DataStore _dataStore;
        private ILogger<AppInterfaceService> _logger;
        public AppInterfaceService(NegotiationService negotiationService, DataStore dataStore, ILogger<AppInterfaceService> logger)
        {
            this._negotiationService = negotiationService;
            this._dataStore = dataStore;
            this._logger = logger;
        }

        internal async Task<object> AddTransaction(HttpContext context)
        {
           
            UserSubmittedTransaction? transaction =   await JsonSerializer.DeserializeAsync<UserSubmittedTransaction>(context.Request.Body);

            if(transaction == null)
            {
                return "Error: invalid transaction in body of post. Received";
            }

          bool success =  _negotiationService.AddTransaction(new AddTransactionRequest {
            MyPublicKey = transaction.publicKey,
            MySignature = Google.Protobuf.ByteString.CopyFrom(System.Convert.FromBase64String(transaction.SignatureBase64)),
            TransactionToAdd = transaction.Transaction.toGrpc()
            });

            return Task.FromResult("success: "+ success);
        }

        internal object GetWallet(HttpContext context, string walletId)
        {
            //Search blockchain for that receiver
       WalletData  walletData = _dataStore.GetWalletData(walletId);
            return JsonSerializer.Serialize(walletData, new JsonSerializerOptions { WriteIndented = true });
        }

        internal object GetBlockchain(HttpContext context)
        {
            _logger.LogInformation("Answering GetBlockchain");
            return JsonSerializer.Serialize(_dataStore.BlockChain, new JsonSerializerOptions {WriteIndented  = true });
        }

        internal object GetStatistics(HttpContext context)
        {
            throw new NotImplementedException();
        }

        private async Task<string> StreamToStringAsync(HttpRequest request)
        {
            using (var sr = new StreamReader(request.Body))
            {
                return await sr.ReadToEndAsync();
            }
        }

      public  class WalletData
        {
            public string WalletId { get; set; }
            public double Amount { get; set; }
         public   List<Transaction> TransactionList { get; set; }
        }
        class UserSubmittedTransaction
        {
            public string publicKey { get; set; }
            public string SignatureBase64 { get; set; }
            public Transaction Transaction { get; set; }
        }
    }
}
