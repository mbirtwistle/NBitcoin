using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MersenneTwister;
using NBitcoin.BouncyCastle.Math;

namespace NBitcoin
{
	public partial class ChainedBlock
	{
		#region POS


		uint nFlags;  // ppcoin: block index flags

		private const uint BLOCK_PROOF_OF_STAKE = (1 << 0); // is proof-of-stake block
		private const uint BLOCK_STAKE_ENTROPY = (1 << 1); // entropy bit for stake modifier
		private const uint BLOCK_STAKE_MODIFIER = (1 << 2); // regenerated stake modifier


		public UInt64 nStakeModifier; // hash modifier for proof-of-stake
		uint nStakeModifierChecksum; // checksum of index; in-memeory only

		// proof-of-stake specific fields

		DateTimeOffset nStakeTime;
		OutPoint prevoutStake;
		uint256 hashProof;
		#endregion




		private int nBestHeight = -1;

		uint256 nBestChainTrust = 0;
		uint256 nBestInvalidTrust = 0;

		uint256 hashBestChain = 0;

		Int64 nTimeBestReceived = 0;


		private const int BLOCK_HEIGHT_KGW_START = 218500;
		private const int BLOCK_HEIGHT_POS_AND_DIGISHIELD_START = 420000;
		private const int BLOCK_HEIGHT_DIGISHIELD_FIX_START = 438500;
		private const int BLOCK_HEIGHT_FINALPOW = 2500000;
		private const int LOW_S_CHECK_SIGNATURES = 1300000;

		private const int BLOCK_HEIGHT_KGW_START_TESTNET = 5;
		private const int BLOCK_HEIGHT_POS_AND_DIGISHIELD_START_TESTNET = 10;
		private const int BLOCK_HEIGHT_DIGISHIELD_FIX_START_TESTNET = 20;
		private const int BLOCK_HEIGHT_FINALPOW_TESTNET = 5000;
		private const int LOW_S_CHECK_SIGNATURES_TESTNET = 30;

		private const int PIR_LEVELS = 6;
		private const int PIR_PHASES = 3;
		private const Int64 PIR_PHASEBLOCKS = 365 * 24 * 60;

		private readonly Int64[] PIR_THRESHOLDS =
		{
			0,
			1000,
			10000,
			100000,
			1000000,
			10000000
		};
		private readonly Int64[,] PIR_RATES =
		{
			{ 10,15,20,30,80,100},
			{ 20,25,30,35,40,45 },
			{ 2,4,6,7,8,10 }
		};

		private int GenerateMTRandom(int s, int range)
		{
			Random rnd = MTRandom.Create(s, MTEdition.Original_19937);
			var x = rnd.Next(1, range);
			return x;
		}
		bool ProtocolRetargetingFixed(Consensus consensus, int nHeight) { return consensus.PowAllowMinDifficultyBlocks || nHeight > 1345000; }

