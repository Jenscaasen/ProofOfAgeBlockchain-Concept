using System.Security.Cryptography;
using System.Text.Json;
using static Node.AppInterfaceService;

namespace Node
{
    public class DataStore: IDisposable
    {
        public List<Block> BlockChain { get; set; } = new List<Block>();
        public List<Transaction> Transactions { get; set; } = new List<Transaction>();
        public List<NeighborData> Neighbors { get; set; } = new List<NeighborData>();
        public string MyId { get { return HashHelper.GetHash(PublicKey); } }
        public string PublicKey { get; internal set; }
        public string PrivateKey { get; internal set; }
        public Block LatestValidBlock { get {
                if (BlockChain != null && BlockChain.Count > 0)
                    return BlockChain.Last();
                else
                    return null;
            } }
        public Block CurrentNewBlock { get;  set; }
        public bool SaveNeeded = false;

        private ILogger<DataStore> _logger;
        string configPath = @"blockchain_config.json";
        public DataStore(ILogger<DataStore> logger)
        {
            _logger = logger;
            if (File.Exists(configPath))
            {
                string jsonConf = File.ReadAllText(configPath);
           var config =     JsonSerializer.Deserialize<Config>(jsonConf);

                if (config != null)
                {
                    this.Neighbors = config.Neighbors;
                    this.PrivateKey = config.PrivateKey;
                    this.PublicKey = config.PublicKey;
                    this.BlockChain = config.BlockChain;

                    _logger.LogInformation("Blockchain loaded from config. Newest Index: " + LatestValidBlock.Index + ". My Id: " + MyId);
                } else
                {
                    _logger.LogError("blockchain_config.json exists, but can not be loaded. Please check or reset the file by deleting it");
                }
            } else
            {
                this.Neighbors = new List<NeighborData> { new NeighborData {IP = "https://localhost:1551", nodeId = "3E-72-D3-B5-98-AE-A0-C8-2E-D1-00-23-36-CD-5C-D0" } };
                RSA rsa = RSA.Create();
                this.PublicKey = rsa.ToXmlString(false);
                this.PrivateKey = rsa.ToXmlString(true);
                SaveConfig();
                _logger.LogInformation("Fresh config created. New Id: " + MyId);
            }
        }

        internal WalletData GetWalletData(string walletId)
        {
            WalletData data = new WalletData() { WalletId = walletId, Amount = 0 };
            data.TransactionList = new List<Transaction>();
            foreach(var block in BlockChain)
            {
                foreach(var transaction in block.Transactions)
                {
                    if(transaction.Receiver == walletId)
                    {
                        data.TransactionList.Add(transaction);
                        data.Amount += transaction.Amount;
                    }
                    if(transaction.Sender == walletId)
                    {
                        data.TransactionList.Add(transaction);
                        data.Amount -= transaction.Amount;
                    }
                }
            }
            return data;
        }

        private Dictionary<string, int> NewBlockHashCount = new Dictionary<string, int>();

        internal void AddBlockByMajority(BlockGrpc blockGrpc)
        {
            Block block = Block.Parse(blockGrpc);
            block.Transactions = block.Transactions.OrderBy(b=> b.Sender).ThenBy(b=> b.Receiver).ThenBy(b=> b.Amount).ToList();
           if(!(blockGrpc.Index == LatestValidBlock.Index+1))
            {
                _logger.LogWarning($"A node tried to add a Block with Index {blockGrpc.Index}, but the known Index is at {BlockChain.Last().Index}");
            }
         var blockHash =   HashHelper.GetHash(blockGrpc);
            if (!NewBlockHashCount.ContainsKey(blockHash)) NewBlockHashCount.Add(blockHash, 0);
            NewBlockHashCount[blockHash]++;

            CurrentNewBlock = block;
            var mostUsedHash = NewBlockHashCount.MaxBy(b => b.Value).Key;

            if(blockHash == mostUsedHash)
            {
                CurrentNewBlock.UpdateWith(block);
            }
        }

        internal void CheckInLastBlock()
        {
            if (CurrentNewBlock != null && !BlockChain.Any( b=> b.Index == CurrentNewBlock.Index))
            {
                BlockChain.Add(CurrentNewBlock);
                _logger.LogInformation($"New Block added with Index " + CurrentNewBlock.Index);
                SaveNeeded = true;
            }
        }

