﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NBitcoin.BouncyCastle.Math;
using NBitcoin.Crypto;
using System.IO;

namespace NBitcoin
{
	class ProofOfStakeKernel
	{
		const int MODIFIER_INTERVAL_RATIO = 3;
		// .Net port of 
		// Copyright (c) 2012-2013 The PPCoin developers
		// Distributed under the MIT/X11 software license, see the accompanying
		// file COPYING or http://www.opensource.org/licenses/mit-license.php.


		// Hard checkpoints of stake modifiers to ensure they are deterministic
		static List<KeyValuePair<int, uint>> mapStakeModifierCheckpoints =
			new List<KeyValuePair<int, uint>> {new KeyValuePair<int, uint>(0, 0x0e00670bu)}
			
		;

		// Hard checkpoints of stake modifiers to ensure they are deterministic (testNet)
		static List<KeyValuePair<int, uint>> mapStakeModifierCheckpointsTestNet =
			new List<KeyValuePair<int, uint>> { new KeyValuePair<int, uint>(0, 0x0e00670bu) }
		;

	// Get time weight
	TimeSpan GetWeight(Consensus consensus, DateTimeOffset intervalBeginning, DateTimeOffset intervalEnd)
	{
			// Kernel hash weight starts from 0 at the min age
			// this change increases active coins participating the hash and helps
			// to secure the network when proof-of-stake difficulty is low
			var weight = (intervalEnd - intervalBeginning) - consensus.StakeMinAge;
			if (weight < consensus.StakeMaxAge)
				return weight;
			else
				return consensus.StakeMaxAge;
	}

	// Get the last stake modifier and its generation time from a given block
	static bool GetLastStakeModifier(ChainedBlock pindex, out ulong nStakeModifier, out ulong nModifierTime)
	{
		if (pindex==null)
			throw new System.Exception("GetLastStakeModifier: null pindex");
		while (pindex != null && pindex.Previous != null && !pindex.GeneratedStakeModifier())
			pindex = pindex.Previous;
		if (!pindex.GeneratedStakeModifier())
			throw new System.Exception("GetLastStakeModifier: no generation at genesis block");
		nStakeModifier = pindex.nStakeModifier;
		nModifierTime =Utils.DateTimeToUnixTimeLong( pindex.Header.BlockTime);
		return true;
	}

	// Get selection interval section (in seconds)
	static long GetStakeModifierSelectionIntervalSection(Consensus consensus, int nSection)
	{
		System.Diagnostics.Debug.Assert(nSection >= 0 && nSection < 64);
		return (consensus.StakeModifierIntervalSeconds * 63 / (63 + ((63 - nSection) * (MODIFIER_INTERVAL_RATIO - 1))));
	}

	// Get stake modifier selection interval (in seconds)
	static long GetStakeModifierSelectionInterval(Consensus consensus)
	{
		long nSelectionInterval = 0;
		for (int nSection = 0; nSection < 64; nSection++)
			nSelectionInterval += GetStakeModifierSelectionIntervalSection(consensus, nSection);
		return nSelectionInterval;
	}