		// TODO Netcoin - New constructor includes POS logic from Main.cpp::AddToBlockIndex
		public bool ProofOfStake_AddToBlockIndex_Logic(Consensus consensus, ConcurrentChain chain,uint256 hashProof)
		{
			// ppcoin: compute chain trust score
			nChainTrust = (pindexNew->pprev ? pindexNew->pprev->nChainTrust : 0) + pindexNew->GetBlockTrust();

			// ppcoin: compute stake entropy bit for stake modifier
			if (!SetStakeEntropyBit(GetStakeEntropyBit()))
				throw new InvalidOperationException("AddToBlockIndex() : SetStakeEntropyBit() failed");

			// Record proof hash value
			this.hashProof = hashProof;

			// ppcoin: compute stake modifier
			UInt64 nStakeModifier = 0;
			bool fGeneratedStakeModifier = false;
			if (!ProofOfStakeKernel.ComputeNextStakeModifier(consensus, chain, previous, out nStakeModifier, out fGeneratedStakeModifier))
				throw new InvalidOperationException("AddToBlockIndex() : ComputeNextStakeModifier() failed");
			SetStakeModifier(nStakeModifier, fGeneratedStakeModifier);
			nStakeModifierChecksum = ProofOfStakeKernel.GetStakeModifierChecksum(consensus, pindexNew);
			if (!CheckStakeModifierCheckpoints(pindexNew->nHeight, pindexNew->nStakeModifierChecksum))
				return error("AddToBlockIndex() : Rejected by stake modifier checkpoint height=%d, modifier=0x%016"PRI64x, pindexNew->nHeight, nStakeModifier);
			return true;
		}
		private Int64 GetProofOfWorkReward(Consensus consensus, int nHeight, Int64 nFees, uint256 prevHash)
		{
			Int64 nSubsidy;

			// Pre v2.4.1 reward
			if (nHeight < 1296010)
			{
				// normal payout
				nSubsidy = 1024L * Money.COIN;

				String cseed_str = prevHash.ToString().Substring(5, 7);
				int seed = Int32.Parse(cseed_str, System.Globalization.NumberStyles.HexNumber);
				int rand = GenerateMTRandom(seed, 6000);

				if (rand > 2000 && rand < 2101)
				{
					nSubsidy *= 8L;
				}

				// 1st week bonus
				if (nHeight < 2881)     // 1st 2 days
				{
					nSubsidy *= 5L;
				}
				else if (nHeight < 5761)    // next 2 days
				{
					nSubsidy *= 3L;
				}
				else if (nHeight < 10081)   // next 3 days
				{
					nSubsidy *= 2L;
				}

				// Subsidy is cut in half every 129,600 blocks, which will occur approximately every 3 months
				nSubsidy >>= (nHeight / consensus.SubsidyHalvingInterval);

				if (nHeight >= BLOCK_HEIGHT_DIGISHIELD_FIX_START)
				{
					nSubsidy += nSubsidy / 4L;  //25% boost to all POW miners to encourage new wallet adoption
				}
				if (nHeight >= BLOCK_HEIGHT_POS_AND_DIGISHIELD_START)
				{
					nSubsidy += nSubsidy / 4L;  //25% boost to all POW miners to encourage new wallet adoption
					nSubsidy *= 2L;             //adjust for POW blocks target changing from 1 to 2 minutes when POW/POS goes live
				}

			}
			else
			{
				nSubsidy = 15L * Money.COIN; // 15 NET static reward and no more superblocks after block 1296000
			}

			return nSubsidy + nFees;
		}

		// Netcoin: PERSONALISED INTEREST RATE CALCULATION
		// madprofezzor@gmail.com

		// returns an integer between 0 and PIR_PHASES-1 representing which PIR phase the supplied block height falls into
		int GetPIRRewardPhase(Consensus consensus, Int64 nHeight)
		{
			Int64 Phase0StartHeight = (!consensus.PowAllowMinDifficultyBlocks ? BLOCK_HEIGHT_POS_AND_DIGISHIELD_START : BLOCK_HEIGHT_POS_AND_DIGISHIELD_START_TESTNET);
			int phase = (int)((nHeight - Phase0StartHeight) / PIR_PHASEBLOCKS);
			return Math.Min(PIR_PHASES - 1, Math.Max(0, phase));
		}

		Int64 GetPIRRewardCoinYear(Consensus consensus, Int64 nCoinValue, Int64 nHeight)
		{
			// work out which phase rates we should use, based on the block height
			int nPhase = GetPIRRewardPhase(consensus, nHeight);

			// find the % band that contains the staked value
			if (nCoinValue >= PIR_THRESHOLDS[PIR_LEVELS - 1] * Money.COIN)
				return PIR_RATES[nPhase, PIR_LEVELS - 1] * Money.CENT;

			int nLevel = 0;
			for (int i = 1; i < PIR_LEVELS; i++)
			{
				if (nCoinValue < PIR_THRESHOLDS[i] * Money.COIN)
				{
					nLevel = i - 1;
					break;
				};
			};


			// interpolate the PIR for this staked value
			// a simple way to interpolate this using integer math is to break the range into 100 slices and find the slice where our coin value lies
			// Rates and Thresholds are integers, CENT and COIN are multiples of 100, so using 100 slices does not introduce any integer math rounding errors


			Int64 nLevelRatePerSlice = ((PIR_RATES[nPhase, nLevel + 1] - PIR_RATES[nPhase, nLevel]) * Money.CENT) / 100;
			Int64 nLevelValuePerSlice = ((PIR_THRESHOLDS[nLevel + 1] - PIR_THRESHOLDS[nLevel]) * Money.COIN) / 100;

			Int64 nTestValue = PIR_THRESHOLDS[nLevel] * Money.COIN;

			Int64 nRewardCoinYear = PIR_RATES[nPhase, nLevel] * Money.CENT;
			while (nTestValue < nCoinValue)
			{
				nTestValue += nLevelValuePerSlice;
				nRewardCoinYear += nLevelRatePerSlice;
			};

			return nRewardCoinYear;

		}

