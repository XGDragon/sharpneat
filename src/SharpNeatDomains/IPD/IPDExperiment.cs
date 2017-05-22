﻿using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml;
using log4net;
using SharpNeat.Core;
using SharpNeat.Decoders;
using SharpNeat.Decoders.Neat;
using SharpNeat.DistanceMetrics;
using SharpNeat.EvolutionAlgorithms;
using SharpNeat.EvolutionAlgorithms.ComplexityRegulation;
using SharpNeat.Genomes.Neat;
using SharpNeat.Phenomes;
using SharpNeat.SpeciationStrategies;

namespace SharpNeat.Domains.IPD
{
    class IPDExperiment : IGuiNeatExperiment
    {
        public enum Opponent
        {
            AllC,
            AllD,
            AllR,
            TFT,
            STFT,
            Grudger
        }

        public enum NoveltyEvaluationMode
        {
            Disable,
            Immediate,
            ArchiveFull,
            SlowArchiveFull
        }

        public enum ObjectiveEvaluationMode
        {
            Fitness,
            Rank
        }

        private static readonly ILog __log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        NeatEvolutionAlgorithmParameters _eaParams;
        NeatGenomeParameters _neatGenomeParams;
        string _name;
        int _populationSize;
        int _specieCount;
        NetworkActivationScheme _activationScheme;
        string _complexityRegulationStr;
        int? _complexityThreshold;
        string _description;
        ParallelOptions _parallelOptions;

        int _pastInputReach;
        int _numberOfGames;
        IPDPlayer[] _opponentPool;
        int _noveltyArchiveSize;
        NoveltyEvaluationMode _noveltyEvaluationMode;
        ObjectiveEvaluationMode _objectiveEvaluationMode;

        Info _info;

        #region Constructor

        /// <summary>
        /// Default constructor.
        /// </summary>
        public IPDExperiment()
        {
        }

        #endregion

        #region INeatExperiment

        /// <summary>
        /// Gets the name of the experiment.
        /// </summary>
        public string Name
        {
            get { return _name; }
        }

        /// <summary>
        /// Gets human readable explanatory text for the experiment.
        /// </summary>
		public string Description
        {
            get { return _description; }
        }

        /// <summary>
        /// Gets the number of inputs required by the network/black-box that the underlying problem domain is based on.
        /// </summary>
        public int InputCount
        {
            get { return _pastInputReach * 2; }
        }

        /// <summary>
        /// Gets the number of outputs required by the network/black-box that the underlying problem domain is based on.
        /// </summary>
        public int OutputCount
        {
            get { return 2; }
        }

        /// <summary>
        /// Gets the default population size to use for the experiment.
        /// </summary>
        public int DefaultPopulationSize
        {
            get { return _populationSize; }
        }

        /// <summary>
        /// Gets the NeatEvolutionAlgorithmParameters to be used for the experiment. Parameters on this object can be 
        /// modified. Calls to CreateEvolutionAlgorithm() make a copy of and use this object in whatever state it is in 
        /// at the time of the call.
        /// </summary>
        public NeatEvolutionAlgorithmParameters NeatEvolutionAlgorithmParameters
        {
            get { return _eaParams; }
        }

        /// <summary>
        /// Gets the NeatGenomeParameters to be used for the experiment. Parameters on this object can be modified. Calls
        /// to CreateEvolutionAlgorithm() make a copy of and use this object in whatever state it is in at the time of the call.
        /// </summary>
        public NeatGenomeParameters NeatGenomeParameters
        {
            get { return _neatGenomeParams; }
        }

        /// <summary>
        /// Initialize the experiment with some optional XML configuration data.
        /// </summary>
        public void Initialize(string name, XmlElement xmlConfig)
        {
            T GetValueAsEnum<T>(string e)
            {
                string r = XmlUtils.GetValueAsString(xmlConfig, e);
                return (T)System.Enum.Parse(typeof(T), r);
            }

            _name = name;
            _populationSize = XmlUtils.GetValueAsInt(xmlConfig, "PopulationSize");
            _specieCount = XmlUtils.GetValueAsInt(xmlConfig, "SpecieCount");
            _activationScheme = ExperimentUtils.CreateActivationScheme(xmlConfig, "Activation");
            _complexityRegulationStr = XmlUtils.TryGetValueAsString(xmlConfig, "ComplexityRegulationStrategy");
            _complexityThreshold = XmlUtils.TryGetValueAsInt(xmlConfig, "ComplexityThreshold");

            _numberOfGames = XmlUtils.GetValueAsInt(xmlConfig, "IPDGames");
            int seed = XmlUtils.GetValueAsInt(xmlConfig, "RandomPlayerSeed");
            int randoms = XmlUtils.GetValueAsInt(xmlConfig, "RandomPlayerCount");
            string[] opps = XmlUtils.GetValueAsString(xmlConfig, "StaticOpponents").Split(',');
            _opponentPool = _CreatePool(seed, randoms, System.Array.ConvertAll(opps, (string o) => { return (Opponent)System.Enum.Parse(typeof(Opponent), o, true); }));
            
            _noveltyArchiveSize = XmlUtils.GetValueAsInt(xmlConfig, "NoveltyArchiveSize");
            _noveltyEvaluationMode = GetValueAsEnum<NoveltyEvaluationMode>("NoveltyEvaluationMode");
            _objectiveEvaluationMode = GetValueAsEnum<ObjectiveEvaluationMode>("ObjectiveEvaluationMode");

            _pastInputReach = XmlUtils.GetValueAsInt(xmlConfig, "PastInputReach");

            _description = XmlUtils.TryGetValueAsString(xmlConfig, "Description");
            _parallelOptions = ExperimentUtils.ReadParallelOptions(xmlConfig);

            _eaParams = new NeatEvolutionAlgorithmParameters();
            _eaParams.SpecieCount = _specieCount;
            _neatGenomeParams = new NeatGenomeParameters();
            _neatGenomeParams.FeedforwardOnly = _activationScheme.AcyclicNetwork;
        }

