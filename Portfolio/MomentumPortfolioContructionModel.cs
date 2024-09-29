#region imports
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Drawing;
using QuantConnect;
using QuantConnect.Algorithm.Framework;
using QuantConnect.Algorithm.Framework.Selection;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Portfolio.SignalExports;
using QuantConnect.Algorithm.Framework.Execution;
using QuantConnect.Algorithm.Framework.Risk;
using QuantConnect.Algorithm.Selection;
using QuantConnect.Api;
using QuantConnect.Parameters;
using QuantConnect.Benchmarks;
using QuantConnect.Brokerages;
using QuantConnect.Configuration;
using QuantConnect.Util;
using QuantConnect.Interfaces;
using QuantConnect.Algorithm;
using QuantConnect.Indicators;
using QuantConnect.Data;
using QuantConnect.Data.Auxiliary;
using QuantConnect.Data.Consolidators;
using QuantConnect.Data.Custom;
using QuantConnect.Data.Custom.IconicTypes;
using QuantConnect.DataSource;
using QuantConnect.Data.Fundamental;
using QuantConnect.Data.Market;
using QuantConnect.Data.Shortable;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Notifications;
using QuantConnect.Orders;
using QuantConnect.Orders.Fees;
using QuantConnect.Orders.Fills;
using QuantConnect.Orders.OptionExercise;
using QuantConnect.Orders.Slippage;
using QuantConnect.Orders.TimeInForces;
using QuantConnect.Python;
using QuantConnect.Scheduling;
using QuantConnect.Securities;
using QuantConnect.Securities.Equity;
using QuantConnect.Securities.Future;
using QuantConnect.Securities.Option;
using QuantConnect.Securities.Positions;
using QuantConnect.Securities.Forex;
using QuantConnect.Securities.Crypto;
using QuantConnect.Securities.CryptoFuture;
using QuantConnect.Securities.Interfaces;
using QuantConnect.Securities.Volatility;
using QuantConnect.Storage;
using QuantConnect.Statistics;
using QCAlgorithmFramework = QuantConnect.Algorithm.QCAlgorithm;
using QCAlgorithmFrameworkBridge = QuantConnect.Algorithm.QCAlgorithm;
#endregion

namespace QuantConnect
{
    public class MomentumPortfolioConstructionModel : PortfolioConstructionModel
    {
        private Dictionary<Symbol, MomentumPercent> _momp;
        private int _lookback;
        private int _shortLookback;
        private int _numLong;
        private bool _rebalance;
        private decimal _adjustmentStep;
        private int _randSeed;
        private HashSet<Symbol> currentHoldings;
        private Dictionary<Symbol, decimal> targetWeights;
        public MomentumPortfolioConstructionModel(int lookback, int shortLookback, int numLong, decimal adjustmentStep, int randSeed)
        {
            _lookback = lookback;
            _shortLookback = shortLookback;
            _numLong = numLong;
            _adjustmentStep = adjustmentStep;
            _randSeed = randSeed;

            _rebalance = true;
            currentHoldings = new HashSet<Symbol>();
            targetWeights = new Dictionary<Symbol, decimal>();
            _momp = new Dictionary<Symbol, MomentumPercent>();
        }
        // Create list of PortfolioTarget objects from Insights.
        public override List<PortfolioTarget> CreateTargets(QCAlgorithm algorithm, Insight[] insights)
        {
            return (List<PortfolioTarget>)base.CreateTargets(algorithm, insights);
        }

        // Determine if the portfolio should rebalance based on the provided rebalancing function.
        protected override bool IsRebalanceDue(Insight[] insights, DateTime algorithmUtc)
        {
            return base.IsRebalanceDue(insights, algorithmUtc);
        }

        // Determine the target percent for each insight.
        protected override Dictionary<Insight, double> DetermineTargetPercent(List<Insight> activeInsights)
        {
            return new Dictionary<Insight, double>();
        }

        // Get the target insights to calculate a portfolio target percent. They will be piped to DetermineTargetPercent().
        protected override List<Insight> GetTargetInsights()
        {
            return base.GetTargetInsights();
        }

        // Determine if the portfolio construction model should create a target for this insight.
        protected override bool ShouldCreateTargetForInsight(Insight insight)
        {
            return base.ShouldCreateTargetForInsight(insight);
        }

        // Security change details.
        public override void OnSecuritiesChanged(QCAlgorithm algorithm, SecurityChanges changes)
        {
            base.OnSecuritiesChanged(algorithm, changes);
        }
    }
}