		public bool IsProofOfWork()
		{
			return ((nFlags & BLOCK_PROOF_OF_STAKE) == 0);
		}

		public bool IsProofOfStake()
		{
			return ((nFlags & BLOCK_PROOF_OF_STAKE) != 0);
		}

		public void SetProofOfStake()
		{
			nFlags |= BLOCK_PROOF_OF_STAKE;
		}

		public uint GetStakeEntropyBit()
		{
			return ((nFlags & BLOCK_STAKE_ENTROPY) >> 1);
		}

		public bool SetStakeEntropyBit(uint nEntropyBit)
		{
			if (nEntropyBit > 1)
				return false;
			nFlags |= ((nEntropyBit != 0) ? BLOCK_STAKE_ENTROPY : 0);
			return true;
		}

		public bool GeneratedStakeModifier()
		{
			return ((nFlags & BLOCK_STAKE_MODIFIER) != 0);
		}

		void SetStakeModifier(UInt64 nModifier, bool fGeneratedStakeModifier)
		{
			nStakeModifier = nModifier;
			if (fGeneratedStakeModifier)
				nFlags |= BLOCK_STAKE_MODIFIER;
		}

		Int64 GetProofOfStakeReward(Consensus consensus, Int64 nCoinAge, Int64 nCoinValue, Int64 nFees, Int64 nHeight)
		{

			Int64 nRewardCoinYear = GetPIRRewardCoinYear(consensus, nCoinValue, nHeight);

			Int64 nSubsidy = nCoinAge * nRewardCoinYear * 33 / (365 * 33 + 8); //integer equivalent of nCoinAge * nRewardCoinYear / 365.2424242..

			//TODO Netcoin logging of stake reward
			/*
			if (fDebug && GetBoolArg("-printcreation"))
				printf("GetProofOfStakeReward(): PIR=%.1f create=%s nCoinAge=%"PRI64d" nCoinValue=%s nFees=%"PRI64d"\n", (double)nRewardCoinYear / (double)CENT, FormatMoney(nSubsidy).c_str(), nCoinAge, FormatMoney(nCoinValue).c_str(), nFees);
			*/
			return nSubsidy + nFees;
		}

		//
		// maximum nBits value could possible be required nTime after
		//
		uint ComputeMaxBits(Consensus consensus, Target bnTargetLimit, uint nBase, TimeSpan nTime)
		{
			// Testnet has min-difficulty blocks
			// after nTargetSpacing*2 time between blocks:
			if (consensus.PowAllowMinDifficultyBlocks && nTime > new TimeSpan(consensus.PowTargetSpacing.Ticks * 2))
				return bnTargetLimit.ToCompact();

			Target bnResult = new Target(nBase);
			TimeSpan TargetX4 = new TimeSpan(consensus.PowTargetTimespan.Ticks * 4L);
			while (nTime > TimeSpan.Zero && bnResult < bnTargetLimit)
			{
				// Maximum 400% adjustment...
				bnResult *= 4;
				// ... in best-case exactly 4-times-normal target time
				nTime -= TargetX4;
			}
			if (bnResult > bnTargetLimit)
				bnResult = bnTargetLimit;
			return bnResult.ToCompact();
		}

		//
		// minimum amount of work that could possibly be required nTime after
		// minimum proof-of-work required was nBase
		//
		uint ComputeMinWork(Consensus consensus, uint nBase, TimeSpan nTime)
		{
			// return ComputeMaxBits(bnProofOfWorkLimit, nBase, nTime);
			return ComputeMaxBits(consensus, consensus.PowLimit, nBase, nTime);
		}

		//
		// minimum amount of stake that could possibly be required nTime after
		// minimum proof-of-stake required was nBase
		//
		uint ComputeMinStake(Consensus consensus, uint nBase, TimeSpan nTime, uint nBlockTime)
		{
			return ComputeMaxBits(consensus, consensus.PosLimit, nBase, nTime);
		}