        /// <summary>
        /// Load a population of genomes from an XmlReader and returns the genomes in a new list.
        /// The genome factory for the genomes can be obtained from any one of the genomes.
        /// </summary>
        public List<NeatGenome> LoadPopulation(XmlReader xr)
        {
            NeatGenomeFactory genomeFactory = (NeatGenomeFactory)CreateGenomeFactory();
            return NeatGenomeXmlIO.ReadCompleteGenomeList(xr, false, genomeFactory);
        }

        /// <summary>
        /// Save a population of genomes to an XmlWriter.
        /// </summary>
        public void SavePopulation(XmlWriter xw, IList<NeatGenome> genomeList)
        {
            // Writing node IDs is not necessary for NEAT.
            NeatGenomeXmlIO.WriteComplete(xw, genomeList, false);
        }

        /// <summary>
        /// Create a genome decoder for the experiment.
        /// </summary>
        public IGenomeDecoder<NeatGenome, IBlackBox> CreateGenomeDecoder()
        {
            return new NeatGenomeDecoder(_activationScheme);
        }

        /// <summary>
        /// Create a genome factory for the experiment.
        /// Create a genome factory with our neat genome parameters object and the appropriate number of input and output neuron genes.
        /// </summary>
        public IGenomeFactory<NeatGenome> CreateGenomeFactory()
        {
            return new NeatGenomeFactory(InputCount, OutputCount, _neatGenomeParams);
        }

        /// <summary>
        /// Create and return a NeatEvolutionAlgorithm object ready for running the NEAT algorithm/search. Various sub-parts
        /// of the algorithm are also constructed and connected up.
        /// Uses the experiments default population size defined in the experiment's config XML.
        /// </summary>
        public NeatEvolutionAlgorithm<NeatGenome> CreateEvolutionAlgorithm()
        {
            return CreateEvolutionAlgorithm(_populationSize);
        }

        /// <summary>
        /// Create and return a NeatEvolutionAlgorithm object ready for running the NEAT algorithm/search. Various sub-parts
        /// of the algorithm are also constructed and connected up.
        /// This overload accepts a population size parameter that specifies how many genomes to create in an initial randomly
        /// generated population.
        /// </summary>
        public NeatEvolutionAlgorithm<NeatGenome> CreateEvolutionAlgorithm(int populationSize)
        {
            // Create a genome factory with our neat genome parameters object and the appropriate number of input and output neuron genes.
            IGenomeFactory<NeatGenome> genomeFactory = CreateGenomeFactory();

            // Create an initial population of randomly generated genomes.
            List<NeatGenome> genomeList = genomeFactory.CreateGenomeList(populationSize, 0);

            // Create evolution algorithm.
            return CreateEvolutionAlgorithm(genomeFactory, genomeList);
        }