	// select a block from the candidate blocks in vSortedByTimestamp, excluding
	// already selected blocks in vSelectedBlocks, and with timestamp up to
	// nSelectionIntervalStop.
	static bool SelectBlockFromCandidates(ConcurrentChain chain, ChainedBlock chainIndex, SortedDictionary<long,uint256> sortedByTimestamp, Dictionary<uint256, ChainedBlock> mapSelectedBlocks,
		DateTimeOffset selectionIntervalStop, ulong nStakeModifierPrev, out ChainedBlock pindexSelected)
	{
		bool fSelected = false;
		uint256 hashBest = 0;
		pindexSelected = null;

		foreach( var item in sortedByTimestamp )
		{
			var pindex = chainIndex.FindAncestorOrSelf(item.Value);

				if (pindex == null)
					return false; // error("SelectBlockFromCandidates: failed to find block index for candidate block {0}", item.Item2.ToString()));

			if (fSelected && pindex.Header.BlockTime > selectionIntervalStop)
				break;
			if (mapSelectedBlocks.ContainsKey(pindex.HashBlock) )
				continue;
				// compute the selection hash by hashing its proof-hash and the
				// previous proof-of-stake modifier
				uint256 hashSelection;
				using (var ms = new MemoryStream())
				{
					var serializer = new BitcoinStream(ms, true);
					serializer.ReadWrite(pindex.hashProof);
					serializer.ReadWrite(nStakeModifierPrev);

					hashSelection = Hashes.Hash256(ms.ToArray());
				}
				// the selection hash is divided by 2**32 so that proof-of-stake block
				// is always favored over proof-of-work block. this is to preserve
				// the energy efficiency property
				if (pindex.IsProofOfStake())
				hashSelection >>= 32;

			if (fSelected && hashSelection < hashBest)
			{
				hashBest = hashSelection;
				pindexSelected = pindex;
			}
			else if (!fSelected)
			{
				fSelected = true;
				hashBest = hashSelection;
				pindexSelected = pindex;
			}
		}

			//if (fDebug && GetBoolArg("-printstakemodifier"))
			//	printf("SelectBlockFromCandidates: selection hash=%s\n", hashBest.ToString().c_str());

			return fSelected;
	}