		// ppcoin: find last block index up to pindex
		ChainedBlock GetLastBlockIndex(ChainedBlock pindex, bool fProofOfStake)
		{
			while (pindex != null && pindex.Previous != null && (pindex.IsProofOfStake() != fProofOfStake))
				pindex = pindex.Previous;
			return pindex;
		}



		Target GetNextWorkRequired_V1(Consensus consensus, ChainedBlock pindexLast, Block pblock)
		{
			// uint nProofOfWorkLimit = bnProofOfWorkLimit.GetCompact();
			uint nProofOfWorkLimit = consensus.PowLimit.ToCompact();

			// Genesis block
			if (pindexLast == null)
				return nProofOfWorkLimit;

			// Only change once per interval
			if ((pindexLast.Height + 1) % consensus.DifficultyAdjustmentInterval != 0)
			{
				// Special difficulty rule for testnet:
				if (consensus.PowAllowMinDifficultyBlocks)
				{
					// If the new block's timestamp is more than 2*nTargetSpacing minutes
					// then allow mining of a min-difficulty block.
					if (pblock.Header.BlockTime > pindexLast.Header.BlockTime.AddTicks(consensus.PowTargetSpacing.Ticks * 2))
						return nProofOfWorkLimit;
					else
					{
						// Return the last non-special-min-difficulty-rules-block
						ChainedBlock pindex = pindexLast;
						while ((pindex.Previous != null) && pindex.Height % consensus.DifficultyAdjustmentInterval != 0 && pindex.Header.Bits == nProofOfWorkLimit)
							pindex = pindex.Previous;
						return pindex.Header.Bits;
					}
				}

				return pindexLast.Header.Bits;
			}

			// NetCoin: This fixes an issue where a 51% attack can change difficulty at will.
			// Go back the full period unless it's the first retarget after genesis. Code courtesy of Art Forz
			int blockstogoback = (int)consensus.DifficultyAdjustmentInterval - 1;
			if ((pindexLast.Height + 1) != consensus.DifficultyAdjustmentInterval)
				blockstogoback = (int)consensus.DifficultyAdjustmentInterval;

			// Go back by what we want to be 14 days worth of blocks
			ChainedBlock pindexFirst = pindexLast;
			for (int i = 0; (pindexFirst != null) && i < blockstogoback; i++)
				pindexFirst = pindexFirst.Previous;
			assert(pindexFirst != null);

			// Limit adjustment step
			TimeSpan nActualTimespan = pindexLast.Header.BlockTime - pindexFirst.Header.BlockTime;

			TimeSpan LimitLo = new TimeSpan(consensus.PowTargetTimespan.Ticks / 4L);
			TimeSpan LimitHi = new TimeSpan(consensus.PowTargetTimespan.Ticks * 4L);

			if (nActualTimespan < LimitLo)
				nActualTimespan = LimitLo;

			if (nActualTimespan > LimitHi)
				nActualTimespan = LimitHi;

			long nActualSeconds = nActualTimespan.Ticks / TimeSpan.TicksPerSecond;
			long nTargetSeconds = consensus.PowTargetTimespan.Ticks / TimeSpan.TicksPerSecond;
			// Retarget
			BigInteger bnNew = pindexLast.Header.Bits.ToBigInteger()
				.Multiply(BigInteger.ValueOf(nActualSeconds))
				.Divide(BigInteger.ValueOf(nTargetSeconds));

			if (bnNew.CompareTo(consensus.PowLimit.ToBigInteger()) == 1)
				bnNew = consensus.PowLimit.ToBigInteger();

			/// debug print
			/*
			printf("GetNextWorkRequired RETARGET\n");
			printf("nTargetTimespan = %"PRI64d"    nActualTimespan = %"PRI64d"\n", nTargetTimespan, nActualTimespan);
			printf("Before: %08x  %s\n", pindexLast->nBits, BigInteger().SetCompact(pindexLast->nBits).getuint256().ToString().c_str());
			printf("After:  %08x  %s\n", bnNew.GetCompact(), bnNew.getuint256().ToString().c_str());
			*/

			return new Target(bnNew);
		}