        /// <summary>
        /// Create and return a NeatEvolutionAlgorithm object ready for running the NEAT algorithm/search. Various sub-parts
        /// of the algorithm are also constructed and connected up.
        /// This overload accepts a pre-built genome population and their associated/parent genome factory.
        /// </summary>
        public NeatEvolutionAlgorithm<NeatGenome> CreateEvolutionAlgorithm(IGenomeFactory<NeatGenome> genomeFactory, List<NeatGenome> genomeList)
        {
            // Create distance metric. Mismatched genes have a fixed distance of 10; for matched genes the distance is their weight difference.
            IDistanceMetric distanceMetric = new ManhattanDistanceMetric(1.0, 0.0, 10.0);
            ISpeciationStrategy<NeatGenome> speciationStrategy = new ParallelKMeansClusteringStrategy<NeatGenome>(distanceMetric, _parallelOptions);

            // Create complexity regulation strategy.
            IComplexityRegulationStrategy complexityRegulationStrategy = ExperimentUtils.CreateComplexityRegulationStrategy(_complexityRegulationStr, _complexityThreshold);

            // Create the evolution algorithm.
            NeatEvolutionAlgorithm<NeatGenome> ea = new NeatEvolutionAlgorithm<NeatGenome>(_eaParams, speciationStrategy, complexityRegulationStrategy);

            // Create genome decoder.
            IGenomeDecoder<NeatGenome, IBlackBox> genomeDecoder = CreateGenomeDecoder();

            // Create IBlackBox evaluator.
            IPDEvaluator evaluator = new IPDEvaluator(_info = new Info(this, ea, genomeDecoder));

            // Create a genome list evaluator. This packages up the genome decoder with the genome evaluator.
            IGenomeListEvaluator<NeatGenome> innerEvaluator = new ParallelGenomeListEvaluator<NeatGenome, IBlackBox>(genomeDecoder, evaluator, _parallelOptions);

            // Wrap the list evaluator in a 'selective' evaluator that will only evaluate new genomes. That is, we skip re-evaluating any genomes
            // that were in the population in previous generations (elite genomes). This is determined by examining each genome's evaluation info object.
            IGenomeListEvaluator<NeatGenome> selectiveEvaluator = new SelectiveGenomeListEvaluator<NeatGenome>(
                                                                                    innerEvaluator,
                                                                                    SelectiveGenomeListEvaluator<NeatGenome>.CreatePredicate_OnceOnly());
            // Initialize the evolution algorithm.
            ea.Initialize(selectiveEvaluator, genomeFactory, genomeList);
         

            // Finished. Return the evolution algorithm
            return ea;
        }

        /// <summary>
        /// Create a System.Windows.Forms derived object for displaying genomes.
        /// </summary>
        public AbstractGenomeView CreateGenomeView()
        {
            return new NeatGenomeView();
        }

        /// <summary>
        /// Create a System.Windows.Forms derived object for displaying output for a domain (e.g. show best genome's output/performance/behaviour in the domain). 
        /// </summary>
        public AbstractDomainView CreateDomainView()
        {
            return new IPDGameTable(CreateGenomeDecoder(), _info);
        }

        #endregion

        private IPDPlayer[] _CreatePool(int seed, int randoms, params Opponent[] opponents)
        {
            Players.IPDPlayerFactory pf = new Players.IPDPlayerFactory(seed);
            var pool = new IPDPlayer[randoms + opponents.Length];
            for (int i = 0; i < randoms; i++)
                pool[i] = pf.Random();
            for (int i = randoms, j = 0; i < pool.Length; i++, j++)
                pool[i] = Players.IPDPlayerFactory.Create(opponents[j]);
            return pool;
        }

        public struct Info
        {
            public int InputCount { get { return _exp.InputCount; } }
            public int OutputCount { get { return _exp.OutputCount; } }

            public int NoveltyArchiveSize { get { return _exp._noveltyArchiveSize; } }
            public NoveltyEvaluationMode NoveltyEvaluationMode { get { return _exp._noveltyEvaluationMode; } }
            public ObjectiveEvaluationMode ObjectiveEvaluationMode { get { return _exp._objectiveEvaluationMode; } }

            public IPDPlayer[] OpponentPool { get { return _exp._opponentPool; } }
            public IPDGame[,] OpponentPoolGames { get; private set; }
            public double[] OpponentScores { get; private set; }
            public int NumberOfGames { get { return _exp._numberOfGames; } }

            public int PopulationSize { get { return _exp._populationSize; } }
            public int CurrentGeneration { get { return (int)_genGet(); } }
            public IBlackBox BestGenome { get { return _boxGet(); } }

            private IPDExperiment _exp;
            private System.Func<uint> _genGet;
            private System.Func<IBlackBox> _boxGet;
            private uint _current;

            public Info(IPDExperiment exp, NeatEvolutionAlgorithm<NeatGenome> ea, IGenomeDecoder<NeatGenome, IBlackBox> decoder)
            {
                _exp = exp;
                _genGet = () => { return ea.CurrentGeneration; };
                _boxGet = () => { return decoder.Decode(ea.CurrentChampGenome); };
                _current = _genGet();

                var pool = _exp._opponentPool;
                OpponentScores = new double[pool.Length];
                OpponentPoolGames = new IPDGame[pool.Length, pool.Length];
                for (int i = 0; i < pool.Length; i++)
                {
                    for (int j = i + 1; j < pool.Length; j++)
                    {
                        if (i != j) //currently not against each other but..
                        {
                            IPDGame g = new IPDGame(NumberOfGames, pool[i], pool[j]);
                            g.Run();
                            OpponentPoolGames[i, j] = g;
                            OpponentPoolGames[j, i] = g;

                            OpponentScores[i] += g.GetScore(pool[i]);
                            OpponentScores[j] += g.GetScore(pool[j]);
                        }
                    }
                }
            }

            public bool HasNewGenerationOccured()
            {
                uint g = _genGet();
                if (_current != g)
                {
                    _current = g;
                    return true;
                }
                return false;
            }
        }
    }
}