	// Stake Modifier (hash modifier of proof-of-stake):
	// The purpose of stake modifier is to prevent a txout (coin) owner from
	// computing future proof-of-stake generated by this txout at the time
	// of transaction confirmation. To meet kernel protocol, the txout
	// must hash with a future stake modifier to generate the proof.
	// Stake modifier consists of bits each of which is contributed from a
	// selected block of a given block group in the past.
	// The selection of a block is based on a hash of the block's proof-hash and
	// the previous stake modifier.
	// Stake modifier is recomputed at a fixed time interval instead of every 
	// block. This is to make it difficult for an attacker to gain control of
	// additional bits in the stake modifier, even after generating a chain of
	// blocks.
	public static bool ComputeNextStakeModifier(Consensus consensus, ConcurrentChain chain, ChainedBlock pindexPrev, out ulong nStakeModifier, out bool fGeneratedStakeModifier)
	{
		nStakeModifier = 0;
		fGeneratedStakeModifier = false;
		if (pindexPrev==null)
		{
			fGeneratedStakeModifier = true;
			return true;  // genesis block's modifier is 0
		}
		// First find current stake modifier and its generation block time
		// if it's not old enough, return the same stake modifier
		ulong nModifierTime = 0;
		if (!GetLastStakeModifier(pindexPrev,out nStakeModifier, out nModifierTime))
			throw new System.Exception("ComputeNextStakeModifier: unable to get last modifier");

		if (nModifierTime / (ulong)consensus.StakeModifierIntervalSeconds >= Utils.DateTimeToUnixTimeLong(pindexPrev.Header.BlockTime) / (ulong)consensus.StakeModifierIntervalSeconds)
			return true;

			// Sort candidate blocks by timestamp
			var vSortedByTimestamp = new SortedDictionary<long, uint256>();
		long nSelectionInterval = GetStakeModifierSelectionInterval(consensus);
		long nSelectionIntervalStart = ((long)Utils.DateTimeToUnixTimeLong(pindexPrev.Header.BlockTime) / consensus.StakeModifierIntervalSeconds) * consensus.StakeModifierIntervalSeconds - nSelectionInterval;
		ChainedBlock pindex = pindexPrev;
		while ((pindex!=null) && (long)Utils.DateTimeToUnixTimeLong(pindexPrev.Header.BlockTime) >= nSelectionIntervalStart)
		{
			vSortedByTimestamp.Add((long)Utils.DateTimeToUnixTimeLong(pindexPrev.Header.BlockTime), pindex.Header.GetHash());
			pindex = pindex.Previous;
		}
		int nHeightFirstCandidate = (pindex!=null) ? (pindex.Height + 1) : 0;

		// Select 64 blocks from candidate blocks to generate stake modifier
		ulong nStakeModifierNew = 0;
		long nSelectionIntervalStop = nSelectionIntervalStart;
		var mapSelectedBlocks = new Dictionary<uint256, ChainedBlock>();
		for (int nRound = 0; nRound < Math.Min(64, vSortedByTimestamp.Count()); nRound++)
		{
			// add an interval section to the current selection round
			nSelectionIntervalStop += GetStakeModifierSelectionIntervalSection(consensus,nRound);
				// select a block from the candidates of current round
				if (!SelectBlockFromCandidates(chain, pindexPrev, vSortedByTimestamp, mapSelectedBlocks, Utils.UnixTimeToDateTime(nSelectionIntervalStop), nStakeModifier, out pindex))
					return false; //	throw new System.Exception(String.Format("ComputeNextStakeModifier: unable to select block at round {0}", nRound));
			// write the entropy bit of the selected block
			nStakeModifierNew |= (((ulong)pindex.GetStakeEntropyBit()) << nRound);
			// add the selected block from candidates to selected list
			mapSelectedBlocks.AddOrReplace(pindex.Header.GetHash(), pindex);
				//TODO: Netcoin POS logging
			//if (fDebug && GetBoolArg("-printstakemodifier"))
			//	printf("ComputeNextStakeModifier: selected round %d stop=%s height=%d bit=%d\n", nRound, DateTimeStrFormat(nSelectionIntervalStop).c_str(), pindex.nHeight, pindex.GetStakeEntropyBit());
		}
			//TODO: Netcoin POS logging
			/*
			// Print selection map for visualization of the selected blocks
			if (fDebug && GetBoolArg("-printstakemodifier"))
		{
			string strSelectionMap = "";
			// '-' indicates proof-of-work blocks not selected
			strSelectionMap.insert(0, pindexPrev.nHeight - nHeightFirstCandidate + 1, '-');
			pindex = pindexPrev;
			while (pindex && pindex.nHeight >= nHeightFirstCandidate)
			{
				// '=' indicates proof-of-stake blocks not selected
				if (pindex.IsProofOfStake())
					strSelectionMap.replace(pindex.nHeight - nHeightFirstCandidate, 1, "=");
				pindex = pindex.Previous;
			}
			BOOST_FOREACH(const PAIRTYPE(uint256, ChainedBlock)&item, mapSelectedBlocks)
        {
				// 'S' indicates selected proof-of-stake blocks
				// 'W' indicates selected proof-of-work blocks
				strSelectionMap.replace(item.second.nHeight - nHeightFirstCandidate, 1, item.second.IsProofOfStake() ? "S" : "W");
			}
			printf("ComputeNextStakeModifier: selection height [%d, %d] map %s\n", nHeightFirstCandidate, pindexPrev.nHeight, strSelectionMap.c_str());
		}
		if (fDebug)
		{
			printf("ComputeNextStakeModifier: new modifier=0x%016"PRI64x" time=%s\n", nStakeModifierNew, DateTimeStrFormat(pindexPrev.GetBlockTime()).c_str());
		}
		*/
		nStakeModifier = nStakeModifierNew;
		fGeneratedStakeModifier = true;
		return true;
	}