		Target KimotoGravityWell(Consensus consensus, ChainedBlock pindexLast, ulong TargetBlocksSpacingSeconds, ulong PastBlocksMin, ulong PastBlocksMax)
		{
			/* current difficulty formula, megacoin - kimoto gravity well */
			ChainedBlock BlockLastSolved = pindexLast;
			ChainedBlock BlockReading = pindexLast;

			ulong PastBlocksMass = 0;
			Int64 PastRateActualSeconds = 0;
			Int64 PastRateTargetSeconds = 0;
			double PastRateAdjustmentRatio = 1;
			BigInteger PastDifficultyAverage = BigInteger.ValueOf(0);
			BigInteger PastDifficultyAveragePrev = BigInteger.ValueOf(0);
			double EventHorizonDeviation;
			double EventHorizonDeviationFast;
			double EventHorizonDeviationSlow;

			if (BlockLastSolved == null || BlockLastSolved.Height == 0 || (ulong)BlockLastSolved.Height < PastBlocksMin) { return consensus.PowLimit.ToCompact(); }

			for (uint i = 1; (BlockReading != null) && BlockReading.Height > 0; i++)
			{
				if (PastBlocksMax > 0 && i > PastBlocksMax) { break; }
				PastBlocksMass++;

				if (i == 1) { PastDifficultyAverage = BlockReading.Header.Bits.ToBigInteger(); }
				else
				{
					PastDifficultyAverage = BlockReading.Header.Bits.ToBigInteger()
						.Subtract(PastDifficultyAveragePrev)
						.Divide(BigInteger.ValueOf(i))
						.Add(PastDifficultyAveragePrev);
				}
				PastDifficultyAveragePrev = PastDifficultyAverage;

				PastRateActualSeconds = (BlockLastSolved.Header.BlockTime - BlockReading.Header.BlockTime).Ticks / TimeSpan.TicksPerSecond;
				PastRateTargetSeconds = (long)(TargetBlocksSpacingSeconds * PastBlocksMass);
				PastRateAdjustmentRatio = 1.0;
				if (PastRateActualSeconds < 0) { PastRateActualSeconds = 0; }
				if (PastRateActualSeconds != 0 && PastRateTargetSeconds != 0)
				{
					PastRateAdjustmentRatio = (double)PastRateTargetSeconds / (double)PastRateActualSeconds;
				}
				EventHorizonDeviation = 1.0 + (0.7084 * Math.Pow(((double)PastBlocksMass / 144.0), -1.228));
				EventHorizonDeviationFast = EventHorizonDeviation;
				EventHorizonDeviationSlow = 1.0 / EventHorizonDeviation;

				if (PastBlocksMass >= PastBlocksMin)
				{
					if ((PastRateAdjustmentRatio <= EventHorizonDeviationSlow) || (PastRateAdjustmentRatio >= EventHorizonDeviationFast)) { assert(BlockReading != null); break; }
				}
				if (BlockReading.Previous == null) { assert(BlockReading != null); break; }
				BlockReading = BlockReading.Previous;
			}
			BigInteger bnNew = PastDifficultyAverage;
			if (PastRateActualSeconds != 0 && PastRateTargetSeconds != 0)
			{
				bnNew = bnNew
					.Multiply(BigInteger.ValueOf(PastRateActualSeconds))
					.Divide(BigInteger.ValueOf(PastRateTargetSeconds));
			}
			if (bnNew.CompareTo(consensus.PowLimit.ToBigInteger()) > 0) { bnNew = consensus.PowLimit.ToBigInteger(); }

			/// debug print
			/*printf("Difficulty Retarget - Kimoto Gravity Well\n");
			printf("PastRateAdjustmentRatio = %g\n", PastRateAdjustmentRatio);
			printf("Before: %08x  %s\n", BlockLastSolved->nBits, BigInteger().SetCompact(BlockLastSolved->nBits).getuint256().ToString().c_str());
			printf("After:  %08x  %s\n", bnNew.GetCompact(), bnNew.getuint256().ToString().c_str());
			*/
			return new Target(bnNew);
		}

