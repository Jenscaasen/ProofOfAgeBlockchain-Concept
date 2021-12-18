using Grpc.Core;
using Grpc.Net.Client;
using Node;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public class NegotiationService
{
    private readonly ILogger<NegotiationService> _logger;
    private DataStore _dataStore;
    private List<string> Top50Hashes = new List<string>();
    public NegotiationService(ILogger<NegotiationService> logger, DataStore dataStore)
    {
        _logger = logger;
        _dataStore = dataStore;
    }

    public void RefreshConnections()
    {
        List<NeighborData> OfflineNeighbors = new List<NeighborData>();

        if(_dataStore.Neighbors.Count > 0)
        _logger.LogInformation($"Checking if {_dataStore.Neighbors.Count} neighbors are online");

        foreach(var neighbor in _dataStore.Neighbors)
        {
            try
            {
                var client = GetClientFromPool(neighbor.IP);
                if(client == null)
                {
                    OfflineNeighbors.Add(neighbor);
                    continue;
                } else
                {
                   // client.AddNeighbor()
                }
                var pong = client.Check(new Ping());
                _logger.LogInformation($"Neightbor {neighbor.nodeId} succeeded HealthCheck");
            }catch(Exception ex)
            {
                OfflineNeighbors.Add(neighbor);
                _logger.LogInformation($"Neightbor {neighbor.nodeId} went offline. IP: {neighbor.IP}");
            }
        }
       
        foreach(var neighbor in OfflineNeighbors)
        {
           // _dataStore.Neighbors.Remove(neighbor);
        }
        if (OfflineNeighbors.Count > 0)
        {
            _logger.LogInformation($"{OfflineNeighbors.Count} are offline and removed. {_dataStore.Neighbors.Count} are online");
        }
    }

    internal async void UpdateBlockchainFrom(int index)
    {
        

    MakeTop50List();

        List<NeighborData> NeighborsToRequestFrom = new List<NeighborData>();
        NeighborsToRequestFrom.AddRange(_dataStore.Neighbors);
        _logger.LogInformation($"Requesting blockchain starting at {index} from {NeighborsToRequestFrom.Count} known nodes");

        if (Top50Hashes.Count > 0 && NeighborsToRequestFrom.Count > 0)
        {
            var Top50OnlineNeighbors = NeighborsToRequestFrom.Where(n => Top50Hashes.Contains(n.nodeId)).ToList();
            _logger.LogInformation($"In Top50, there are {Top50OnlineNeighbors.Count} online");
            if (Top50OnlineNeighbors.Count > 0) NeighborsToRequestFrom = Top50OnlineNeighbors;
            else
            {
                _logger.LogInformation($"No node in Top50 is online, reverting to {NeighborsToRequestFrom.Count} known nodes");
            }
        }
        _logger.LogInformation($"Requesting Blockchain from {NeighborsToRequestFrom.Count} nodes");
        if(NeighborsToRequestFrom.Count == 0)
        {
            _logger.LogWarning("No Nodes available, can not request blockchain from anywhere #alone");
            return;
        }
        List <AsyncUnaryCall<GetBlockChainReply>> BlockChainRequests = new List<AsyncUnaryCall<GetBlockChainReply>>();
        foreach(NeighborData neighbor in NeighborsToRequestFrom)
        {
            var client = GetClientFromPool(neighbor.IP);
            if(client != null)
            {
                BlockChainRequests.Add(client.GetBlockChainAsync(new GetBlockChainRequest { StartIndex = index }));
            }
        }
        _logger.LogInformation($"Waiting for {BlockChainRequests.Count} Blockchains to receive");
        if(BlockChainRequests.Count == 0) return;
    var BlockChains =    await Task.WhenAll(BlockChainRequests.Select(c => c.ResponseAsync));

        Dictionary<string, int> HashToCount = new Dictionary<string, int>();
        Dictionary<string, List<Block>> HashToBlockchain = new Dictionary<string, List<Block>>();
        foreach(var blockchainGrpc in BlockChains)
        {
            List<Block> BlockChain = blockchainGrpc.BlockChain.Select(b => Block.Parse(b)).ToList();

            if (!_dataStore.ValidateBlockchain(BlockChain)) continue;

            var lastBlock = BlockChain.Last();
            string lastBlockHash = HashHelper.GetHash(lastBlock);

            if (!HashToCount.ContainsKey(lastBlockHash)) HashToCount.Add(lastBlockHash, 0);
            HashToCount[lastBlockHash]++;

            if (!HashToBlockchain.ContainsKey(lastBlockHash))
            {
                
                HashToBlockchain.Add(lastBlockHash, BlockChain);
            }
        }

        string mostUsedHash = HashToCount.MaxBy(h => h.Value).Key;
        int mostUsedHashCount = HashToCount.MaxBy(h => h.Value).Value;

        var mostUsedBlockchain = HashToBlockchain[mostUsedHash];

        _logger.LogInformation($"Received {HashToCount.Count} different Blockchains, best one is present {mostUsedHashCount} times");
        if(mostUsedBlockchain.First().Index == 1)
        {
            _dataStore.BlockChain = mostUsedBlockchain;
            _dataStore.SaveNeeded = true;
        } else
        {
            _logger.LogInformation($"Adding blocks from {_dataStore.BlockChain.Last().Index} to {mostUsedBlockchain.Last().Index}");
            for(int nextNeededBlockId = _dataStore.BlockChain.Last().Index; nextNeededBlockId < mostUsedBlockchain.Last().Index; nextNeededBlockId++)
            {
                var nextNeededBlock = mostUsedBlockchain.SingleOrDefault(b=> b.Index == nextNeededBlockId);
                if(nextNeededBlock == null)
                {
                    _logger.LogError($"Tried to receive Block with ID {nextNeededBlockId}, but it was not provided after asking for Blocks {index} and onward");
                    _logger.LogError($"Received Blockchain was used {HashToCount[mostUsedHash]} times and has {mostUsedBlockchain.Count} Blocks");
                    return;
                }
                _dataStore.BlockChain.Add(nextNeededBlock);
                _dataStore.SaveNeeded = true;
            }
        }
    }

    internal void HelloToEveryone()
    {
        _logger.LogInformation($"Saying hello to {_dataStore.Neighbors.Count} neighbors");
        foreach (var neighbor in _dataStore.Neighbors)
        {
            var client = GetClientFromPool(neighbor.IP);
            if (client == null) continue;
            client.AddNeighbor(new Neighbor { IP = Globals.Kestrel_Host, NodeId = _dataStore.MyId });

            var newNeighbors = client.GetNeighbors(new GetNeighborsRequest { });
            _logger.LogInformation($"got {newNeighbors.NeighborList.Count} neighbors from {neighbor.IP}");
            foreach (var newNeighbor in newNeighbors.NeighborList)
            {
                if (newNeighbor.IP != Globals.Kestrel_Host && !_dataStore.Neighbors.Any(n => n.IP == newNeighbor.IP))
                {
                    _dataStore.Neighbors.Add(new NeighborData { IP = newNeighbor.IP, nodeId = newNeighbor.NodeId });
                }
            }
        }
    }

    internal bool AddTransaction(AddTransactionRequest request)
    {
     
        var transactionGrpc = request.TransactionToAdd;
        Transaction requestedTransaction = Transaction.Parse(transactionGrpc);
        _logger.LogInformation($"Add transaction requested, transaction: {JsonSerializer.Serialize(requestedTransaction)}");
        bool accepted = false;
        if (!_dataStore.Transactions.Contains(requestedTransaction))
        {
            if (ValidateTransaction(request))
            {
                _dataStore.Transactions.Add(requestedTransaction);
                DistributeTransaction(request);
                accepted = true;
            }
        }

        return accepted;
    }

    internal bool ValidateBlock(AddBlockRequest request)
    {
        //Step1: Ist der Absender der, der er behauptet zu sein?
        Block block = Block.Parse(request.BlockToAdd);
        var signature = request.MySignature.ToByteArray();
        var publicKey = request.MyPublicKey;
        string senderId = HashHelper.GetHash(publicKey);

        if (!HashHelper.CheckSignature(block, signature, publicKey))
        {
            _logger.LogWarning($"Validation of block failed: signature incorrect. Possibly compromised: " + senderId);
            return false;
        }

        //Step2: Ist der Block der nächste in der Reihe?
        var myLatestIndex = _dataStore.LatestValidBlock.Index;

        if (block.Index != myLatestIndex + 1)
        {
            _logger.LogWarning($"Validation of block failed: Index missmatch. Latest known Index: {myLatestIndex}. Block index: {block.Index}");
            return false;
        }

        return true;
    }

    internal bool ValidateTransaction(AddTransactionRequest addTransactionRequest)
    {
        //Step1: Ist die Transaktion korrekt signiert?
        Transaction transaction = Transaction.Parse(addTransactionRequest.TransactionToAdd);
        var signature = addTransactionRequest.MySignature.ToByteArray();
        var publicKey = addTransactionRequest.MyPublicKey;
        string senderId = HashHelper.GetHash(publicKey);

        if (!  HashHelper.CheckSignature(transaction, signature, publicKey))
        {
            _logger.LogWarning($"Validation of Transaction failed: signature incorrect. Possibly compromised: " + senderId);
            return false;
        }

      //Step2: ist der Signierer auch der Geldgeber?
     
      if(transaction.Sender != senderId)
        {
            _logger.LogWarning($"Validation of Transaction failed: Sender is not wallet owner (Sender: {senderId}, owner: {transaction.Sender}");
            return false;
        }

        ////####HACK
        /// WEIL NOCH NIEMAND IRGENDWAS HAT
        return true;

        //Step3: Hat der Geldgeber genug Geld auf der Blockchain?
        double senderFunds = 0;
      foreach(var block in _dataStore.BlockChain)
        {
            if (block.Transactions == null || block.Transactions.Count == 0) continue;
            foreach(var blockTransaction in block.Transactions)
            {
                if (blockTransaction.Sender == senderId) senderFunds -= transaction.Amount;
                if (blockTransaction.Receiver == senderId) senderFunds += transaction.Amount;
            }
        }
      if(senderFunds < transaction.Amount)
        {
            _logger.LogWarning($"Validation of Transaction failed: Wallet does not contain enough funds (Funds needed: { transaction.Amount}, Funds exist: {senderFunds}");
            return false;
        }

        return true;
    }

    internal void RequestAllBlockchains()
    {
        UpdateBlockchainFrom(1);
    }

    internal void MakeBlock()
    {
       
        Block block = new Block();

        if (_dataStore.BlockChain.Count > 0)
        {
            var previousBlock = _dataStore.BlockChain.Last();
            block.NodeIds = _dataStore.Neighbors.Select(n=> n.nodeId).ToList();
            block.Index = previousBlock.Index + 1;
            block.PreviousHash = HashHelper.GetHash(previousBlock);
            block.Transactions = new List<Transaction>();
            if(_dataStore.Transactions.Count > 0)
            {
                foreach(var transaction in _dataStore.Transactions)
                {
                    block.Transactions.Add(transaction);
                }
            }
        } else
        {
            //first one
            block.NodeIds = new List<string> { _dataStore.MyId };
            block.Index = 1;
            block.PreviousHash = "first";
            block.Transactions = new List<Transaction>();
        }
        _logger.LogInformation($"Making block with {block.NodeIds} nodeIds," +
            $" Index {block.Index}, previousHash {block.PreviousHash}, transactions: {block.Transactions.Count}");

        _dataStore.CurrentNewBlock = block;
    }

    internal async Task SyncNewestBlockAsync()
    {
        var newestBlock = _dataStore.CurrentNewBlock;

        var BlockToSync = new BlockGrpc
        {
            Index = newestBlock.Index,
            PreviousHash = newestBlock.PreviousHash

        };
        BlockToSync.NodeIds.AddRange(newestBlock.NodeIds);
        var transactionsAsGrpcObjects = newestBlock.Transactions.Select(t => new TransactionGrpc
        {
            Amount = t.Amount,
            Receiver = t.Receiver,
            Sender = t.Sender
        }).ToArray();

        BlockToSync.Transactions.Add(transactionsAsGrpcObjects);

        Block block = Block.Parse(BlockToSync);
        byte[] encryptedData = HashHelper.MakeSignature(block, _dataStore.PrivateKey);

        var addBlock = new AddBlockRequest
        {
            MyPublicKey = _dataStore.PublicKey,
            BlockToAdd = BlockToSync,
            MySignature = Google.Protobuf.ByteString.CopyFrom(encryptedData)
        };
        _logger.LogInformation($"Block to sync has {block.Transactions.Count} transactions and {block.NodeIds.Count} nodeIds");
        _logger.LogInformation($"Sending block {newestBlock.Index} to {_dataStore.Neighbors.Count} neighbors");
        foreach (var neighbor in _dataStore.Neighbors)
        {
            Discovery.DiscoveryClient client = GetClientFromPool(neighbor.IP);
            if (client != null)
            {
                await client.AddBlockAsync(addBlock);
            }
        }
    }

    Dictionary<string, Discovery.DiscoveryClient> ClientPool = new Dictionary<string, Discovery.DiscoveryClient>();
    private Discovery.DiscoveryClient GetClientFromPool(string iP)
    {
        //  _logger.LogInformation("Looking up '" + iP + "' from ConnectionPool");
        if (iP == Globals.Kestrel_Host) return null; //me

        Discovery.DiscoveryClient client;
        if(!ClientPool.TryGetValue(iP, out client))
        {
            try
            {
                var channel = GrpcChannel.ForAddress(iP, new GrpcChannelOptions { });
            
                client = new Discovery.DiscoveryClient(channel);
               
                client.Check(new Ping(), deadline: DateTime.UtcNow.AddSeconds(10));
                ClientPool.Add(iP, client);
            }catch(Exception e)
            {
                _logger.LogError($"Error connecting to {iP}");
                return null;
            }
        }
        return client;
    }

    internal bool MeIsInTop50()
    {
        string myId = _dataStore.MyId;
        return (Top50Hashes.Contains(myId));
    }

    internal void MakeTop50List()
    {
        _logger.LogInformation("Recreating the Top50 List");

    Top50Hashes.Clear();
        var previousIdLists = _dataStore.BlockChain.Select(block => block.NodeIds);
        Dictionary<string, int> IdToCount = new Dictionary<string, int>();

        foreach(var idList in previousIdLists)
        {
            foreach(string id in idList)
            {
                if (!IdToCount.ContainsKey(id)) IdToCount.Add(id, 0);
                IdToCount[id]++;
            }
        }
      var orderedList =  IdToCount.OrderByDescending(id=> id.Value).ToList();
        Top50Hashes= orderedList.Take(50).Select(entry => entry.Key).ToList();
        _logger.LogInformation($"Found {Top50Hashes.Count} top servers");
    }

    internal void DistributeTransaction(AddTransactionRequest transactionRequestToDistribute)
    {
        _logger.LogInformation($"Distributing transaction to {_dataStore.Neighbors}");
        foreach(var neighbor in _dataStore.Neighbors)
        {
            var client = GetClientFromPool(neighbor.IP);
            if (client != null)
            {
                client.AddTransactionAsync(new AddTransactionRequest { 
                    TransactionToAdd = new TransactionGrpc(transactionRequestToDistribute.TransactionToAdd), 
                    MyPublicKey = transactionRequestToDistribute.MyPublicKey, 
                    MySignature = transactionRequestToDistribute.MySignature
                });
            }
        }
    }

    internal void DistributeNeighbor(Neighbor request)
    {
        _logger.LogInformation($"Distributing transaction to {_dataStore.Neighbors}");
        foreach (var neighbor in _dataStore.Neighbors)
        {
            if (neighbor.IP == request.IP) continue;
            var client = GetClientFromPool(neighbor.IP);
            if (client != null)
            {
                client.AddNeighborAsync(request);
            }
        }
    }

    internal bool NodeIsInTop50(ServerCallContext context)
    {
        var ip = context.Host;
    var existingNeighbor =    _dataStore.Neighbors.FirstOrDefault(n => n.IP == ip);
        if (existingNeighbor == null) return false;
       
            return (Top50Hashes.Contains(existingNeighbor.nodeId));
       
    }
}