	// The stake modifier used to hash for a stake kernel is chosen as the stake
	// modifier about a selection interval later than the coin generating the kernel
	static bool GetKernelStakeModifier(Consensus consensus, ConcurrentChain chain, uint256 hashBlockFrom, out ulong nStakeModifier, out int nStakeModifierHeight, out DateTimeOffset nStakeModifierTime, bool fPrintProofOfStake)
	{
		nStakeModifier = 0;
		if (!chain.Contains(hashBlockFrom))
			throw new System.Exception("GetKernelStakeModifier() : block not indexed");
		ChainedBlock pindexFrom = chain.GetBlock(hashBlockFrom);
		nStakeModifierHeight = pindexFrom.Height;
		nStakeModifierTime = pindexFrom.Header.BlockTime;
		long nStakeModifierSelectionInterval = GetStakeModifierSelectionInterval(consensus);
		ChainedBlock pindex = pindexFrom;
		// loop to find the stake modifier later by a selection interval
		while (nStakeModifierTime < (pindexFrom.Header.BlockTime + TimeSpan.FromSeconds(nStakeModifierSelectionInterval)))
		{
			if (!pindex.pnext)
			{   // reached best block; may happen if node is behind on block chain
				if (fPrintProofOfStake || (pindex.Header.BlockTime.Add(consensus.StakeMinAge).AddSeconds(-nStakeModifierSelectionInterval) > GetAdjustedTime()))
					throw new System.Exception(string.Format("GetKernelStakeModifier() : reached best block {0} at height {1} from block {2}",
						pindex.Header.GetHash().ToString(), pindex.Height, hashBlockFrom.ToString()));
				else
					return false;
			}
			pindex = pindex.pnext;
			if (pindex.GeneratedStakeModifier())
			{
				nStakeModifierHeight = pindex.Height;
				nStakeModifierTime = pindex.Header.BlockTime;
			}
		}
		nStakeModifier = pindex.nStakeModifier;
		return true;
	}

