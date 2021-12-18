using Grpc.Core;
using Node;

namespace Node.Services
{
    public class DiscoveryService : Discovery.DiscoveryBase
    {
        private readonly ILogger<DiscoveryService> _logger;
        private DataStore _dataStore;
        private NegotiationService _negotiationService;
        public DiscoveryService(ILogger<DiscoveryService> logger, DataStore dataStore, NegotiationService negotiationService)
        {
            _logger = logger;
            _dataStore = dataStore;
            _negotiationService = negotiationService;
        }


        public override Task<GetBlockChainReply> GetBlockChain(GetBlockChainRequest request, ServerCallContext context)
        {
            GetBlockChainReply reply = new GetBlockChainReply();
            _logger.LogInformation("My blockchain is requested. Startindex: " + request.StartIndex);
           var blocksToSend = _dataStore.BlockChain.Where(b=> b.Index >= request.StartIndex).OrderBy(b=>b.Index).ToList();

            _logger.LogInformation($"Preparing to send {blocksToSend.Count} blocks");
            foreach(var block in blocksToSend)
            {
                var blockGrpc = new BlockGrpc { Index = block.Index };
               blockGrpc.NodeIds.AddRange(block.NodeIds);
                blockGrpc.PreviousHash = block.PreviousHash;
                blockGrpc.Transactions.AddRange(block.Transactions.Select(t => t.toGrpc()));
                reply.BlockChain.Add(blockGrpc);
            }

            return Task.FromResult(reply);

        }
        public override Task<NeighborsReply> GetNeighbors(GetNeighborsRequest request, ServerCallContext context)
        {
            NeighborsReply neighbors = new NeighborsReply();
            List<Neighbor> neighborsList = new List<Neighbor>();
            foreach(var neighborData in _dataStore.Neighbors)
            {
                neighborsList.Add(new Neighbor {IP = neighborData.IP, NodeId = neighborData.nodeId });
            }
            neighbors.NeighborList.AddRange(neighborsList);
            return Task.FromResult(neighbors);
        }

        public override Task<AddTransactionReply> AddTransaction(AddTransactionRequest request, ServerCallContext context)
        {

            bool accepted = _negotiationService.AddTransaction(request);
        
            return Task.FromResult(new AddTransactionReply { Accepted= accepted });
        }

        public override Task<AddNeighborReply> AddNeighbor(Neighbor request, ServerCallContext context)
        {
            NeighborData neighborData = new NeighborData { IP = request.IP, nodeId = request.NodeId };
            if (!_dataStore.Neighbors.Contains(neighborData))
            {
                _logger.LogInformation("New neighbor received: " + neighborData.IP);
                _dataStore.Neighbors.Add(neighborData);
                _dataStore.SaveNeeded = true;
                _negotiationService.DistributeNeighbor(request);
            }
            return Task.FromResult(new AddNeighborReply { });
        }

        public override Task<Pong> Check(Ping request, ServerCallContext context)
        {
           return Task.FromResult(new Pong());
           
        }
        public override Task<AddBlockReply> AddBlock(AddBlockRequest request, ServerCallContext context)
        {

            if (_negotiationService.ValidateBlock(request))
            {
                if (_negotiationService.NodeIsInTop50(context))
                {
                    _dataStore.AddBlockByMajority(request.BlockToAdd);
                }
            }
            return Task.FromResult(new AddBlockReply { });
        }
    }
}