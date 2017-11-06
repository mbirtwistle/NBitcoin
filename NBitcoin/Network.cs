using NBitcoin.DataEncoders;
using NBitcoin.Protocol;
using NBitcoin.Stealth;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NBitcoin
{
	public class DNSSeedData
	{
		string name, host;
		public string Name
		{
			get
			{
				return name;
			}
		}
		public string Host
		{
			get
			{
				return host;
			}
		}
		public DNSSeedData(string name, string host)
		{
			this.name = name;
			this.host = host;
		}
#if !NOSOCKET
		IPAddress[] _Addresses = null;
		public IPAddress[] GetAddressNodes()
		{
			if(_Addresses != null)
				return _Addresses;
			try
			{
				_Addresses = Dns.GetHostAddressesAsync(host).Result;
			}
			catch(AggregateException ex)
			{
				System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
			}
			return _Addresses;
		}
#endif
		public override string ToString()
		{
			return name + " (" + host + ")";
		}
	}
	public enum Base58Type
	{
		PUBKEY_ADDRESS,
		SCRIPT_ADDRESS,
		SECRET_KEY,
		EXT_PUBLIC_KEY,
		EXT_SECRET_KEY,
		ENCRYPTED_SECRET_KEY_EC,
		ENCRYPTED_SECRET_KEY_NO_EC,
		PASSPHRASE_CODE,
		CONFIRMATION_CODE,
		STEALTH_ADDRESS,
		ASSET_ID,
		COLORED_ADDRESS,
		MAX_BASE58_TYPES,
	};

	public enum Bech32Type
	{
		WITNESS_PUBKEY_ADDRESS,
		WITNESS_SCRIPT_ADDRESS
	}

	public partial class Network
	{
		internal byte[][] base58Prefixes = new byte[12][];
		internal Bech32Encoder[] bech32Encoders = new Bech32Encoder[2];
		public Bech32Encoder GetBech32Encoder(Bech32Type type, bool throws)
		{
			var encoder = bech32Encoders[(int)type];
			if(encoder == null && throws)
				throw new NotImplementedException("The network " + this + " does not have any prefix for bech32 " + Enum.GetName(typeof(Bech32Type), type));
			return encoder;
		}

		public byte[] GetVersionBytes(Base58Type type, bool throws)
		{
			var prefix = base58Prefixes[(int)type];
			if(prefix == null && throws)
				throw new NotImplementedException("The network " + this + " does not have any prefix for base58 " + Enum.GetName(typeof(Base58Type), type));
			return prefix?.ToArray();
		}

		internal static string CreateBase58(Base58Type type, byte[] bytes, Network network)
		{
			if(network == null)
				throw new ArgumentNullException("network");
			if(bytes == null)
				throw new ArgumentNullException("bytes");
			var versionBytes = network.GetVersionBytes(type, true);
			return Encoders.Base58Check.EncodeData(versionBytes.Concat(bytes));
		}

		internal static string CreateBech32(Bech32Type type, byte[] bytes, byte witnessVersion, Network network)
		{
			if(network == null)
				throw new ArgumentNullException("network");
			if(bytes == null)
				throw new ArgumentNullException("bytes");
			var encoder = network.GetBech32Encoder(type, true);
			return encoder.Encode(witnessVersion, bytes);
		}
	}

	public enum BuriedDeployments : int
	{
		/// <summary>
		/// Height in coinbase
		/// </summary>
		BIP34,
		/// <summary>
		/// Height in OP_CLTV
		/// </summary>
		BIP65,
		/// <summary>
		/// Strict DER signature
		/// </summary>
		BIP66

	    //TODO Netcoin buried deployments
	}

	public class Consensus
	{
		public static Consensus Main
		{
			get
			{
				return Network.Main.Consensus;
			}
		}
		public static Consensus TestNet
		{
			get
			{
				return Network.TestNet.Consensus;
			}
		}
		public static Consensus RegTest
		{
			get
			{
				return Network.RegTest.Consensus;
			}
		}
		public class BuriedDeploymentsArray
		{
			Consensus _Parent;
			int[] _Heights;
			public BuriedDeploymentsArray(Consensus parent)
			{
				_Parent = parent;
				_Heights = new int[Enum.GetValues(typeof(BuriedDeployments)).Length];
			}
			public int this[BuriedDeployments index]
			{
				get
				{
					return _Heights[(int)index];
				}
				set
				{
					_Parent.EnsureNotFrozen();
					_Heights[(int)index] = value;
				}
			}
		}
		public class BIP9DeploymentsArray
		{
			Consensus _Parent;
			BIP9DeploymentsParameters[] _Parameters;
			public BIP9DeploymentsArray(Consensus parent)
			{
				_Parent = parent;
				_Parameters = new BIP9DeploymentsParameters[Enum.GetValues(typeof(BIP9Deployments)).Length];
			}

			public BIP9DeploymentsParameters this[BIP9Deployments index]
			{
				get
				{
					return _Parameters[(int)index];
				}
				set
				{
					_Parent.EnsureNotFrozen();
					_Parameters[(int)index] = value;
				}
			}
		}

		public Consensus()
		{
			_BuriedDeployments = new BuriedDeploymentsArray(this);
			_BIP9Deployments = new BIP9DeploymentsArray(this);
		}
		private readonly BuriedDeploymentsArray _BuriedDeployments;
		public BuriedDeploymentsArray BuriedDeployments
		{
			get
			{
				return _BuriedDeployments;
			}
		}


		private readonly BIP9DeploymentsArray _BIP9Deployments;
		public BIP9DeploymentsArray BIP9Deployments
		{
			get
			{
				return _BIP9Deployments;
			}
		}

		int _SubsidyHalvingInterval;
		public int SubsidyHalvingInterval
		{
			get
			{
				return _SubsidyHalvingInterval;
			}
			set
			{
				EnsureNotFrozen();
				_SubsidyHalvingInterval = value;
			}
		}

		private Func<BlockHeader, uint256> _GetPoWHash = h => h.GetHash();

		public Func<BlockHeader, uint256> GetPoWHash
		{
			get
			{
				return _GetPoWHash;
			}
			set
			{
				EnsureNotFrozen();
				_GetPoWHash = value;
			}
		}


		int _MajorityEnforceBlockUpgrade;

		public int MajorityEnforceBlockUpgrade
		{
			get
			{
				return _MajorityEnforceBlockUpgrade;
			}
			set
			{
				EnsureNotFrozen();
				_MajorityEnforceBlockUpgrade = value;
			}
		}

		int _MajorityRejectBlockOutdated;
		public int MajorityRejectBlockOutdated
		{
			get
			{
				return _MajorityRejectBlockOutdated;
			}
			set
			{
				EnsureNotFrozen();
				_MajorityRejectBlockOutdated = value;
			}
		}

		int _MajorityWindow;
		public int MajorityWindow
		{
			get
			{
				return _MajorityWindow;
			}
			set
			{
				EnsureNotFrozen();
				_MajorityWindow = value;
			}
		}

		uint256 _BIP34Hash;
		public uint256 BIP34Hash
		{
			get
			{
				return _BIP34Hash;
			}
			set
			{
				EnsureNotFrozen();
				_BIP34Hash = value;
			}
		}


		Target _PowLimit;
		public Target PowLimit
		{
			get
			{
				return _PowLimit;
			}
			set
			{
				EnsureNotFrozen();
				_PowLimit = value;
			}
		}


		TimeSpan _PowTargetTimespan;
		public TimeSpan PowTargetTimespan
		{
			get
			{
				return _PowTargetTimespan;
			}
			set
			{
				EnsureNotFrozen();
				_PowTargetTimespan = value;
			}
		}


		TimeSpan _PowTargetSpacing;
		public TimeSpan PowTargetSpacing
		{
			get
			{
				return _PowTargetSpacing;
			}
			set
			{
				EnsureNotFrozen();
				_PowTargetSpacing = value;
			}
		}


		bool _PowAllowMinDifficultyBlocks;
		public bool PowAllowMinDifficultyBlocks
		{
			get
			{
				return _PowAllowMinDifficultyBlocks;
			}
			set
			{
				EnsureNotFrozen();
				_PowAllowMinDifficultyBlocks = value;
			}
		}


		bool _PowNoRetargeting;
		public bool PowNoRetargeting
		{
			get
			{
				return _PowNoRetargeting;
			}
			set
			{
				EnsureNotFrozen();
				_PowNoRetargeting = value;
			}
		}


		uint256 _HashGenesisBlock;
		public uint256 HashGenesisBlock
		{
			get
			{
				return _HashGenesisBlock;
			}
			set
			{
				EnsureNotFrozen();
				_HashGenesisBlock = value;
			}
		}

		uint256 _MinimumChainWork;
		public uint256 MinimumChainWork
		{
			get
			{
				return _MinimumChainWork;
			}
			set
			{
				EnsureNotFrozen();
				_MinimumChainWork = value;
			}
		}

		public long DifficultyAdjustmentInterval
		{
			get
			{
				return ((long)PowTargetTimespan.TotalSeconds / (long)PowTargetSpacing.TotalSeconds);
			}
		}

		int _MinerConfirmationWindow;
		public int MinerConfirmationWindow
		{
			get
			{
				return _MinerConfirmationWindow;
			}
			set
			{
				EnsureNotFrozen();
				_MinerConfirmationWindow = value;
			}
		}

		int _RuleChangeActivationThreshold;
		public int RuleChangeActivationThreshold
		{
			get
			{
				return _RuleChangeActivationThreshold;
			}
			set
			{
				EnsureNotFrozen();
				_RuleChangeActivationThreshold = value;
			}
		}


		int _CoinbaseMaturity = 100;
		public int CoinbaseMaturity
		{
			get
			{
				return _CoinbaseMaturity;
			}
			set
			{
				EnsureNotFrozen();
				_CoinbaseMaturity = value;
			}
		}

		int _CoinType;

		/// <summary>
		/// Specify the BIP44 coin type for this network
		/// </summary>
		public int CoinType
		{
			get
			{
				return _CoinType;
			}
			set
			{
				EnsureNotFrozen();
				_CoinType = value;
			}
		}


		bool _LitecoinWorkCalculation;
		/// <summary>
		/// Specify using litecoin calculation for difficulty
		/// </summary>
		public bool LitecoinWorkCalculation
		{
			get
			{
				return _LitecoinWorkCalculation;
			}
			set
			{
				EnsureNotFrozen();
				_LitecoinWorkCalculation = value;
			}
		}

		bool frozen = false;
		public void Freeze()
		{
			frozen = true;
		}
		private void EnsureNotFrozen()
		{
			if(frozen)
				throw new InvalidOperationException("This instance can't be modified");
		}

		public virtual Consensus Clone()
		{
			var consensus = new Consensus();
			Fill(consensus);
			return consensus;
		}

		protected void Fill(Consensus consensus)
		{
			consensus.EnsureNotFrozen();
			consensus._BIP34Hash = _BIP34Hash;
			consensus._HashGenesisBlock = _HashGenesisBlock;
			consensus._MajorityEnforceBlockUpgrade = _MajorityEnforceBlockUpgrade;
			consensus._MajorityRejectBlockOutdated = _MajorityRejectBlockOutdated;
			consensus._MajorityWindow = _MajorityWindow;
			consensus._MinerConfirmationWindow = _MinerConfirmationWindow;
			consensus._PowAllowMinDifficultyBlocks = _PowAllowMinDifficultyBlocks;
			consensus._PowLimit = _PowLimit;
			consensus._PowNoRetargeting = _PowNoRetargeting;
			consensus._PowTargetSpacing = _PowTargetSpacing;
			consensus._PowTargetTimespan = _PowTargetTimespan;
			consensus._RuleChangeActivationThreshold = _RuleChangeActivationThreshold;
			consensus._SubsidyHalvingInterval = _SubsidyHalvingInterval;
			consensus._CoinbaseMaturity = _CoinbaseMaturity;
			consensus._MinimumChainWork = _MinimumChainWork;
			consensus.GetPoWHash = GetPoWHash;
			consensus._CoinType = CoinType;
			consensus._LitecoinWorkCalculation = _LitecoinWorkCalculation;
		}
	}
	public partial class Network
	{




		static string[] pnSeed = new[] { "127.0.0.1:11310" };


		uint magic;
		byte[] vAlertPubKey;
		PubKey _AlertPubKey;
		public PubKey AlertPubKey
		{
			get
			{
				if(_AlertPubKey == null)
				{
					_AlertPubKey = new PubKey(vAlertPubKey);
				}
				return _AlertPubKey;
			}
		}

#if !NOSOCKET
		List<DNSSeedData> vSeeds = new List<DNSSeedData>();
		List<NetworkAddress> vFixedSeeds = new List<NetworkAddress>();
#else
		List<string> vSeeds = new List<string>();
		List<string> vFixedSeeds = new List<string>();
#endif
		Block genesis = new Block();

		private int nRPCPort;
		public int RPCPort
		{
			get
			{
				return nRPCPort;
			}
		}

		private int nDefaultPort;
		public int DefaultPort
		{
			get
			{
				return nDefaultPort;
			}
		}


		private Consensus consensus = new Consensus();
		public Consensus Consensus
		{
			get
			{
				return consensus;
			}
		}

		private Network()
		{
		}

		private string name;

		public string Name
		{
			get
			{
				return name;
			}
		}

		static Network()
		{
			_Main = new Network();
			_Main.InitMain();
			_Main.Consensus.Freeze();

			_TestNet = new Network();
			_TestNet.InitTest();
			_TestNet.Consensus.Freeze();

			_RegTest = new Network();
			_RegTest.InitReg();
		}

		static Network _Main;
		public static Network Main
		{
			get
			{
				return _Main;
			}
		}

		static Network _TestNet;
		public static Network TestNet
		{
			get
			{
				return _TestNet;
			}
		}

		static Network _RegTest;
		public static Network RegTest
		{
			get
			{
				return _RegTest;
			}
		}

		static Dictionary<string, Network> _OtherAliases = new Dictionary<string, Network>();
		static List<Network> _OtherNetworks = new List<Network>();
		internal static Network Register(NetworkBuilder builder)
		{
			if(builder._Name == null)
				throw new InvalidOperationException("A network name need to be provided");
			if(GetNetwork(builder._Name) != null)
				throw new InvalidOperationException("The network " + builder._Name + " is already registered");
			Network network = new Network();
			network.name = builder._Name;
			network.consensus = builder._Consensus;
			network.magic = builder._Magic;
			network.nDefaultPort = builder._Port;
			network.nRPCPort = builder._RPCPort;
			network.genesis = builder._Genesis;
			network.consensus.HashGenesisBlock = network.genesis.GetHash();
			network.consensus.Freeze();

#if !NOSOCKET
			foreach(var seed in builder.vSeeds)
			{
				network.vSeeds.Add(seed);
			}
			foreach(var seed in builder.vFixedSeeds)
			{
				network.vFixedSeeds.Add(seed);
			}
#endif
			network.base58Prefixes = Network.Main.base58Prefixes.ToArray();
			foreach(var kv in builder._Base58Prefixes)
			{
				network.base58Prefixes[(int)kv.Key] = kv.Value;
			}
			network.bech32Encoders = Network.Main.bech32Encoders.ToArray();
			foreach(var kv in builder._Bech32Prefixes)
			{
				network.bech32Encoders[(int)kv.Key] = kv.Value;
			}
			lock(_OtherAliases)
			{
				foreach(var alias in builder._Aliases)
				{
					_OtherAliases.Add(alias.ToLowerInvariant(), network);
				}
				_OtherAliases.Add(network.name.ToLowerInvariant(), network);
			}
			lock(_OtherNetworks)
			{
				_OtherNetworks.Add(network);
			}
			return network;
		}

		private void InitMain()
		{
			name = "Main";

			consensus.CoinbaseMaturity = 10;
			consensus.SubsidyHalvingInterval = 210000;
			consensus.MajorityEnforceBlockUpgrade = 0;
			consensus.MajorityRejectBlockOutdated = 0;
			consensus.MajorityWindow = 1000;
			consensus.BuriedDeployments[BuriedDeployments.BIP34] = 0;
			consensus.BuriedDeployments[BuriedDeployments.BIP65] = 0;
			consensus.BuriedDeployments[BuriedDeployments.BIP66] = 0;
			consensus.BIP34Hash = new uint256("0x000000000000024b89b42a942fe0d9fea3bb44ab7bd1b19115dd6a759c0808b8");
			consensus.PowLimit = new Target(new uint256("00000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"));
			consensus.MinimumChainWork = new uint256("0x0000000000000000000000000000000000000000002cb971dd56d1c583c20f90");
			consensus.PowTargetTimespan = TimeSpan.FromSeconds(60); 
			consensus.PowTargetSpacing = TimeSpan.FromSeconds(10 * 60);
			consensus.PowAllowMinDifficultyBlocks = false;
			consensus.PowNoRetargeting = false;
			consensus.RuleChangeActivationThreshold = 1916; // 95% of 2016
			consensus.MinerConfirmationWindow = 2016; // nPowTargetTimespan / nPowTargetSpacing
			consensus.LitecoinWorkCalculation = true;

			consensus.BIP9Deployments[BIP9Deployments.Segwit] = new BIP9DeploymentsParameters(1, 0, 0);

			consensus.CoinType = 0;

			// The message start string is designed to be unlikely to occur in normal data.
			// The characters are rarely used upper ASCII, not valid as UTF-8, and produce
			// a large 4-byte int at any alignment.
			magic = 0xFDB6A5DB;
			vAlertPubKey = Encoders.Hex.DecodeData("04ef014b36647e8433a2cedf76f1d6ea0bc5914ba936fadceda90d7472da3cf442469d3a1ab5ee416e7428726761dd3188bda3d0ae163db491f8ca0bdad92a0506");
			nDefaultPort = 11310;
			nRPCPort = 11211;

			genesis = CreateGenesisBlock(1377903314, 12344321, 0x1e0ffff0, 1, Money.Coins(0m));
			consensus.HashGenesisBlock = genesis.GetHash();
			assert(consensus.HashGenesisBlock == uint256.Parse("0x38624e3834cfdc4410a5acbc32f750171aadad9620e6ba6d5c73201c16f7c8d1"));
			assert(genesis.Header.HashMerkleRoot == uint256.Parse("0xe5981b72a47998b021ee8995726282d1a575477897d9d5a319167601fffebb21"));
#if !NOSOCKET
			vSeeds.Add(new DNSSeedData("presstab.pw", "netseed.presstab.pw"));
#endif
			base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { (112) };
			base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { (5) };
			base58Prefixes[(int)Base58Type.SECRET_KEY] = new byte[] { (240) };
			base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_NO_EC] = new byte[] { 0x01, 0x42 };						// TODO Netcoin set base58prefix for this feature
			base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_EC] = new byte[] { 0x01, 0x43 };						// TODO Netcoin set base58prefix for this feature
			base58Prefixes[(int)Base58Type.EXT_PUBLIC_KEY] = new byte[] { (0x04), (0x88), (0xB2), (0x1E) };
			base58Prefixes[(int)Base58Type.EXT_SECRET_KEY] = new byte[] { (0x04), (0x88), (0xAD), (0xE4) };
			base58Prefixes[(int)Base58Type.PASSPHRASE_CODE] = new byte[] { 0x2C, 0xE9, 0xB3, 0xE1, 0xFF, 0x39, 0xE2 };	// TODO Netcoin set base58prefix for this feature
			base58Prefixes[(int)Base58Type.CONFIRMATION_CODE] = new byte[] { 0x64, 0x3B, 0xF6, 0xA8, 0x9A };			// TODO Netcoin set base58prefix for this feature
			base58Prefixes[(int)Base58Type.STEALTH_ADDRESS] = new byte[] { 0x2a };										// TODO Netcoin set base58prefix for this feature
			base58Prefixes[(int)Base58Type.ASSET_ID] = new byte[] { 23 };												// TODO Netcoin set base58prefix for this feature
			base58Prefixes[(int)Base58Type.COLORED_ADDRESS] = new byte[] { 0x13 };										// TODO Netcoin set base58prefix for this feature

			var encoder = new Bech32Encoder("net");  // TODO Netcoin set Bech32Encoder hrp
			bech32Encoders[(int)Bech32Type.WITNESS_PUBKEY_ADDRESS] = encoder; 
			bech32Encoders[(int)Bech32Type.WITNESS_SCRIPT_ADDRESS] = encoder;

#if !NOSOCKET
			// Convert the pnSeeds array into usable address objects.
			Random rand = new Random();
			TimeSpan nOneWeek = TimeSpan.FromDays(7);
			for(int i = 0; i < pnSeed.Length; i++)
			{
				// It'll only connect to one or two seed nodes because once it connects,
				// it'll get a pile of addresses with newer timestamps.				
				NetworkAddress addr = new NetworkAddress();
				// Seed nodes are given a random 'last seen time' of between one and two
				// weeks ago.
				addr.Time = DateTime.UtcNow - (TimeSpan.FromSeconds(rand.NextDouble() * nOneWeek.TotalSeconds)) - nOneWeek;
				addr.Endpoint = Utils.ParseIpEndpoint(pnSeed[i], DefaultPort);
				vFixedSeeds.Add(addr);
			}
#endif
		}
		private void InitTest()
		{
			name = "TestNet";

			consensus.SubsidyHalvingInterval = 210000;
			consensus.MajorityEnforceBlockUpgrade = 51;
			consensus.MajorityRejectBlockOutdated = 75;
			consensus.MajorityWindow = 100;
			consensus.BuriedDeployments[BuriedDeployments.BIP34] = 0;
			consensus.BuriedDeployments[BuriedDeployments.BIP65] = 0;
			consensus.BuriedDeployments[BuriedDeployments.BIP66] = 0;
			consensus.BIP34Hash = new uint256();
			consensus.PowLimit = new Target(new uint256("0000ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"));
			consensus.MinimumChainWork = new uint256("0x0000000000000000000000000000000000000000000000198b4def2baa9338d6");
			consensus.PowTargetTimespan = TimeSpan.FromSeconds(14 * 24 * 60 * 60); // two weeks
			consensus.PowTargetSpacing = TimeSpan.FromSeconds(10 * 60);
			consensus.PowAllowMinDifficultyBlocks = true;
			consensus.PowNoRetargeting = false;
			consensus.RuleChangeActivationThreshold = 1512; // 75% for testchains
			consensus.MinerConfirmationWindow = 2016; // nPowTargetTimespan / nPowTargetSpacing
			consensus.LitecoinWorkCalculation = true;

			consensus.BIP9Deployments[BIP9Deployments.TestDummy] = new BIP9DeploymentsParameters(28, 1199145601, 1230767999);
			consensus.BIP9Deployments[BIP9Deployments.CSV] = new BIP9DeploymentsParameters(0, 1456790400, 1493596800);
			consensus.BIP9Deployments[BIP9Deployments.Segwit] = new BIP9DeploymentsParameters(1, 1462060800, 1493596800);

			consensus.CoinType = 1;

			magic = 0xFDC0B6F1;

			vAlertPubKey = DataEncoders.Encoders.Hex.DecodeData("04302390343f91cc401d56d68b123028bf52e5fca1939df127f63c6467cdf9c8e2c14b61104cf817d0b780da337893ecc4aaff1309e536162dabbdb45200ca2b0a");
			nDefaultPort = 21310;
			nRPCPort = 22444;
			//strDataDir = "testnet3";

			// Modify the testnet genesis block so the timestamp is valid for a later start.
			genesis = CreateGenesisBlock(1300000000, 0, consensus.PowLimit.ToCompact(), 1, Money.Coins(0m));
			consensus.HashGenesisBlock = genesis.GetHash();

			assert(consensus.HashGenesisBlock == uint256.Parse("0x560dbb3ee136ccaae9a7dcd60e1d170508619c6934efc4b22168f6f614bbedff"));

#if !NOSOCKET
			vFixedSeeds.Clear();
			vSeeds.Clear();
#endif

			base58Prefixes = Network.Main.base58Prefixes.ToArray();
			base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { (111) };
			base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { (196) };
			base58Prefixes[(int)Base58Type.SECRET_KEY] = new byte[] { (239) };
			base58Prefixes[(int)Base58Type.EXT_PUBLIC_KEY] = new byte[] { (0x04), (0x35), (0x87), (0xCF) };
			base58Prefixes[(int)Base58Type.EXT_SECRET_KEY] = new byte[] { (0x04), (0x35), (0x83), (0x94) };
			base58Prefixes[(int)Base58Type.STEALTH_ADDRESS] = new byte[] { 0x2b };
			base58Prefixes[(int)Base58Type.ASSET_ID] = new byte[] { 115 };
			base58Prefixes[(int)Base58Type.COLORED_ADDRESS] = new byte[] { 0x13 };

			var encoder = new Bech32Encoder("tnet");
			bech32Encoders[(int)Bech32Type.WITNESS_PUBKEY_ADDRESS] = encoder;
			bech32Encoders[(int)Bech32Type.WITNESS_SCRIPT_ADDRESS] = encoder;
		}
		private void InitReg()
		{
			name = "RegTest";
			consensus.SubsidyHalvingInterval = 150;
			consensus.MajorityEnforceBlockUpgrade = 750;
			consensus.MajorityRejectBlockOutdated = 950;
			consensus.MajorityWindow = 1000;
			consensus.BuriedDeployments[BuriedDeployments.BIP34] = 100000000;
			consensus.BuriedDeployments[BuriedDeployments.BIP65] = 100000000;
			consensus.BuriedDeployments[BuriedDeployments.BIP66] = 100000000;
			consensus.BIP34Hash = new uint256();
			consensus.PowLimit = new Target(new uint256("7fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"));
			consensus.MinimumChainWork = uint256.Zero;
			consensus.PowTargetTimespan = TimeSpan.FromSeconds(14 * 24 * 60 * 60); // two weeks
			consensus.PowTargetSpacing = TimeSpan.FromSeconds(10 * 60);
			consensus.PowAllowMinDifficultyBlocks = true;
			consensus.PowNoRetargeting = true;
			consensus.RuleChangeActivationThreshold = 108;
			consensus.MinerConfirmationWindow = 144;
			consensus.LitecoinWorkCalculation = true;

			magic = 0xFABFB5DA;

			consensus.BIP9Deployments[BIP9Deployments.TestDummy] = new BIP9DeploymentsParameters(28, 0, 999999999);
			consensus.BIP9Deployments[BIP9Deployments.CSV] = new BIP9DeploymentsParameters(0, 0, 999999999);
			consensus.BIP9Deployments[BIP9Deployments.Segwit] = new BIP9DeploymentsParameters(1, 0, 999999999);

			genesis = CreateGenesisBlock(1296688602, 2, consensus.PowLimit.ToCompact(), 1, Money.Coins(0m));
			consensus.HashGenesisBlock = genesis.GetHash();
			nDefaultPort = 18444;
			nRPCPort = 18332;
			//strDataDir = "regtest";
			assert(consensus.HashGenesisBlock == uint256.Parse("0xa05031a4091a978f203526bc1833027eb54061a65cbc219506d844eb541b3e8a"));

#if !NOSOCKET
			vSeeds.Clear();  // Regtest mode doesn't have any DNS seeds.
#endif
			base58Prefixes = Network.TestNet.base58Prefixes.ToArray();
			base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { (111) };
			base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { (196) };
			base58Prefixes[(int)Base58Type.SECRET_KEY] = new byte[] { (239) };
			base58Prefixes[(int)Base58Type.EXT_PUBLIC_KEY] = new byte[] { (0x04), (0x35), (0x87), (0xCF) };
			base58Prefixes[(int)Base58Type.EXT_SECRET_KEY] = new byte[] { (0x04), (0x35), (0x83), (0x94) };
			base58Prefixes[(int)Base58Type.COLORED_ADDRESS] = new byte[] { 0x13 };

			var encoder = new Bech32Encoder("rnet");
			;
			bech32Encoders[(int)Bech32Type.WITNESS_PUBKEY_ADDRESS] = encoder;
			bech32Encoders[(int)Bech32Type.WITNESS_SCRIPT_ADDRESS] = encoder;
		}

		private Block CreateGenesisBlock(uint nTime, uint nNonce, uint nBits, int nVersion, Money genesisReward)
		{
			string pszTimestamp = "Aug 31, 2013: US STOCKS-Wall Street falls, ends worst month since May 2012.";
			Script genesisOutputScript = new Script(Op.GetPushOp(Encoders.Hex.DecodeData("0457575678901234567890000222222333444555666777888999000000aaaaabbbbbcccccdddddeeeeeff00ff00ff00ff001234567890abcdef0022446688abc89")), OpcodeType.OP_CHECKSIG);
			return CreateGenesisBlock(pszTimestamp, genesisOutputScript, nTime, nNonce, nBits, nVersion, genesisReward);
		}

		private Block CreateGenesisBlock(string pszTimestamp, Script genesisOutputScript, uint nTime, uint nNonce, uint nBits, int nVersion, Money genesisReward)
		{
			Transaction txNew = new Transaction();
			txNew.Version =2;
			txNew.AddInput(new TxIn()
			{
				ScriptSig = new Script(Op.GetPushOp(486604799), new Op()
				{
					Code = (OpcodeType)0x1,
					PushData = new[] { (byte)4 }
				}, Op.GetPushOp(Encoders.ASCII.DecodeData(pszTimestamp)))
			});
			txNew.AddOutput(new TxOut()
			{
				Value = genesisReward,
				ScriptPubKey = genesisOutputScript
			});
			Block genesis = new Block();
			genesis.Header.BlockTime = Utils.UnixTimeToDateTime(nTime);
			genesis.Header.Bits = nBits;
			genesis.Header.Nonce = nNonce;
			genesis.Header.Version = nVersion;
			genesis.Transactions.Add(txNew);
			genesis.Header.HashPrevBlock = uint256.Zero;
			genesis.UpdateMerkleRoot();
			return genesis;
		}

		private static void assert(bool v)
		{
			if(!v)
				throw new InvalidOperationException("Invalid network");
		}

		public BitcoinSecret CreateBitcoinSecret(string base58)
		{
			return new BitcoinSecret(base58, this);
		}

		/// <summary>
		/// Create a bitcoin address from base58 data, return a BitcoinAddress or BitcoinScriptAddress
		/// </summary>
		/// <param name="base58">base58 address</param>
		/// <exception cref="System.FormatException">Invalid base58 address</exception>
		/// <returns>BitcoinScriptAddress, BitcoinAddress</returns>
		public BitcoinAddress CreateBitcoinAddress(string base58)
		{
			var type = GetBase58Type(base58);
			if(!type.HasValue)
				throw new FormatException("Invalid Base58 version");
			if(type == Base58Type.PUBKEY_ADDRESS)
				return new BitcoinPubKeyAddress(base58, this);
			if(type == Base58Type.SCRIPT_ADDRESS)
				return new BitcoinScriptAddress(base58, this);
			throw new FormatException("Invalid Base58 version");
		}

		public BitcoinScriptAddress CreateBitcoinScriptAddress(string base58)
		{
			return new BitcoinScriptAddress(base58, this);
		}

		private Base58Type? GetBase58Type(string base58)
		{
			var bytes = Encoders.Base58Check.DecodeData(base58);
			for(int i = 0; i < base58Prefixes.Length; i++)
			{
				var prefix = base58Prefixes[i];
				if(prefix == null)
					continue;
				if(bytes.Length < prefix.Length)
					continue;
				if(Utils.ArrayEqual(bytes, 0, prefix, 0, prefix.Length))
					return (Base58Type)i;
			}
			return null;
		}


		internal static Network GetNetworkFromBase58Data(string base58, Base58Type? expectedType = null)
		{
			foreach(var network in GetNetworks())
			{
				var type = network.GetBase58Type(base58);
				if(type.HasValue)
				{
					if(expectedType != null && expectedType.Value != type.Value)
						continue;
					if(type.Value == Base58Type.COLORED_ADDRESS)
					{
						var raw = Encoders.Base58Check.DecodeData(base58);
						var version = network.GetVersionBytes(type.Value, false);
						if(version == null)
							continue;
						raw = raw.Skip(version.Length).ToArray();
						base58 = Encoders.Base58Check.EncodeData(raw);
						return GetNetworkFromBase58Data(base58, null);
					}
					return network;
				}
			}
			return null;
		}

		public IBitcoinString Parse(string str)
		{
			return Parse<IBitcoinString>(str, this);
		}
		public T Parse<T>(string str) where T : IBitcoinString
		{
			return Parse<T>(str, this);
		}

		public static IBitcoinString Parse(string str, Network expectedNetwork)
		{
			return Parse<IBitcoinString>(str, expectedNetwork);
		}
		public static T Parse<T>(string str, Network expectedNetwork = null) where T : IBitcoinString
		{
			if(str == null)
				throw new ArgumentNullException("str");
			var networks = expectedNetwork == null ? GetNetworks() : new[] { expectedNetwork };
			var maybeb58 = true;
			if(maybeb58)
			{
				for(int i = 0; i < str.Length; i++)
				{
					if(!Base58Encoder.pszBase58Chars.Contains(str[i]))
					{
						maybeb58 = false;
						break;
					}
				}
			}
			if(maybeb58)
			{
				try
				{
					Encoders.Base58Check.DecodeData(str);
				}
				catch(FormatException) { maybeb58 = false; }
				if(maybeb58)
				{
					foreach(var candidate in GetCandidates(networks, str))
					{
						bool rightNetwork = expectedNetwork == null || (candidate.Network == expectedNetwork);
						bool rightType = candidate is T;
						if(rightNetwork && rightType)
							return (T)(object)candidate;
					}
					throw new FormatException("Invalid base58 string");
				}
			}

			foreach(var network in networks)
			{
				int i = -1;
				foreach(var encoder in network.bech32Encoders)
				{
					i++;
					if(encoder == null)
						continue;
					var type = (Bech32Type)i;
					try
					{
						byte witVersion;
						var bytes = encoder.Decode(str, out witVersion);
						object candidate = null;

						if(witVersion == 0 && bytes.Length == 20 && type == Bech32Type.WITNESS_PUBKEY_ADDRESS)
							candidate = new BitcoinWitPubKeyAddress(str, network);
						if(witVersion == 0 && bytes.Length == 32 && type == Bech32Type.WITNESS_SCRIPT_ADDRESS)
							candidate = new BitcoinWitScriptAddress(str, network);

						if(candidate is T)
							return (T)(object)candidate;
					}
					catch(Bech32FormatException) { throw; }
					catch(FormatException) { continue; }
				}

			}

			throw new FormatException("Invalid string");
		}


		private static IEnumerable<IBase58Data> GetCandidates(IEnumerable<Network> networks, string base58)
		{
			if(base58 == null)
				throw new ArgumentNullException("base58");
			foreach(var network in networks)
			{
				var type = network.GetBase58Type(base58);
				if(type.HasValue)
				{
					if(type.Value == Base58Type.COLORED_ADDRESS)
					{
						var wrapped = BitcoinColoredAddress.GetWrappedBase58(base58, network);
						var wrappedType = network.GetBase58Type(wrapped);
						if(wrappedType == null)
							continue;
						try
						{
							var inner = network.CreateBase58Data(wrappedType.Value, wrapped);
							if(inner.Network != network)
								continue;
						}
						catch(FormatException) { }
					}
					IBase58Data data = null;
					try
					{
						data = network.CreateBase58Data(type.Value, base58);
					}
					catch(FormatException) { }
					if(data != null)
						yield return data;
				}
			}
		}

		private IBase58Data CreateBase58Data(Base58Type type, string base58)
		{
			if(type == Base58Type.EXT_PUBLIC_KEY)
				return CreateBitcoinExtPubKey(base58);
			if(type == Base58Type.EXT_SECRET_KEY)
				return CreateBitcoinExtKey(base58);
			if(type == Base58Type.PUBKEY_ADDRESS)
				return new BitcoinPubKeyAddress(base58, this);
			if(type == Base58Type.SCRIPT_ADDRESS)
				return CreateBitcoinScriptAddress(base58);
			if(type == Base58Type.SECRET_KEY)
				return CreateBitcoinSecret(base58);
			if(type == Base58Type.CONFIRMATION_CODE)
				return CreateConfirmationCode(base58);
			if(type == Base58Type.ENCRYPTED_SECRET_KEY_EC)
				return CreateEncryptedKeyEC(base58);
			if(type == Base58Type.ENCRYPTED_SECRET_KEY_NO_EC)
				return CreateEncryptedKeyNoEC(base58);
			if(type == Base58Type.PASSPHRASE_CODE)
				return CreatePassphraseCode(base58);
			if(type == Base58Type.STEALTH_ADDRESS)
				return CreateStealthAddress(base58);
			if(type == Base58Type.ASSET_ID)
				return CreateAssetId(base58);
			if(type == Base58Type.COLORED_ADDRESS)
				return CreateColoredAddress(base58);
			throw new NotSupportedException("Invalid Base58Data type : " + type.ToString());
		}

		//private BitcoinWitScriptAddress CreateWitScriptAddress(string base58)
		//{
		//	return new BitcoinWitScriptAddress(base58, this);
		//}

		//private BitcoinWitPubKeyAddress CreateWitPubKeyAddress(string base58)
		//{
		//	return new BitcoinWitPubKeyAddress(base58, this);
		//}

		private BitcoinColoredAddress CreateColoredAddress(string base58)
		{
			return new BitcoinColoredAddress(base58, this);
		}

		public NBitcoin.OpenAsset.BitcoinAssetId CreateAssetId(string base58)
		{
			return new NBitcoin.OpenAsset.BitcoinAssetId(base58, this);
		}

		public BitcoinStealthAddress CreateStealthAddress(string base58)
		{
			return new BitcoinStealthAddress(base58, this);
		}

		private BitcoinPassphraseCode CreatePassphraseCode(string base58)
		{
			return new BitcoinPassphraseCode(base58, this);
		}

		private BitcoinEncryptedSecretNoEC CreateEncryptedKeyNoEC(string base58)
		{
			return new BitcoinEncryptedSecretNoEC(base58, this);
		}

		private BitcoinEncryptedSecretEC CreateEncryptedKeyEC(string base58)
		{
			return new BitcoinEncryptedSecretEC(base58, this);
		}

		private Base58Data CreateConfirmationCode(string base58)
		{
			return new BitcoinConfirmationCode(base58, this);
		}

		private Base58Data CreateBitcoinExtPubKey(string base58)
		{
			return new BitcoinExtPubKey(base58, this);
		}


		public BitcoinExtKey CreateBitcoinExtKey(ExtKey key)
		{
			return new BitcoinExtKey(key, this);
		}

		public BitcoinExtPubKey CreateBitcoinExtPubKey(ExtPubKey pubkey)
		{
			return new BitcoinExtPubKey(pubkey, this);
		}

		public BitcoinExtKey CreateBitcoinExtKey(string base58)
		{
			return new BitcoinExtKey(base58, this);
		}

		public override string ToString()
		{
			return name;
		}

		public Block GetGenesis()
		{
			var block = new Block();
			block.ReadWrite(genesis.ToBytes());
			return block;
		}


		public uint256 GenesisHash
		{
			get
			{
				return consensus.HashGenesisBlock;
			}
		}

		public static IEnumerable<Network> GetNetworks()
		{
			yield return Main;
			yield return TestNet;
			yield return RegTest;

			if(_OtherNetworks.Count != 0)
			{
				List<Network> others = new List<Network>();
				lock(_OtherNetworks)
				{
					others = _OtherNetworks.ToList();
				}
				foreach(var network in others)
				{
					yield return network;
				}
			}
		}

		/// <summary>
		/// Get network from protocol magic number
		/// </summary>
		/// <param name="magic">Magic number</param>
		/// <returns>The network, or null of the magic number does not match any network</returns>
		public static Network GetNetwork(uint magic)
		{
			return GetNetworks().FirstOrDefault(r => r.Magic == magic);
		}

		/// <summary>
		/// Get network from name
		/// </summary>
		/// <param name="name">main,mainnet,testnet,test,testnet3,reg,regtest,seg,segnet</param>
		/// <returns>The network or null of the name does not match any network</returns>
		public static Network GetNetwork(string name)
		{
			if(name == null)
				throw new ArgumentNullException("name");
			name = name.ToLowerInvariant();
			switch(name)
			{
				case "main":
				case "mainnet":
					return Network.Main;
				case "testnet":
				case "test":
				case "testnet3":
					return Network.TestNet;
				case "reg":
				case "regtest":
				case "regnet":
					return Network.RegTest;
			}

			if(_OtherAliases.Count != 0)
			{
				return _OtherAliases.TryGet(name);
			}
			return null;
		}

		public BitcoinSecret CreateBitcoinSecret(Key key)
		{
			return new BitcoinSecret(key, this);
		}
		public BitcoinPubKeyAddress CreateBitcoinAddress(KeyId dest)
		{
			if(dest == null)
				throw new ArgumentNullException("dest");
			return new BitcoinPubKeyAddress(dest, this);
		}

		private BitcoinAddress CreateBitcoinScriptAddress(ScriptId scriptId)
		{
			return new BitcoinScriptAddress(scriptId, this);
		}

		public Message ParseMessage(byte[] bytes, ProtocolVersion version = ProtocolVersion.PROTOCOL_VERSION)
		{
			BitcoinStream bstream = new BitcoinStream(bytes);
			Message message = new Message();
			using(bstream.ProtocolVersionScope(version))
			{
				bstream.ReadWrite(ref message);
			}
			if(message.Magic != magic)
				throw new FormatException("Unexpected magic field in the message");
			return message;
		}