		Target GetNextWorkRequired_KGW(Consensus consensus, ChainedBlock pindexLast)
		{
			const ulong BlocksTargetSpacing = 1 * 60; // 1 minute
			uint TimeDaySeconds = 60 * 60 * 24;

			ulong PastSecondsMin = (ulong)((double)TimeDaySeconds * 0.01);
			ulong PastSecondsMax = (ulong)((double)TimeDaySeconds * 0.14);
			ulong PastBlocksMin = PastSecondsMin / BlocksTargetSpacing;
			ulong PastBlocksMax = PastSecondsMax / BlocksTargetSpacing;

			return KimotoGravityWell(consensus, pindexLast, BlocksTargetSpacing, PastBlocksMin, PastBlocksMax);
		}



		// Netcoin: Digishield inspired difficulty algorithm
		//  Digibyte code was simplified and reduced by assuming nInterval==1
		//  added fProofOfStake to selectively locate last two blocks of the requested type for the time comparison.
		// includes extra flag to backtrack either Proof of Stake or Proof of Work blocks in the chain
		Target GetNextTrust_DigiShield(Consensus consensus, ChainedBlock pindexLast, bool fProofOfStake)
		{
			//uint nProofOfWorkLimit = Params().ProofOfWorkLimit().GetCompact();

			// find the previous 2 blocks of the requested type (either POS or POW)
			ChainedBlock pindexPrev = GetLastBlockIndex(pindexLast, fProofOfStake);

			// netcoin POW and POS blocks each separately retarget to 2 minutes, giving 1 minute overall average block target time.
			Int64 retargetTimespanSeconds = (consensus.PowTargetSpacing.Ticks / TimeSpan.TicksPerSecond) * 2;

			// Genesis block,  or first POS block not yet mined
			if (pindexPrev == null) return consensus.PowLimit;

			// is there another block of the correct type prior to pindexPrev?
			ChainedBlock pindexPrevPrev = GetLastBlockIndex(pindexPrev.Previous, fProofOfStake);
			if (pindexPrevPrev == null)
				pindexPrevPrev = pindexPrev;

			// Limit adjustment step
			Int64 nActualTimespanSeconds = (pindexPrev.Header.BlockTime - pindexPrevPrev.Header.BlockTime).Ticks / TimeSpan.TicksPerSecond;

			// thanks to RealSolid & WDC for this code

			// amplitude filter - thanks to daft27 for this code
			nActualTimespanSeconds = retargetTimespanSeconds + (nActualTimespanSeconds - retargetTimespanSeconds) / 8;

			if (nActualTimespanSeconds < (retargetTimespanSeconds - (retargetTimespanSeconds / 4L))) nActualTimespanSeconds = (retargetTimespanSeconds - (retargetTimespanSeconds / 4L));
			if (nActualTimespanSeconds > (retargetTimespanSeconds + (retargetTimespanSeconds / 2L))) nActualTimespanSeconds = (retargetTimespanSeconds + (retargetTimespanSeconds / 2L));

			BigInteger bnNew;

			bnNew = pindexLast.Header.Bits.ToBigInteger()
			.Multiply(BigInteger.ValueOf(nActualTimespanSeconds))
			.Divide(BigInteger.ValueOf(retargetTimespanSeconds));

			if (bnNew.CompareTo(consensus.PowLimit.ToBigInteger()) > 0)
				bnNew = consensus.PowLimit.ToBigInteger();

			// debug print
			//TODO NetCoin - tracing of difficulty retargetting
			/*
			if (fDebug && GetBoolArg("-printdigishield"))
			{
				System.Diagnostics.Trace.WriteLine("GetNextWorkRequired RETARGET\n");
				System.Diagnostics.Trace.WriteLine("nTargetTimespan = %"PRI64d" nActualTimespan = %"PRI64d"\n", retargetTimespan, nActualTimespan);
				System.Diagnostics.Trace.WriteLine("Before: %08x %s\n", pindexLast->nBits, BigInteger().SetCompact(pindexLast->nBits).getuint256().ToString().c_str());
				System.Diagnostics.Trace.WriteLine("After: %08x %s\n", bnNew.GetCompact(), bnNew.getuint256().ToString().c_str());
			};
			*/
			return new Target(bnNew);
		}