	// ppcoin kernel protocol
	// coinstake must meet hash target according to the protocol:
	// kernel (input 0) must meet the formula
	//     hash(nStakeModifier + txPrev.block.nTime + txPrev.offset + txPrev.nTime + txPrev.vout.n + nTime) < bnTarget * nCoinDayWeight
	// this ensures that the chance of getting a coinstake is proportional to the
	// amount of coin age one owns.
	// The reason this hash is chosen is the following:
	//   nStakeModifier: scrambles computation to make it very difficult to precompute
	//                  future proof-of-stake at the time of the coin's confirmation
	//   txPrev.block.nTime: prevent nodes from guessing a good timestamp to
	//                       generate transaction for future advantage
	//   txPrev.offset: offset of txPrev inside block, to reduce the chance of 
	//                  nodes generating coinstake at the same time
	//   txPrev.nTime: reduce the chance of nodes generating coinstake at the same
	//                 time
	//   txPrev.vout.n: output number of txPrev, to reduce the chance of nodes
	//                  generating coinstake at the same time
	//   block/tx hash should not be used here as they can be generated in vast
	//   quantities so as to generate blocks faster, degrading the system back into
	//   a proof-of-work situation.
	//
	bool CheckStakeKernelHash(Consensus consensus,ConcurrentChain chain, uint nBits, ChainedBlock blockFrom, uint nTxPrevOffset, Transaction txPrev, OutPoint prevout, DateTimeOffset nTimeTx, uint256 hashProofOfStake, Target targetProofOfStake, bool fPrintProofOfStake)
	{
		DateTimeOffset nTimeBlockFrom = blockFrom.Header.BlockTime;
		if (nTimeBlockFrom + consensus.StakeMinAge > nTimeTx) // Min age requirement
			throw new System.Exception("CheckStakeKernelHash() : min age violation");

		BigInteger bnTargetPerCoinDay;
		bnTargetPerCoinDay= new Target(nBits).ToBigInteger();
			Money nValueIn = txPrev.Outputs[prevout.N].Value;

		uint256 hashBlockFrom = blockFrom.Header.GetHash();
			
		BigInteger bnCoinDayWeight = BigInteger.ValueOf(nValueIn.Satoshi)
				.Multiply(BigInteger.ValueOf(GetWeight(consensus, nTimeBlockFrom, nTimeTx).Ticks / TimeSpan.TicksPerSecond)) // stake time weight in seconds
				.Divide(BigInteger.ValueOf(Money.COIN * (24 * 60 * 60))); // coindays
		targetProofOfStake = new Target(bnCoinDayWeight.Multiply(bnTargetPerCoinDay));
		// Calculate hash
		ulong nStakeModifier = 0;
		int nStakeModifierHeight = 0;
		DateTimeOffset nStakeModifierTime = Utils.UnixTimeToDateTime(0);

		if (!GetKernelStakeModifier(consensus, chain, hashBlockFrom,out nStakeModifier,out nStakeModifierHeight,out nStakeModifierTime, fPrintProofOfStake))
			return false;
		
			using (var ms = new MemoryStream())
			{
				var serializer = new BitcoinStream(ms, true);

				serializer.ReadWrite(nStakeModifier);
				serializer.ReadWrite(Utils.DateTimeToUnixTime(nTimeBlockFrom));
				

				serializer.ReadWrite(nTxPrevOffset);
				serializer.ReadWrite(Utils.DateTimeToUnixTime(nTimeBlockFrom));
				serializer.ReadWrite(prevout.N);
				serializer.ReadWrite(Utils.DateTimeToUnixTime(nTimeTx));

				hashProofOfStake = Hashes.Hash256(ms.ToArray());
			}
			/* TODO: Netcoin - POS logging
			if (fPrintProofOfStake)
		{
			printf("CheckStakeKernelHash() : using modifier 0x%016"PRI64x" at height=%d timestamp=%s for block from height=%d timestamp=%s\n",
				nStakeModifier, nStakeModifierHeight,
				DateTimeStrFormat(nStakeModifierTime).c_str(),
				mapBlockIndex[hashBlockFrom].nHeight,
				DateTimeStrFormat(blockFrom.GetBlockTime()).c_str());
			printf("CheckStakeKernelHash() : check modifier=0x%016"PRI64x" nTimeBlockFrom=%u nTxPrevOffset=%u nTimeTxPrev=%u nPrevout=%u nTimeTx=%u hashProof=%s\n",
				nStakeModifier,
				nTimeBlockFrom, nTxPrevOffset, nTimeBlockFrom, prevout.n, nTimeTx,
				hashProofOfStake.ToString().c_str());
		}
		*/
			// Now check if proof-of-stake hash meets target protocol

			if (new BigInteger(hashProofOfStake.ToString()).CompareTo( bnCoinDayWeight.Multiply( bnTargetPerCoinDay)) > 0 )
			return false;
		/* TODO: Netcoin - POS logging
		if (fDebug && !fPrintProofOfStake)
		{
			printf("CheckStakeKernelHash() : using modifier 0x%016"PRI64x" at height=%d timestamp=%s for block from height=%d timestamp=%s\n",
				nStakeModifier, nStakeModifierHeight,
				DateTimeStrFormat(nStakeModifierTime).c_str(),
				mapBlockIndex[hashBlockFrom].nHeight,
				DateTimeStrFormat(blockFrom.GetBlockTime()).c_str());
			printf("CheckStakeKernelHash() : pass modifier=0x%016"PRI64x" nTimeBlockFrom=%u nTxPrevOffset=%u nTimeTxPrev=%u nPrevout=%u nTimeTx=%u hashProof=%s\n",
				nStakeModifier,
				nTimeBlockFrom, nTxPrevOffset, nTimeBlockFrom, prevout.n, nTimeTx,
				hashProofOfStake.ToString().c_str());
		}
		*/
		return true;
	}
		private static bool VerifySignature(Transaction txFrom, Transaction txTo, int txToInN, ScriptVerify flagScriptVerify)
		{
			var input = txTo.Inputs[txToInN];

			if (input.PrevOut.N >= txFrom.Outputs.Count)
				return false;

			if (input.PrevOut.Hash != txFrom.GetHash())
				return false;

			var output = txFrom.Outputs[input.PrevOut.N];

			var txData = new PrecomputedTransactionData(txFrom);
			var checker = new TransactionChecker(txTo, txToInN, output.Value, txData);
			var ctx = new ScriptEvaluationContext { ScriptVerify = flagScriptVerify };

			return ctx.VerifyScript(input.ScriptSig, output.ScriptPubKey, checker);
		}
		// Check kernel hash target and coinstake signature
		//		bool CheckProofOfStake(Transaction tx, DateTimeOffset txTime, uint nBits, uint256 hashProofOfStake, uint256 targetProofOfStake)
		public static bool CheckProofOfStake(IBlockRepository blockStore, ITransactionRepository transactionStore, IBlockTransactionMapStore mapStore,
			 ChainedBlock pindexPrev, ChainedBlock prevBlockStake, Transaction tx, uint nBits, out uint256 hashProofOfStake, out uint256 targetProofOfStake)