#if !NOSOCKET
		public IEnumerable<NetworkAddress> SeedNodes
		{
			get
			{
				return this.vFixedSeeds;
			}
		}
		public IEnumerable<DNSSeedData> DNSSeeds
		{
			get
			{
				return this.vSeeds;
			}
		}
#endif
		public byte[] _MagicBytes;
		public byte[] MagicBytes
		{
			get
			{
				if(_MagicBytes == null)
				{
					var bytes = new byte[]
					{
						(byte)Magic,
						(byte)(Magic >> 8),
						(byte)(Magic >> 16),
						(byte)(Magic >> 24)
					};
					_MagicBytes = bytes;
				}
				return _MagicBytes;
			}
		}
		public uint Magic
		{
			get
			{
				return magic;
			}
		}

		public Money GetReward(int nHeight)
		{
			long nSubsidy = new Money(50 * Money.COIN);
			int halvings = nHeight / consensus.SubsidyHalvingInterval;

			// Force block reward to zero when right shift is undefined.
			if(halvings >= 64)
				return Money.Zero;

			// Subsidy is cut in half every 210,000 blocks which will occur approximately every 4 years.
			nSubsidy >>= halvings;

			return new Money(nSubsidy);
		}

		public bool ReadMagic(Stream stream, CancellationToken cancellation, bool throwIfEOF = false)
		{
			byte[] bytes = new byte[1];
			for(int i = 0; i < MagicBytes.Length; i++)
			{
				i = Math.Max(0, i);
				cancellation.ThrowIfCancellationRequested();

				var read = stream.ReadEx(bytes, 0, bytes.Length, cancellation);
				if(read == 0)
					if(throwIfEOF)
						throw new EndOfStreamException("No more bytes to read");
					else
						return false;
				if(read != 1)
					i--;
				else if(_MagicBytes[i] != bytes[0])
					i = _MagicBytes[0] == bytes[0] ? 0 : -1;
			}
			return true;
		}
	}
}
