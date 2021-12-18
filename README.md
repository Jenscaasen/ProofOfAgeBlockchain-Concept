# ProofOfAgeBlockchain-Concept

This is a full .NET 6 based implementation of a blockchain that uses Proof of Age as a consensus mechanism.

Proof of Age works by selecting the 50 most active nodes in the network. The majority of those 50 nodes have the authority to negotiate new blocks.
To keep track of the age of a node the activity is saved on the blockchain.

To run the code, first compile the "node" project, and run it node.exe --urls "https://localhost:port"

Synchronize multiple nodes by letting one of the nodes know the address of a different node in the blockchain_config.json

Nodes use "gRPC" to synchronized, clients have a REST based interface