		Target GetNextWorkRequiredV2(Consensus consensus, ChainedBlock pindexLast, bool fProofOfStake)
		{
			//NEW DIGISHIELD START
			// Timespan variables mentioned here are Integer Seconds

			const Int64 nTargetTimespanRe = 1 * 60; // 60 Seconds

			const Int64 nMaxAdjustDown = 40; // 40% adjustment down
			const Int64 nMaxAdjustUp = 20; // 20% adjustment up

			const Int64 nMinActualTimespan = nTargetTimespanRe * (100 - nMaxAdjustUp) / 100;
			const Int64 nMaxActualTimespan = nTargetTimespanRe * (100 + nMaxAdjustDown) / 100;
			//uint nProofOfWorkLimit = Params().ProofOfWorkLimit().GetCompact();

			// find the previous 2 blocks of the requested type (either POS or POW)
			ChainedBlock pindexPrev = GetLastBlockIndex(pindexLast, fProofOfStake);

			// netcoin POW and POS blocks each separately retarget to 2 minutes, giving 1 minute overall average block target time.
			Int64 retargetTimespanSeconds = consensus.PowTargetSpacing.Ticks / TimeSpan.TicksPerSecond;

			// Genesis block,  or first POS block not yet mined
			if (pindexPrev == null) return consensus.PowLimit;

			// is there another block of the correct type prior to pindexPrev?
			ChainedBlock pindexPrevPrev = GetLastBlockIndex(pindexPrev.Previous, fProofOfStake);

			if (pindexPrevPrev == null)
				pindexPrevPrev = pindexPrev;

			// Limit adjustment step
			Int64 nActualTimespan = (pindexPrev.Header.BlockTime - pindexPrevPrev.Header.BlockTime).Ticks / TimeSpan.TicksPerSecond;

			// System.Diagnostics.Trace.WriteLine(" nActualTimespan = {0} before bounds\n", nActualTimespan);
			if (nActualTimespan < nMinActualTimespan)
				nActualTimespan = nMinActualTimespan;
			if (nActualTimespan > nMaxActualTimespan)
				nActualTimespan = nMaxActualTimespan;

			BigInteger bnNew = pindexLast.Header.Bits.ToBigInteger()
			.Multiply(BigInteger.ValueOf(nActualTimespan))
			.Divide(BigInteger.ValueOf(retargetTimespanSeconds));

			if (bnNew.CompareTo(consensus.PowLimit.ToBigInteger()) > 0)
				bnNew = consensus.PowLimit.ToBigInteger();

			// debug print
			/*
			if (fDebug && GetBoolArg("-printdigishield"))
			{
				System.Diagnostics.Trace.WriteLine("GetNextWorkRequired RETARGET\n");
				System.Diagnostics.Trace.WriteLine("nTargetTimespan = %"PRI64d" nActualTimespan = %"PRI64d"\n", retargetTimespan, nActualTimespan);
				System.Diagnostics.Trace.WriteLine("Before: %08x %s\n", pindexLast->nBits, BigInteger().SetCompact(pindexLast->nBits).getuint256().ToString().c_str());
				System.Diagnostics.Trace.WriteLine("After: %08x %s\n", bnNew.GetCompact(), bnNew.getuint256().ToString().c_str());
			};
			*/

			return new Target(bnNew);
			//END OLD DIGI
		}

		// POW blocks tried various algorithms starting at different block height
		Target GetNextProofOfWork(Consensus consensus, ChainedBlock pindexLast, Block pblock)
		{
			ChainedBlock pindexLastPOW = GetLastBlockIndex(pindexLast, false);

			// most recent (highest block height DIGISHIELD FIX)
			if (pindexLastPOW.Height + 1 >= (consensus.PowAllowMinDifficultyBlocks ? BLOCK_HEIGHT_DIGISHIELD_FIX_START_TESTNET : BLOCK_HEIGHT_DIGISHIELD_FIX_START))
				return GetNextWorkRequiredV2(consensus, pindexLastPOW, false);

			// most recent (highest block height)
			if (pindexLastPOW.Height + 1 >= (consensus.PowAllowMinDifficultyBlocks ? BLOCK_HEIGHT_POS_AND_DIGISHIELD_START_TESTNET : BLOCK_HEIGHT_POS_AND_DIGISHIELD_START))
				return GetNextTrust_DigiShield(consensus, pindexLastPOW, false);

			if (pindexLastPOW.Height + 1 >= (consensus.PowAllowMinDifficultyBlocks ? BLOCK_HEIGHT_KGW_START_TESTNET : BLOCK_HEIGHT_KGW_START))
				return GetNextWorkRequired_KGW(consensus, pindexLastPOW);

			// first netcoin difficulty algorithm
			return GetNextWorkRequired_V1(consensus, pindexLastPOW, pblock);
		}