		{
			targetProofOfStake = null; hashProofOfStake = null;
			if (!tx.IsCoinStake())
				return false;  // throw new System.Exception(string.Format("CheckProofOfStake() : called on non-coinstake {0}", tx.GetHash().ToString()));

		// Kernel (input 0) must match the stake hash target per coin age (nBits)
		TxIn txIn = tx.Inputs[0];
	
			// First try finding the previous transaction in database

			Transaction txPrev = transactionStore.Get(txIn.PrevOut.Hash);
			if (txPrev == null)
				return false; // tx.DoS(1, error("CheckProofOfStake() : INFO: read txPrev failed"));  // previous transaction not in main chain, may occur during initial download

			// Verify signature
			if (!VerifySignature(txPrev, tx, 0, ScriptVerify.None))
				return false; // tx.DoS(100, error("CheckProofOfStake() : VerifySignature failed on coinstake %s", tx.GetHash().ToString()));

			// Read block header
			
			uint256 blockHashPrev = mapStore.GetBlockHash(txIn.PrevOut.Hash);
			Block block = (blockHashPrev == null) ? null : blockStore.GetBlock(blockHashPrev);
			if (block == null)
				return false; //fDebug? error("CheckProofOfStake() : read block failed") : false; // unable to read block of previous transaction


			// Min age requirement
			// TODO Netcoin When / How to implement Netcoin POS Version 3 Hard fork??
			/*
			if (IsProtocolV3((int)tx.Time))
			{
				int nDepth = 0;
				if (IsConfirmedInNPrevBlocks(blockStore, txPrev, pindexPrev, StakeMinConfirmations - 1, ref nDepth))
					return false; // tx.DoS(100, error("CheckProofOfStake() : tried to stake at depth %d", nDepth + 1));
			}
			*/
			if (!CheckStakeKernelHash(pindexPrev, nBits, block, txPrev, prevBlockStake, txIn.PrevOut, tx.Time, out hashProofOfStake, out targetProofOfStake, false))
				return false; // tx.DoS(1, error("CheckProofOfStake() : INFO: check kernel failed on coinstake %s, hashProof=%s", tx.GetHash().ToString(), hashProofOfStake.ToString())); // may occur during initial download or if behind on block chain sync

			if (!CheckStakeKernelHash(nBits, block, txindex.pos.nTxPos - txindex.pos.nBlockPos, txPrev, txin.prevout, txTime, hashProofOfStake, targetProofOfStake, fDebug))
			return tx.DoS(1, error("CheckProofOfStake() : INFO: check kernel failed on coinstake %s, hashProof=%s", tx.GetHash().ToString(), hashProofOfStake.ToString())); // may occur during initial download or if behind on block chain sync

		return true;
	}

	// Check whether the coinstake timestamp meets protocol
	bool CheckCoinStakeTimestamp(long nTimeBlock, long nTimeTx)
	{
		// v0.3 protocol
		return (nTimeBlock == nTimeTx);
	}

	// Get stake modifier checksum
	

	// Check stake modifier hard checkpoints
	public static bool CheckStakeModifierCheckpoints(Consensus consensus, int nHeight, uint nStakeModifierChecksum)
	{
		MapModifierCheckpoints  checkpoints = (TestNet() ? mapStakeModifierCheckpointsTestNet : mapStakeModifierCheckpoints);

		if (checkpoints.count(nHeight))
			return nStakeModifierChecksum == checkpoints[nHeight];
		return true;
	}

	}
}