        internal void CleanupTransactions()
        {
            if (LatestValidBlock == null) return;
            var LatestAddestTransactions = LatestValidBlock.Transactions;
            int count = 0;
          if(LatestAddestTransactions == null) return;
            foreach (var transaction in LatestAddestTransactions)
            {
                if(Transactions.Contains(transaction))
                {
                    Transactions.Remove(transaction);
                    count++;
                }
            }
            if(count > 0)
            {
                _logger.LogInformation($"{count} transactions were added with the last block. There are {Transactions.Count} transactions left in memory");
                SaveNeeded = true;
            }
        }

        internal bool ValidateBlockchain(List<Block> blockChain)
        {
           for(int index = 1; index < blockChain.Count; index++)
            {
                var block = blockChain[index];                
                var previousBlock = blockChain[index - 1];

            var previousHash =    HashHelper.GetHash(previousBlock);
                if(block.PreviousHash != previousHash)
                {
                    string json = JsonSerializer.Serialize(previousBlock);
                    _logger.LogWarning($"Invalid Blockchain: Index {block.Index} has a previousHash of {block.PreviousHash}, but Block {previousBlock.Index} has {previousHash} as hash");
                    _logger.LogWarning("PreviousBlock is: " + json);
                    return false;
                }
            }
            return true;
        }

        public void Dispose()
        {
            SaveConfig();
        }

        public void SaveConfig()
        {
            Config conf = new Config
            {
                PublicKey = PublicKey,
                PrivateKey = PrivateKey,
                BlockChain = BlockChain,
                Neighbors = Neighbors
            };
            //todo: https://docs.microsoft.com/en-us/dotnet/standard/security/how-to-store-asymmetric-keys-in-a-key-container
            string jsonConf = JsonSerializer.Serialize(conf, new JsonSerializerOptions { WriteIndented = true});
            File.WriteAllText(configPath, jsonConf);
            _logger.LogInformation($"blockchain_config.json saved ({jsonConf.Length} bytes)");
        }

        private class Config
        {
            public string PublicKey { get; set; }
            public string PrivateKey { get; set; }
            public List<NeighborData> Neighbors { get; set; }
            public List<Block> BlockChain { get; set; }
        }
    }

    public class NeighborData: IEquatable<NeighborData>
    {
        public string IP { get; set; }
        public string nodeId { get; set; }
        public bool Equals(NeighborData? other)
        {
            if(other == null) return false;
            if(ReferenceEquals(this, other)) return true;
            return (this.IP == other.IP);
        }
    }
    public class Transaction: IEquatable<Transaction>
    {
        public string Sender { get; set; }
        public string Receiver { get; set; }
        public double Amount { get; set; }

        public bool Equals(Transaction? other)
        {
           if(other == null) return false;
           Transaction oTransaction = (Transaction)other;
            return (this.Sender == oTransaction.Sender && this.Receiver == oTransaction.Receiver && this.Amount == oTransaction.Amount);
        }

        internal static Transaction Parse(TransactionGrpc t)
        {
            Transaction transaction = new Transaction { Sender = t.Sender,
            Receiver = t.Receiver,
            Amount = t.Amount
            };

            return transaction;
        }

        internal TransactionGrpc toGrpc()
        {
            return new TransactionGrpc { Amount = this.Amount, Receiver = this.Receiver, Sender = this.Sender };
        }
    }

    public class Block
    {
        public int Index { get; set; }
        public List<Transaction> Transactions { get; set; } = new List<Transaction>();
        public List<string> NodeIds { get; set; } = new List<string>();
        public string PreviousHash { get; set; }

        internal static Block Parse(BlockGrpc blockGrpc)
        {
            return new Block { Index = blockGrpc.Index, 
                NodeIds = blockGrpc.NodeIds.ToList(), 
                PreviousHash = blockGrpc.PreviousHash, 
                Transactions = blockGrpc.Transactions.Select(t=> Transaction.Parse(t)).ToList()
            };
        }

        internal void UpdateWith(Block block)
        {
            this.Index = block.Index;
            this.NodeIds = block.NodeIds.ToList();
            this.PreviousHash = block.PreviousHash;
            this.Transactions = block.Transactions;
            
        }
    }
}
