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
namespace QuantConnect.Algorithm.CSharp
{
    public class League2024Q4 : QCAlgorithm
    {

        public override void Initialize()
        {
            SetStartDate(2015, 01, 01);
            SetEndDate(2015, 12, 01);
            SetCash(100000);

            UniverseSettings.Asynchronous = true;
            
            SetUniverseSelection(new Top500());
            AddAlpha(new EmaCrossAlphaModel());
			SetPortfolioConstruction(new EqualWeightingPortfolioConstructionModel());
			AddRiskManagement(new TrailingStopRiskManagementModel());
			SetExecution(new VolumeWeightedAveragePriceExecutionModel());
        }

        private class Top500 : FundamentalUniverseSelectionModel
		{
			public override IEnumerable<Symbol> Select(QCAlgorithm algorithm, IEnumerable<Fundamental> fundamental)
			{
				return fundamental.Where(x => x.HasFundamentalData)
					.OrderByDescending(x => x.MarketCap)
					.Take(500)
					.Select(x => x.Symbol);
			}
		}

        private class CustomAlphaModel : AlphaModel
		{
			public override IEnumerable<Insight> Update(QCAlgorithm algorithm, Slice data)
			{
				var insights = new List<Insight>();
				return insights;
			}

			public override void OnSecuritiesChanged(QCAlgorithm algorithm, SecurityChanges changes)
			{
				// changes.AddedSecurities
				// changes.RemovedSecurities
			}

			// public override string Name { get; }
		}
    }
}