		// select stake target limit according to hard-coded conditions
		Target GetProofOfStakeLimit(Consensus consensus, int nHeight, long nTime)
		{
			if (consensus.PowAllowMinDifficultyBlocks) // separate proof of stake target limit for testnet
				return consensus.PosLimit;
			else
				return consensus.PosLimit;
			// return bnProofOfWorkLimit; // return bnProofOfWorkLimit of none matched
		}

		uint GetNextTargetRequired(Consensus consensus, ChainedBlock pindexLast, bool fProofOfStake)
		{
			Target bnTargetLimit = !fProofOfStake ? consensus.PowLimit : GetProofOfStakeLimit(consensus, pindexLast.Height, Utils.DateTimeToUnixTime(pindexLast.Header.BlockTime));

			if (pindexLast == null)
				return bnTargetLimit; // genesis block

			ChainedBlock pindexPrev = GetLastBlockIndex(pindexLast, fProofOfStake);
			if (pindexPrev.Previous == null)
				return bnTargetLimit; // first block
			ChainedBlock pindexPrevPrev = GetLastBlockIndex(pindexPrev.Previous, fProofOfStake);
			if (pindexPrevPrev.Previous == null)
				return bnTargetLimit; // second block

			Int64 nActualSpacingSeconds = (pindexPrev.Header.BlockTime - pindexPrevPrev.Header.BlockTime).Ticks / TimeSpan.TicksPerSecond;

			if (ProtocolRetargetingFixed(consensus, pindexLast.Height))
			{
				if (nActualSpacingSeconds < 0)
					nActualSpacingSeconds = consensus.PowTargetSpacing.Ticks / TimeSpan.TicksPerSecond;
			}

			// ppcoin: target change every block
			// ppcoin: retarget with exponential moving toward target spacing
			Int64 nInterval = consensus.PowTargetTimespan.Ticks / consensus.StakeTargetSpacing.Ticks;
			Int64 nStakeTargetSpacingSecs = consensus.StakeTargetSpacing.Ticks / TimeSpan.TicksPerSecond;
			BigInteger bnNew = pindexPrev.Header.Bits.ToBigInteger()
			.Multiply(BigInteger.ValueOf((nInterval - 1L) * nStakeTargetSpacingSecs + nActualSpacingSeconds + nActualSpacingSeconds))
			.Divide(BigInteger.ValueOf((nInterval + 1) * nStakeTargetSpacingSecs));

			if ((bnNew.SignValue <= 0) || (bnNew.CompareTo(bnTargetLimit.ToBigInteger()) > 0))
				bnNew = bnTargetLimit.ToBigInteger();

			return new Target(bnNew);
		}


		// GET NEXT WORK REQUIRED - MAIN FUNCTION ROUTER FOR DIFFERENT AGE AND TYPE OF BLOCKS
		Target GetNextWorkRequired(Consensus consensus, ChainedBlock pindexLast, Block pblock, bool fProofOfStake)
		{
			if (fProofOfStake)
			{
				if (pindexLast.Height > 530000)
				{
					return GetNextTargetRequired(consensus, pindexLast, true);
				}
				return GetNextTrust_DigiShield(consensus, pindexLast, true); // first proof of stake blocks use digishield
			}
			else
				return GetNextProofOfWork(consensus, pindexLast, pblock);
		}

		bool CheckProofOfWork(Consensus consensus, uint256 hash, Target bits)
		{
			uint256 bnTarget = bits.ToUInt256();

			// Check range
			// if (bnTarget <= 0 || bnTarget > bnProofOfWorkLimit)
			if (bnTarget <= 0 || bnTarget > consensus.PowLimit.ToUInt256())
				throw new System.InvalidOperationException("CheckProofOfWork() : nBits below minimum work");

			// Check proof of work matches claimed amount
			if (hash > bnTarget)
				throw new System.InvalidOperationException("CheckProofOfWork() : hash doesn't match nBits");

			return true;
		}

	}
}
